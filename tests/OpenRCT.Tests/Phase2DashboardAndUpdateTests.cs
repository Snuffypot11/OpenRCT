using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenRCT.Core.Analytics;
using OpenRCT.Core.Models;
using OpenRCT.Core.Settings;
using OpenRCT.Core.Updates;
using Xunit;

namespace OpenRCT.Tests
{
    public sealed class Phase2DashboardAndUpdateTests
    {
        [Fact]
        public void BitLockerComplianceAnalyzer_ReconcilesAdAndConfigMgrKeyStorageAndCompliance()
        {
            var adComputers = new[]
            {
                new AdComputerRecord { Name = "PC-01", DistinguishedName = "CN=PC-01,OU=Workstations,DC=lab,DC=local", OperatingSystem = "Windows 11 Enterprise" },
                new AdComputerRecord { Name = "PC-02", DistinguishedName = "CN=PC-02,OU=Workstations,DC=lab,DC=local", OperatingSystem = "Windows 11 Enterprise" },
                new AdComputerRecord { Name = "PC-03", DistinguishedName = "CN=PC-03,OU=Workstations,DC=lab,DC=local", OperatingSystem = "Windows 10 Enterprise" },
                new AdComputerRecord { Name = "PC-04", DistinguishedName = "CN=PC-04,OU=Workstations,DC=lab,DC=local", OperatingSystem = "Windows 11 Enterprise" }
            };

            var adKeySet = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "CN=PC-01,OU=Workstations,DC=lab,DC=local",
                "PC-02"
            };

            var cmRecords = new[]
            {
                new ConfigMgrBitLockerRecord { Name = "PC-01.lab.local", HasConfigMgrRecoveryKey = true, CmBitLockerCompliance = "Compliant", EncryptionMethod = "XTS_AES_256", ProtectionStatus = "Protection On" },
                new ConfigMgrBitLockerRecord { Name = "PC-02", HasConfigMgrRecoveryKey = false, CmBitLockerCompliance = "Unknown", EncryptionMethod = "XTS_AES_256", ProtectionStatus = "Protection On" },
                new ConfigMgrBitLockerRecord { Name = "PC-03", HasConfigMgrRecoveryKey = true, CmBitLockerCompliance = "Non-Compliant", EncryptionMethod = "NotEncrypted", ProtectionStatus = "Protection Off" }
            };

            var res = BitLockerComplianceAnalyzer.Analyze(adComputers, adKeySet, cmRecords, primarySourceIsCollection: false);
            Assert.Equal(4, res.Rows.Count);

            var r1 = res.Rows.Single(r => r.Name == "PC-01");
            Assert.Equal(BitLockerKeyStorageBucket.MultipleLocations, r1.KeyStorageBucket);
            Assert.Equal("AD, CM", r1.KeyStorage);
            Assert.Equal(BitLockerUnifiedComplianceState.CmCompliant, r1.UnifiedComplianceState);

            var r2 = res.Rows.Single(r => r.Name == "PC-02");
            Assert.Equal(BitLockerKeyStorageBucket.ActiveDirectory, r2.KeyStorageBucket);
            Assert.Equal(BitLockerUnifiedComplianceState.EncryptedUnmanaged, r2.UnifiedComplianceState);

            var r3 = res.Rows.Single(r => r.Name == "PC-03");
            Assert.Equal(BitLockerKeyStorageBucket.ConfigMgr, r3.KeyStorageBucket);
            Assert.Equal(BitLockerUnifiedComplianceState.NonCompliant, r3.UnifiedComplianceState);

            var r4 = res.Rows.Single(r => r.Name == "PC-04");
            Assert.Equal(BitLockerKeyStorageBucket.None, r4.KeyStorageBucket);
            Assert.Equal(BitLockerUnifiedComplianceState.Unknown, r4.UnifiedComplianceState);

            Assert.Equal(4, res.KeyStorageSlices.Sum(s => s.Value));
            Assert.Equal(4, res.UnifiedComplianceSlices.Sum(s => s.Value));
        }

        [Fact]
        public void ContentDistributionAnalyzer_AggregatesDpCountsAndFiltersAccurately()
        {
            var dps = new[]
            {
                new DistributionPointSummary { ServerName = "DP01.lab.local", NalPath = "[\"Display=\\\\DP01.lab.local\\\"]MSWNET:[\"SMS_SITE=LAB\"]\\\\DP01.lab.local\\", PxeEnabled = true },
                new DistributionPointSummary { ServerName = "DP02.lab.local", NalPath = "[\"Display=\\\\DP02.lab.local\\\"]MSWNET:[\"SMS_SITE=LAB\"]\\\\DP02.lab.local\\" }
            };

            var items = new[]
            {
                new ContentDistributionItem { PackageId = "LAB00001", ContentName = "7-Zip", DistributionPoint = "DP01.lab.local", State = ContentDistributionAnalyzer.MapMessageState(1) },
                new ContentDistributionItem { PackageId = "LAB00002", ContentName = "BrokenApp", DistributionPoint = "DP01.lab.local", State = ContentDistributionAnalyzer.MapMessageState(4) },
                new ContentDistributionItem { PackageId = "LAB00003", ContentName = "OS Image", DistributionPoint = "DP02.lab.local", State = ContentDistributionAnalyzer.MapMessageState(2) }
            };

            var snap = ContentDistributionAnalyzer.Aggregate(dps, items);
            Assert.Equal(2, snap.DistributionPoints.Count);
            Assert.Equal("DP01.lab.local", snap.DistributionPoints[0].ServerName); // ordered by ErrorCount desc
            Assert.Equal(1, snap.DistributionPoints[0].SuccessCount);
            Assert.Equal(1, snap.DistributionPoints[0].ErrorCount);
            Assert.Equal(1, snap.DistributionPoints[1].InProgressCount);

            var onlyErrors = ContentDistributionAnalyzer.FilterItems(snap.Items, "DP01.lab.local", "Error", "broken");
            Assert.Single(onlyErrors);
            Assert.Equal("LAB00002", onlyErrors[0].PackageId);

            Assert.Equal("DP01.lab.local", ContentDistributionAnalyzer.ExtractServerNameFromNalPath("[\"Display=\\\\DP01.lab.local\\\"]MSWNET:[\"SMS_SITE=LAB\"]\\\\DP01.lab.local\\"));
            Assert.Equal("Application", ContentDistributionAnalyzer.MapContentType(31));
        }

