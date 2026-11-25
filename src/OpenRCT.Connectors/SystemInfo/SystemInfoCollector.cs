using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.DirectoryServices;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using OpenRCT.Connectors.Wmi;
using OpenRCT.Core.Interfaces;
using OpenRCT.Core.Models;

namespace OpenRCT.Connectors.SystemInfo
{
    /// <summary>
    /// Collects all 10 tabs of the System Information tool and executes management actions
    /// (Uninstall application, control services, delete user profiles, modify local group members).
    /// </summary>
    public sealed class SystemInfoCollector : ISystemInfoCollector
    {
        private readonly WmiClient _wmi;

        public SystemInfoCollector(WmiClient? wmi = null)
        {
            _wmi = wmi ?? new WmiClient();
        }

        public async Task<OperatingSystemInfo> GetOperatingSystemAsync(TargetDevice device, CancellationToken ct = default)
        {
            var host = device.ConnectAddress;
            var osRows = await _wmi.QueryAsync(host, @"root\cimv2",
                "SELECT Caption, Version, BuildNumber, OSArchitecture, InstallDate, LastBootUpTime, RegisteredUser, SerialNumber, WindowsDirectory FROM Win32_OperatingSystem",
                ct: ct).ConfigureAwait(false);
            var csRows = await _wmi.QueryAsync(host, @"root\cimv2",
                "SELECT Domain, UserName FROM Win32_ComputerSystem",
                ct: ct).ConfigureAwait(false);

            var os = osRows.FirstOrDefault() ?? new Dictionary<string, object?>();
            var cs = csRows.FirstOrDefault() ?? new Dictionary<string, object?>();

            return new OperatingSystemInfo
            {
                DeviceName = device.DisplayName,
                Caption = WmiClient.GetString(os, "Caption"),
                Version = WmiClient.GetString(os, "Version"),
                BuildNumber = WmiClient.GetString(os, "BuildNumber"),
                Architecture = WmiClient.GetString(os, "OSArchitecture"),
                InstallDate = WmiClient.GetDmtfDateTime(os, "InstallDate"),
                LastBootUpTime = WmiClient.GetDmtfDateTime(os, "LastBootUpTime"),
                RegisteredUser = WmiClient.GetString(os, "RegisteredUser"),
                SerialNumber = WmiClient.GetString(os, "SerialNumber"),
                WindowsDirectory = WmiClient.GetString(os, "WindowsDirectory"),
                Domain = WmiClient.GetString(cs, "Domain"),
                LoggedOnUser = WmiClient.GetString(cs, "UserName")
            };
        }

        public async Task<HardwareInfo> GetHardwareAsync(TargetDevice device, CancellationToken ct = default)
        {
            var host = device.ConnectAddress;
            var csRows = await _wmi.QueryAsync(host, @"root\cimv2",
                "SELECT Manufacturer, Model, TotalPhysicalMemory, SystemType FROM Win32_ComputerSystem", ct: ct).ConfigureAwait(false);
            var biosRows = await _wmi.QueryAsync(host, @"root\cimv2",
                "SELECT SerialNumber, SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS", ct: ct).ConfigureAwait(false);
            var cpuRows = await _wmi.QueryAsync(host, @"root\cimv2",
                "SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor", ct: ct).ConfigureAwait(false);
            var osRows = await _wmi.QueryAsync(host, @"root\cimv2",
                "SELECT FreePhysicalMemory FROM Win32_OperatingSystem", ct: ct).ConfigureAwait(false);

            var cs = csRows.FirstOrDefault() ?? new Dictionary<string, object?>();
            var bios = biosRows.FirstOrDefault() ?? new Dictionary<string, object?>();
            var os = osRows.FirstOrDefault() ?? new Dictionary<string, object?>();

            string tpmVersion = "Unavailable";
            try
            {
                var tpmRows = await _wmi.QueryAsync(host, @"root\CIMV2\Security\MicrosoftTpm",
                    "SELECT SpecVersion, IsEnabled_InitialValue FROM Win32_Tpm", ct: ct).ConfigureAwait(false);
                var tpm = tpmRows.FirstOrDefault();
                if (tpm != null)
                {
                    tpmVersion = WmiClient.GetString(tpm, "SpecVersion", "Present");
                }
            }
            catch { }

            var totalBytes = WmiClient.GetInt64(cs, "TotalPhysicalMemory");
            var freeKb = WmiClient.GetInt64(os, "FreePhysicalMemory");

            return new HardwareInfo
            {
                DeviceName = device.DisplayName,
                Manufacturer = WmiClient.GetString(cs, "Manufacturer"),
                Model = WmiClient.GetString(cs, "Model"),
                SerialNumber = WmiClient.GetString(bios, "SerialNumber"),
                BiosVersion = WmiClient.GetString(bios, "SMBIOSBIOSVersion"),
                BiosReleaseDate = WmiClient.GetDmtfDateTime(bios, "ReleaseDate"),
                ProcessorName = cpuRows.Select(c => WmiClient.GetString(c, "Name")).FirstOrDefault() ?? string.Empty,
                PhysicalCores = cpuRows.Sum(c => WmiClient.GetInt32(c, "NumberOfCores")),
                LogicalProcessors = cpuRows.Sum(c => WmiClient.GetInt32(c, "NumberOfLogicalProcessors")),
                TotalMemoryGb = Math.Round(totalBytes / (1024.0 * 1024.0 * 1024.0), 2),
                FreeMemoryGb = Math.Round(freeKb / (1024.0 * 1024.0), 2),
                SystemType = WmiClient.GetString(cs, "SystemType"),
                TpmVersion = tpmVersion
            };
        }

