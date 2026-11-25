using System;
using System.Collections.Generic;
using System.Linq;
using OpenRCT.Core.Models;

namespace OpenRCT.Core.Analytics
{
    /// <summary>
    /// Classifies AD computer records and optional ConfigMgr client inventory into LAPS Dashboard metrics,
    /// supporting both Legacy Microsoft LAPS (ms-Mcs-AdmPwd) and Windows LAPS (msLAPS-Password).
    /// </summary>
    public static class LapsComplianceAnalyzer
    {
        public static LapsDashboardScanResult Analyze(
            IEnumerable<AdComputerRecord> adComputers,
            IEnumerable<ConfigMgrDeviceRecord>? sccmDevices = null,
            IEnumerable<DeviceErrorEntry>? errors = null)
        {
            var cmByHost = new Dictionary<string, ConfigMgrDeviceRecord>(StringComparer.OrdinalIgnoreCase);
            foreach (var cm in sccmDevices ?? Enumerable.Empty<ConfigMgrDeviceRecord>())
            {
                var key = AdCleanupReconciler.NormalizeHostName(cm.Name);
                if (!string.IsNullOrEmpty(key) && !cmByHost.ContainsKey(key))
                {
                    cmByHost[key] = cm;
                }
            }

            var rows = new List<LapsDashboardRow>();
            int withPassword = 0, withoutPassword = 0;
            int x64Count = 0, x86Count = 0, builtInCount = 0, noClientCount = 0;

            foreach (var ad in (adComputers ?? Enumerable.Empty<AdComputerRecord>()).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            {
                var hasPwd = ad.HasWindowsLapsPassword || ad.HasLegacyLapsPassword;
                if (hasPwd) withPassword++; else withoutPassword++;

                DateTime? expiration = ad.WindowsLapsExpiration ?? ad.LegacyLapsExpiration;

                var key = AdCleanupReconciler.NormalizeHostName(ad.Name);
                cmByHost.TryGetValue(key, out var cm);

                LapsClientInstallState installState;
                if (ad.HasWindowsLapsPassword)
                {
                    installState = LapsClientInstallState.WindowsBuiltIn;
                    builtInCount++;
                }
                else if (cm != null && string.Equals(cm.LapsClientArchitecture, "x64", StringComparison.OrdinalIgnoreCase))
                {
                    installState = LapsClientInstallState.X64Installed;
                    x64Count++;
                }
                else if (cm != null && string.Equals(cm.LapsClientArchitecture, "x86", StringComparison.OrdinalIgnoreCase))
                {
                    installState = LapsClientInstallState.X86Installed;
                    x86Count++;
                }
                else
                {
                    installState = LapsClientInstallState.NotInstalled;
                    noClientCount++;
                }

                rows.Add(new LapsDashboardRow
                {
                    Name = ad.Name,
                    DistinguishedName = ad.DistinguishedName,
                    PasswordLastSet = ad.PasswordLastSet,
                    Os = ad.OperatingSystem,
                    OsVersion = ad.OperatingSystemVersion,
                    LapsPasswordExpiration = expiration,
                    HasLapsPassword = hasPwd,
                    ClientInstallState = installState,
                    Created = ad.Created
                });
            }

            var clientSlices = new List<PieSliceData>
            {
                new PieSliceData { Key = nameof(LapsClientInstallState.X64Installed), Label = "Computers with x64 LAPS Client Installed", Value = x64Count, ColorHex = "#7CC6E8" },
                new PieSliceData { Key = nameof(LapsClientInstallState.X86Installed), Label = "Computers with x86 LAPS Client Installed", Value = x86Count, ColorHex = "#F7DC88" }
            };
            if (builtInCount > 0)
            {
                clientSlices.Add(new PieSliceData { Key = nameof(LapsClientInstallState.WindowsBuiltIn), Label = "Computers with Windows Built-in LAPS", Value = builtInCount, ColorHex = "#81C784" });
            }
            clientSlices.Add(new PieSliceData { Key = nameof(LapsClientInstallState.NotInstalled), Label = "No LAPS Client Installed", Value = noClientCount, ColorHex = "#F28B82" });

            return new LapsDashboardScanResult
            {
                Rows = rows,
                PasswordInAdSlices = new List<PieSliceData>
                {
                    new PieSliceData { Key = "WithPassword", Label = "Computers with LAPS Password", Value = withPassword, ColorHex = "#7CC6E8" },
                    new PieSliceData { Key = "WithoutPassword", Label = "Computers without LAPS Password", Value = withoutPassword, ColorHex = "#F28B82" }
                },
                ClientInstallStateSlices = clientSlices,
                Errors = errors?.ToList() ?? new List<DeviceErrorEntry>()
            };
        }
    }
}
