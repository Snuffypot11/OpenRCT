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
