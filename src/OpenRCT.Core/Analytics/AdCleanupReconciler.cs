using System;
using System.Collections.Generic;
using System.Linq;
using OpenRCT.Core.Models;

namespace OpenRCT.Core.Analytics
{
    /// <summary>
    /// Reconciles Active Directory computer objects against ConfigMgr device records
    /// for the Active Directory Cleanup Tool dashboard.
    /// </summary>
    public static class AdCleanupReconciler
    {
        public static AdCleanupScanResult Reconcile(
            IEnumerable<AdComputerRecord> adComputers,
            IEnumerable<ConfigMgrDeviceRecord> sccmDevices,
            IEnumerable<DeviceErrorEntry>? errors = null)
        {
            var adByHost = new Dictionary<string, AdComputerRecord>(StringComparer.OrdinalIgnoreCase);
            foreach (var ad in adComputers ?? Enumerable.Empty<AdComputerRecord>())
            {
                var key = NormalizeHostName(ad.Name);
                if (!string.IsNullOrEmpty(key) && !adByHost.ContainsKey(key))
                {
                    adByHost[key] = ad;
                }
            }

            var sccmByHost = new Dictionary<string, ConfigMgrDeviceRecord>(StringComparer.OrdinalIgnoreCase);
            foreach (var cm in sccmDevices ?? Enumerable.Empty<ConfigMgrDeviceRecord>())
            {
                var key = NormalizeHostName(cm.Name);
                if (!string.IsNullOrEmpty(key))
                {
                    if (!sccmByHost.TryGetValue(key, out var existing) ||
                        (cm.LastCheckInTime ?? DateTime.MinValue) > (existing.LastCheckInTime ?? DateTime.MinValue))
                    {
                        sccmByHost[key] = cm;
                    }
                }
            }

            var allKeys = new HashSet<string>(adByHost.Keys, StringComparer.OrdinalIgnoreCase);
            allKeys.UnionWith(sccmByHost.Keys);

            var rows = new List<AdCleanupRow>(allKeys.Count);
            int cmOnly = 0, both = 0, adOnly = 0;

            foreach (var key in allKeys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
            {
                var inAd = adByHost.TryGetValue(key, out var ad);
                var inCm = sccmByHost.TryGetValue(key, out var cm);

                DevicePresenceBucket bucket;
                if (inAd && inCm)
                {
                    bucket = DevicePresenceBucket.Both;
                    both++;
                }
                else if (inCm)
                {
                    bucket = DevicePresenceBucket.ConfigMgrOnly;
                    cmOnly++;
                }
                else
                {
                    bucket = DevicePresenceBucket.ActiveDirectoryOnly;
                    adOnly++;
                }

                rows.Add(new AdCleanupRow
                {
                    Bucket = bucket,
                    Name = inAd ? ad!.Name : cm!.Name,
                    AdDistinguishedName = ad?.DistinguishedName ?? string.Empty,
                    AdPasswordLastSet = ad?.PasswordLastSet,
                    AdOs = ad?.OperatingSystem ?? string.Empty,
                    AdOsVersion = ad?.OperatingSystemVersion ?? string.Empty,
                    AdCreated = ad?.Created,
                    AdLastLogonTimestamp = ad?.LastLogonTimestamp,
                    SccmOperatingSystem = cm?.OperatingSystem ?? string.Empty,
                    SccmLastLogonUser = cm?.LastLogonUser ?? string.Empty,
                    SccmLastLogonDomain = cm?.LastLogonDomain ?? string.Empty,
                    SccmLastLogonTime = cm?.LastLogonTime,
                    SccmDomain = cm?.Domain ?? string.Empty,
                    SccmDistinguishedName = cm?.DistinguishedName ?? string.Empty,
                    SccmLastCheckInTime = cm?.LastCheckInTime,
                    SccmClientInstalled = inCm ? cm!.ClientInstalled : (bool?)null,
                    SccmResourceId = cm?.ResourceId
                });
            }

            return new AdCleanupScanResult
            {
                Rows = rows,
                ChartSlices = new List<PieSliceData>
                {
                    new PieSliceData { Key = nameof(DevicePresenceBucket.ConfigMgrOnly), Label = "ConfigMgr", Value = cmOnly, ColorHex = "#F7DC88" },
                    new PieSliceData { Key = nameof(DevicePresenceBucket.Both), Label = "Both", Value = both, ColorHex = "#7CC6E8" },
                    new PieSliceData { Key = nameof(DevicePresenceBucket.ActiveDirectoryOnly), Label = "Active Directory", Value = adOnly, ColorHex = "#F28B82" }
                },
                Errors = errors?.ToList() ?? new List<DeviceErrorEntry>()
            };
        }

        public static string NormalizeHostName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            var trimmed = name!.Trim().TrimEnd('$');
            var dotIdx = trimmed.IndexOf('.');
            return (dotIdx > 0 ? trimmed.Substring(0, dotIdx) : trimmed).ToUpperInvariant();
        }
    }
}
