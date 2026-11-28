using System;
using System.Collections.Generic;
using System.Linq;
using OpenCMTools.Core.Models;

namespace OpenCMTools.Core.Analytics
{
    /// <summary>
    /// Reconciles BitLocker recovery key storage and encryption compliance across Active Directory
    /// and Microsoft Configuration Manager (strictly on-premises AD + ConfigMgr; no Entra ID).
    /// </summary>
    public static class BitLockerComplianceAnalyzer
    {
        public static BitLockerComplianceScanResult Analyze(
            IEnumerable<AdComputerRecord> adComputers,
            ISet<string> computersWithAdKeys,
            IEnumerable<ConfigMgrBitLockerRecord> configMgrRecords,
            bool primarySourceIsCollection)
        {
            var adList = (adComputers ?? Enumerable.Empty<AdComputerRecord>()).ToList();
            var cmList = (configMgrRecords ?? Enumerable.Empty<ConfigMgrBitLockerRecord>()).ToList();

            var adKeySet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (computersWithAdKeys != null)
            {
                foreach (var entry in computersWithAdKeys)
                {
                    if (string.IsNullOrWhiteSpace(entry)) continue;
                    adKeySet.Add(entry.Trim());
                    adKeySet.Add(NormalizeHostName(entry));
                }
            }

            var adByHost = new Dictionary<string, AdComputerRecord>(StringComparer.OrdinalIgnoreCase);
            foreach (var ad in adList)
            {
                var key = NormalizeHostName(ad.Name);
                if (!string.IsNullOrEmpty(key) && !adByHost.ContainsKey(key))
                {
                    adByHost[key] = ad;
                }
            }

            var cmByHost = new Dictionary<string, ConfigMgrBitLockerRecord>(StringComparer.OrdinalIgnoreCase);
            foreach (var cm in cmList)
            {
                var key = NormalizeHostName(cm.Name);
                if (!string.IsNullOrEmpty(key) && !cmByHost.ContainsKey(key))
                {
                    cmByHost[key] = cm;
                }
            }

            var hostKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (primarySourceIsCollection)
            {
                foreach (var k in cmByHost.Keys) hostKeys.Add(k);
            }
            else
            {
                foreach (var k in adByHost.Keys) hostKeys.Add(k);
            }

            var rows = new List<BitLockerComplianceRow>();

            foreach (var host in hostKeys.OrderBy(h => h, StringComparer.OrdinalIgnoreCase))
            {
                adByHost.TryGetValue(host, out var ad);
                cmByHost.TryGetValue(host, out var cm);

                bool hasAdKey = adKeySet.Contains(host) ||
                                (ad != null && !string.IsNullOrWhiteSpace(ad.DistinguishedName) && adKeySet.Contains(ad.DistinguishedName)) ||
                                (cm != null && !string.IsNullOrWhiteSpace(cm.DistinguishedName) && adKeySet.Contains(cm.DistinguishedName));
                bool hasCmKey = cm != null && cm.HasConfigMgrRecoveryKey;

                BitLockerKeyStorageBucket storageBucket;
                string storageLabel;
                if (hasAdKey && hasCmKey)
                {
                    storageBucket = BitLockerKeyStorageBucket.MultipleLocations;
                    storageLabel = "AD, CM";
                }
                else if (hasCmKey)
                {
                    storageBucket = BitLockerKeyStorageBucket.ConfigMgr;
                    storageLabel = "CM";
                }
                else if (hasAdKey)
                {
                    storageBucket = BitLockerKeyStorageBucket.ActiveDirectory;
                    storageLabel = "AD";
                }
                else
                {
                    storageBucket = BitLockerKeyStorageBucket.None;
                    storageLabel = "None";
                }

                var cmCompliance = cm?.CmBitLockerCompliance ?? "Unknown";
                var encMethod = cm?.EncryptionMethod ?? "Unknown";
                var protStatus = cm?.ProtectionStatus ?? "Unknown";
                var unified = DetermineUnifiedCompliance(cmCompliance, encMethod, protStatus, cm?.ConversionStatus);

                rows.Add(new BitLockerComplianceRow
                {
                    Name = !string.IsNullOrWhiteSpace(ad?.Name) ? ad!.Name : (cm?.Name ?? host),
                    ResourceId = cm?.ResourceId ?? string.Empty,
                    KeyStorageBucket = storageBucket,
                    KeyStorage = storageLabel,
                    UnifiedComplianceState = unified,
                    CmBitLockerCompliance = cmCompliance,
                    EncryptionMethod = encMethod,
                    ProtectionStatus = protStatus,
                    DistinguishedName = !string.IsNullOrWhiteSpace(ad?.DistinguishedName) ? ad!.DistinguishedName : (cm?.DistinguishedName ?? string.Empty),
                    PasswordLastSet = ad?.PasswordLastSet,
                    OperatingSystem = !string.IsNullOrWhiteSpace(ad?.OperatingSystem) ? ad!.OperatingSystem : (cm?.OperatingSystem ?? string.Empty),
                    OperatingSystemVersion = !string.IsNullOrWhiteSpace(ad?.OperatingSystemVersion) ? ad!.OperatingSystemVersion : (cm?.OperatingSystemVersion ?? string.Empty)
                });
            }

            var keySlices = new List<PieSliceData>
            {
                new PieSliceData
                {
                    Key = BitLockerKeyStorageBucket.None.ToString(),
                    Label = "Computers without stored keys",
                    Value = rows.Count(r => r.KeyStorageBucket == BitLockerKeyStorageBucket.None),
                    ColorHex = "#EF4444"
                },
                new PieSliceData
                {
                    Key = BitLockerKeyStorageBucket.ActiveDirectory.ToString(),
                    Label = "Computers with keys stored in AD",
                    Value = rows.Count(r => r.KeyStorageBucket == BitLockerKeyStorageBucket.ActiveDirectory),
                    ColorHex = "#F59E0B"
                },
                new PieSliceData
                {
                    Key = BitLockerKeyStorageBucket.ConfigMgr.ToString(),
                    Label = "Computers with keys stored in ConfigMgr",
                    Value = rows.Count(r => r.KeyStorageBucket == BitLockerKeyStorageBucket.ConfigMgr),
                    ColorHex = "#A855F7"
                },
                new PieSliceData
                {
                    Key = BitLockerKeyStorageBucket.MultipleLocations.ToString(),
                    Label = "Computers with keys stored in multiple locations",
                    Value = rows.Count(r => r.KeyStorageBucket == BitLockerKeyStorageBucket.MultipleLocations),
                    ColorHex = "#3B82F6"
                }
            };

            var compSlices = new List<PieSliceData>
            {
                new PieSliceData
                {
                    Key = BitLockerUnifiedComplianceState.CmCompliant.ToString(),
                    Label = "CM Compliant",
                    Value = rows.Count(r => r.UnifiedComplianceState == BitLockerUnifiedComplianceState.CmCompliant),
                    ColorHex = "#A855F7"
                },
                new PieSliceData
                {
                    Key = BitLockerUnifiedComplianceState.EncryptedUnmanaged.ToString(),
                    Label = "Encrypted (Unmanaged)",
                    Value = rows.Count(r => r.UnifiedComplianceState == BitLockerUnifiedComplianceState.EncryptedUnmanaged),
                    ColorHex = "#14B8A6"
                },
                new PieSliceData
                {
                    Key = BitLockerUnifiedComplianceState.NonCompliant.ToString(),
                    Label = "Non-Compliant",
                    Value = rows.Count(r => r.UnifiedComplianceState == BitLockerUnifiedComplianceState.NonCompliant),
                    ColorHex = "#EF4444"
                },
                new PieSliceData
                {
                    Key = BitLockerUnifiedComplianceState.Unknown.ToString(),
                    Label = "Unknown",
                    Value = rows.Count(r => r.UnifiedComplianceState == BitLockerUnifiedComplianceState.Unknown),
                    ColorHex = "#475569"
                }
            };

            return new BitLockerComplianceScanResult
            {
                Rows = rows,
                KeyStorageSlices = keySlices,
                UnifiedComplianceSlices = compSlices
            };
        }

