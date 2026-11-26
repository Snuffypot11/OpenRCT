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
    }
}