        public async Task<IReadOnlyList<DiskInfo>> GetDisksAsync(TargetDevice device, CancellationToken ct = default)
        {
            var rows = await _wmi.QueryAsync(device.ConnectAddress, @"root\cimv2",
                "SELECT DeviceID, VolumeName, FileSystem, DriveType, Size, FreeSpace FROM Win32_LogicalDisk", ct: ct).ConfigureAwait(false);

            return rows.Select(r =>
            {
                var size = WmiClient.GetInt64(r, "Size");
                var free = WmiClient.GetInt64(r, "FreeSpace");
                return new DiskInfo
                {
                    DeviceName = device.DisplayName,
                    DriveLetter = WmiClient.GetString(r, "DeviceID"),
                    VolumeName = WmiClient.GetString(r, "VolumeName"),
                    FileSystem = WmiClient.GetString(r, "FileSystem"),
                    DriveType = MapDriveType(WmiClient.GetInt32(r, "DriveType")),
                    TotalSizeGb = Math.Round(size / (1024.0 * 1024.0 * 1024.0), 2),
                    FreeSpaceGb = Math.Round(free / (1024.0 * 1024.0 * 1024.0), 2)
                };
            }).OrderBy(d => d.DriveLetter, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public Task<IReadOnlyList<InstalledApplicationInfo>> GetApplicationsAsync(TargetDevice device, CancellationToken ct = default)
        {
            // Uses Remote Registry (Uninstall + WOW6432Node) — fast and completely avoids Win32_Product MSI self-repair!
            return Task.Run<IReadOnlyList<InstalledApplicationInfo>>(() =>
            {
                ct.ThrowIfCancellationRequested();
                var apps = new List<InstalledApplicationInfo>();
                var host = device.ConnectAddress;

                using var hklm = RegistryKey.OpenRemoteBaseKey(RegistryHive.LocalMachine, host, RegistryView.Registry64);
                ReadUninstallHive(apps, device.DisplayName, hklm, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", "x64", ct);
                ReadUninstallHive(apps, device.DisplayName, hklm, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", "x86", ct);

                return apps
                    .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }, ct);
        }

        public async Task<ActionExecutionResult> UninstallApplicationAsync(TargetDevice device, InstalledApplicationInfo app, CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            var cmd = !string.IsNullOrWhiteSpace(app.QuietUninstallString)
                ? app.QuietUninstallString
                : BuildSilentUninstallCommand(app.UninstallString);

            if (string.IsNullOrWhiteSpace(cmd))
            {
                return ActionExecutionResult.Failure(device.DisplayName, "Uninstall Application",
                    $"No uninstall command is available for '{app.Name}'.", null, sw.Elapsed);
            }

            var pid = await _wmi.CreateRemoteProcessAsync(device.ConnectAddress, cmd, ct: ct).ConfigureAwait(false);
            return ActionExecutionResult.Success(device.DisplayName, "Uninstall Application",
                $"Started uninstall for '{app.Name}' (PID {pid}).", sw.Elapsed);
        }

        public async Task<IReadOnlyList<WindowsUpdateInfo>> GetWindowsUpdatesAsync(TargetDevice device, CancellationToken ct = default)
        {
            var rows = await _wmi.QueryAsync(device.ConnectAddress, @"root\cimv2",
                "SELECT HotFixID, Description, InstalledBy, InstalledOn FROM Win32_QuickFixEngineering", ct: ct).ConfigureAwait(false);

            return rows.Select(r => new WindowsUpdateInfo
            {
                DeviceName = device.DisplayName,
                HotFixId = WmiClient.GetString(r, "HotFixID"),
                Description = WmiClient.GetString(r, "Description"),
                InstalledBy = WmiClient.GetString(r, "InstalledBy"),
                InstalledOn = WmiClient.GetString(r, "InstalledOn"),
                Status = "Installed"
            }).OrderByDescending(u => u.InstalledOn, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public async Task<IReadOnlyList<WindowsServiceInfo>> GetServicesAsync(TargetDevice device, CancellationToken ct = default)
        {
            var rows = await _wmi.QueryAsync(device.ConnectAddress, @"root\cimv2",
                "SELECT Name, DisplayName, State, StartMode, StartName, PathName FROM Win32_Service", ct: ct).ConfigureAwait(false);

            return rows.Select(r => new WindowsServiceInfo
            {
                DeviceName = device.DisplayName,
                Name = WmiClient.GetString(r, "Name"),
                DisplayName = WmiClient.GetString(r, "DisplayName"),
                State = WmiClient.GetString(r, "State"),
                StartMode = WmiClient.GetString(r, "StartMode"),
                StartName = WmiClient.GetString(r, "StartName"),
                PathName = WmiClient.GetString(r, "PathName")
            }).OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public Task<ActionExecutionResult> ControlServiceAsync(
            TargetDevice device,
            string serviceName,
            string operation,
            string? newStartMode = null,
            CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                ct.ThrowIfCancellationRequested();
                var scope = _wmi.CreateScope(device.ConnectAddress, @"root\cimv2");
                var safeName = serviceName.Replace("\\", "\\\\").Replace("'", "\\'");
                using var svc = new ManagementObject(scope, new ManagementPath($"Win32_Service.Name='{safeName}'"), null);

                switch (operation.ToLowerInvariant())
                {
                    case "start":
                        svc.InvokeMethod("StartService", null);
                        return ActionExecutionResult.Success(device.DisplayName, "Start Service", $"Started '{serviceName}'.", sw.Elapsed);
                    case "stop":
                        svc.InvokeMethod("StopService", null);
                        return ActionExecutionResult.Success(device.DisplayName, "Stop Service", $"Stopped '{serviceName}'.", sw.Elapsed);
                    case "restart":
                        svc.InvokeMethod("StopService", null);
                        Thread.Sleep(2000);
                        svc.InvokeMethod("StartService", null);
                        return ActionExecutionResult.Success(device.DisplayName, "Restart Service", $"Restarted '{serviceName}'.", sw.Elapsed);
                    case "startmode":
                        using (var inParams = svc.GetMethodParameters("ChangeStartMode"))
                        {
                            inParams["StartMode"] = newStartMode ?? "Automatic";
                            svc.InvokeMethod("ChangeStartMode", inParams, null);
                        }
                        return ActionExecutionResult.Success(device.DisplayName, "Change Startup Type", $"Changed '{serviceName}' startup to {newStartMode}.", sw.Elapsed);
                    default:
                        throw new ArgumentException($"Unsupported service operation '{operation}'.", nameof(operation));
                }
            }, ct);
        }

        public async Task<IReadOnlyList<DriverInfo>> GetDriversAsync(TargetDevice device, CancellationToken ct = default)
        {
            var rows = await _wmi.QueryAsync(device.ConnectAddress, @"root\cimv2",
                "SELECT FriendlyName, DeviceName, DeviceClass, Manufacturer, DriverVersion, DriverDate, IsSigned, InfName FROM Win32_PnPSignedDriver",
                ct: ct).ConfigureAwait(false);

            return rows.Select(r =>
            {
                var friendly = WmiClient.GetString(r, "FriendlyName");
                if (string.IsNullOrWhiteSpace(friendly)) friendly = WmiClient.GetString(r, "DeviceName");
                return new DriverInfo
                {
                    DeviceName = device.DisplayName,
                    FriendlyName = friendly,
                    DeviceClass = WmiClient.GetString(r, "DeviceClass"),
                    Manufacturer = WmiClient.GetString(r, "Manufacturer"),
                    DriverVersion = WmiClient.GetString(r, "DriverVersion"),
                    DriverDate = WmiClient.GetDmtfDateTime(r, "DriverDate"),
                    IsSigned = WmiClient.GetBool(r, "IsSigned"),
                    InfName = WmiClient.GetString(r, "InfName")
                };
            }).Where(d => !string.IsNullOrWhiteSpace(d.FriendlyName))
              .OrderBy(d => d.FriendlyName, StringComparer.OrdinalIgnoreCase)
              .ToList();
        }

        public async Task<IReadOnlyList<UserProfileInfo>> GetUserProfilesAsync(TargetDevice device, CancellationToken ct = default)
        {
            var rows = await _wmi.QueryAsync(device.ConnectAddress, @"root\cimv2",
                "SELECT SID, LocalPath, LastUseTime, Loaded, Special, RoamingConfigured FROM Win32_UserProfile", ct: ct).ConfigureAwait(false);

            return rows.Select(r =>
            {
                var sid = WmiClient.GetString(r, "SID");
                var localPath = WmiClient.GetString(r, "LocalPath");
                string account = TryTranslateSid(sid);
                if (string.IsNullOrWhiteSpace(account) && !string.IsNullOrWhiteSpace(localPath))
                {
                    account = System.IO.Path.GetFileName(localPath);
                }

                return new UserProfileInfo
                {
                    DeviceName = device.DisplayName,
                    Sid = sid,
                    AccountName = account,
                    LocalPath = localPath,
                    LastUseTime = WmiClient.GetDmtfDateTime(r, "LastUseTime"),
                    Loaded = WmiClient.GetBool(r, "Loaded"),
                    Special = WmiClient.GetBool(r, "Special"),
                    RoamingConfigured = WmiClient.GetBool(r, "RoamingConfigured")
                };
            }).OrderBy(p => p.AccountName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public Task<ActionExecutionResult> DeleteUserProfileAsync(TargetDevice device, string sid, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                ct.ThrowIfCancellationRequested();
                var scope = _wmi.CreateScope(device.ConnectAddress, @"root\cimv2");
                var safeSid = sid.Replace("'", "\\'");
                using var profile = new ManagementObject(scope, new ManagementPath($"Win32_UserProfile.SID='{safeSid}'"), null);
                profile.Delete();
                return ActionExecutionResult.Success(device.DisplayName, "Delete User Profile", $"Deleted profile {sid}.", sw.Elapsed);
            }, ct);
        }

        public Task<IReadOnlyList<LocalGroupMemberInfo>> GetLocalGroupMembersAsync(TargetDevice device, string groupName, CancellationToken ct = default)
        {
            return Task.Run<IReadOnlyList<LocalGroupMemberInfo>>(() =>
            {
                ct.ThrowIfCancellationRequested();
                var results = new List<LocalGroupMemberInfo>();
                var host = device.ConnectAddress;
                using var group = new DirectoryEntry($"WinNT://{host}/{groupName},group");
                var members = group.Invoke("Members");
                if (members is System.Collections.IEnumerable enumerable)
                {
                    foreach (var m in enumerable)
                    {
                        using var memberEntry = new DirectoryEntry(m);
                        var pathParts = (memberEntry.Path ?? string.Empty).Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                        var domain = pathParts.Length >= 3 ? pathParts[pathParts.Length - 2] : string.Empty;
                        results.Add(new LocalGroupMemberInfo
                        {
                            DeviceName = device.DisplayName,
                            GroupName = groupName,
                            Domain = domain,
                            MemberName = memberEntry.Name,
                            MemberType = memberEntry.SchemaClassName
                        });
                    }
                }
                return results.OrderBy(x => x.FullName, StringComparer.OrdinalIgnoreCase).ToList();
            }, ct);
        }

        public Task<ActionExecutionResult> ModifyLocalGroupMemberAsync(TargetDevice device, string groupName, string accountName, bool add, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                ct.ThrowIfCancellationRequested();
                var host = device.ConnectAddress;
                var normalized = accountName.Trim().Replace('\\', '/');
                if (!normalized.Contains('/'))
                {
                    normalized = $"{host}/{normalized}";
                }

                using var group = new DirectoryEntry($"WinNT://{host}/{groupName},group");
                group.Invoke(add ? "Add" : "Remove", new object[] { $"WinNT://{normalized}" });
                group.CommitChanges();

                var verb = add ? "Added" : "Removed";
                return ActionExecutionResult.Success(device.DisplayName, "Modify Local Group",
                    $"{verb} '{accountName}' {(add ? "to" : "from")} '{groupName}'.", sw.Elapsed);
            }, ct);
        }

        public async Task<IReadOnlyList<BatteryInfo>> GetBatteriesAsync(TargetDevice device, CancellationToken ct = default)
        {
            var host = device.ConnectAddress;
            var rows = await _wmi.QueryAsync(host, @"root\cimv2",
                "SELECT Name, Status, EstimatedChargeRemaining, EstimatedRunTime, BatteryStatus FROM Win32_Battery", ct: ct).ConfigureAwait(false);

            int? designCap = null;
            int? fullCap = null;
            try
            {
                var staticRows = await _wmi.QueryAsync(host, @"root\wmi", "SELECT DesignedCapacity FROM BatteryStaticData", ct: ct).ConfigureAwait(false);
                var fullRows = await _wmi.QueryAsync(host, @"root\wmi", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity", ct: ct).ConfigureAwait(false);
                if (staticRows.Count > 0) designCap = WmiClient.GetInt32(staticRows[0], "DesignedCapacity");
                if (fullRows.Count > 0) fullCap = WmiClient.GetInt32(fullRows[0], "FullChargedCapacity");
            }
            catch { }

            return rows.Select(r => new BatteryInfo
            {
                DeviceName = device.DisplayName,
                Name = WmiClient.GetString(r, "Name"),
                Status = WmiClient.GetString(r, "Status"),
                EstimatedChargeRemaining = WmiClient.GetInt32(r, "EstimatedChargeRemaining"),
                EstimatedRunTimeMinutes = WmiClient.GetInt32(r, "EstimatedRunTime"),
                BatteryStatusDescription = MapBatteryStatus(WmiClient.GetInt32(r, "BatteryStatus")),
                DesignCapacityMwh = designCap,
                FullChargeCapacityMwh = fullCap
            }).ToList();
        }

        private static void ReadUninstallHive(
            List<InstalledApplicationInfo> output,
            string deviceName,
            RegistryKey root,
            string subKeyPath,
            string architecture,
            CancellationToken ct)
        {
            using var uninstallKey = root.OpenSubKey(subKeyPath);
            if (uninstallKey == null) return;

            foreach (var subName in uninstallKey.GetSubKeyNames())
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var appKey = uninstallKey.OpenSubKey(subName);
                    if (appKey == null) continue;

                    var displayName = appKey.GetValue("DisplayName") as string;
                    if (string.IsNullOrWhiteSpace(displayName)) continue;

                    var sysComp = appKey.GetValue("SystemComponent");
                    if (sysComp != null && Convert.ToInt32(sysComp) == 1) continue;

                    var installDateRaw = appKey.GetValue("InstallDate") as string;
                    var formattedDate = FormatInstallDate(installDateRaw);

                    output.Add(new InstalledApplicationInfo
                    {
                        DeviceName = deviceName,
                        Name = displayName!.Trim(),
                        Architecture = architecture,
                        Publisher = (appKey.GetValue("Publisher") as string ?? string.Empty).Trim(),
                        InstalledOn = formattedDate,
                        Version = (appKey.GetValue("DisplayVersion") as string ?? string.Empty).Trim(),
                        UninstallString = (appKey.GetValue("UninstallString") as string ?? string.Empty).Trim(),
                        QuietUninstallString = (appKey.GetValue("QuietUninstallString") as string ?? string.Empty).Trim(),
                        InstallLocation = (appKey.GetValue("InstallLocation") as string ?? string.Empty).Trim(),
                        RegistryKeyPath = $@"HKLM\{subKeyPath}\{subName}"
                    });
                }
                catch
                {
                    // Skip unreadable registry subkey
                }
            }
        }

        private static string FormatInstallDate(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "n/a";
            if (DateTime.TryParseExact(raw!.Trim(), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                return dt.ToString("yyyy-MM-dd");
            }
            return raw.Trim();
        }

        private static string BuildSilentUninstallCommand(string uninstallString)
        {
            if (string.IsNullOrWhiteSpace(uninstallString)) return string.Empty;
            var trimmed = uninstallString.Trim();
            if (trimmed.IndexOf("msiexec", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var updated = trimmed.Replace("/I", "/X").Replace("/i", "/X");
                if (updated.IndexOf("/qn", StringComparison.OrdinalIgnoreCase) < 0 &&
                    updated.IndexOf("/quiet", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    updated += " /qn /norestart";
                }
                return updated;
            }
            return trimmed;
        }

        private static string TryTranslateSid(string sid)
        {
            try
            {
                var secId = new SecurityIdentifier(sid);
                return secId.Translate(typeof(NTAccount)).Value;
            }
            catch
            {
                return sid;
            }
        }

        private static string MapDriveType(int driveType) => driveType switch
        {
            2 => "Removable",
            3 => "Local Fixed Disk",
            4 => "Network Drive",
            5 => "CD/DVD",
            6 => "RAM Disk",
            _ => "Unknown"
        };

        private static string MapBatteryStatus(int status) => status switch
        {
            1 => "Discharging",
            2 => "AC Connected",
            3 => "Fully Charged",
            4 => "Low",
            5 => "Critical",
            6 => "Charging",
            7 => "Charging & High",
            8 => "Charging & Low",
            9 => "Charging & Critical",
            _ => "Unknown"
        };
    }
}
