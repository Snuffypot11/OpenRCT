using System;
using System.Collections.Generic;
using System.Linq;
using OpenCMTools.Core.Models;

namespace OpenCMTools.Core.Analytics
{
    /// <summary>
    /// Computes Software Updates Deployment Status compliance pie charts and per-device rows
    /// based on deployed flag, Software Update Groups, age threshold, and update classifications.
    /// </summary>
    public static class SoftwareUpdatesAnalyzer
    {
        public static SoftwareUpdatesAnalysisResult Analyze(
            IEnumerable<TargetDevice> collectionDevices,
            IEnumerable<RawDeviceUpdateRecord> rawUpdateRecords,
            SoftwareUpdatesFilter filter,
            DateTime? nowUtc = null,
            IEnumerable<DeviceErrorEntry>? errors = null)
        {
            var referenceNow = nowUtc ?? DateTime.UtcNow;
            var cutoffDate = filter.UpdatesOlderThanDays > 0
                ? referenceNow.AddDays(-filter.UpdatesOlderThanDays)
                : (DateTime?)null;

            var filteredRecords = (rawUpdateRecords ?? Enumerable.Empty<RawDeviceUpdateRecord>())
                .Where(r => MatchesFilter(r, filter, cutoffDate))
                .ToList();

            var recordsByDevice = filteredRecords
                .GroupBy(r => string.IsNullOrWhiteSpace(r.ResourceId) ? r.DeviceName : r.ResourceId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var devices = (collectionDevices ?? Enumerable.Empty<TargetDevice>()).ToList();
            if (devices.Count == 0 && filteredRecords.Count > 0)
            {
                devices = filteredRecords
                    .GroupBy(r => string.IsNullOrWhiteSpace(r.ResourceId) ? r.DeviceName : r.ResourceId, StringComparer.OrdinalIgnoreCase)
                    .Select(g => new TargetDevice { ResourceId = g.First().ResourceId, Name = g.First().DeviceName })
                    .ToList();
            }

            var deviceRows = new List<DeviceUpdateComplianceRow>(devices.Count);
            int compliant = 0, nonCompliant = 0, unknown = 0;
            var missingByCategory = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Update Rollups"] = 0,
                ["Definition"] = 0,
                ["Critical"] = 0,
                ["Other"] = 0,
                ["Security"] = 0
            };

            foreach (var dev in devices.OrderBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                var lookupKey = !string.IsNullOrWhiteSpace(dev.ResourceId) ? dev.ResourceId! : dev.DisplayName;
                recordsByDevice.TryGetValue(lookupKey, out var devRecords);
                devRecords ??= new List<RawDeviceUpdateRecord>();

                var missing = devRecords.Where(r => r.StatusCode == 2).ToList();
                var installed = devRecords.Count(r => r.StatusCode == 3);
                var unk = devRecords.Count(r => r.StatusCode == 0);

                UpdateComplianceState state;
                if (devRecords.Count == 0 || (missing.Count == 0 && installed == 0 && unk > 0))
                {
                    state = UpdateComplianceState.Unknown;
                    unknown++;
                }
                else if (missing.Count > 0)
                {
                    state = UpdateComplianceState.NonCompliant;
                    nonCompliant++;
                }
                else
                {
                    state = UpdateComplianceState.Compliant;
                    compliant++;
                }

                var cats = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var m in missing)
                {
                    var cat = MapCategoryBucket(m.Classification);
                    cats.Add(cat);
                    missingByCategory[cat] = missingByCategory.TryGetValue(cat, out var cur) ? cur + 1 : 1;
                }

                deviceRows.Add(new DeviceUpdateComplianceRow
                {
                    ResourceId = dev.ResourceId ?? string.Empty,
                    DeviceName = dev.DisplayName,
                    ComplianceState = state,
                    MissingCount = missing.Count,
                    InstalledCount = installed,
                    UnknownCount = unk,
                    MissingCategories = string.Join(", ", cats.OrderBy(c => c)),
                    MissingUpdates = missing
                });
            }

            return new SoftwareUpdatesAnalysisResult
            {
                DeviceRows = deviceRows,
                ComplianceSlices = new List<PieSliceData>
                {
                    new PieSliceData { Key = nameof(UpdateComplianceState.Compliant), Label = "Compliant", Value = compliant, ColorHex = "#7CC6E8" },
                    new PieSliceData { Key = nameof(UpdateComplianceState.NonCompliant), Label = "NonCompliant", Value = nonCompliant, ColorHex = "#F28B82" },
                    new PieSliceData { Key = nameof(UpdateComplianceState.Unknown), Label = "Unknown", Value = unknown, ColorHex = "#7E8C84" }
                },
                MissingByCategorySlices = new List<PieSliceData>
                {
                    new PieSliceData { Key = "Update Rollups", Label = "Update Rollups", Value = missingByCategory["Update Rollups"], ColorHex = "#A78BFA" },
                    new PieSliceData { Key = "Definition", Label = "Definition", Value = missingByCategory["Definition"], ColorHex = "#B7E49E" },
                    new PieSliceData { Key = "Critical", Label = "Critical", Value = missingByCategory["Critical"], ColorHex = "#9E9E9E" },
                    new PieSliceData { Key = "Other", Label = "Other", Value = missingByCategory["Other"], ColorHex = "#80CBC4" },
                    new PieSliceData { Key = "Security", Label = "Security", Value = missingByCategory["Security"], ColorHex = "#F48FB1" }
                },
                Errors = errors?.ToList() ?? new List<DeviceErrorEntry>()
            };
        }

        public static string MapCategoryBucket(string? classification)
        {
            if (string.IsNullOrWhiteSpace(classification)) return "Other";
            var c = classification!.Trim();
            if (c.IndexOf("Security", StringComparison.OrdinalIgnoreCase) >= 0) return "Security";
            if (c.IndexOf("Critical", StringComparison.OrdinalIgnoreCase) >= 0) return "Critical";
            if (c.IndexOf("Definition", StringComparison.OrdinalIgnoreCase) >= 0) return "Definition";
            if (c.IndexOf("Rollup", StringComparison.OrdinalIgnoreCase) >= 0) return "Update Rollups";
            return "Other";
        }

        private static bool MatchesFilter(RawDeviceUpdateRecord record, SoftwareUpdatesFilter filter, DateTime? cutoffDateUtc)
        {
            if (filter.LimitToDeployedUpdates && !record.IsDeployed)
            {
                return false;
            }

            if (filter.SelectedUpdateGroupIds.Count > 0 &&
                !record.UpdateGroupIds.Any(id => filter.SelectedUpdateGroupIds.Contains(id)))
            {
                return false;
            }

            if (cutoffDateUtc.HasValue && record.DatePosted.HasValue && record.DatePosted.Value > cutoffDateUtc.Value)
            {
                return false;
            }

            var c = record.Classification ?? string.Empty;
            if (c.IndexOf("Critical", StringComparison.OrdinalIgnoreCase) >= 0) return filter.IncludeCriticalUpdates;
            if (c.IndexOf("Definition", StringComparison.OrdinalIgnoreCase) >= 0) return filter.IncludeDefinitionUpdates;
            if (c.IndexOf("Security", StringComparison.OrdinalIgnoreCase) >= 0) return filter.IncludeSecurityUpdates;
            if (c.IndexOf("Rollup", StringComparison.OrdinalIgnoreCase) >= 0) return filter.IncludeUpdateRollups;
            if (string.Equals(c.Trim(), "Updates", StringComparison.OrdinalIgnoreCase)) return filter.IncludeUpdates;
            return filter.IncludeOtherClassifications;
        }
    }
}
