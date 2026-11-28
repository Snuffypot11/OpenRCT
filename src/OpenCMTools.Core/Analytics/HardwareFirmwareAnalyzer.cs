using System;
using System.Collections.Generic;
using System.Linq;
using OpenCMTools.Core.Models;

namespace OpenCMTools.Core.Analytics
{
    /// <summary>
    /// Classifies hardware/firmware inventory records (SMBIOS chassis types, VM heuristics,
    /// UEFI/SecureBoot/TPM status), builds pie chart slices for user-selected dimensions,
    /// and applies single or cumulative Nested Filtering.
    /// </summary>
    public static class HardwareFirmwareAnalyzer
    {
        private static readonly string[] Palette =
        {
            "#2196F3", "#EF4444", "#10B981", "#F59E0B",
            "#8B5CF6", "#EC4899", "#14B8A6", "#6366F1",
            "#F97316", "#64748B"
        };

        public static string GetDimensionDisplayName(HardwareAuditDimension dimension) => dimension switch
        {
            HardwareAuditDimension.Chassis => "Chassis",
            HardwareAuditDimension.Manufacturer => "Manufacturer",
            HardwareAuditDimension.Model => "Model",
            HardwareAuditDimension.UefiStatus => "UEFI Status",
            HardwareAuditDimension.SecureBootStatus => "Secure Boot Status",
            HardwareAuditDimension.TpmStatus => "TPM Status",
            HardwareAuditDimension.BiosVersion => "BIOS Version",
            HardwareAuditDimension.OsVersion => "OS Version",
            _ => dimension.ToString()
        };

        public static string GetDimensionValue(HardwareFirmwareAuditRow row, HardwareAuditDimension dimension)
        {
            var raw = dimension switch
            {
                HardwareAuditDimension.Chassis => row.ChassisType,
                HardwareAuditDimension.Manufacturer => row.Manufacturer,
                HardwareAuditDimension.Model => row.Model,
                HardwareAuditDimension.UefiStatus => row.UefiStatus,
                HardwareAuditDimension.SecureBootStatus => row.SecureBootStatus,
                HardwareAuditDimension.TpmStatus => row.TpmStatus,
                HardwareAuditDimension.BiosVersion => row.BiosVersion,
                HardwareAuditDimension.OsVersion => row.OsVersion,
                _ => "Unknown"
            };
            return string.IsNullOrWhiteSpace(raw) ? "Unknown" : raw.Trim();
        }

        public static string MapChassisType(IEnumerable<int>? chassisCodes, string? manufacturer, string? model)
        {
            if (IsVirtualMachine(manufacturer, model))
            {
                return "VirtualMachine";
            }

            if (chassisCodes != null)
            {
                foreach (var code in chassisCodes)
                {
                    switch (code)
                    {
                        case 8:
                        case 9:
                        case 10:
                        case 11:
                        case 12:
                        case 14:
                        case 18:
                        case 21:
                        case 31:
                            return "Laptop";
                        case 30:
                        case 32:
                            return "Tablet";
                        case 13:
                            return "AllInOne";
                        case 3:
                        case 4:
                        case 5:
                        case 6:
                        case 7:
                        case 15:
                        case 16:
                        case 35:
                        case 36:
                            return "Desktop";
                        case 17:
                        case 23:
                        case 25:
                        case 28:
                        case 29:
                            return "Server";
                    }
                }
            }

            return "Unknown";
        }

