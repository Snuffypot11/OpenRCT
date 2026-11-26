using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using OpenRCT.Connectors.Wmi;
using OpenRCT.Core.Interfaces;
using OpenRCT.Core.Models;

namespace OpenRCT.Connectors.SmsProvider
{
    /// <summary>
    /// Connects to the MECM SMS Provider (\\Server\root\SMS\site_XXX) using standard WMI/WQL
    /// without requiring Microsoft ConfigMgr Console SDK binaries.
    /// </summary>
    public sealed class SmsProviderClient : ISmsProviderClient
    {
        private readonly WmiClient _wmi;

        public SmsProviderClient(WmiClient? wmi = null)
        {
            _wmi = wmi ?? new WmiClient();
        }

        private static string SiteNamespace(string siteCode) => $@"root\SMS\site_{siteCode.Trim()}";

        public bool TryAutoDiscover(out string server, out string siteCode)
        {
            server = string.Empty;
            siteCode = string.Empty;

            var registryPaths = new[]
            {
                @"SOFTWARE\Microsoft\ConfigMgr10\AdminUI\Connection",
                @"SOFTWARE\WOW6432Node\Microsoft\ConfigMgr10\AdminUI\Connection"
            };

            foreach (var regPath in registryPaths)
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(regPath)
                                 ?? Registry.LocalMachine.OpenSubKey(regPath);
                    if (key == null) continue;

                    var s = key.GetValue("Server") as string;
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        server = s!.Trim();
                        var rows = _wmi.QueryAsync(server, @"root\SMS", "SELECT SiteCode FROM SMS_ProviderLocation WHERE ProviderForLocalSite = TRUE")
                            .GetAwaiter().GetResult();
                        var sc = rows.Select(r => WmiClient.GetString(r, "SiteCode")).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
                        if (!string.IsNullOrWhiteSpace(sc))
                        {
                            siteCode = sc!;
                            return true;
                        }
                    }
                }
                catch
                {
                    // Continue searching
                }
            }

            return false;
        }

        public async Task<IReadOnlyList<SmsCollectionSummary>> GetDeviceCollectionsAsync(string server, string siteCode, CancellationToken ct = default)
        {
            var rows = await _wmi.QueryAsync(
                server,
                SiteNamespace(siteCode),
                "SELECT CollectionID, Name, MemberCount, Comment FROM SMS_Collection WHERE CollectionType = 2",
                ct: ct).ConfigureAwait(false);

            return rows.Select(r => new SmsCollectionSummary
            {
                CollectionId = WmiClient.GetString(r, "CollectionID"),
                Name = WmiClient.GetString(r, "Name"),
                MemberCount = WmiClient.GetInt32(r, "MemberCount"),
                Comment = WmiClient.GetString(r, "Comment")
            }).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public async Task<IReadOnlyList<TargetDevice>> GetCollectionMembersAsync(string server, string siteCode, string collectionId, CancellationToken ct = default)
        {
            var safeId = EscapeWql(collectionId);
            var rows = await _wmi.QueryAsync(
                server,
                SiteNamespace(siteCode),
                $"SELECT ResourceID, Name, Domain FROM SMS_FullCollectionMembership WHERE CollectionID = '{safeId}' AND ResourceType = 5",
                ct: ct).ConfigureAwait(false);

            return rows.Select(r => new TargetDevice
            {
                ResourceId = WmiClient.GetString(r, "ResourceID"),
                Name = WmiClient.GetString(r, "Name"),
                Domain = WmiClient.GetString(r, "Domain"),
                SiteCode = siteCode,
                SmsProviderServer = server,
                CollectionId = collectionId
            }).OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public async Task<IReadOnlyList<ConfigMgrDeviceRecord>> GetCollectionDeviceRecordsAsync(
            string server,
            string siteCode,
            IEnumerable<string> collectionIds,
            CancellationToken ct = default)
        {
            var ids = (collectionIds ?? Enumerable.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var ns = SiteNamespace(siteCode);
            var byResourceId = new Dictionary<string, ConfigMgrDeviceRecord>(StringComparer.OrdinalIgnoreCase);

            var resourceFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var colId in ids)
            {
                var members = await _wmi.QueryAsync(
                    server,
                    ns,
                    $"SELECT ResourceID FROM SMS_FullCollectionMembership WHERE CollectionID = '{EscapeWql(colId)}' AND ResourceType = 5",
                    ct: ct).ConfigureAwait(false);
                foreach (var m in members)
                {
                    var rid = WmiClient.GetString(m, "ResourceID");
                    if (!string.IsNullOrEmpty(rid)) resourceFilter.Add(rid);
                }
            }

            var sysRows = await _wmi.QueryAsync(
                server,
                ns,
                "SELECT ResourceID, Name, OperatingSystemNameandVersion, LastLogonUserName, LastLogonUserDomain, LastLogonTimestamp, ResourceDomainORWorkgroup, DistinguishedName, Client FROM SMS_R_System",
                ct: ct).ConfigureAwait(false);

            var summaryRows = await _wmi.QueryAsync(
                server,
                ns,
                "SELECT ResourceID, LastActiveTime FROM SMS_CombinedDeviceResources",
                ct: ct).ConfigureAwait(false);

            var lastCheckInMap = new Dictionary<string, DateTime?>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in summaryRows)
            {
                var rid = WmiClient.GetString(s, "ResourceID");
                if (!string.IsNullOrEmpty(rid))
                {
                    lastCheckInMap[rid] = WmiClient.GetDmtfDateTime(s, "LastActiveTime");
                }
            }

            foreach (var r in sysRows)
            {
                var rid = WmiClient.GetString(r, "ResourceID");
                if (ids.Count > 0 && !resourceFilter.Contains(rid)) continue;

                lastCheckInMap.TryGetValue(rid, out var lastCheckIn);
                byResourceId[rid] = new ConfigMgrDeviceRecord
                {
                    ResourceId = rid,
                    Name = WmiClient.GetString(r, "Name"),
                    OperatingSystem = WmiClient.GetString(r, "OperatingSystemNameandVersion"),
                    LastLogonUser = WmiClient.GetString(r, "LastLogonUserName"),
                    LastLogonDomain = WmiClient.GetString(r, "LastLogonUserDomain"),
                    LastLogonTime = WmiClient.GetDmtfDateTime(r, "LastLogonTimestamp"),
                    Domain = WmiClient.GetString(r, "ResourceDomainORWorkgroup"),
                    DistinguishedName = WmiClient.GetString(r, "DistinguishedName"),
                    LastCheckInTime = lastCheckIn,
                    ClientInstalled = WmiClient.GetInt32(r, "Client") == 1
                };
            }

            return byResourceId.Values.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public async Task<IReadOnlyList<DeviceCollectionMembershipInfo>> GetDeviceCollectionsForAssetAsync(
            string server,
            string siteCode,
            TargetDevice device,
            CancellationToken ct = default)
        {
            var ns = SiteNamespace(siteCode);
            var resourceId = device.ResourceId;
            if (string.IsNullOrWhiteSpace(resourceId))
            {
                var lookup = await _wmi.QueryAsync(server, ns,
                    $"SELECT ResourceID FROM SMS_R_System WHERE Name = '{EscapeWql(device.DisplayName)}'", ct: ct).ConfigureAwait(false);
                resourceId = lookup.Select(r => WmiClient.GetString(r, "ResourceID")).FirstOrDefault();
            }

            if (string.IsNullOrWhiteSpace(resourceId))
            {
                return Array.Empty<DeviceCollectionMembershipInfo>();
            }

            var membershipRows = await _wmi.QueryAsync(
                server,
                ns,
                $"SELECT CollectionID, IsDirect FROM SMS_FullCollectionMembership WHERE ResourceID = {resourceId}",
                ct: ct).ConfigureAwait(false);

            var results = new List<DeviceCollectionMembershipInfo>();
            foreach (var m in membershipRows)
            {
                var colId = WmiClient.GetString(m, "CollectionID");
                var isDirect = WmiClient.GetBool(m, "IsDirect");

                var colRows = await _wmi.QueryAsync(
                    server,
                    ns,
                    $"SELECT Name, MemberCount, Comment FROM SMS_Collection WHERE CollectionID = '{EscapeWql(colId)}'",
                    ct: ct).ConfigureAwait(false);

                var col = colRows.FirstOrDefault();
                var colName = col != null ? WmiClient.GetString(col, "Name", colId) : colId;

                var mwList = new List<MaintenanceWindowInfo>();
                try
                {
                    var settingsRows = await _wmi.QueryAsync(
                        server,
                        ns,
                        $"SELECT ServiceWindows FROM SMS_CollectionSettings WHERE CollectionID = '{EscapeWql(colId)}'",
                        ct: ct).ConfigureAwait(false);

                    foreach (var sr in settingsRows)
                    {
                        if (sr.TryGetValue("ServiceWindows", out var swObj) && swObj is ManagementBaseObject[] swArr)
                        {
                            foreach (var sw in swArr)
                            {
                                mwList.Add(new MaintenanceWindowInfo
                                {
                                    CollectionId = colId,
                                    CollectionName = colName,
                                    Name = sw["Name"]?.ToString() ?? string.Empty,
                                    Description = sw["Description"]?.ToString() ?? string.Empty,
                                    ServiceWindowType = MapServiceWindowType(Convert.ToInt32(sw["ServiceWindowType"] ?? 1)),
                                    IsEnabled = Convert.ToBoolean(sw["IsEnabled"] ?? true),
                                    DurationMinutes = Convert.ToInt32(sw["Duration"] ?? 0)
                                });
                            }
                        }
                    }
                }
                catch
                {
                    // Maintenance windows optional if RBAC restricts SMS_CollectionSettings
                }

                results.Add(new DeviceCollectionMembershipInfo
                {
                    DeviceName = device.DisplayName,
                    CollectionId = colId,
                    CollectionName = colName,
                    MembershipType = isDirect ? "Direct" : "Query / Include",
                    MemberCount = col != null ? WmiClient.GetInt32(col, "MemberCount") : 0,
                    Comment = col != null ? WmiClient.GetString(col, "Comment") : string.Empty,
                    MaintenanceWindows = mwList
                });
            }

            return results.OrderBy(r => r.CollectionName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public Task AddDevicesToCollectionAsync(
            string server,
            string siteCode,
            string collectionId,
            IEnumerable<TargetDevice> devices,
            CancellationToken ct = default)
        {
            return Task.Run(async () =>
            {
                ct.ThrowIfCancellationRequested();
                var ns = SiteNamespace(siteCode);
                var scope = _wmi.CreateScope(server, ns);
                using var collectionObj = new ManagementObject(scope, new ManagementPath($"SMS_Collection.CollectionID='{EscapeWql(collectionId)}'"), null);
                using var ruleClass = new ManagementClass(scope, new ManagementPath("SMS_CollectionRuleDirect"), null);

                foreach (var dev in devices)
                {
                    ct.ThrowIfCancellationRequested();
                    var rid = dev.ResourceId;
                    if (string.IsNullOrWhiteSpace(rid))
                    {
                        var lookup = await _wmi.QueryAsync(server, ns,
                            $"SELECT ResourceID FROM SMS_R_System WHERE Name = '{EscapeWql(dev.DisplayName)}'", ct: ct).ConfigureAwait(false);
                        rid = lookup.Select(r => WmiClient.GetString(r, "ResourceID")).FirstOrDefault();
                    }

                    if (string.IsNullOrWhiteSpace(rid))
                    {
                        throw new InvalidOperationException($"Could not resolve ConfigMgr ResourceID for '{dev.DisplayName}'.");
                    }

                    using var rule = ruleClass.CreateInstance();
                    rule["ResourceClassName"] = "SMS_R_System";
                    rule["RuleName"] = dev.DisplayName;
                    rule["ResourceID"] = uint.Parse(rid!);

                    using var inParams = collectionObj.GetMethodParameters("AddMembershipRule");
                    inParams["collectionRule"] = rule;
                    collectionObj.InvokeMethod("AddMembershipRule", inParams, null);
                }
            }, ct);
        }

        public Task DeleteDeviceAsync(string server, string siteCode, TargetDevice device, CancellationToken ct = default)
        {
            return Task.Run(async () =>
            {
                ct.ThrowIfCancellationRequested();
                var ns = SiteNamespace(siteCode);
                var rid = device.ResourceId;
                if (string.IsNullOrWhiteSpace(rid))
                {
                    var lookup = await _wmi.QueryAsync(server, ns,
                        $"SELECT ResourceID FROM SMS_R_System WHERE Name = '{EscapeWql(device.DisplayName)}'", ct: ct).ConfigureAwait(false);
                    rid = lookup.Select(r => WmiClient.GetString(r, "ResourceID")).FirstOrDefault();
                }

                if (string.IsNullOrWhiteSpace(rid))
                {
                    throw new InvalidOperationException($"Device '{device.DisplayName}' was not found in ConfigMgr.");
                }

                var scope = _wmi.CreateScope(server, ns);
                using var sysObj = new ManagementObject(scope, new ManagementPath($"SMS_R_System.ResourceID={rid}"), null);
                sysObj.Delete();
            }, ct);
        }

        public async Task<IReadOnlyList<SoftwareUpdateGroupInfo>> GetSoftwareUpdateGroupsAsync(string server, string siteCode, CancellationToken ct = default)
        {
            var rows = await _wmi.QueryAsync(
                server,
                SiteNamespace(siteCode),
                "SELECT CI_UniqueID, LocalizedDisplayName, NumberOfUpdates FROM SMS_AuthorizationList",
                ct: ct).ConfigureAwait(false);

            return rows.Select(r => new SoftwareUpdateGroupInfo
            {
                CiUniqueId = WmiClient.GetString(r, "CI_UniqueID"),
                Title = WmiClient.GetString(r, "LocalizedDisplayName"),
                UpdateCount = WmiClient.GetInt32(r, "NumberOfUpdates")
            }).OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public async Task<IReadOnlyList<RawDeviceUpdateRecord>> GetUpdateComplianceForCollectionAsync(
            string server,
            string siteCode,
            string collectionId,
            CancellationToken ct = default)
        {
            var ns = SiteNamespace(siteCode);
            var members = await GetCollectionMembersAsync(server, siteCode, collectionId, ct).ConfigureAwait(false);
            var memberByRid = members
                .Where(m => !string.IsNullOrWhiteSpace(m.ResourceId))
                .ToDictionary(m => m.ResourceId!, m => m.DisplayName, StringComparer.OrdinalIgnoreCase);

            if (memberByRid.Count == 0)
            {
                return Array.Empty<RawDeviceUpdateRecord>();
            }

            var updates = await _wmi.QueryAsync(
                server,
                ns,
                "SELECT CI_ID, ArticleID, LocalizedDisplayName, DatePosted, IsDeployed FROM SMS_SoftwareUpdate WHERE IsSuperseded = 0 AND IsExpired = 0",
                ct: ct).ConfigureAwait(false);

            var updateMap = new Dictionary<string, (string ArticleId, string Title, DateTime? DatePosted, bool IsDeployed)>(StringComparer.OrdinalIgnoreCase);
            foreach (var u in updates)
            {
                var ciId = WmiClient.GetString(u, "CI_ID");
                if (!string.IsNullOrEmpty(ciId))
                {
                    updateMap[ciId] = (
                        WmiClient.GetString(u, "ArticleID"),
                        WmiClient.GetString(u, "LocalizedDisplayName"),
                        WmiClient.GetDmtfDateTime(u, "DatePosted"),
                        WmiClient.GetBool(u, "IsDeployed")
                    );
                }
            }

            var complianceRows = await _wmi.QueryAsync(
                server,
                ns,
                "SELECT MachineID, CI_ID, Status FROM SMS_UpdateComplianceStatus",
                ct: ct).ConfigureAwait(false);

            var results = new List<RawDeviceUpdateRecord>();
            foreach (var row in complianceRows)
            {
                var rid = WmiClient.GetString(row, "MachineID");
                if (!memberByRid.TryGetValue(rid, out var devName)) continue;

                var ciId = WmiClient.GetString(row, "CI_ID");
                updateMap.TryGetValue(ciId, out var uMeta);

                results.Add(new RawDeviceUpdateRecord
                {
                    ResourceId = rid,
                    DeviceName = devName,
                    UpdateCiId = ciId,
                    ArticleId = uMeta.ArticleId ?? string.Empty,
                    UpdateTitle = uMeta.Title ?? $"CI {ciId}",
                    Classification = InferClassificationFromTitle(uMeta.Title),
                    DatePosted = uMeta.DatePosted,
                    IsDeployed = uMeta.IsDeployed,
                    StatusCode = WmiClient.GetInt32(row, "Status")
                });
            }

            return results;
        }

        public async Task<IReadOnlyList<StatusMessageEntry>> GetDeviceStatusMessagesAsync(
            string server,
            string siteCode,
            TargetDevice device,
            int daysBack = 7,
            CancellationToken ct = default)
        {
            var cutoff = ManagementDateTimeConverter.ToDmtfDateTime(DateTime.UtcNow.AddDays(-Math.Max(1, daysBack)));
            var rows = await _wmi.QueryAsync(
                server,
                SiteNamespace(siteCode),
                $"SELECT Time, Severity, MessageID, Component, SiteCode, MachineName FROM SMS_StatusMessage WHERE MachineName = '{EscapeWql(device.DisplayName)}' AND Time >= '{cutoff}'",
                ct: ct).ConfigureAwait(false);

            return rows.Select(r =>
            {
                var sevVal = WmiClient.GetUInt32(r, "Severity");
                var severity = sevVal == 0xC0000000 ? "Error"
                             : sevVal == 0x80000000 ? "Warning"
                             : "Informational";

                return new StatusMessageEntry
                {
                    DeviceName = WmiClient.GetString(r, "MachineName", device.DisplayName),
                    Time = WmiClient.GetDmtfDateTime(r, "Time") ?? DateTime.MinValue,
                    Severity = severity,
                    MessageId = WmiClient.GetUInt32(r, "MessageID"),
                    Component = WmiClient.GetString(r, "Component"),
                    SiteCode = WmiClient.GetString(r, "SiteCode", siteCode),
                    Description = $"Message ID {WmiClient.GetUInt32(r, "MessageID")} ({WmiClient.GetString(r, "Component")})"
                };
            }).OrderByDescending(s => s.Time).ToList();
        }

        public async Task<IReadOnlyList<string>> GetDeviceMacAddressesAsync(string server, string siteCode, TargetDevice device, CancellationToken ct = default)
        {
            var ns = SiteNamespace(siteCode);
            var rid = device.ResourceId;
            if (string.IsNullOrWhiteSpace(rid))
            {
                var lookup = await _wmi.QueryAsync(server, ns,
                    $"SELECT ResourceID FROM SMS_R_System WHERE Name = '{EscapeWql(device.DisplayName)}'", ct: ct).ConfigureAwait(false);
                rid = lookup.Select(r => WmiClient.GetString(r, "ResourceID")).FirstOrDefault();
            }

            if (string.IsNullOrWhiteSpace(rid)) return Array.Empty<string>();

            var rows = await _wmi.QueryAsync(
                server,
                ns,
                $"SELECT MACAddress FROM SMS_G_System_NETWORK_ADAPTER_CONFIGURATION WHERE ResourceID = {rid} AND MACAddress IS NOT NULL",
                ct: ct).ConfigureAwait(false);

            return rows.Select(r => WmiClient.GetString(r, "MACAddress"))
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public async Task<IReadOnlyList<BitLockerRecoveryKeyInfo>> GetConfigMgrBitLockerKeysAsync(
            string server,
            string siteCode,
            TargetDevice device,
            CancellationToken ct = default)
        {
            var results = new List<BitLockerRecoveryKeyInfo>();
            try
            {
                var rows = await _wmi.QueryAsync(
                    server,
                    SiteNamespace(siteCode),
                    $"SELECT RecoveryKeyId, RecoveryKey, VolumeId, LastUpdateTime FROM SMS_BitLockerRecoveryKey WHERE MachineName = '{EscapeWql(device.DisplayName)}'",
                    ct: ct).ConfigureAwait(false);

                foreach (var r in rows)
                {
                    results.Add(new BitLockerRecoveryKeyInfo
                    {
                        DeviceName = device.DisplayName,
                        Source = "ConfigMgr",
                        RecoveryKeyId = WmiClient.GetString(r, "RecoveryKeyId"),
                        RecoveryPassword = WmiClient.GetString(r, "RecoveryKey"),
                        VolumeGuid = WmiClient.GetString(r, "VolumeId"),
                        CreatedUtc = WmiClient.GetDmtfDateTime(r, "LastUpdateTime")
                    });
                }
            }
            catch
            {
                // BitLocker Management may not be enabled in this MECM site
            }
            return results;
        }

        public async Task<IReadOnlyList<ConfigMgrBitLockerRecord>> GetBitLockerComplianceRecordsAsync(
            string server,
            string siteCode,
            IEnumerable<string> collectionIds,
            CancellationToken ct = default)
        {
            var ns = SiteNamespace(siteCode);
            var ids = (collectionIds ?? Enumerable.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var resourceFilter = await ResolveCollectionResourceIdsAsync(server, ns, ids, ct).ConfigureAwait(false);

            var sysRows = await _wmi.QueryAsync(
                server,
                ns,
                "SELECT ResourceID, Name, DistinguishedName, OperatingSystemNameandVersion FROM SMS_R_System",
                ct: ct).ConfigureAwait(false);

            // Optional OS version table
            var osMap = new Dictionary<string, (string Caption, string Version)>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var osRows = await _wmi.QueryAsync(server, ns, "SELECT ResourceID, Caption, Version FROM SMS_G_System_OPERATING_SYSTEM", ct: ct).ConfigureAwait(false);
                foreach (var o in osRows)
                {
                    var rid = WmiClient.GetString(o, "ResourceID");
                    if (!string.IsNullOrEmpty(rid))
                    {
                        osMap[rid] = (WmiClient.GetString(o, "Caption"), WmiClient.GetString(o, "Version"));
                    }
                }
            }
            catch { }

            // Optional ConfigMgr BitLocker recovery key table
            var cmKeyHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var cmKeyRids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var keyRows = await _wmi.QueryAsync(server, ns, "SELECT * FROM SMS_BitLockerRecoveryKey", ct: ct).ConfigureAwait(false);
                foreach (var kr in keyRows)
                {
                    var mName = WmiClient.GetString(kr, "MachineName");
                    if (!string.IsNullOrWhiteSpace(mName)) cmKeyHosts.Add(OpenRCT.Core.Analytics.BitLockerComplianceAnalyzer.NormalizeHostName(mName));
                    var rid = WmiClient.GetString(kr, "ResourceID");
                    if (!string.IsNullOrWhiteSpace(rid)) cmKeyRids.Add(rid);
                }
            }
            catch { }

            // Optional Encryptable Volume HW inventory (prefer OS volume C:)
            var volMap = new Dictionary<string, (int? Method, int? Protection, int? Conversion, string Drive)>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var volRows = await _wmi.QueryAsync(server, ns, "SELECT * FROM SMS_G_System_ENCRYPTABLE_VOLUME", ct: ct).ConfigureAwait(false);
                foreach (var v in volRows)
                {
                    var rid = WmiClient.GetString(v, "ResourceID");
                    if (string.IsNullOrEmpty(rid)) continue;
                    var drive = WmiClient.GetString(v, "DriveLetter");
                    var isOsDrive = string.Equals(drive, "C:", StringComparison.OrdinalIgnoreCase);
                    if (volMap.TryGetValue(rid, out var existing) && string.Equals(existing.Drive, "C:", StringComparison.OrdinalIgnoreCase) && !isOsDrive)
                    {
                        continue;
                    }
                    volMap[rid] = (
                        GetNullableInt32(v, "EncryptionMethod"),
                        GetNullableInt32(v, "ProtectionStatus"),
                        GetNullableInt32(v, "ConversionStatus"),
                        drive);
                }
            }
            catch { }

            // Optional ConfigMgr BitLocker Management compliance details
            var detailsMap = new Dictionary<string, (string Compliance, string Encryption)>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var detRows = await _wmi.QueryAsync(server, ns, "SELECT * FROM SMS_G_System_BITLOCKER_DETAILS", ct: ct).ConfigureAwait(false);
                foreach (var d in detRows)
                {
                    var rid = WmiClient.GetString(d, "ResourceID");
                    if (string.IsNullOrEmpty(rid)) continue;

                    var compRaw = GetNullableInt32(d, "Compliant") ?? GetNullableInt32(d, "ComplianceStatus");
                    string compStr = compRaw switch
                    {
                        1 => "Compliant",
                        0 => "Non-Compliant",
                        2 => "NotManagedByCMSite",
                        _ => WmiClient.GetString(d, "ComplianceStatus", "Unknown")
                    };
                    var encCode = GetNullableInt32(d, "EncryptionMethod");
                    var encStr = encCode.HasValue
                        ? OpenRCT.Core.Analytics.BitLockerComplianceAnalyzer.MapEncryptionMethod(encCode)
                        : WmiClient.GetString(d, "EncryptionMethod", string.Empty);

                    detailsMap[rid] = (compStr, encStr);
                }
            }
            catch { }

            var results = new List<ConfigMgrBitLockerRecord>();
            foreach (var r in sysRows)
            {
                var rid = WmiClient.GetString(r, "ResourceID");
                if (ids.Count > 0 && !resourceFilter.Contains(rid)) continue;

                var name = WmiClient.GetString(r, "Name");
                var norm = OpenRCT.Core.Analytics.BitLockerComplianceAnalyzer.NormalizeHostName(name);
                osMap.TryGetValue(rid, out var osInfo);
                var hasVol = volMap.TryGetValue(rid, out var vol);
                var hasDet = detailsMap.TryGetValue(rid, out var det);

                var encMethod = hasVol && vol.Method.HasValue
                    ? OpenRCT.Core.Analytics.BitLockerComplianceAnalyzer.MapEncryptionMethod(vol.Method)
                    : (hasDet && !string.IsNullOrWhiteSpace(det.Encryption) ? det.Encryption : "Unknown");

                var protStatus = hasVol && vol.Protection.HasValue
                    ? OpenRCT.Core.Analytics.BitLockerComplianceAnalyzer.MapProtectionStatus(vol.Protection)
                    : "Unknown";

                var cmCompliance = hasDet && !string.IsNullOrWhiteSpace(det.Compliance)
                    ? det.Compliance
                    : "Unknown";

                results.Add(new ConfigMgrBitLockerRecord
                {
                    ResourceId = rid,
                    Name = name,
                    DistinguishedName = WmiClient.GetString(r, "DistinguishedName"),
                    OperatingSystem = !string.IsNullOrWhiteSpace(osInfo.Caption) ? osInfo.Caption : WmiClient.GetString(r, "OperatingSystemNameandVersion"),
                    OperatingSystemVersion = osInfo.Version ?? string.Empty,
                    HasConfigMgrRecoveryKey = cmKeyRids.Contains(rid) || cmKeyHosts.Contains(norm),
                    CmBitLockerCompliance = cmCompliance,
                    EncryptionMethod = encMethod,
                    ProtectionStatus = protStatus,
                    ConversionStatus = hasVol ? vol.Conversion : null
                });
            }

            return results.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public async Task<ContentDistributionSnapshot> GetContentDistributionSnapshotAsync(
            string server,
            string siteCode,
            CancellationToken ct = default)
        {
            var ns = SiteNamespace(siteCode);
            var errors = new List<DeviceErrorEntry>();

            // 1. Distribution Points metadata
            var dpSummaries = new List<DistributionPointSummary>();
            var freeSpaceByDp = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var driveRows = await _wmi.QueryAsync(server, ns, "SELECT * FROM SMS_DistributionPointDriveInfo", ct: ct).ConfigureAwait(false);
                foreach (var dr in driveRows)
                {
                    var dpName = WmiClient.GetString(dr, "ServerName");
                    if (string.IsNullOrWhiteSpace(dpName)) dpName = OpenRCT.Core.Analytics.ContentDistributionAnalyzer.ExtractServerNameFromNalPath(WmiClient.GetString(dr, "NALPath"));
                    var pct = WmiClient.GetString(dr, "PercentFreeSpace");
                    if (string.IsNullOrWhiteSpace(pct))
                    {
                        var total = GetNullableDouble(dr, "BytesTotal");
                        var free = GetNullableDouble(dr, "BytesFree");
                        if (total.HasValue && total.Value > 0 && free.HasValue)
                        {
                            pct = $"{(free.Value / total.Value) * 100.0:0.#}%";
                        }
                    }
                    else if (!pct.EndsWith("%"))
                    {
                        pct += "%";
                    }
                    if (!string.IsNullOrWhiteSpace(dpName) && !string.IsNullOrWhiteSpace(pct) && !freeSpaceByDp.ContainsKey(dpName))
                    {
                        freeSpaceByDp[dpName] = pct;
                    }
                }
            }
            catch { }

            try
            {
                var dpRows = await _wmi.QueryAsync(server, ns, "SELECT * FROM SMS_DistributionPointInfo", ct: ct).ConfigureAwait(false);
                foreach (var r in dpRows)
                {
                    var nalPath = WmiClient.GetString(r, "NALPath");
                    var dpName = WmiClient.GetString(r, "ServerName");
                    if (string.IsNullOrWhiteSpace(dpName)) dpName = OpenRCT.Core.Analytics.ContentDistributionAnalyzer.ExtractServerNameFromNalPath(nalPath);
                    freeSpaceByDp.TryGetValue(dpName, out var pctFree);
                    if (string.IsNullOrWhiteSpace(pctFree))
                    {
                        var directPct = WmiClient.GetString(r, "PercentFreeSpace");
                        if (!string.IsNullOrWhiteSpace(directPct)) pctFree = directPct.EndsWith("%") ? directPct : directPct + "%";
                    }

                    var sslState = WmiClient.GetInt32(r, "SslState");
                    var https = WmiClient.GetBool(r, "IsHttps") || sslState > 0;
                    var ledbat = WmiClient.GetBool(r, "IsLedbatEnabled") || WmiClient.GetBool(r, "LEDBAT");
                    var internet = WmiClient.GetBool(r, "IsInternetFacing") || WmiClient.GetBool(r, "InternetFacing") || WmiClient.GetInt32(r, "IsFileContentEnabled") > 0 && nalPath.IndexOf("AZURE", StringComparison.OrdinalIgnoreCase) >= 0;

                    dpSummaries.Add(new DistributionPointSummary
                    {
                        ServerName = dpName,
                        NalPath = nalPath,
                        SiteCode = WmiClient.GetString(r, "SiteCode", siteCode),
                        Description = WmiClient.GetString(r, "Description"),
                        HttpsEnabled = https,
                        LedbatEnabled = ledbat,
                        InternetFacing = internet,
                        MulticastEnabled = WmiClient.GetBool(r, "IsMulticast"),
                        PeerDp = WmiClient.GetBool(r, "IsPeerDP"),
                        PullDp = WmiClient.GetBool(r, "IsPullDP"),
                        MaintenanceModeEnabled = WmiClient.GetBool(r, "MaintenanceMode"),
                        PxeEnabled = WmiClient.GetBool(r, "IsPXE"),
                        PercentFreeSpace = pctFree ?? string.Empty
                    });
                }
            }
            catch (Exception ex)
            {
                errors.Add(new DeviceErrorEntry { Source = $"{server} (SMS_DistributionPointInfo)", Message = ex.Message, Details = ex.ToString() });
            }

            // 2. Content metadata catalog (SMS_ObjectContentInfo)
            var contentByPkg = new Dictionary<string, (string Name, string ObjectId, uint ObjectTypeId, int SourceVersion, double SizeMb, DateTime? LastUpdate, string Description)>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var contentRows = await _wmi.QueryAsync(server, ns, "SELECT * FROM SMS_ObjectContentInfo", ct: ct).ConfigureAwait(false);
                foreach (var c in contentRows)
                {
                    var pkgId = WmiClient.GetString(c, "PackageID");
                    if (string.IsNullOrWhiteSpace(pkgId)) continue;
                    var sizeKb = GetNullableDouble(c, "SourceSize") ?? 0;
                    contentByPkg[pkgId] = (
                        WmiClient.GetString(c, "SoftwareName", pkgId),
                        WmiClient.GetString(c, "ObjectID"),
                        WmiClient.GetUInt32(c, "ObjectTypeID"),
                        WmiClient.GetInt32(c, "SourceVersion"),
                        Math.Round(sizeKb / 1024.0, 2),
                        WmiClient.GetDmtfDateTime(c, "LastUpdateDate"),
                        WmiClient.GetString(c, "Description"));
                }
            }
            catch (Exception ex)
            {
                errors.Add(new DeviceErrorEntry { Source = $"{server} (SMS_ObjectContentInfo)", Message = ex.Message, Details = ex.ToString() });
            }

            // 3. Per-DP content status (SMS_DistributionDPStatus)
            var items = new List<ContentDistributionItem>();
            try
            {
                var statusRows = await _wmi.QueryAsync(server, ns, "SELECT * FROM SMS_DistributionDPStatus", ct: ct).ConfigureAwait(false);
                foreach (var s in statusRows)
                {
                    var pkgId = WmiClient.GetString(s, "PackageID");
                    var nalPath = WmiClient.GetString(s, "NalPath");
                    var dpName = WmiClient.GetString(s, "Name");
                    if (string.IsNullOrWhiteSpace(dpName)) dpName = OpenRCT.Core.Analytics.ContentDistributionAnalyzer.ExtractServerNameFromNalPath(nalPath);

                    var msgState = WmiClient.GetInt32(s, "MessageState");
                    var msgId = WmiClient.GetUInt32(s, "MessageID");
                    var objTypeId = WmiClient.GetUInt32(s, "ObjectTypeID");
                    var hasMeta = contentByPkg.TryGetValue(pkgId, out var meta);

                    var insStrings = CollectInsStrings(s);
                    var effectiveTypeId = hasMeta && meta.ObjectTypeId > 0 ? meta.ObjectTypeId : objTypeId;

                    items.Add(new ContentDistributionItem
                    {
                        ContentName = hasMeta && !string.IsNullOrWhiteSpace(meta.Name) ? meta.Name : pkgId,
                        ContentType = OpenRCT.Core.Analytics.ContentDistributionAnalyzer.MapContentType(effectiveTypeId),
                        DistributionPoint = dpName,
                        ServerNalPath = nalPath,
                        SourceVersion = hasMeta ? meta.SourceVersion : 0,
                        PackageId = pkgId,
                        ObjectId = hasMeta && !string.IsNullOrWhiteSpace(meta.ObjectId) ? meta.ObjectId : WmiClient.GetString(s, "ObjectID"),
                        SourceSizeMb = hasMeta ? meta.SizeMb : 0,
                        State = OpenRCT.Core.Analytics.ContentDistributionAnalyzer.MapMessageState(msgState),
                        MessageState = msgState,
                        MessageId = msgId,
                        LastUpdateDate = WmiClient.GetDmtfDateTime(s, "LastUpdateDate") ?? (hasMeta ? meta.LastUpdate : null),
                        Description = hasMeta ? meta.Description : string.Empty,
                        StatusDetails = OpenRCT.Core.Analytics.ContentDistributionAnalyzer.DescribeMessageId(msgId, insStrings)
                    });
                }
            }
            catch (Exception ex)
            {
                errors.Add(new DeviceErrorEntry { Source = $"{server} (SMS_DistributionDPStatus)", Message = ex.Message, Details = ex.ToString() });
            }

            var snapshot = OpenRCT.Core.Analytics.ContentDistributionAnalyzer.Aggregate(dpSummaries, items);
            snapshot.Errors.AddRange(errors);
            return snapshot;
        }

        public async Task<IReadOnlyList<ContentDpStatusDetail>> GetContentStatusDetailsAsync(
            string server,
            string siteCode,
            string packageId,
            CancellationToken ct = default)
        {
            var ns = SiteNamespace(siteCode);
            var rows = await _wmi.QueryAsync(
                server,
                ns,
                $"SELECT * FROM SMS_DistributionDPStatus WHERE PackageID = '{EscapeWql(packageId)}'",
                ct: ct).ConfigureAwait(false);

            var list = new List<ContentDpStatusDetail>();
            foreach (var r in rows)
            {
                var nal = WmiClient.GetString(r, "NalPath");
                var dpName = WmiClient.GetString(r, "Name");
                if (string.IsNullOrWhiteSpace(dpName)) dpName = OpenRCT.Core.Analytics.ContentDistributionAnalyzer.ExtractServerNameFromNalPath(nal);
                var msgState = WmiClient.GetInt32(r, "MessageState");
                var msgId = WmiClient.GetUInt32(r, "MessageID");
                var insStrings = CollectInsStrings(r);

                list.Add(new ContentDpStatusDetail
                {
                    PackageId = packageId,
                    DistributionPoint = dpName,
                    ServerNalPath = nal,
                    SiteCode = WmiClient.GetString(r, "SiteCode", siteCode),
                    State = OpenRCT.Core.Analytics.ContentDistributionAnalyzer.MapMessageState(msgState),
                    MessageId = msgId,
                    MessageSummary = OpenRCT.Core.Analytics.ContentDistributionAnalyzer.DescribeMessageId(msgId, insStrings),
                    LastUpdateDate = WmiClient.GetDmtfDateTime(r, "LastUpdateDate")
                });
            }

            return list.OrderBy(x => x.State == ContentDistributionState.Error ? 0 : x.State == ContentDistributionState.InProgress ? 1 : 2)
                .ThenBy(x => x.DistributionPoint, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public Task<ActionExecutionResult> RedistributeContentAsync(
            string server,
            string siteCode,
            string packageId,
            string serverNalPath,
            string dpDisplayName,
            CancellationToken ct = default)
        {
            return ExecuteDistributionPointActionAsync(
                server, siteCode, packageId, serverNalPath, dpDisplayName,
                "Redistribute Content",
                dpObj =>
                {
                    dpObj["RefreshNow"] = true;
                    dpObj.Put();
                    return $"Triggered redistribution (RefreshNow=true) for {packageId} on {dpDisplayName}.";
                }, ct);
        }

        public Task<ActionExecutionResult> ValidateContentAsync(
            string server,
            string siteCode,
            string packageId,
            string serverNalPath,
            string dpDisplayName,
            CancellationToken ct = default)
        {
            return ExecuteDistributionPointActionAsync(
                server, siteCode, packageId, serverNalPath, dpDisplayName,
                "Validate Content",
                dpObj =>
                {
                    dpObj.InvokeMethod("VerifyPackage", null, null);
                    return $"Triggered content hash validation (VerifyPackage) for {packageId} on {dpDisplayName}.";
                }, ct);
        }

        public Task<ActionExecutionResult> RemoveContentFromDpAsync(
            string server,
            string siteCode,
            string packageId,
            string serverNalPath,
            string dpDisplayName,
            CancellationToken ct = default)
        {
            return ExecuteDistributionPointActionAsync(
                server, siteCode, packageId, serverNalPath, dpDisplayName,
                "Remove Content from DP",
                dpObj =>
                {
                    dpObj.Delete();
                    return $"Removed {packageId} from distribution point {dpDisplayName}.";
                }, ct);
        }

        public async Task<IReadOnlyList<HardwareFirmwareAuditRow>> GetHardwareFirmwareInventoryAsync(
            string server,
            string siteCode,
            IEnumerable<string> collectionIds,
            CancellationToken ct = default)
        {
            var ns = SiteNamespace(siteCode);
            var ids = (collectionIds ?? Enumerable.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var resourceFilter = await ResolveCollectionResourceIdsAsync(server, ns, ids, ct).ConfigureAwait(false);

            var sysRows = await _wmi.QueryAsync(
                server,
                ns,
                "SELECT ResourceID, Name, DistinguishedName, OperatingSystemNameandVersion FROM SMS_R_System",
                ct: ct).ConfigureAwait(false);

            var csMap = new Dictionary<string, (string Manufacturer, string Model)>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var rows = await _wmi.QueryAsync(server, ns, "SELECT * FROM SMS_G_System_COMPUTER_SYSTEM", ct: ct).ConfigureAwait(false);
                foreach (var r in rows)
                {
                    var rid = WmiClient.GetString(r, "ResourceID");
                    if (!string.IsNullOrEmpty(rid))
                    {
                        csMap[rid] = (WmiClient.GetString(r, "Manufacturer"), WmiClient.GetString(r, "Model"));
                    }
                }
            }
            catch { }

            var encMap = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var rows = await _wmi.QueryAsync(server, ns, "SELECT * FROM SMS_G_System_SYSTEM_ENCLOSURE", ct: ct).ConfigureAwait(false);
                foreach (var r in rows)
                {
                    var rid = WmiClient.GetString(r, "ResourceID");
                    if (!string.IsNullOrEmpty(rid) && r.TryGetValue("ChassisTypes", out var ctObj) && ctObj != null)
                    {
                        encMap[rid] = ParseIntArray(ctObj);
                    }
                }
            }
            catch { }

            var biosMap = new Dictionary<string, (string Version, DateTime? ReleaseDate)>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var rows = await _wmi.QueryAsync(server, ns, "SELECT * FROM SMS_G_System_PC_BIOS", ct: ct).ConfigureAwait(false);
                foreach (var r in rows)
                {
                    var rid = WmiClient.GetString(r, "ResourceID");
                    if (!string.IsNullOrEmpty(rid))
                    {
                        var ver = WmiClient.GetString(r, "SMBIOSBIOSVersion");
                        if (string.IsNullOrWhiteSpace(ver)) ver = WmiClient.GetString(r, "Version");
                        biosMap[rid] = (ver, WmiClient.GetDmtfDateTime(r, "ReleaseDate"));
                    }
                }
            }
            catch { }

            var fwMap = new Dictionary<string, (string Uefi, string SecureBoot)>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var rows = await _wmi.QueryAsync(server, ns, "SELECT * FROM SMS_G_System_FIRMWARE", ct: ct).ConfigureAwait(false);
                foreach (var r in rows)
                {
                    var rid = WmiClient.GetString(r, "ResourceID");
                    if (!string.IsNullOrEmpty(rid))
                    {
                        var uefiInt = GetNullableInt32(r, "UEFI");
                        var sbInt = GetNullableInt32(r, "SecureBoot");
                        string uefi = uefiInt == 1 ? "Enabled" : (uefiInt == 0 ? "Legacy BIOS" : "Unknown");
                        string sb = sbInt == 1 ? "Enabled" : (sbInt == 0 ? "Disabled" : "Unknown");
                        fwMap[rid] = (uefi, sb);
                    }
                }
            }
            catch { }

            var tpmMap = new Dictionary<string, (string Status, string SpecVersion)>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var rows = await _wmi.QueryAsync(server, ns, "SELECT * FROM SMS_G_System_TPM", ct: ct).ConfigureAwait(false);
                foreach (var r in rows)
                {
                    var rid = WmiClient.GetString(r, "ResourceID");
                    if (!string.IsNullOrEmpty(rid))
                    {
                        var specRaw = WmiClient.GetString(r, "SpecVersion");
                        var specShort = !string.IsNullOrWhiteSpace(specRaw) ? specRaw.Split(',')[0].Trim() : string.Empty;
                        var enabled = WmiClient.GetBool(r, "IsEnabled_InitialValue");
                        var activated = WmiClient.GetBool(r, "IsActivated_InitialValue");
                        string status = (enabled && activated)
                            ? (string.IsNullOrWhiteSpace(specShort) ? "Enabled" : $"TPM {specShort} Ready")
                            : (enabled ? "Enabled (Not Activated)" : "Disabled");
                        tpmMap[rid] = (status, specShort);
                    }
                }
            }
            catch { }

            var osMap = new Dictionary<string, (string Caption, string Version)>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var rows = await _wmi.QueryAsync(server, ns, "SELECT ResourceID, Caption, Version FROM SMS_G_System_OPERATING_SYSTEM", ct: ct).ConfigureAwait(false);
                foreach (var r in rows)
                {
                    var rid = WmiClient.GetString(r, "ResourceID");
                    if (!string.IsNullOrEmpty(rid))
                    {
                        osMap[rid] = (WmiClient.GetString(r, "Caption"), WmiClient.GetString(r, "Version"));
                    }
                }
            }
            catch { }

            var results = new List<HardwareFirmwareAuditRow>();
            foreach (var r in sysRows)
            {
                var rid = WmiClient.GetString(r, "ResourceID");
                if (ids.Count > 0 && !resourceFilter.Contains(rid)) continue;

                csMap.TryGetValue(rid, out var cs);
                encMap.TryGetValue(rid, out var chassisCodes);
                biosMap.TryGetValue(rid, out var bios);
                fwMap.TryGetValue(rid, out var fw);
                tpmMap.TryGetValue(rid, out var tpm);
                osMap.TryGetValue(rid, out var os);

                var mfg = OpenRCT.Core.Analytics.HardwareFirmwareAnalyzer.NormalizeManufacturer(cs.Manufacturer);
                var model = string.IsNullOrWhiteSpace(cs.Model) ? "Unknown" : cs.Model.Trim();
                var chassis = OpenRCT.Core.Analytics.HardwareFirmwareAnalyzer.MapChassisType(chassisCodes, cs.Manufacturer, cs.Model);

                results.Add(new HardwareFirmwareAuditRow
                {
                    ResourceId = rid,
                    Name = WmiClient.GetString(r, "Name"),
                    DistinguishedName = WmiClient.GetString(r, "DistinguishedName"),
                    Manufacturer = mfg,
                    Model = model,
                    ChassisType = chassis,
                    UefiStatus = string.IsNullOrWhiteSpace(fw.Uefi) ? "Unknown" : fw.Uefi,
                    SecureBootStatus = string.IsNullOrWhiteSpace(fw.SecureBoot) ? "Unknown" : fw.SecureBoot,
                    TpmStatus = string.IsNullOrWhiteSpace(tpm.Status) ? "Unknown" : tpm.Status,
                    TpmVersion = tpm.SpecVersion ?? string.Empty,
                    BiosVersion = string.IsNullOrWhiteSpace(bios.Version) ? "Unknown" : bios.Version.Trim(),
                    BiosReleaseDate = bios.ReleaseDate,
                    OperatingSystem = !string.IsNullOrWhiteSpace(os.Caption) ? os.Caption : WmiClient.GetString(r, "OperatingSystemNameandVersion"),
                    OsVersion = string.IsNullOrWhiteSpace(os.Version) ? "Unknown" : os.Version
                });
            }

            return results.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private async Task<HashSet<string>> ResolveCollectionResourceIdsAsync(
            string server,
            string ns,
            List<string> collectionIds,
            CancellationToken ct)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var colId in collectionIds)
            {
                var members = await _wmi.QueryAsync(
                    server,
                    ns,
                    $"SELECT ResourceID FROM SMS_FullCollectionMembership WHERE CollectionID = '{EscapeWql(colId)}' AND ResourceType = 5",
                    ct: ct).ConfigureAwait(false);
                foreach (var m in members)
                {
                    var rid = WmiClient.GetString(m, "ResourceID");
                    if (!string.IsNullOrEmpty(rid)) set.Add(rid);
                }
            }
            return set;
        }

        private Task<ActionExecutionResult> ExecuteDistributionPointActionAsync(
            string server,
            string siteCode,
            string packageId,
            string serverNalPath,
            string dpDisplayName,
            string actionName,
            Func<ManagementObject, string> operation,
            CancellationToken ct)
        {
            return Task.Run(() =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var targetLabel = $"{packageId} @ {dpDisplayName}";
                try
                {
                    ct.ThrowIfCancellationRequested();
                    var ns = SiteNamespace(siteCode);
                    var scope = _wmi.CreateScope(server, ns);
                    var query = new ObjectQuery($"SELECT * FROM SMS_DistributionPoint WHERE PackageID = '{EscapeWql(packageId)}'");
                    using var searcher = new ManagementObjectSearcher(scope, query);
                    using var results = searcher.Get();

                    ManagementObject? matched = null;
                    foreach (ManagementObject obj in results)
                    {
                        var nal = obj["ServerNALPath"]?.ToString() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(serverNalPath) &&
                            string.Equals(nal, serverNalPath, StringComparison.OrdinalIgnoreCase))
                        {
                            matched = obj;
                            break;
                        }
                        if (!string.IsNullOrWhiteSpace(dpDisplayName) &&
                            nal.IndexOf(dpDisplayName, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            matched = obj;
                            break;
                        }
                        obj.Dispose();
                    }

                    if (matched == null)
                    {
                        return ActionExecutionResult.Failure(targetLabel, actionName, $"Could not find SMS_DistributionPoint assignment for package '{packageId}' on '{dpDisplayName}'.", null, sw.Elapsed);
                    }

                    using (matched)
                    {
                        var msg = operation(matched);
                        return ActionExecutionResult.Success(targetLabel, actionName, msg, sw.Elapsed);
                    }
                }
                catch (Exception ex)
                {
                    return ActionExecutionResult.Failure(targetLabel, actionName, ex.Message, ex.ToString(), sw.Elapsed);
                }
            }, ct);
        }

        private static int? GetNullableInt32(Dictionary<string, object?> row, string key)
        {
            if (row.TryGetValue(key, out var val) && val != null)
            {
                try { return Convert.ToInt32(val); } catch { }
            }
            return null;
        }

        private static double? GetNullableDouble(Dictionary<string, object?> row, string key)
        {
            if (row.TryGetValue(key, out var val) && val != null)
            {
                try { return Convert.ToDouble(val); } catch { }
            }
            return null;
        }

        private static int[] ParseIntArray(object value)
        {
            if (value is ushort[] u16) return u16.Select(x => (int)x).ToArray();
            if (value is uint[] u32) return u32.Select(x => (int)x).ToArray();
            if (value is int[] i32) return i32;
            if (value is short[] i16) return i16.Select(x => (int)x).ToArray();
            if (value is Array arr)
            {
                var list = new List<int>();
                foreach (var item in arr)
                {
                    try { list.Add(Convert.ToInt32(item)); } catch { }
                }
                return list.ToArray();
            }
            try { return new[] { Convert.ToInt32(value) }; } catch { return Array.Empty<int>(); }
        }

        private static string CollectInsStrings(Dictionary<string, object?> row)
        {
            var parts = new List<string>();
            for (int i = 1; i <= 10; i++)
            {
                var val = WmiClient.GetString(row, $"InsString{i}");
                if (!string.IsNullOrWhiteSpace(val)) parts.Add(val);
            }
            return string.Join(" | ", parts);
        }

        private static string MapServiceWindowType(int type) => type switch
        {
            1 => "All Deployments",
            4 => "Software Updates",
            5 => "Task Sequences",
            _ => $"Type {type}"
        };

        private static string InferClassificationFromTitle(string? title)
        {
            if (string.IsNullOrWhiteSpace(title)) return "Updates";
            if (title!.IndexOf("Security", StringComparison.OrdinalIgnoreCase) >= 0) return "Security Updates";
            if (title.IndexOf("Definition", StringComparison.OrdinalIgnoreCase) >= 0 || title.IndexOf("Intelligence", StringComparison.OrdinalIgnoreCase) >= 0) return "Definition Updates";
            if (title.IndexOf("Rollup", StringComparison.OrdinalIgnoreCase) >= 0 || title.IndexOf("Cumulative", StringComparison.OrdinalIgnoreCase) >= 0) return "Update Rollups";
            if (title.IndexOf("Critical", StringComparison.OrdinalIgnoreCase) >= 0) return "Critical Updates";
            return "Updates";
        }

        private static string EscapeWql(string value) => (value ?? string.Empty).Replace("\\", "\\\\").Replace("'", "\\'");
    }
}