        public static string MapEncryptionMethod(int? code) => code switch
        {
            0 => "NotEncrypted",
            1 => "AES_128_WITH_DIFFUSER",
            2 => "AES_256_WITH_DIFFUSER",
            3 => "AES_128",
            4 => "AES_256",
            5 => "HARDWARE_ENCRYPTION",
            6 => "XTS_AES_128",
            7 => "XTS_AES_256",
            _ => "Unknown"
        };

        public static string MapProtectionStatus(int? code) => code switch
        {
            0 => "Protection Off",
            1 => "Protection On",
            2 => "Protection Unknown",
            _ => "Unknown"
        };

        private static BitLockerUnifiedComplianceState DetermineUnifiedCompliance(
            string cmCompliance,
            string encryptionMethod,
            string protectionStatus,
            int? conversionStatus)
        {
            if (string.Equals(cmCompliance, "Compliant", StringComparison.OrdinalIgnoreCase))
            {
                return BitLockerUnifiedComplianceState.CmCompliant;
            }

            if (string.Equals(cmCompliance, "Non-Compliant", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(cmCompliance, "NonCompliant", StringComparison.OrdinalIgnoreCase))
            {
                return BitLockerUnifiedComplianceState.NonCompliant;
            }

            bool isEncrypted =
                string.Equals(protectionStatus, "Protection On", StringComparison.OrdinalIgnoreCase) ||
                conversionStatus == 1 ||
                (!string.IsNullOrWhiteSpace(encryptionMethod) &&
                 !string.Equals(encryptionMethod, "Unknown", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(encryptionMethod, "NotEncrypted", StringComparison.OrdinalIgnoreCase));

            if (isEncrypted)
            {
                return BitLockerUnifiedComplianceState.EncryptedUnmanaged;
            }

            if (string.Equals(encryptionMethod, "NotEncrypted", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(protectionStatus, "Protection Off", StringComparison.OrdinalIgnoreCase) ||
                conversionStatus == 0)
            {
                return BitLockerUnifiedComplianceState.NonCompliant;
            }

            return BitLockerUnifiedComplianceState.Unknown;
        }

        public static string NormalizeHostName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            var trimmed = name!.Trim().TrimEnd('$');
            var dot = trimmed.IndexOf('.');
            return dot > 0 ? trimmed.Substring(0, dot).ToUpperInvariant() : trimmed.ToUpperInvariant();
        }
    }
}