        public static bool IsVirtualMachine(string? manufacturer, string? model)
        {
            var combined = $"{manufacturer} {model}";
            if (string.IsNullOrWhiteSpace(combined)) return false;

            return combined.IndexOf("Virtual", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   combined.IndexOf("VMware", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   combined.IndexOf("QEMU", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   combined.IndexOf("KVM", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   combined.IndexOf("Xen", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   combined.IndexOf("Bochs", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   combined.IndexOf("Parallels", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   combined.IndexOf("Hyper-V", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static string NormalizeManufacturer(string? manufacturer)
        {
            if (string.IsNullOrWhiteSpace(manufacturer)) return "Unknown";
            var m = manufacturer!.Trim();
            if (m.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase)) return "Microsoft";
            if (m.StartsWith("Dell", StringComparison.OrdinalIgnoreCase)) return "Dell";
            if (m.StartsWith("HP", StringComparison.OrdinalIgnoreCase) || m.StartsWith("Hewlett", StringComparison.OrdinalIgnoreCase)) return "HP";
            if (m.StartsWith("Lenovo", StringComparison.OrdinalIgnoreCase)) return "Lenovo";
            if (m.StartsWith("VMware", StringComparison.OrdinalIgnoreCase)) return "VMware";
            if (m.StartsWith("Apple", StringComparison.OrdinalIgnoreCase)) return "Apple";
            if (m.StartsWith("ASUS", StringComparison.OrdinalIgnoreCase)) return "ASUS";
            if (m.StartsWith("Acer", StringComparison.OrdinalIgnoreCase)) return "Acer";
            return m;
        }

        public static List<PieSliceData> BuildSlices(
            IEnumerable<HardwareFirmwareAuditRow> rows,
            HardwareAuditDimension dimension,
            int maxNamedSlices = 8)
        {
            var groups = (rows ?? Enumerable.Empty<HardwareFirmwareAuditRow>())
                .GroupBy(r => GetDimensionValue(r, dimension), StringComparer.OrdinalIgnoreCase)
                .Select(g => new { Value = g.First() != null ? GetDimensionValue(g.First(), dimension) : g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .ThenBy(g => g.Value, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var slices = new List<PieSliceData>();
            int limit = Math.Max(1, maxNamedSlices);

            for (int i = 0; i < groups.Count && i < limit; i++)
            {
                var g = groups[i];
                slices.Add(new PieSliceData
                {
                    Key = g.Value,
                    Label = g.Value,
                    Value = g.Count,
                    ColorHex = PickColor(dimension, g.Value, i)
                });
            }

            if (groups.Count > limit)
            {
                var otherCount = groups.Skip(limit).Sum(g => g.Count);
                slices.Add(new PieSliceData
                {
                    Key = "__OTHER__",
                    Label = $"Other ({groups.Count - limit})",
                    Value = otherCount,
                    ColorHex = "#94A3B8"
                });
            }

            return slices;
        }

        public static List<HardwareFirmwareAuditRow> ApplyFilters(
            IEnumerable<HardwareFirmwareAuditRow> allRows,
            IDictionary<HardwareAuditDimension, string> activeFilters,
            int maxNamedSlices = 8)
        {
            IEnumerable<HardwareFirmwareAuditRow> current = allRows ?? Enumerable.Empty<HardwareFirmwareAuditRow>();
            if (activeFilters == null || activeFilters.Count == 0)
            {
                return current.ToList();
            }

            var allList = current.ToList();
            foreach (var kv in activeFilters)
            {
                if (string.IsNullOrWhiteSpace(kv.Value)) continue;

                if (string.Equals(kv.Value, "__OTHER__", StringComparison.OrdinalIgnoreCase))
                {
                    var topKeys = BuildSlices(allList, kv.Key, maxNamedSlices)
                        .Where(s => !string.Equals(s.Key, "__OTHER__", StringComparison.OrdinalIgnoreCase))
                        .Select(s => s.Key)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    current = current.Where(r => !topKeys.Contains(GetDimensionValue(r, kv.Key)));
                }
                else
                {
                    var target = kv.Value;
                    var dim = kv.Key;
                    current = current.Where(r => string.Equals(GetDimensionValue(r, dim), target, StringComparison.OrdinalIgnoreCase));
                }
            }

            return current.ToList();
        }

        private static string PickColor(HardwareAuditDimension dimension, string value, int index)
        {
            if (string.Equals(value, "Unknown", StringComparison.OrdinalIgnoreCase)) return "#64748B";
            if (dimension == HardwareAuditDimension.UefiStatus ||
                dimension == HardwareAuditDimension.SecureBootStatus ||
                dimension == HardwareAuditDimension.TpmStatus)
            {
                if (value.IndexOf("Enabled", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    value.IndexOf("Ready", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    value.IndexOf("2.0", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "#10B981";
                if (value.IndexOf("Disabled", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    value.IndexOf("Legacy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    value.IndexOf("Not", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "#EF4444";
            }

            return Palette[index % Palette.Length];
        }
    }
}
