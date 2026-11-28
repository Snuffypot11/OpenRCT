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
                "Device and Power Tools",
                "Security Tools",
                "Active Directory and ConfigMgr"
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

            // MECM's console menu adapter naively calls Replace('&', '_') before passing labels to WPF AccessText:
            // single '&' disappears ("Device  Power Tools") and '&&' becomes literal '_' ("Device _ Power Tools").
            // Enforce plain "and" (matching MECM's own "Assets and Compliance") across all console extension XMLs.
            var actionsDir = Path.Combine(root, "console-extensions", "XmlStorage", "Extensions", "Actions");
            foreach (var xmlPath in Directory.GetFiles(actionsDir, "*.xml"))
            {
                var doc = XDocument.Load(xmlPath);
                foreach (var elem in doc.Descendants("ActionDescription"))
                {
                    foreach (var attrName in new[] { "DisplayName", "MnemonicDisplayName" })
                    {
                        var val = (string?)elem.Attribute(attrName);
                        if (!string.IsNullOrEmpty(val))
                        {
                            Assert.DoesNotContain("&", val);
                        }
                    }
                }
            }
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

            var duotoneDir = Path.Combine(root, "src", "OpenRCT.Icons", "Icons");
            var outlineDir = Path.Combine(root, "src", "OpenRCT.Icons", "IconsOutline");
            Assert.True(Directory.Exists(duotoneDir), "Duotone Icons directory must exist.");
            Assert.True(Directory.Exists(outlineDir), "Outline IconsOutline directory must exist.");

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
                    var duotoneIco = Path.Combine(duotoneDir, name + ".ico");
                    var outlineIco = Path.Combine(outlineDir, name + ".ico");
                    Assert.True(File.Exists(duotoneIco), $"Referenced MECM Duotone icon '{name}' missing at {duotoneIco}");
                    Assert.True(File.Exists(outlineIco), $"Referenced MECM Outline icon '{name}' missing at {outlineIco}");
                }
            }

            Assert.Contains("OpenRctLogo", referencedNames);
            Assert.Contains("DashBitLocker", referencedNames);
            Assert.Contains("ToolWindowsSecurity", referencedNames);

            // Ensure Duotone and Outline sets are visually distinct (non-logo icons differ in bytes)
            var duotoneBitLocker = File.ReadAllBytes(Path.Combine(duotoneDir, "DashBitLocker.ico"));
            var outlineBitLocker = File.ReadAllBytes(Path.Combine(outlineDir, "DashBitLocker.ico"));
            Assert.False(duotoneBitLocker.SequenceEqual(outlineBitLocker),
                "Duotone and Outline DashBitLocker.ico files must differ.");

#if NET48
            var duotoneDll = Path.Combine(root, "src", "OpenRCT.Icons", "bin", "Release", "net48", "OpenRCT.Icons.Duotone.dll");
            var outlineDll = Path.Combine(root, "src", "OpenRCT.Icons", "bin", "Release", "net48", "OpenRCT.Icons.Outline.dll");
            if (File.Exists(duotoneDll) && File.Exists(outlineDll))
            {
                foreach (var dllPath in new[] { duotoneDll, outlineDll })
                {
                    var asm = System.Reflection.Assembly.LoadFrom(dllPath);
                    var rm = new System.Resources.ResourceManager("OpenRCT.Icons.ConsoleIcons", asm);
                    foreach (var name in referencedNames)
                    {
                        var obj = rm.GetObject(name);
                        Assert.NotNull(obj);
                    }
                }

                Assert.False(File.ReadAllBytes(duotoneDll).SequenceEqual(File.ReadAllBytes(outlineDll)),
                    "Built OpenRCT.Icons.Duotone.dll and OpenRCT.Icons.Outline.dll must differ in content.");
            }
