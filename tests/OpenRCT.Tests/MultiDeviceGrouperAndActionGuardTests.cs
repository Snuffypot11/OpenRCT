using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using OpenRCT.Core.Analytics;
using OpenRCT.Core.Models;
using Xunit;

namespace OpenRCT.Tests
{
    public sealed class MultiDeviceGrouperAndActionGuardTests
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
            throw new DirectoryNotFoundException("Could not locate OpenRCT.sln above test output directory.");
        }

        [Fact]
        public void GroupSoftwareCenterItems_Applications_AggregatesInstalledAndFailedCounts()
        {
            var items = new[]
            {
                new RemoteSoftwareCenterItem
                {
                    DeviceName = "PC-02",
                    ItemType = SoftwareCenterItemType.Application,
                    Name = "7-Zip",
                    Publisher = "Igor Pavlov",
                    SoftwareVersion = "24.08",
                    InstallState = "Installed",
                    EvaluationState = "Installed"
                },
                new RemoteSoftwareCenterItem
                {
                    DeviceName = "PC-01",
                    ItemType = SoftwareCenterItemType.Application,
                    Name = "7-Zip",
                    Publisher = "Igor Pavlov",
                    SoftwareVersion = "24.08",
                    InstallState = "NotInstalled",
                    EvaluationState = "Enforcement failed",
                    ErrorCode = 0x80070005
                },
                new RemoteSoftwareCenterItem
                {
                    DeviceName = "PC-03",
                    ItemType = SoftwareCenterItemType.Application,
                    Name = "7-Zip",
                    Publisher = "Igor Pavlov",
                    SoftwareVersion = "24.08",
                    InstallState = "NotInstalled",
                    EvaluationState = "Available"
                },
                new RemoteSoftwareCenterItem
                {
                    DeviceName = "PC-01",
                    ItemType = SoftwareCenterItemType.ComplianceBaseline,
                    Name = "BitLocker Baseline",
                    SoftwareVersion = "1",
                    ComplianceState = "Compliant"
                }
            };

            var groups = MultiDeviceGrouper.GroupSoftwareCenterItems(items, SoftwareCenterItemType.Application);
            Assert.Single(groups);
            var g = groups[0];
            Assert.Equal("7-Zip", g.Name);
            Assert.Equal("Igor Pavlov", g.SecondaryInfo);
            Assert.Equal("24.08", g.Version);
            Assert.Equal(3, g.DeviceCount);
            Assert.Equal("1 / 3 installed (1 failed)", g.StatusSummary);
            Assert.Equal(new[] { "PC-01", "PC-02", "PC-03" }, g.Children.Select(c => c.DeviceName).ToArray());
            Assert.Equal("0x80070005", g.Children[0].ErrorCode);
        }

        [Fact]
        public void GroupSoftwareCenterItems_ComplianceBaselines_ComputesComplianceRatio()
        {
            var items = new[]
            {
                new RemoteSoftwareCenterItem { DeviceName = "PC-01", ItemType = SoftwareCenterItemType.ComplianceBaseline, Name = "Security Baseline", SoftwareVersion = "3", ComplianceState = "Compliant" },
                new RemoteSoftwareCenterItem { DeviceName = "PC-02", ItemType = SoftwareCenterItemType.ComplianceBaseline, Name = "Security Baseline", SoftwareVersion = "3", ComplianceState = "Compliant" },
                new RemoteSoftwareCenterItem { DeviceName = "PC-03", ItemType = SoftwareCenterItemType.ComplianceBaseline, Name = "Security Baseline", SoftwareVersion = "3", ComplianceState = "Non-Compliant" }
            };

            var groups = MultiDeviceGrouper.GroupSoftwareCenterItems(items, SoftwareCenterItemType.ComplianceBaseline);
            Assert.Single(groups);
            Assert.Equal("2 / 3 compliant", groups[0].StatusSummary);
            Assert.Equal(3, groups[0].DeviceCount);
        }

        [Fact]
        public void GroupWindowsServices_ComputesStateBreakdownAndMixedStartModes()
        {
            var services = new[]
            {
                new WindowsServiceInfo { DeviceName = "SRV-01", Name = "wuauserv", DisplayName = "Windows Update", State = "Running", StartMode = "Manual" },
                new WindowsServiceInfo { DeviceName = "SRV-02", Name = "wuauserv", DisplayName = "Windows Update", State = "Running", StartMode = "Auto" },
                new WindowsServiceInfo { DeviceName = "SRV-03", Name = "wuauserv", DisplayName = "Windows Update", State = "Stopped", StartMode = "Manual" }
            };

            var groups = MultiDeviceGrouper.GroupWindowsServices(services);
            Assert.Single(groups);
            Assert.Equal("Windows Update", groups[0].Name);
            Assert.Equal("wuauserv", groups[0].SecondaryInfo);
            Assert.Equal("Mixed", groups[0].Version);
            Assert.Equal("Running: 2, Stopped: 1", groups[0].StatusSummary);
            Assert.Equal(3, groups[0].DeviceCount);
        }

        [Fact]
        public void GroupClientCacheItems_AggregatesTotalSizeAndDeviceCount()
        {
            var cacheItems = new[]
            {
                new ClientCacheItem { DeviceName = "PC-01", ContentId = "Content_ABC", ContentVersion = "2", SizeMb = 120.5, CacheId = "1", Location = @"C:\Windows\ccmcache\a" },
                new ClientCacheItem { DeviceName = "PC-02", ContentId = "Content_ABC", ContentVersion = "2", SizeMb = 120.5, CacheId = "4", Location = @"C:\Windows\ccmcache\b" }
            };

            var groups = MultiDeviceGrouper.GroupClientCacheItems(cacheItems);
            Assert.Single(groups);
            Assert.Equal("Content_ABC", groups[0].Name);
            Assert.Equal("v2", groups[0].SecondaryInfo);
            Assert.Equal("241 MB total", groups[0].Version);
            Assert.Equal("Cached on 2 device(s)", groups[0].StatusSummary);
            Assert.Equal(2, groups[0].DeviceCount);
        }

        [Fact]
        public void AllClientActionLiteralsInSourceAndXml_ExistInClientActionRegistry()
        {
            var root = FindRepoRoot();
            var regex = new Regex(@"ClientAction\.[A-Za-z0-9_]+", RegexOptions.Compiled);
            var invalid = new List<string>();

            var searchDirs = new[]
            {
                Path.Combine(root, "src"),
                Path.Combine(root, "console-extensions")
            };

            foreach (var dir in searchDirs)
            {
                if (!Directory.Exists(dir)) continue;
                var files = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories)
                    .Where(f => (f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) &&
                                f.IndexOf($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) < 0 &&
                                f.IndexOf($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) < 0);

                foreach (var file in files)
                {
                    var text = File.ReadAllText(file);
                    foreach (Match match in regex.Matches(text))
                    {
                        var key = match.Value;
                        if (!ClientActionRegistry.TryGet(key, out _))
                        {
                            invalid.Add($"{Path.GetRelativePath(root, file)}: unknown action '{key}'");
                        }
                    }
                }
            }

            Assert.True(invalid.Count == 0, "Invalid ClientAction keys found:\n" + string.Join("\n", invalid));
        }
    }
}
