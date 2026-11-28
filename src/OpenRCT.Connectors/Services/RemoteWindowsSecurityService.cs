using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using OpenRCT.Connectors.Wmi;
using OpenRCT.Core.Interfaces;
using OpenRCT.Core.Models;

namespace OpenRCT.Connectors.Services
{
    /// <summary>
    /// Queries and manages remote Windows Security posture across BitLocker, TPM, Firmware/Secure Boot,
    /// Virtualization-Based Security (VBS), Windows Defender, Defender Exclusions, and Windows Firewall.
    /// Pure native WMI/CIM (zero PowerShell / zero external binaries).
    /// </summary>
    public sealed class RemoteWindowsSecurityService : IRemoteWindowsSecurityService
    {
        private const uint HklmHive = 0x80000002;
        private readonly WmiClient _wmi;

        public RemoteWindowsSecurityService(WmiClient wmi)
        {
            _wmi = wmi ?? throw new ArgumentNullException(nameof(wmi));
        }

        // =====================================================================
        // 1. BitLocker
        // =====================================================================
        public Task<IReadOnlyList<BitLockerVolumeInfo>> GetBitLockerVolumesAsync(TargetDevice device, CancellationToken ct = default)
        {
            return Task.Run<IReadOnlyList<BitLockerVolumeInfo>>(() =>
            {
                ct.ThrowIfCancellationRequested();
                var list = new List<BitLockerVolumeInfo>();
                var scope = _wmi.CreateScope(device.ConnectAddress, @"root\cimv2\Security\MicrosoftVolumeEncryption");
                using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM Win32_EncryptableVolume"));
                using var results = searcher.Get();

                foreach (ManagementObject vol in results)
                {
                    using (vol)
                    {
                        var drive = vol["DriveLetter"]?.ToString() ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(drive))
                        {
                            drive = vol["DeviceID"]?.ToString() ?? "(No Letter)";
                        }

                        var volTypeCode = TryConvertInt(vol["VolumeType"]);
                        var protectionStatus = "Unknown";
                        var conversionStatus = "Unknown";
                        var encryptionPct = 0;
                        var encryptionMethod = "None";
                        var lockStatus = "Unknown";
                        var protectors = new List<string>();

                        try
                        {
                            using var outProt = vol.InvokeMethod("GetProtectionStatus", null, null);
                            protectionStatus = MapProtectionStatus(TryConvertInt(outProt?["ProtectionStatus"]));
                        }
                        catch { }

                        try
                        {
                            using var outConv = vol.InvokeMethod("GetConversionStatus", null, null);
                            conversionStatus = MapConversionStatus(TryConvertInt(outConv?["ConversionStatus"]));
                            encryptionPct = TryConvertInt(outConv?["EncryptionPercentage"]);
                        }
                        catch { }

                        try
                        {
                            using var outEnc = vol.InvokeMethod("GetEncryptionMethod", null, null);
                            encryptionMethod = MapEncryptionMethod(TryConvertInt(outEnc?["EncryptionMethod"]));
                        }
                        catch { }

                        try
                        {
                            using var outLock = vol.InvokeMethod("GetLockStatus", null, null);
                            lockStatus = TryConvertInt(outLock?["LockStatus"]) == 1 ? "Locked" : "Unlocked";
                        }
                        catch { }

                        try
                        {
                            using var inParams = vol.GetMethodParameters("GetKeyProtectors");
                            inParams["KeyProtectorType"] = (uint)0;
                            using var outKp = vol.InvokeMethod("GetKeyProtectors", inParams, null);
                            if (outKp?["VolumeKeyProtectorID"] is string[] kpIds)
                            {
                                foreach (var kpId in kpIds)
                                {
                                    try
                                    {
                                        using var typeIn = vol.GetMethodParameters("GetKeyProtectorType");
                                        typeIn["VolumeKeyProtectorID"] = kpId;
                                        using var typeOut = vol.InvokeMethod("GetKeyProtectorType", typeIn, null);
                                        var label = MapKeyProtectorType(TryConvertInt(typeOut?["KeyProtectorType"]));
                                        if (!protectors.Contains(label)) protectors.Add(label);
                                    }
                                    catch { }
                                }
                            }
                        }
                        catch { }

                        list.Add(new BitLockerVolumeInfo
                        {
                            DeviceName = device.DisplayName,
                            DriveLetter = drive,
                            VolumeType = volTypeCode switch
                            {
                                0 => "OS Volume",
                                1 => "Fixed Data",
                                2 => "Removable",
                                _ => "Volume"
                            },
                            ProtectionStatus = protectionStatus,
                            ConversionStatus = conversionStatus,
                            EncryptionPercentage = encryptionPct,
                            EncryptionMethod = encryptionMethod,
                            LockStatus = lockStatus,
                            KeyProtectors = protectors.Count > 0 ? string.Join(", ", protectors) : "None"
                        });
                    }
                }

                return list;
            }, ct);
        }

