using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using OpenCMTools.Core.Models;
using Xunit;

namespace OpenCMTools.Tests
{
    public sealed class ConsoleExtensionAndXamlValidationTests
    {
        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "OpenCMTools.sln")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("Could not locate OpenCMTools.sln repository root.");
        }

        [Fact]
        public void AllConsoleExtensionXmlFiles_AreWellFormed_AndMapEveryDisplayNameToValidActionKey()
        {
            var root = FindRepoRoot();
            var extDir = Path.Combine(root, "console-extensions");
            var xmlFiles = Directory.GetFiles(extDir, "*.xml", SearchOption.AllDirectories);
            Assert.NotEmpty(xmlFiles);

            var allowedClasses = new[] { "Group", "AssemblyType", "Executable" };
            foreach (var file in xmlFiles)
            {
                var doc = XDocument.Load(file);
                Assert.NotNull(doc.Root);

                foreach (var anyAction in doc.Descendants("ActionDescription"))
                {
                    var cls = (string?)anyAction.Attribute("Class");
                    Assert.Contains(cls, allowedClasses, StringComparer.OrdinalIgnoreCase);
                }

                var assemblyActions = doc.Descendants("ActionDescription")
                    .Where(e => string.Equals((string?)e.Attribute("Class"), "AssemblyType", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var act in assemblyActions)
                {
                    // AssemblyType must NOT contain <Parameters> (invalid on ActionDescription schema)
                    Assert.Null(act.Element("Parameters"));

                    var displayName = (string?)act.Attribute("DisplayName");
                    Assert.False(string.IsNullOrWhiteSpace(displayName), $"Missing DisplayName in {Path.GetFileName(file)}");

                    var resolvedKey = ToolActionKeys.ResolveByDisplayName(displayName);
                    Assert.False(string.IsNullOrWhiteSpace(resolvedKey), $"DisplayName '{displayName}' in {Path.GetFileName(file)} did not map to any ToolActionKey.");

                    Assert.NotNull(act.Element("ActionAssembly")?.Element("Assembly"));
                    Assert.NotNull(act.Element("ActionAssembly")?.Element("Type"));
                    Assert.NotNull(act.Element("ActionAssembly")?.Element("Method"));
                }
            }
        }

        [Fact]
        public void DeviceActionsXml_ContainsAll12ClientActions()
        {
            var root = FindRepoRoot();
            var file = Path.Combine(root, "console-extensions", "XmlStorage", "Extensions", "Actions", "OpenCMTools_DeviceActions.xml");
            var doc = XDocument.Load(file);

            var displayNames = doc.Descendants("ActionDescription")
                .Where(e => string.Equals((string?)e.Attribute("Class"), "AssemblyType", StringComparison.OrdinalIgnoreCase))
                .Select(e => (string?)e.Attribute("DisplayName") ?? string.Empty)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var clientAction in ClientActionRegistry.All)
            {
                Assert.Contains(clientAction.DisplayName, displayNames);
            }
        }

        [Fact]
        public void AllWpfXamlFiles_AreWellFormed_AndUseDynamicResourceForThemeStyles()
        {
            var root = FindRepoRoot();
            var xamlFiles = Directory.GetFiles(Path.Combine(root, "src"), "*.xaml", SearchOption.AllDirectories);
            Assert.True(xamlFiles.Length >= 20, $"Expected >=20 XAML files, found {xamlFiles.Length}");

            foreach (var file in xamlFiles)
            {
                var doc = XDocument.Load(file);
                Assert.NotNull(doc.Root);

                var fileName = Path.GetFileName(file);
                if (fileName == "LightTheme.xaml" || fileName == "DarkTheme.xaml" || fileName == "App.xaml") continue;

                var raw = File.ReadAllText(file);
                Assert.DoesNotContain("{StaticResource StandardButton}", raw);
                Assert.DoesNotContain("{StaticResource PrimaryButton}", raw);
                Assert.DoesNotContain("{StaticResource DangerButton}", raw);
                Assert.DoesNotContain("{StaticResource OpenCMToolsDataGrid}", raw);
            }

            var wxsFile = Path.Combine(root, "installer", "OpenCMTools.Installer", "Package.wxs");
            var wxsDoc = XDocument.Load(wxsFile);
            Assert.NotNull(wxsDoc.Root);
        }

        [Fact]
        public void ConsoleExtensionBridgeFiles_AreCSharp5CompatibleForBuiltinCsc()
        {
            var root = FindRepoRoot();
            var bridgeFiles = new[]
            {
                Path.Combine(root, "src", "OpenCMTools.ConsoleExtension", "OpenCMToolsActionHandler.cs"),
                Path.Combine(root, "src", "OpenCMTools.ConsoleExtension", "DashboardConsoleViews.cs")
            };

            foreach (var file in bridgeFiles)
            {
                var code = File.ReadAllText(file);
                Assert.DoesNotMatch(new Regex(@"\$\"""), code);   // no C# 6 string interpolation
                Assert.DoesNotMatch(new Regex(@"\?\."), code);    // no C# 6 null-conditional
                Assert.DoesNotMatch(new Regex(@"using\s+var\b"), code); // no C# 8 using var
                Assert.DoesNotMatch(new Regex(@"\bstring\?"), code);    // no C# 8 nullable annotations
            }
        }

        [Fact]
        public void PowerShellInstallerScripts_ArePureAsciiForPowerShell51()
        {
            var root = FindRepoRoot();
            var scripts = Directory.GetFiles(Path.Combine(root, "installer"), "*.ps1", SearchOption.AllDirectories);
            Assert.NotEmpty(scripts);

            foreach (var script in scripts)
            {
                var bytes = File.ReadAllBytes(script);
                var nonAscii = bytes.FirstOrDefault(b => b > 127);
                Assert.True(nonAscii == 0, $"Non-ASCII byte 0x{nonAscii:X2} found in {Path.GetFileName(script)} (breaks Windows PowerShell 5.1 without BOM).");

                // Catch PowerShell $var: parser errors (e.g. "$consoleBin:" instead of "$($consoleBin):")
                // Allow valid PowerShell scope/drive prefixes: $env:, $script:, $global:, $local:, $private:, $using:
                var text = File.ReadAllText(script);
                var matches = Regex.Matches(text, @"\$(?<name>[A-Za-z_][A-Za-z0-9_]*):");
                var allowedScopes = new[] { "env", "script", "global", "local", "private", "using", "variable", "function" };
                foreach (Match m in matches)
                {
                    var name = m.Groups["name"].Value;
                    Assert.Contains(name, allowedScopes, StringComparer.OrdinalIgnoreCase);
                }
            }
        }

        [Fact]
        public void ConsoleExtensionXmlFiles_ArePureAscii()
        {
            var root = FindRepoRoot();
            var xmlFiles = Directory.GetFiles(Path.Combine(root, "console-extensions"), "*.xml", SearchOption.AllDirectories);
            Assert.NotEmpty(xmlFiles);

            foreach (var xml in xmlFiles)
            {
                var bytes = File.ReadAllBytes(xml);
                var nonAscii = bytes.FirstOrDefault(b => b > 127);
                Assert.True(nonAscii == 0, $"Non-ASCII byte 0x{nonAscii:X2} found in {Path.GetFileName(xml)} (causes mojibake in MECM Console menus).");
            }
        }

        [Fact]
        public void DeviceActionsXml_SupportsMultiSelectAndRibbonDropdown()
        {
            var root = FindRepoRoot();
            var deviceFile = Path.Combine(root, "console-extensions", "XmlStorage", "Extensions", "Actions", "OpenCMTools_DeviceActions.xml");
            var doc = XDocument.Load(deviceFile);
            Assert.NotNull(doc.Root);

            // Root OpenCMTools group and all 4 submenus must allow Both (single + multi-select)
            Assert.Equal("Both", (string?)doc.Root!.Attribute("SelectionMode"));
            var groups = doc.Descendants("ActionDescription")
                .Where(e => string.Equals((string?)e.Attribute("Class"), "Group", StringComparison.OrdinalIgnoreCase))
                .ToList();
            Assert.All(groups, g => Assert.Equal("Both", (string?)g.Attribute("SelectionMode")));

            // Every action must explicitly specify SelectionMode (Both or Single) and include DefaultHomeTab so the Ribbon button populates
            var allActions = doc.Descendants("ActionDescription").ToList();
            foreach (var act in allActions)
            {
                var mode = (string?)act.Attribute("SelectionMode");
                Assert.True(mode == "Both" || mode == "Single", $"Action '{act.Attribute("DisplayName")?.Value}' must set SelectionMode to Both or Single.");

                var showOnValues = act.Element("ShowOn")?.Elements("string").Select(s => s.Value).ToList() ?? new System.Collections.Generic.List<string>();
                Assert.Contains("ContextMenu", showOnValues);
                Assert.Contains("DefaultHomeTab", showOnValues);
            }

            // Ensure bulk tools (e.g. all 12 Client Actions, Ping, Restart, Shutdown) have SelectionMode="Both"
            var multiTools = doc.Descendants("ActionDescription")
                .Where(e => string.Equals((string?)e.Attribute("Class"), "AssemblyType", StringComparison.OrdinalIgnoreCase) &&
                            string.Equals((string?)e.Attribute("SelectionMode"), "Both", StringComparison.OrdinalIgnoreCase))
                .ToList();
            Assert.True(multiTools.Count >= 40, $"Expected >=40 multi-select tools, found {multiTools.Count}");
        }

        [Fact]
        public void ConsoleNodesXml_IsFlattenedDirectlyUnderOpenCMTools_AndStandaloneLauncherOmits10TabsLabel()
        {
            var root = FindRepoRoot();
            var nodesFile = Path.Combine(root, "console-extensions", "XmlStorage", "Extensions", "Nodes", "OpenCMTools_Nodes.xml");
            var doc = XDocument.Load(nodesFile);
            Assert.NotNull(doc.Root);
            Assert.Equal("OpenCMToolsRootNode", (string?)doc.Root!.Attribute("Id"));

            // All 6 dashboard nodes must sit directly under OpenCMToolsRootNode with no extra "Endpoint Tools" layer
            var directChildren = doc.Root.Element("ChildNodes")?.Elements("RootNodeDescription").ToList() ?? new System.Collections.Generic.List<XElement>();
            Assert.Equal(6, directChildren.Count);
            Assert.DoesNotContain(directChildren, e => string.Equals((string?)e.Attribute("Id"), "OpenCMToolsEndpointToolsNode", StringComparison.OrdinalIgnoreCase));
            Assert.All(directChildren, child =>
            {
                Assert.NotNull(child.Element("ViewAssemblyDescriptions")?.Element("ViewAssemblyDescription"));
                Assert.NotNull(child.Element("Actions")?.Element("ActionDescription"));
            });

            var launcherXaml = File.ReadAllText(Path.Combine(root, "src", "OpenCMTools.App", "MainLauncherWindow.xaml"));
            Assert.DoesNotContain("System Information (10 Tabs)", launcherXaml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Phase 2 Dashboards (Roadmap)", launcherXaml, StringComparison.OrdinalIgnoreCase);
        }
    }
}