        [Fact]
        public void HardwareFirmwareAnalyzer_MapsChassisAndSupportsNestedFiltering()
        {
            Assert.Equal("VirtualMachine", HardwareFirmwareAnalyzer.MapChassisType(new[] { 3 }, "Microsoft Corporation", "Virtual Machine"));
            Assert.Equal("Laptop", HardwareFirmwareAnalyzer.MapChassisType(new[] { 10 }, "Dell Inc.", "Latitude 7440"));
            Assert.Equal("Desktop", HardwareFirmwareAnalyzer.MapChassisType(new[] { 3 }, "Lenovo", "ThinkCentre M90"));
            Assert.Equal("Dell", HardwareFirmwareAnalyzer.NormalizeManufacturer("Dell Inc."));

            var rows = new[]
            {
                new HardwareFirmwareAuditRow { Name = "VM1", Manufacturer = "Microsoft", Model = "Virtual Machine", ChassisType = "VirtualMachine", UefiStatus = "Enabled", SecureBootStatus = "Enabled" },
                new HardwareFirmwareAuditRow { Name = "VM2", Manufacturer = "Microsoft", Model = "Virtual Machine", ChassisType = "VirtualMachine", UefiStatus = "Enabled", SecureBootStatus = "Disabled" },
                new HardwareFirmwareAuditRow { Name = "LT1", Manufacturer = "Dell", Model = "Latitude 7440", ChassisType = "Laptop", UefiStatus = "Enabled", SecureBootStatus = "Enabled" }
            };

            var chassisSlices = HardwareFirmwareAnalyzer.BuildSlices(rows, HardwareAuditDimension.Chassis);
            Assert.Equal(2, chassisSlices.Count);
            Assert.Equal("VirtualMachine", chassisSlices[0].Key);
            Assert.Equal(2, chassisSlices[0].Value);

            var nested = HardwareFirmwareAnalyzer.ApplyFilters(rows, new Dictionary<HardwareAuditDimension, string>
            {
                [HardwareAuditDimension.Chassis] = "VirtualMachine",
                [HardwareAuditDimension.SecureBootStatus] = "Enabled"
            });
            Assert.Single(nested);
            Assert.Equal("VM1", nested[0].Name);
        }

        [Fact]
        public async System.Threading.Tasks.Task GitHubUpdateChecker_ParsesReposSemVerAndReleaseJson_WithoutHardcodedRepo()
        {
            Assert.True(GitHubUpdateChecker.TryNormalizeRepository("owner/OpenRCT", out var r1));
            Assert.Equal("owner/OpenRCT", r1);

            Assert.True(GitHubUpdateChecker.TryNormalizeRepository("https://github.com/fork-org/OpenRCT/releases", out var r2));
            Assert.Equal("fork-org/OpenRCT", r2);

            Assert.False(GitHubUpdateChecker.TryNormalizeRepository("https://example.com/not-github/repo", out _));
            Assert.False(GitHubUpdateChecker.TryNormalizeRepository("not-a-repo", out _));

            Assert.True(GitHubUpdateChecker.IsNewerVersion("0.2.0", "v0.2.1"));
            Assert.True(GitHubUpdateChecker.IsNewerVersion("0.2.0", "1.0.0"));
            Assert.False(GitHubUpdateChecker.IsNewerVersion("0.2.0", "v0.2.0"));
            Assert.False(GitHubUpdateChecker.IsNewerVersion("0.2.1", "v0.2.0"));

            const string sampleJson = "{\"tag_name\":\"v0.3.0\",\"name\":\"OpenRCT v0.3.0\",\"body\":\"New features\",\"html_url\":\"https://github.com/example/openrct/releases/tag/v0.3.0\",\"published_at\":\"2026-12-01T10:00:00Z\"}";
            var parsed = GitHubUpdateChecker.ParseReleaseResponseJson(sampleJson, "example/openrct", "0.2.0");
            Assert.True(parsed.IsSuccess);
            Assert.True(parsed.IsUpdateAvailable);
            Assert.Equal("v0.3.0", parsed.LatestVersion);
            Assert.Equal("https://github.com/example/openrct/releases/tag/v0.3.0", parsed.ReleaseUrl);

            // Ensure disabled setting returns immediately without attempting any HTTP request
            var disabledResult = await GitHubUpdateChecker.CheckForUpdatesAsync(new OpenRctSettings { EnableUpdateChecks = false });
            Assert.False(disabledResult.IsConfigured);
            Assert.Contains("disabled", disabledResult.Message, StringComparison.OrdinalIgnoreCase);

            // Verify source file has no personal GitHub repo hardcoded
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "OpenRCT.sln"))) dir = dir.Parent;
            Assert.NotNull(dir);
            var checkerSource = File.ReadAllText(Path.Combine(dir!.FullName, "src", "OpenRCT.Core", "Updates", "GitHubUpdateChecker.cs"));
            Assert.DoesNotContain("Snuffypot11", checkerSource, StringComparison.OrdinalIgnoreCase);
        }
    }
}