        public Task<ActionExecutionResult> SetBitLockerProtectionAsync(TargetDevice device, string driveLetter, bool suspend, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                var actionName = suspend ? "Suspend BitLocker" : "Resume BitLocker";
                try
                {
                    ct.ThrowIfCancellationRequested();
                    var scope = _wmi.CreateScope(device.ConnectAddress, @"root\cimv2\Security\MicrosoftVolumeEncryption");
                    using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery($"SELECT * FROM Win32_EncryptableVolume WHERE DriveLetter = '{EscapeWql(driveLetter)}'"));
                    using var results = searcher.Get();
                    ManagementObject? vol = null;
                    foreach (ManagementObject obj in results)
                    {
                        vol = obj;
                        break;
                    }

                    if (vol == null)
                    {
                        return ActionExecutionResult.Failure(device.DisplayName, actionName, $"Volume '{driveLetter}' not found.", null, sw.Elapsed);
                    }

                    using (vol)
                    {
                        if (suspend)
                        {
                            using var inParams = vol.GetMethodParameters("DisableKeyProtectors");
                            inParams["RebootCount"] = (uint)1;
                            using var outParams = vol.InvokeMethod("DisableKeyProtectors", inParams, null);
                            var rc = TryConvertInt(outParams?["ReturnValue"]);
                            if (rc != 0)
                            {
                                return ActionExecutionResult.Failure(device.DisplayName, actionName, $"DisableKeyProtectors returned 0x{rc:X8}.", null, sw.Elapsed);
                            }
                            return ActionExecutionResult.Success(device.DisplayName, actionName, $"BitLocker suspended on {driveLetter} (1 reboot).", sw.Elapsed);
                        }
                        else
                        {
                            using var outParams = vol.InvokeMethod("EnableKeyProtectors", null, null);
                            var rc = TryConvertInt(outParams?["ReturnValue"]);
                            if (rc != 0)
                            {
                                return ActionExecutionResult.Failure(device.DisplayName, actionName, $"EnableKeyProtectors returned 0x{rc:X8}.", null, sw.Elapsed);
                            }
                            return ActionExecutionResult.Success(device.DisplayName, actionName, $"BitLocker protection resumed on {driveLetter}.", sw.Elapsed);
                        }
                    }
                }
                catch (Exception ex)
                {
                    return ActionExecutionResult.Failure(device.DisplayName, actionName, ex.Message, ex.ToString(), sw.Elapsed);
                }
            }, ct);
        }

        // =====================================================================
        // 2. TPM
        // =====================================================================
        public Task<TpmStatusInfo> GetTpmStatusAsync(TargetDevice device, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var scope = _wmi.CreateScope(device.ConnectAddress, @"root\cimv2\Security\MicrosoftTpm");
                using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM Win32_Tpm"));
                using var results = searcher.Get();

                foreach (ManagementObject tpm in results)
                {
                    using (tpm)
                    {
                        var info = new TpmStatusInfo
                        {
                            DeviceName = device.DisplayName,
                            SpecVersion = tpm["SpecVersion"]?.ToString() ?? "Unknown",
                            IsActivated = TryConvertBool(tpm["IsActivated_InitialValue"]),
                            IsEnabled = TryConvertBool(tpm["IsEnabled_InitialValue"]),
                            IsOwned = TryConvertBool(tpm["IsOwned_InitialValue"]),
                            Manufacturer = DecodeTpmManufacturer(tpm["ManufacturerId"]),
                            ManufacturerVersion = tpm["ManufacturerVersion"]?.ToString() ?? string.Empty
                        };

                        try
                        {
                            using var outReady = tpm.InvokeMethod("IsReady", null, null);
                            if (outReady?["IsReady"] != null) info.IsReady = TryConvertBool(outReady["IsReady"]);
                        }
                        catch
                        {
                            info.IsReady = info.IsActivated && info.IsEnabled && info.IsOwned;
                        }

                        try
                        {
                            using var outFips = tpm.InvokeMethod("IsFips", null, null);
                            if (outFips?["IsFips"] != null) info.FipsReady = TryConvertBool(outFips["IsFips"]);
                        }
                        catch { }

                        try
                        {
                            using var outLock = tpm.InvokeMethod("IsLockedOut", null, null);
                            if (outLock?["IsLockedOut"] != null) info.LockedOut = TryConvertBool(outLock["IsLockedOut"]);
                        }
                        catch { }

                        return info;
                    }
                }

                return new TpmStatusInfo
                {
                    DeviceName = device.DisplayName,
                    SpecVersion = "Not Present"
                };
            }, ct);
        }

        public Task<ActionExecutionResult> ClearTpmAsync(TargetDevice device, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    ct.ThrowIfCancellationRequested();
                    var scope = _wmi.CreateScope(device.ConnectAddress, @"root\cimv2\Security\MicrosoftTpm");
                    using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM Win32_Tpm"));
                    using var results = searcher.Get();
                    foreach (ManagementObject tpm in results)
                    {
                        using (tpm)
                        {
                            // Request 5 = Clear TPM (requires physical presence on reboot if configured)
                            using var inParams = tpm.GetMethodParameters("SetPhysicalPresenceRequest");
                            inParams["Request"] = (uint)5;
                            using var outParams = tpm.InvokeMethod("SetPhysicalPresenceRequest", inParams, null);
                            var rc = TryConvertInt(outParams?["ReturnValue"]);
                            if (rc != 0)
                            {
                                return ActionExecutionResult.Failure(device.DisplayName, "Clear TPM", $"SetPhysicalPresenceRequest(5) returned 0x{rc:X8}.", null, sw.Elapsed);
                            }
                            return ActionExecutionResult.Success(device.DisplayName, "Clear TPM", "TPM Clear request submitted (reboot required to complete).", sw.Elapsed);
                        }
                    }

                    return ActionExecutionResult.Failure(device.DisplayName, "Clear TPM", "No TPM instance found on target device.", null, sw.Elapsed);
                }
                catch (Exception ex)
                {
                    return ActionExecutionResult.Failure(device.DisplayName, "Clear TPM", ex.Message, ex.ToString(), sw.Elapsed);
                }
            }, ct);
        }

        // =====================================================================
        // 3. Firmware & Secure Boot (including 2023 CA Certificate Readiness)
        // =====================================================================
        public async Task<FirmwareSecurityInfo> GetFirmwareSecurityAsync(TargetDevice device, CancellationToken ct = default)
        {
            var host = device.ConnectAddress;
            var info = new FirmwareSecurityInfo { DeviceName = device.DisplayName };

            // BIOS Version
            try
            {
                var biosRows = await _wmi.QueryAsync(host, @"root\cimv2", "SELECT SMBIOSBIOSVersion FROM Win32_BIOS", ct: ct).ConfigureAwait(false);
                var bios = biosRows.FirstOrDefault();
                if (bios != null)
                {
                    info.BiosVersion = WmiClient.GetString(bios, "SMBIOSBIOSVersion");
                }
            }
            catch { }

            // Secure Boot & 2023 CA Registry state via WMI StdRegProv (works without RemoteRegistry service)
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var scope = _wmi.CreateScope(host, @"root\default");
                using var reg = new ManagementClass(scope, new ManagementPath("StdRegProv"), null);

                var secureBootVal = ReadRegDword(reg, @"SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled");
                if (secureBootVal.HasValue)
                {
                    info.UefiEnabled = true;
                    info.SecureBootEnabled = secureBootVal.Value == 1;
                }
                else
                {
                    var fwType = ReadRegDword(reg, @"SYSTEM\CurrentControlSet\Control", "PEFirmwareType");
                    info.UefiEnabled = fwType == 2;
                    info.SecureBootEnabled = false;
                }

                const string servicingPath = @"SYSTEM\CurrentControlSet\Control\SecureBoot\Servicing";
                const string deviceAttrPath = @"SYSTEM\CurrentControlSet\Control\SecureBoot\Servicing\DeviceAttributes";

                var caStatus = ReadRegString(reg, servicingPath, "UEFICA2023Status");
                var caCapable = ReadRegDword(reg, servicingPath, "WindowsUEFICA2023Capable");
                var caError = ReadRegDword(reg, servicingPath, "UEFICA2023Error");
                var caErrorEvent = ReadRegDword(reg, servicingPath, "UEFICA2023ErrorEvent");
                var confidence = ReadRegString(reg, servicingPath, "ConfidenceLevel")
                              ?? ReadRegString(reg, deviceAttrPath, "BucketConfidenceLevel")
                              ?? string.Empty;

                info.CertificateStatus = !string.IsNullOrWhiteSpace(caStatus) ? caStatus! : "Unknown";

                var availableUpdates = ReadRegDword(reg, @"SYSTEM\CurrentControlSet\Control\SecureBoot", "AvailableUpdates");
                if (caCapable == 2)
                {
                    info.CertificateUpdateReadiness = "Updated (2023 CA Active)";
                }
                else if (availableUpdates.HasValue && availableUpdates.Value != 0)
                {
                    info.CertificateUpdateReadiness = $"Ready To Update (0x{availableUpdates.Value:X})";
                }
                else if (caCapable == 1)
                {
                    info.CertificateUpdateReadiness = "In Progress (DB Updated)";
                }
                else
                {
                    info.CertificateUpdateReadiness = "Not Ready (0x0)";
                }

                if (caError.HasValue && caError.Value != 0)
                {
                    info.CertificateError = $"0x{caError.Value:X8}";
                }
                if (caErrorEvent.HasValue && caErrorEvent.Value != 0)
                {
                    info.CertificateErrorEvent = caErrorEvent.Value.ToString();
                }
                info.ConfidenceLevel = confidence;
            }, ct).ConfigureAwait(false);

            return info;
        }

        public async Task<ActionExecutionResult> RunSecureBootTaskAsync(TargetDevice device, CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var pid = await _wmi.CreateRemoteProcessAsync(
                    device.ConnectAddress,
                    @"schtasks.exe /Run /TN ""\Microsoft\Windows\PI\Secure-Boot-Update""",
                    ct: ct).ConfigureAwait(false);

                return ActionExecutionResult.Success(device.DisplayName, "Run Secure Boot Task", $"Triggered Secure-Boot-Update scheduled task (PID {pid}).", sw.Elapsed);
            }
            catch (Exception ex)
            {
                return ActionExecutionResult.Failure(device.DisplayName, "Run Secure Boot Task", ex.Message, ex.ToString(), sw.Elapsed);
            }
        }

        // =====================================================================
        // 4. Virtualization-Based Security (VBS)
        // =====================================================================
        public async Task<VbsStatusInfo> GetVbsStatusAsync(TargetDevice device, CancellationToken ct = default)
        {
            var rows = await _wmi.QueryAsync(
                device.ConnectAddress,
                @"root\Microsoft\Windows\DeviceGuard",
                "SELECT VirtualizationBasedSecurityStatus, SecurityServicesRunning, SecurityServicesConfigured, RequiredSecurityProperties FROM Win32_DeviceGuard",
                ct: ct).ConfigureAwait(false);

            var row = rows.FirstOrDefault();
            if (row == null)
            {
                return new VbsStatusInfo { DeviceName = device.DisplayName, VbsStatus = "Not Available" };
            }

            var status = WmiClient.GetInt32(row, "VirtualizationBasedSecurityStatus");
            var running = ReadUIntArray(row, "SecurityServicesRunning");
            var configured = ReadUIntArray(row, "SecurityServicesConfigured");
            var reqProps = ReadUIntArray(row, "RequiredSecurityProperties");

            string MapService(uint id) => id switch
            {
                1 => "Credential Guard",
                2 => "HVCI (Memory Integrity)",
                3 => "System Guard Secure Launch",
                4 => "SMM Firmware Measurement",
                _ => $"Service {id}"
            };

            string MapProp(uint id) => id switch
            {
                1 => "Base Virtualization",
                2 => "Secure Boot",
                3 => "DMA Protection",
                4 => "Secure Memory Overwrite",
                5 => "UEFI Code Readonly",
                6 => "SMM Security Mitigations",
                7 => "Mode-Based Execution Control",
                _ => $"Prop {id}"
            };

            return new VbsStatusInfo
            {
                DeviceName = device.DisplayName,
                VbsStatus = status switch
                {
                    0 => "Not Enabled",
                    1 => "Enabled (Not Running)",
                    2 => "Running",
                    _ => $"Status {status}"
                },
                CredentialGuardStatus = running.Contains(1u)
                    ? "Running"
                    : (configured.Contains(1u) ? "Configured (Not Running)" : "Not Configured"),
                HvciStatus = running.Contains(2u)
                    ? "Running"
                    : (configured.Contains(2u) ? "Configured (Not Running)" : "Not Configured"),
                RunningServices = running.Count > 0 ? string.Join(", ", running.Select(MapService)) : "None",
                ConfiguredServices = configured.Count > 0 ? string.Join(", ", configured.Select(MapService)) : "None",
                RequiredProperties = reqProps.Count > 0 ? string.Join(", ", reqProps.Select(MapProp)) : "None"
            };
        }

        // =====================================================================
        // 5. Windows Defender Status
        // =====================================================================
        public async Task<DefenderStatusInfo> GetDefenderStatusAsync(TargetDevice device, CancellationToken ct = default)
        {
            var rows = await _wmi.QueryAsync(
                device.ConnectAddress,
                @"root\Microsoft\Windows\Defender",
                "SELECT AntivirusEnabled, RealTimeProtectionEnabled, AntivirusSignatureVersion, AMProductVersion, AMEngineVersion, AntivirusSignatureLastUpdated, FullScanEndTime, QuickScanEndTime FROM MSFT_MpComputerStatus",
                ct: ct).ConfigureAwait(false);

            var row = rows.FirstOrDefault();
            if (row == null)
            {
                return new DefenderStatusInfo { DeviceName = device.DisplayName };
            }

            return new DefenderStatusInfo
            {
                DeviceName = device.DisplayName,
                AntivirusEnabled = WmiClient.GetBool(row, "AntivirusEnabled"),
                RealTimeProtectionEnabled = WmiClient.GetBool(row, "RealTimeProtectionEnabled"),
                SignatureVersion = WmiClient.GetString(row, "AntivirusSignatureVersion"),
                ClientVersion = WmiClient.GetString(row, "AMProductVersion"),
                EngineVersion = WmiClient.GetString(row, "AMEngineVersion"),
                LastSignatureUpdate = WmiClient.GetDmtfDateTime(row, "AntivirusSignatureLastUpdated"),
                LastFullScan = WmiClient.GetDmtfDateTime(row, "FullScanEndTime"),
                LastQuickScan = WmiClient.GetDmtfDateTime(row, "QuickScanEndTime")
            };
        }

        // =====================================================================
        // 6. Windows Defender Exclusions
        // =====================================================================
        public async Task<IReadOnlyList<DefenderExclusionInfo>> GetDefenderExclusionsAsync(TargetDevice device, CancellationToken ct = default)
        {
            var rows = await _wmi.QueryAsync(
                device.ConnectAddress,
                @"root\Microsoft\Windows\Defender",
                "SELECT ExclusionPath, ExclusionExtension, ExclusionProcess, ExclusionIpAddress FROM MSFT_MpPreference",
                ct: ct).ConfigureAwait(false);

            var list = new List<DefenderExclusionInfo>();
            var row = rows.FirstOrDefault();
            if (row == null) return list;

            void AddEntries(string col, string typeLabel)
            {
                if (row.TryGetValue(col, out var val) && val is string[] arr)
                {
                    foreach (var s in arr.Where(x => !string.IsNullOrWhiteSpace(x)))
                    {
                        list.Add(new DefenderExclusionInfo
                        {
                            DeviceName = device.DisplayName,
                            ExclusionType = typeLabel,
                            Value = s.Trim()
                        });
                    }
                }
            }

            AddEntries("ExclusionPath", "Path");
            AddEntries("ExclusionExtension", "Extension");
            AddEntries("ExclusionProcess", "Process");
            AddEntries("ExclusionIpAddress", "IP Address");

            return list;
        }

        // =====================================================================
        // 7. Windows Firewall Profiles
        // =====================================================================
        public async Task<IReadOnlyList<FirewallProfileInfo>> GetFirewallProfilesAsync(TargetDevice device, CancellationToken ct = default)
        {
            // Query ActiveStore (merged local + GPO/MDM effective policy) rather than the
            // provider's default PersistentStore, which ignores Group Policy / MDM overrides.
            var rows = await _wmi.QueryAsync(
                device.ConnectAddress,
                @"root\StandardCimv2",
                "SELECT Name, Enabled, DefaultInboundAction, DefaultOutboundAction, LogFileName FROM MSFT_NetFirewallProfile",
                ct: ct,
                context: new Dictionary<string, object?> { ["PolicyStore"] = "ActiveStore" }).ConfigureAwait(false);

            return rows.Select(r => new FirewallProfileInfo
            {
                DeviceName = device.DisplayName,
                ProfileName = WmiClient.GetString(r, "Name"),
                Enabled = WmiClient.GetInt32(r, "Enabled") == 1,
                DefaultInboundAction = MapFirewallAction(WmiClient.GetInt32(r, "DefaultInboundAction")),
                DefaultOutboundAction = MapFirewallAction(WmiClient.GetInt32(r, "DefaultOutboundAction")),
                LogFileName = WmiClient.GetString(r, "LogFileName")
            }).ToList();
        }

        // =====================================================================
        // 8. Windows Firewall Rules
        // =====================================================================
        public async Task<IReadOnlyList<FirewallRuleInfo>> GetFirewallRulesAsync(TargetDevice device, CancellationToken ct = default)
        {
            var rows = await _wmi.QueryAsync(
                device.ConnectAddress,
                @"root\StandardCimv2",
                "SELECT InstanceID, DisplayName, DisplayGroup, Action, Direction, Profiles, Enabled FROM MSFT_NetFirewallRule",
                ct: ct).ConfigureAwait(false);

            return rows.Select(r => new FirewallRuleInfo
            {
                DeviceName = device.DisplayName,
                RuleId = WmiClient.GetString(r, "InstanceID"),
                DisplayName = WmiClient.GetString(r, "DisplayName"),
                Group = WmiClient.GetString(r, "DisplayGroup"),
                Action = MapFirewallAction(WmiClient.GetInt32(r, "Action")),
                Direction = WmiClient.GetInt32(r, "Direction") == 2 ? "Outbound" : "Inbound",
                Profiles = MapFirewallProfiles(WmiClient.GetInt32(r, "Profiles")),
                Enabled = WmiClient.GetInt32(r, "Enabled") == 1
            }).ToList();
        }

        public Task<ActionExecutionResult> SetFirewallRuleEnabledAsync(TargetDevice device, string ruleId, bool enabled, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                var actionName = enabled ? "Enable Firewall Rule" : "Disable Firewall Rule";
                try
                {
                    ct.ThrowIfCancellationRequested();
                    var scope = _wmi.CreateScope(device.ConnectAddress, @"root\StandardCimv2");
                    using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery($"SELECT * FROM MSFT_NetFirewallRule WHERE InstanceID = '{EscapeWql(ruleId)}'"));
                    using var results = searcher.Get();
                    foreach (ManagementObject rule in results)
                    {
                        using (rule)
                        {
                            rule["Enabled"] = (ushort)(enabled ? 1 : 2);
                            rule.Put();
                            var name = rule["DisplayName"]?.ToString() ?? ruleId;
                            return ActionExecutionResult.Success(device.DisplayName, actionName, $"{(enabled ? "Enabled" : "Disabled")} firewall rule '{name}'.", sw.Elapsed);
                        }
                    }
                    return ActionExecutionResult.Failure(device.DisplayName, actionName, $"Firewall rule '{ruleId}' not found.", null, sw.Elapsed);
                }
                catch (Exception ex)
                {
                    return ActionExecutionResult.Failure(device.DisplayName, actionName, ex.Message, ex.ToString(), sw.Elapsed);
                }
            }, ct);
        }

        // =====================================================================
        // Helpers
        // =====================================================================
        private static uint? ReadRegDword(ManagementClass reg, string subKey, string valueName)
        {
            try
            {
                using var inParams = reg.GetMethodParameters("GetDWORDValue");
                inParams["hDefKey"] = HklmHive;
                inParams["sSubKeyName"] = subKey;
                inParams["sValueName"] = valueName;
                using var outParams = reg.InvokeMethod("GetDWORDValue", inParams, null);
                if (outParams != null && TryConvertInt(outParams["ReturnValue"]) == 0 && outParams["uValue"] != null)
                {
                    return Convert.ToUInt32(outParams["uValue"]);
                }
            }
            catch { }
            return null;
        }

        private static string? ReadRegString(ManagementClass reg, string subKey, string valueName)
        {
            try
            {
                using var inParams = reg.GetMethodParameters("GetStringValue");
                inParams["hDefKey"] = HklmHive;
                inParams["sSubKeyName"] = subKey;
                inParams["sValueName"] = valueName;
                using var outParams = reg.InvokeMethod("GetStringValue", inParams, null);
                if (outParams != null && TryConvertInt(outParams["ReturnValue"]) == 0)
                {
                    return outParams["sValue"]?.ToString();
                }
            }
            catch { }
            return null;
        }

        private static List<uint> ReadUIntArray(Dictionary<string, object?> row, string key)
        {
            var list = new List<uint>();
            if (!row.TryGetValue(key, out var val) || val == null) return list;
            if (val is Array arr)
            {
                foreach (var item in arr)
                {
                    try { list.Add(Convert.ToUInt32(item)); } catch { }
                }
            }
            return list;
        }

        private static int TryConvertInt(object? val)
        {
            if (val == null) return 0;
            try { return Convert.ToInt32(val); } catch { return 0; }
        }

        private static bool TryConvertBool(object? val)
        {
            if (val == null) return false;
            try { return Convert.ToBoolean(val); } catch { return false; }
        }

        private static string DecodeTpmManufacturer(object? val)
        {
            if (val == null) return string.Empty;
            try
            {
                var id = Convert.ToUInt32(val);
                var bytes = BitConverter.GetBytes(id);
                if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
                var ascii = System.Text.Encoding.ASCII.GetString(bytes).Trim('\0', ' ');
                return !string.IsNullOrWhiteSpace(ascii) ? ascii : $"0x{id:X8}";
            }
            catch
            {
                return val.ToString() ?? string.Empty;
            }
        }

        private static string MapProtectionStatus(int code) => code switch
        {
            0 => "Protection Off",
            1 => "Protection On",
            2 => "Protection Unknown",
            _ => $"Status {code}"
        };

        private static string MapConversionStatus(int code) => code switch
        {
            0 => "Fully Decrypted",
            1 => "Fully Encrypted",
            2 => "Encryption In Progress",
            3 => "Decryption In Progress",
            4 => "Encryption Paused",
            5 => "Decryption Paused",
            _ => $"Status {code}"
        };

        private static string MapEncryptionMethod(int code) => code switch
        {
            0 => "None",
            1 => "AES 128 Diffuser",
            2 => "AES 256 Diffuser",
            3 => "AES 128",
            4 => "AES 256",
            5 => "Hardware Encryption",
            6 => "XTS-AES 128",
            7 => "XTS-AES 256",
            _ => $"Method {code}"
        };

        private static string MapKeyProtectorType(int code) => code switch
        {
            1 => "TPM",
            2 => "External Key",
            3 => "Numerical Password",
            4 => "TPM + PIN",
            5 => "TPM + Startup Key",
            6 => "TPM + PIN + Startup Key",
            7 => "Public Key",
            8 => "Passphrase",
            9 => "TPM Certificate",
            10 => "SID Protector",
            _ => $"Type {code}"
        };

        private static string MapFirewallAction(int code) => code switch
        {
            0 => "Not Configured",
            2 => "Allow",
            3 => "Allow Bypass",
            4 => "Block",
            _ => $"Action {code}"
        };

        private static string MapFirewallProfiles(int mask)
        {
            if (mask == 0 || mask == 7) return "All";
            var parts = new List<string>();
            if ((mask & 1) != 0) parts.Add("Domain");
            if ((mask & 2) != 0) parts.Add("Private");
            if ((mask & 4) != 0) parts.Add("Public");
            if (parts.Count == 3) return "All";
            return parts.Count > 0 ? string.Join(" and ", parts) : "Any";
        }

        private static string EscapeWql(string value) => (value ?? string.Empty).Replace("\\", "\\\\").Replace("'", "\\'");
    }
}
