using System;
using System.Collections.Generic;
using System.Linq;
using OpenCMTools.Core.Analytics;
using OpenCMTools.Core.Models;
using Xunit;

namespace OpenCMTools.Tests
{
    public sealed class DashboardAnalyticsTests
    {
        [Fact]
        public void AdCleanupReconciler_ClassifiesConfigMgrBothAndAdOnlyCorrectly()
        {
            var ad = new[]
            {
                new AdComputerRecord { Name = "WKSTN-01", DistinguishedName = "CN=WKSTN-01,DC=corp,DC=local", OperatingSystem = "Windows 11" },
                new AdComputerRecord { Name = "STALE-AD", DistinguishedName = "CN=STALE-AD,DC=corp,DC=local", OperatingSystem = "Windows 10" }
            };
            var sccm = new[]
            {
                new ConfigMgrDeviceRecord { ResourceId = "1001", Name = "wkstn-01.corp.local", ClientInstalled = true, OperatingSystem = "Windows 11" },
                new ConfigMgrDeviceRecord { ResourceId = "1002", Name = "ORPHAN-CM", ClientInstalled = false, OperatingSystem = "Windows 10" }
            };

            var result = AdCleanupReconciler.Reconcile(ad, sccm);

            Assert.Equal(3, result.Rows.Count);
            Assert.Equal(1, result.ChartSlices.Single(s => s.Label == "ConfigMgr").Value);
            Assert.Equal(1, result.ChartSlices.Single(s => s.Label == "Both").Value);
            Assert.Equal(1, result.ChartSlices.Single(s => s.Label == "Active Directory").Value);
            Assert.Equal(DevicePresenceBucket.Both, result.Rows.Single(r => r.Name == "WKSTN-01").Bucket);
            Assert.Equal("1001", result.Rows.Single(r => r.Name == "WKSTN-01").SccmResourceId);
        }

        [Fact]
        public void LapsComplianceAnalyzer_HandlesLegacyAndWindowsLapsAndClientStates()
        {
            var ad = new[]
            {
                new AdComputerRecord { Name = "WIN11-LAPS", HasWindowsLapsPassword = true, WindowsLapsExpiration = DateTime.UtcNow.AddDays(20) },
                new AdComputerRecord { Name = "LEGACY-X64", HasLegacyLapsPassword = true, LegacyLapsExpiration = DateTime.UtcNow.AddDays(10) },
                new AdComputerRecord { Name = "LEGACY-X86", HasLegacyLapsPassword = false },
                new AdComputerRecord { Name = "NO-LAPS", HasLegacyLapsPassword = false }
            };
            var cm = new[]
            {
                new ConfigMgrDeviceRecord { Name = "LEGACY-X64", LapsClientArchitecture = "x64" },
                new ConfigMgrDeviceRecord { Name = "LEGACY-X86", LapsClientArchitecture = "x86" }
            };

            var result = LapsComplianceAnalyzer.Analyze(ad, cm);

            Assert.Equal(4, result.Rows.Count);
            Assert.Equal(2, result.PasswordInAdSlices.Single(s => s.Key == "WithPassword").Value);
            Assert.Equal(2, result.PasswordInAdSlices.Single(s => s.Key == "WithoutPassword").Value);
            Assert.Equal(1, result.ClientInstallStateSlices.Single(s => s.Key == nameof(LapsClientInstallState.WindowsBuiltIn)).Value);
            Assert.Equal(1, result.ClientInstallStateSlices.Single(s => s.Key == nameof(LapsClientInstallState.X64Installed)).Value);
            Assert.Equal(1, result.ClientInstallStateSlices.Single(s => s.Key == nameof(LapsClientInstallState.X86Installed)).Value);
            Assert.Equal(1, result.ClientInstallStateSlices.Single(s => s.Key == nameof(LapsClientInstallState.NotInstalled)).Value);
        }

        [Fact]
        public void SoftwareUpdatesAnalyzer_AppliesAgeDeployedAndClassificationFilters()
        {
            var now = new DateTime(2026, 11, 24, 0, 0, 0, DateTimeKind.Utc);
            var devices = new[]
            {
                new TargetDevice { ResourceId = "1", Name = "PC-COMPLIANT" },
                new TargetDevice { ResourceId = "2", Name = "PC-MISSING" },
                new TargetDevice { ResourceId = "3", Name = "PC-UNKNOWN" }
            };

            var raw = new[]
            {
                new RawDeviceUpdateRecord { ResourceId = "1", DeviceName = "PC-COMPLIANT", Classification = "Security Updates", IsDeployed = true, DatePosted = now.AddDays(-40), StatusCode = 3 },
                new RawDeviceUpdateRecord { ResourceId = "2", DeviceName = "PC-MISSING", Classification = "Security Updates", IsDeployed = true, DatePosted = now.AddDays(-40), StatusCode = 2 },
                new RawDeviceUpdateRecord { ResourceId = "2", DeviceName = "PC-MISSING", Classification = "Critical Updates", IsDeployed = false, DatePosted = now.AddDays(-5), StatusCode = 2 }
            };

            var filter = new SoftwareUpdatesFilter
            {
                LimitToDeployedUpdates = true,
                UpdatesOlderThanDays = 30
            };

            var result = SoftwareUpdatesAnalyzer.Analyze(devices, raw, filter, nowUtc: now);

            Assert.Equal(1, result.ComplianceSlices.Single(s => s.Label == "Compliant").Value);
            Assert.Equal(1, result.ComplianceSlices.Single(s => s.Label == "NonCompliant").Value);
            Assert.Equal(1, result.ComplianceSlices.Single(s => s.Label == "Unknown").Value);
            Assert.Equal(1, result.MissingByCategorySlices.Single(s => s.Label == "Security").Value);
            Assert.Equal(0, result.MissingByCategorySlices.Single(s => s.Label == "Critical").Value);
        }
    }
}