#endif
        }

        [Fact]
        public void InstallerScripts_ArePure7BitAscii()
        {
            var root = FindRepoRoot();
            var scriptsDir = Path.Combine(root, "installer", "scripts");
            foreach (var ps1 in Directory.GetFiles(scriptsDir, "*.ps1", SearchOption.AllDirectories))
            {
                var bytes = File.ReadAllBytes(ps1);
                var nonAsciiIndex = Array.FindIndex(bytes, b => b > 127);
                Assert.True(nonAsciiIndex < 0,
                    $"{Path.GetFileName(ps1)} contains non-ASCII byte 0x{(nonAsciiIndex >= 0 ? bytes[nonAsciiIndex] : 0):X2} at offset {nonAsciiIndex}.");
            }
        }

        [Fact]
        public void ThemeManager_NeverClearsHostApplicationMergedDictionaries()
        {
            var root = FindRepoRoot();
            var themeMgrText = File.ReadAllText(Path.Combine(root, "src", "OpenRCT.UI", "Themes", "ThemeManager.cs"));
            Assert.DoesNotContain("Application.Current.Resources.MergedDictionaries.Clear()", themeMgrText);
            Assert.Contains("OpenRCT.App.App", themeMgrText);
        }

        [Fact]
        public void FastOfflineCheckSettings_DefaultEnabled_AndNormalizeTimeoutBounds()
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"openrct_offline_{Guid.NewGuid():N}.json");
            try
            {
                var svc = new OpenRCT.Core.Settings.SettingsService(tempFile);
                var def = svc.Load();
                Assert.True(def.EnableFastOfflineCheck);
                Assert.Equal(1500, def.OfflineCheckTimeoutMs);

                def.EnableFastOfflineCheck = false;
                def.OfflineCheckTimeoutMs = 50; // below min 300
                svc.Save(def);

                var reloaded = svc.Load();
                Assert.False(reloaded.EnableFastOfflineCheck);
                Assert.Equal(300, reloaded.OfflineCheckTimeoutMs);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void SoftwareUpdatesQuery_ScopesComplianceStatusServerSide()
        {
            var root = FindRepoRoot();
            var smsClientText = File.ReadAllText(Path.Combine(root, "src", "OpenRCT.Connectors", "SmsProvider", "SmsProviderClient.cs"));
            Assert.DoesNotContain("\"SELECT MachineID, CI_ID, Status FROM SMS_UpdateComplianceStatus\"", smsClientText);
            Assert.Contains("WHERE (Status = 2 OR Status = 3) AND MachineID IN", smsClientText);
        }

        [Fact]
        public void LegacySettingsJson_DeserializesNewFastOfflineDefaultsProperly()
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"openrct_legacy_{Guid.NewGuid():N}.json");
            try
            {
                // Simulate an existing v0.2.5 settings.json that does not have EnableFastOfflineCheck or OfflineCheckTimeoutMs
                File.WriteAllText(tempFile, "{\n  \"ConnectionTimeoutSeconds\": 20,\n  \"MaxConcurrentDevices\": 12,\n  \"IconStyle\": 0\n}");
                var svc = new OpenRCT.Core.Settings.SettingsService(tempFile);
                var loaded = svc.Load();
                Assert.True(loaded.EnableFastOfflineCheck);
                Assert.Equal(1500, loaded.OfflineCheckTimeoutMs);
                Assert.True(loaded.EnableUpdateChecks);
                Assert.True(loaded.ConfirmDestructiveActions);
                Assert.Equal(20, loaded.ConnectionTimeoutSeconds);
                Assert.Equal(12, loaded.MaxConcurrentDevices);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void FirewallProfilesQuery_UsesActiveStorePolicyContext()
        {
            var root = FindRepoRoot();
            var wmiClientText = File.ReadAllText(Path.Combine(root, "src", "OpenRCT.Connectors", "Wmi", "WmiClient.cs"));
            Assert.Contains("ManagementNamedValueCollection", wmiClientText);
            Assert.Contains("enumOptions.Context", wmiClientText);

            var securityServiceText = File.ReadAllText(Path.Combine(root, "src", "OpenRCT.Connectors", "Services", "RemoteWindowsSecurityService.cs"));
            var profilesStart = securityServiceText.IndexOf("GetFirewallProfilesAsync", StringComparison.Ordinal);
            var rulesStart = securityServiceText.IndexOf("GetFirewallRulesAsync", StringComparison.Ordinal);
            var setRuleStart = securityServiceText.IndexOf("SetFirewallRuleEnabledAsync", StringComparison.Ordinal);

            Assert.True(profilesStart > 0 && rulesStart > profilesStart && setRuleStart > rulesStart);

            var profilesMethod = securityServiceText.Substring(profilesStart, rulesStart - profilesStart);
            Assert.Contains("MSFT_NetFirewallProfile", profilesMethod);
            Assert.Contains("[\"PolicyStore\"] = \"ActiveStore\"", profilesMethod);

            var rulesMethod = securityServiceText.Substring(rulesStart, setRuleStart - rulesStart);
            Assert.Contains("MSFT_NetFirewallRule", rulesMethod);
            Assert.DoesNotContain("ActiveStore", rulesMethod);
        }
    }
}
