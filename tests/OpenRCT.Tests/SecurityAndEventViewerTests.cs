using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using OpenRCT.Core.Analytics;
using OpenRCT.Core.Models;
using Xunit;

namespace OpenRCT.Tests
{
    public sealed class SecurityAndEventViewerTests
    {
        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "OpenRCT.sln"))) return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("Could not locate OpenRCT.sln.");
        }

        [Fact]
        public void EventLogXPathBuilder_BuildsExpectedFiltersForSeverityEventIdsAndDateRange()
        {
            Assert.Equal("*", EventLogXPathBuilder.BuildXPath(new RemoteEventLogQuery()));

            var q = new RemoteEventLogQuery
            {
                Severity = "Critical & Error",
                EventIdsText = "1000, 7036, bad, 1000",
                StartDate = new DateTime(2026, 5, 1),
                EndDate = new DateTime(2026, 5, 10)
            };

            var xpath = EventLogXPathBuilder.BuildXPath(q);
            Assert.StartsWith("*[System[", xpath);
            Assert.Contains("(Level=1 or Level=2)", xpath);
            Assert.Contains("(EventID=1000 or EventID=7036)", xpath);
            Assert.Contains("TimeCreated[@SystemTime>=", xpath);
            Assert.Contains("@SystemTime<=", xpath);
        }

        [Fact]
        public void MultiDeviceGrouper_GroupsSecurityTabsAccurately()
        {
            var tpmGroups = MultiDeviceGrouper.GroupTpmStatuses(new[]
            {
                new TpmStatusInfo { DeviceName = "PC-01", SpecVersion = "2.0, 0, 1.16", IsActivated = true, IsEnabled = true, IsOwned = true, IsReady = true, Manufacturer = "MSFT", ManufacturerVersion = "8213.275", FipsReady = false, LockedOut = false },
                new TpmStatusInfo { DeviceName = "PC-02", SpecVersion = "2.0, 0, 1.16", IsActivated = true, IsEnabled = true, IsOwned = true, IsReady = true, Manufacturer = "INTC", ManufacturerVersion = "600.18", FipsReady = true, LockedOut = false }
            });
            Assert.Single(tpmGroups);
            Assert.Equal(2, tpmGroups[0].DeviceCount);
            Assert.Equal("True", tpmGroups[0].ExtraInfo); // Owned
            Assert.Equal("True", tpmGroups[0].StatusSummary); // Ready

            var fwGroups = MultiDeviceGrouper.GroupFirmwareSecurity(new[]
            {
                new FirmwareSecurityInfo { DeviceName = "PC-01", UefiEnabled = true, SecureBootEnabled = true, BiosVersion = "v4.1", CertificateStatus = "Unknown", CertificateUpdateReadiness = "Ready To Update (0x5944)", ConfidenceLevel = "High Confidence" },
                new FirmwareSecurityInfo { DeviceName = "PC-02", UefiEnabled = true, SecureBootEnabled = true, BiosVersion = "v4.1", CertificateStatus = "NotStarted", CertificateUpdateReadiness = "Not Ready (0x0)" }
            });
            Assert.Equal(2, fwGroups.Count);
            Assert.Equal("High Confidence", fwGroups.First(g => g.StatusSummary == "Unknown").Children[0].ErrorCode);

            var defGroups = MultiDeviceGrouper.GroupDefenderStatuses(new[]
            {
                new DefenderStatusInfo { DeviceName = "PC-01", AntivirusEnabled = true, RealTimeProtectionEnabled = true, SignatureVersion = "1.421.1.0", ClientVersion = "4.18.24090.11", EngineVersion = "1.1.24090.11", LastQuickScan = new DateTime(2026, 1, 2, 10, 30, 0) }
            });
            Assert.Single(defGroups);
            Assert.Equal("Enabled", defGroups[0].Name);
            Assert.Equal("1.421.1.0", defGroups[0].Version);
            Assert.Equal("4.18.24090.11", defGroups[0].ExtraInfo);
            Assert.Equal("1.1.24090.11", defGroups[0].StatusSummary);

            var ruleGroups = MultiDeviceGrouper.GroupFirewallRules(new[]
            {
                new FirewallRuleInfo { DeviceName = "PC-01", RuleId = "r1", DisplayName = "Delivery Optimization (TCP-In)", Action = "Allow", Direction = "Inbound", Profiles = "All", Enabled = true },
                new FirewallRuleInfo { DeviceName = "PC-02", RuleId = "r1", DisplayName = "Delivery Optimization (TCP-In)", Action = "Allow", Direction = "Inbound", Profiles = "All", Enabled = true }
            });
            Assert.Single(ruleGroups);
            Assert.Equal(2, ruleGroups[0].DeviceCount);
            Assert.Equal("All", ruleGroups[0].ExtraInfo);
            Assert.Equal("Enabled", ruleGroups[0].StatusSummary);
        }

        [Fact]
        public void ReorganizedDeviceAndCollectionMenus_IncludeRemoteToolsAndExpectedGroups()
        {
            var root = FindRepoRoot();
            var deviceXml = XDocument.Load(Path.Combine(root, "console-extensions", "XmlStorage", "Extensions", "Actions", "OpenRCT_DeviceActions.xml"));
            var deviceGroups = deviceXml.Root!.Element("ActionGroups")!.Elements("ActionDescription")
                .Where(e => (string?)e.Attribute("Class") == "Group")
                .Select(e => (string)e.Attribute("DisplayName")!)
                .ToList();

            Assert.Equal(new[]
            {
                "Remote Tools",
                "Client Actions",
                "Client Tools",
                "Device & Power Tools",
                "Security Tools",
                "Active Directory & ConfigMgr"
            }, deviceGroups);

            var deviceItems = deviceXml.Descendants("ActionDescription")
                .Where(e => (string?)e.Attribute("Class") == "AssemblyType")
                .Select(e => (string)e.Attribute("DisplayName")!)
                .ToList();

            Assert.Contains("Remote Windows Security", deviceItems);
            Assert.Contains("Remote Event Viewer", deviceItems);
            Assert.Contains("Remote Registry Editor", deviceItems);
            Assert.Contains("Remote Software Center", deviceItems);
            Assert.Contains("System Information", deviceItems);

            var collectionXml = XDocument.Load(Path.Combine(root, "console-extensions", "XmlStorage", "Extensions", "Actions", "OpenRCT_CollectionActions.xml"));
            var collectionItems = collectionXml.Descendants("ActionDescription")
                .Where(e => (string?)e.Attribute("Class") == "AssemblyType")
                .Select(e => (string)e.Attribute("DisplayName")!)
                .ToList();

            Assert.Contains("System Information", collectionItems);
            Assert.Contains("Remote Software Center", collectionItems);
            Assert.Contains("Remote Windows Security", collectionItems);
            Assert.Contains("Client Information", collectionItems);
        }

        [Fact]
        public void AllStarWidthDataGridColumns_HaveMinWidth_AndThemesEnableAutoScrollbars()
        {
            var root = FindRepoRoot();
            var uiDir = Path.Combine(root, "src", "OpenRCT.UI");

            foreach (var theme in new[] { "LightTheme.xaml", "DarkTheme.xaml" })
            {
                var content = File.ReadAllText(Path.Combine(uiDir, "Themes", theme));
                Assert.Contains("HorizontalScrollBarVisibility", content);
                Assert.Contains("CanUserResizeColumns", content);
            }

            foreach (var file in Directory.GetFiles(uiDir, "*.xaml", SearchOption.AllDirectories))
            {
                var doc = XDocument.Load(file);
                var starColsWithoutMin = doc.Descendants()
                    .Where(e => e.Name.LocalName == "DataGridTextColumn" &&
                                string.Equals((string?)e.Attribute("Width"), "*", StringComparison.OrdinalIgnoreCase) &&
                                e.Attribute("MinWidth") == null)
                    .Select(e => (string?)e.Attribute("Header") ?? "(unnamed)")
                    .ToList();

                Assert.True(starColsWithoutMin.Count == 0,
                    $"{Path.GetFileName(file)} has star-width DataGridTextColumn(s) without MinWidth: {string.Join(", ", starColsWithoutMin)}");
            }
        }

        [Fact]
        public void IconStylePreference_DefaultsToFluentDuotone_AndRoundTrips()
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"openrct_iconstyle_{Guid.NewGuid():N}.json");
            try
            {
                var svc = new OpenRCT.Core.Settings.SettingsService(tempFile);
                var def = svc.Load();
                Assert.Equal(OpenRCT.Core.Settings.IconStylePreference.FluentDuotone, def.IconStyle);

                def.IconStyle = OpenRCT.Core.Settings.IconStylePreference.CrispOutline;
                svc.Save(def);

                var reloaded = svc.Load();
                Assert.Equal(OpenRCT.Core.Settings.IconStylePreference.CrispOutline, reloaded.IconStyle);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void AllMecmXmlIconReferences_ExistAsIcoFiles_AndLoadInNet48IconsDll()
        {
            var root = FindRepoRoot();
            Assert.True(File.Exists(Path.Combine(root, "src", "OpenRCT.App", "Assets", "OpenRCT.ico")),
                "Primary OpenRCT.ico must exist under src/OpenRCT.App/Assets.");

            var iconsDir = Path.Combine(root, "src", "OpenRCT.Icons", "Icons");
            Assert.True(Directory.Exists(iconsDir));

            var xmlPaths = new[]
            {
                Path.Combine(root, "console-extensions", "XmlStorage", "Extensions", "Nodes", "OpenRCT_Nodes.xml"),
                Path.Combine(root, "console-extensions", "XmlStorage", "Extensions", "Actions", "OpenRCT_DeviceActions.xml"),
                Path.Combine(root, "console-extensions", "XmlStorage", "Extensions", "Actions", "OpenRCT_CollectionActions.xml")
            };

            var referencedNames = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in xmlPaths)
            {
                var doc = XDocument.Load(p);
                var imgs = doc.Descendants("ImageResourceName").Select(e => e.Value.Trim()).Where(v => !string.IsNullOrEmpty(v)).ToList();
                Assert.True(imgs.Count > 0, $"{Path.GetFileName(p)} should declare ImagesDescription icons.");
                foreach (var name in imgs)
                {
                    referencedNames.Add(name);
                    var icoPath = Path.Combine(iconsDir, name + ".ico");
                    Assert.True(File.Exists(icoPath), $"Referenced MECM icon '{name}' missing at {icoPath}");
                }
            }

            Assert.Contains("OpenRctLogo", referencedNames);
            Assert.Contains("DashBitLocker", referencedNames);
            Assert.Contains("ToolWindowsSecurity", referencedNames);

#if NET48
            var builtIconsDll = Path.Combine(root, "src", "OpenRCT.Icons", "bin", "Release", "net48", "OpenRCT.Icons.dll");
            if (File.Exists(builtIconsDll))
            {
                var asm = System.Reflection.Assembly.LoadFrom(builtIconsDll);
                var rm = new System.Resources.ResourceManager("OpenRCT.Icons.ConsoleIcons", asm);
                foreach (var name in referencedNames)
                {
                    var obj = rm.GetObject(name);
                    Assert.NotNull(obj);
                }
            }
#endif
        }
    }
}
