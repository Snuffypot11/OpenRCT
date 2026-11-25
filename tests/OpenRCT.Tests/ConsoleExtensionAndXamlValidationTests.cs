using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using OpenRCT.Core.Models;
using Xunit;

namespace OpenRCT.Tests
{
    public sealed class ConsoleExtensionAndXamlValidationTests
    {
        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "OpenRCT.sln")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("Could not locate OpenRCT.sln repository root.");
        }

        private static HashSet<string> GetAllKnownActionKeys()
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in ClientActionRegistry.All) keys.Add(a.Key);
            foreach (var field in typeof(ToolActionKeys).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.GetRawConstantValue() is string val) keys.Add(val);
            }
            return keys;
        }

        [Fact]
        public void AllConsoleExtensionXmlFiles_AreWellFormed_AndReferenceValidActionKeys()
        {
            var root = FindRepoRoot();
            var extDir = Path.Combine(root, "console-extensions");
            var xmlFiles = Directory.GetFiles(extDir, "*.xml", SearchOption.AllDirectories);
            Assert.NotEmpty(xmlFiles);

            var knownKeys = GetAllKnownActionKeys();

            foreach (var file in xmlFiles)
            {
                var doc = XDocument.Load(file);
                Assert.NotNull(doc.Root);

                // Validate all AssemblyType actions reference valid action keys in <Parameters>
                var assemblyActions = doc.Descendants("ActionDescription")
                    .Where(e => string.Equals((string?)e.Attribute("Class"), "AssemblyType", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var act in assemblyActions)
                {
                    var paramKey = act.Element("Parameters")?.Value?.Trim();
                    Assert.False(string.IsNullOrWhiteSpace(paramKey), $"Missing <Parameters> in {Path.GetFileName(file)}");
                    Assert.Contains(paramKey!, knownKeys);
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
            var file = Path.Combine(root, "console-extensions", "XmlStorage", "Extensions", "Actions", "OpenRCT_DeviceActions.xml");
            var doc = XDocument.Load(file);

            var paramValues = doc.Descendants("Parameters").Select(e => e.Value.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var clientAction in ClientActionRegistry.All)
            {
                Assert.Contains(clientAction.Key, paramValues);
            }
        }

        [Fact]
        public void AllWpfXamlAndWixFiles_AreWellFormedXml()
        {
            var root = FindRepoRoot();
            var xamlFiles = Directory.GetFiles(Path.Combine(root, "src"), "*.xaml", SearchOption.AllDirectories);
            Assert.True(xamlFiles.Length >= 20, $"Expected >=20 XAML files, found {xamlFiles.Length}");

            foreach (var file in xamlFiles)
            {
                var doc = XDocument.Load(file);
                Assert.NotNull(doc.Root);
            }

            var wxsFile = Path.Combine(root, "installer", "OpenRCT.Installer", "Package.wxs");
            var wxsDoc = XDocument.Load(wxsFile);
            Assert.NotNull(wxsDoc.Root);
        }
    }
}
