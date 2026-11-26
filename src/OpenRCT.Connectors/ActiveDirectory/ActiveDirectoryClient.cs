using System;
using System.Collections.Generic;
using System.DirectoryServices;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenRCT.Core.Interfaces;
using OpenRCT.Core.Models;

namespace OpenRCT.Connectors.ActiveDirectory
{
    /// <summary>
    /// Native LDAP / System.DirectoryServices Active Directory connector.
    /// Requires zero RSAT PowerShell modules. Supports both Legacy LAPS and Windows LAPS.
    /// </summary>
    public sealed class ActiveDirectoryClient : IActiveDirectoryClient
    {
        private const int AdsUfAccountDisable = 0x0002;

        public Task<IReadOnlyList<OrganizationalUnitInfo>> GetOrganizationalUnitsAsync(CancellationToken ct = default)
        {
            return Task.Run<IReadOnlyList<OrganizationalUnitInfo>>(() =>
            {
                ct.ThrowIfCancellationRequested();
                var results = new List<OrganizationalUnitInfo>();
                using var rootDse = new DirectoryEntry("LDAP://RootDSE");
                var defaultNamingContext = rootDse.Properties["defaultNamingContext"].Value?.ToString();
                if (string.IsNullOrWhiteSpace(defaultNamingContext))
                {
                    return results;
                }

                results.Add(new OrganizationalUnitInfo
                {
                    Name = defaultNamingContext!,
                    DistinguishedName = defaultNamingContext!,
                    CanonicalPath = $"[Domain Root] {defaultNamingContext}"
                });

                using var baseEntry = new DirectoryEntry($"LDAP://{defaultNamingContext}");
                using var searcher = new DirectorySearcher(baseEntry)
                {
                    Filter = "(objectClass=organizationalUnit)",
                    PageSize = 500,
                    SearchScope = SearchScope.Subtree
                };
                searcher.PropertiesToLoad.AddRange(new[] { "name", "distinguishedName", "canonicalName" });

                using var found = searcher.FindAll();
                foreach (SearchResult sr in found)
                {
                    ct.ThrowIfCancellationRequested();
                    var dn = GetPropString(sr, "distinguishedName");
                    var name = GetPropString(sr, "name");
                    var canonical = GetPropString(sr, "canonicalName");
                    results.Add(new OrganizationalUnitInfo
                    {
                        Name = name,
                        DistinguishedName = dn,
                        CanonicalPath = string.IsNullOrWhiteSpace(canonical) ? dn : canonical
                    });
                }

                return results.OrderBy(o => o.CanonicalPath, StringComparer.OrdinalIgnoreCase).ToList();
            }, ct);
        }

        public Task<IReadOnlyList<AdComputerRecord>> GetComputersInOusAsync(IEnumerable<string> ouDistinguishedNames, CancellationToken ct = default)
        {
            return Task.Run<IReadOnlyList<AdComputerRecord>>(() =>
            {
                ct.ThrowIfCancellationRequested();
                var ous = (ouDistinguishedNames ?? Enumerable.Empty<string>())
                    .Where(o => !string.IsNullOrWhiteSpace(o))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (ous.Count == 0)
                {
                    using var rootDse = new DirectoryEntry("LDAP://RootDSE");
                    var defaultNc = rootDse.Properties["defaultNamingContext"].Value?.ToString();
                    if (!string.IsNullOrWhiteSpace(defaultNc)) ous.Add(defaultNc!);
                }

                var byDn = new Dictionary<string, AdComputerRecord>(StringComparer.OrdinalIgnoreCase);

                foreach (var ou in ous)
                {
                    ct.ThrowIfCancellationRequested();
                    using var entry = new DirectoryEntry($"LDAP://{ou}");
                    using var searcher = new DirectorySearcher(entry)
                    {
                        Filter = "(&(objectCategory=computer)(objectClass=computer))",
                        PageSize = 1000,
                        SearchScope = SearchScope.Subtree
                    };

                    searcher.PropertiesToLoad.AddRange(new[]
                    {
                        "name",
                        "distinguishedName",
                        "pwdLastSet",
                        "operatingSystem",
                        "operatingSystemVersion",
                        "whenCreated",
                        "lastLogonTimestamp",
                        "userAccountControl",
                        "ms-Mcs-AdmPwdExpirationTime",
                        "msLAPS-PasswordExpirationTime"
                    });

                    using var found = searcher.FindAll();
                    foreach (SearchResult sr in found)
                    {
                        ct.ThrowIfCancellationRequested();
                        var dn = GetPropString(sr, "distinguishedName");
                        if (string.IsNullOrEmpty(dn) || byDn.ContainsKey(dn)) continue;

                        var uac = GetPropInt(sr, "userAccountControl");
                        var legacyExp = GetPropFileTime(sr, "ms-Mcs-AdmPwdExpirationTime");
                        var winLapsExp = GetPropFileTime(sr, "msLAPS-PasswordExpirationTime");

                        byDn[dn] = new AdComputerRecord
                        {
                            Name = GetPropString(sr, "name"),
                            DistinguishedName = dn,
                            PasswordLastSet = GetPropFileTime(sr, "pwdLastSet"),
                            OperatingSystem = GetPropString(sr, "operatingSystem"),
                            OperatingSystemVersion = GetPropString(sr, "operatingSystemVersion"),
                            Created = GetPropDateTime(sr, "whenCreated"),
                            LastLogonTimestamp = GetPropFileTime(sr, "lastLogonTimestamp"),
                            Enabled = (uac & AdsUfAccountDisable) == 0,
                            HasLegacyLapsPassword = legacyExp.HasValue,
                            LegacyLapsExpiration = legacyExp,
                            HasWindowsLapsPassword = winLapsExp.HasValue,
                            WindowsLapsExpiration = winLapsExp
                        };
                    }
                }

                return byDn.Values.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
            }, ct);
        }

        public Task<HashSet<string>> GetComputersWithBitLockerKeysAsync(IEnumerable<string> ouDistinguishedNames, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var ous = (ouDistinguishedNames ?? Enumerable.Empty<string>())
                    .Where(o => !string.IsNullOrWhiteSpace(o))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (ous.Count == 0)
                {
                    using var rootDse = new DirectoryEntry("LDAP://RootDSE");
                    var defaultNc = rootDse.Properties["defaultNamingContext"].Value?.ToString();
                    if (!string.IsNullOrWhiteSpace(defaultNc)) ous.Add(defaultNc!);
                }

                foreach (var ou in ous)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        using var entry = new DirectoryEntry($"LDAP://{ou}");
                        using var searcher = new DirectorySearcher(entry)
                        {
                            Filter = "(objectClass=msFVE-RecoveryInformation)",
                            PageSize = 1000,
                            SearchScope = SearchScope.Subtree
                        };
                        searcher.PropertiesToLoad.Add("distinguishedName");

                        using var found = searcher.FindAll();
                        foreach (SearchResult sr in found)
                        {
                            ct.ThrowIfCancellationRequested();
                            var keyDn = GetPropString(sr, "distinguishedName");
                            if (string.IsNullOrWhiteSpace(keyDn)) continue;

                            // Parent DN of an msFVE-RecoveryInformation leaf is the computer object DN
                            var parentDn = GetParentDistinguishedName(keyDn);
                            if (!string.IsNullOrWhiteSpace(parentDn))
                            {
                                result.Add(parentDn);
                                var cn = ExtractCn(parentDn);
                                if (!string.IsNullOrWhiteSpace(cn))
                                {
                                    result.Add(cn);
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Continue scanning remaining OUs if one OU denies read on msFVE-RecoveryInformation
                    }
                }

                return result;
            }, ct);
        }

        private static string GetParentDistinguishedName(string dn)
        {
            if (string.IsNullOrWhiteSpace(dn)) return string.Empty;
            bool escaped = false;
            for (int i = 0; i < dn.Length; i++)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }
                if (dn[i] == '\\')
                {
                    escaped = true;
                    continue;
                }
                if (dn[i] == ',' && i + 1 < dn.Length)
                {
                    return dn.Substring(i + 1).Trim();
                }
            }
            return string.Empty;
        }

        public Task<IReadOnlyList<AdGroupMembershipInfo>> GetComputerGroupsAsync(string computerName, CancellationToken ct = default)
        {
            return Task.Run<IReadOnlyList<AdGroupMembershipInfo>>(() =>
            {
                ct.ThrowIfCancellationRequested();
                using var compEntry = FindComputerEntry(computerName);
                var results = new List<AdGroupMembershipInfo>();
                if (compEntry.Properties["memberOf"] == null) return results;

                foreach (var obj in compEntry.Properties["memberOf"])
                {
                    var groupDn = obj?.ToString();
                    if (string.IsNullOrWhiteSpace(groupDn)) continue;

                    string groupName = ExtractCn(groupDn!);
                    string description = string.Empty;
                    try
                    {
                        using var groupEntry = new DirectoryEntry($"LDAP://{groupDn}");
                        groupName = groupEntry.Properties["cn"].Value?.ToString() ?? groupName;
                        description = groupEntry.Properties["description"].Value?.ToString() ?? string.Empty;
                    }
                    catch { }

                    results.Add(new AdGroupMembershipInfo
                    {
                        DeviceName = computerName,
                        GroupName = groupName,
                        DistinguishedName = groupDn!,
                        Description = description
                    });
                }

                return results.OrderBy(g => g.GroupName, StringComparer.OrdinalIgnoreCase).ToList();
            }, ct);
        }

        public Task AddComputerToGroupAsync(string computerName, string groupNameOrDn, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                using var compEntry = FindComputerEntry(computerName);
                var compDn = compEntry.Properties["distinguishedName"].Value?.ToString()
                    ?? throw new InvalidOperationException($"Could not determine DistinguishedName for '{computerName}'.");

                using var groupEntry = FindGroupEntry(groupNameOrDn);
                groupEntry.Properties["member"].Add(compDn);
                groupEntry.CommitChanges();
            }, ct);
        }

        public Task RemoveComputerFromGroupAsync(string computerName, string groupDistinguishedName, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                using var compEntry = FindComputerEntry(computerName);
                var compDn = compEntry.Properties["distinguishedName"].Value?.ToString()
                    ?? throw new InvalidOperationException($"Could not determine DistinguishedName for '{computerName}'.");

                using var groupEntry = new DirectoryEntry($"LDAP://{groupDistinguishedName}");
                groupEntry.Properties["member"].Remove(compDn);
                groupEntry.CommitChanges();
            }, ct);
        }

        public Task SetComputerEnabledAsync(string computerName, bool enabled, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                using var compEntry = FindComputerEntry(computerName);
                int uac = Convert.ToInt32(compEntry.Properties["userAccountControl"].Value ?? 0);
                if (enabled)
                {
                    uac &= ~AdsUfAccountDisable;
                }
                else
                {
                    uac |= AdsUfAccountDisable;
                }
                compEntry.Properties["userAccountControl"].Value = uac;
                compEntry.CommitChanges();
            }, ct);
        }

        public Task DeleteComputerAsync(string computerName, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                using var compEntry = FindComputerEntry(computerName);
                compEntry.DeleteTree();
                compEntry.CommitChanges();
            }, ct);
        }

        public Task<LapsPasswordResult> GetLapsPasswordAsync(string computerName, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                using var compEntry = FindComputerEntry(computerName);
                var dn = compEntry.Properties["distinguishedName"].Value?.ToString() ?? string.Empty;

                // 1. Check Windows LAPS cleartext password attribute (msLAPS-Password)
                var winLapsRaw = compEntry.Properties["msLAPS-Password"].Value?.ToString();
                if (!string.IsNullOrWhiteSpace(winLapsRaw))
                {
                    var winExp = ParseLargeIntegerFileTime(compEntry.Properties["msLAPS-PasswordExpirationTime"].Value);
                    var (account, pwd) = ParseWindowsLapsJson(winLapsRaw!);
                    return new LapsPasswordResult
                    {
                        DeviceName = computerName,
                        DistinguishedName = dn,
                        LapsType = "Windows LAPS",
                        AccountName = account,
                        Password = pwd,
                        ExpirationTime = winExp
                    };
                }

                // 2. Check Legacy Microsoft LAPS (ms-Mcs-AdmPwd)
                var legacyPwd = compEntry.Properties["ms-Mcs-AdmPwd"].Value?.ToString();
                if (!string.IsNullOrWhiteSpace(legacyPwd))
                {
                    var legacyExp = ParseLargeIntegerFileTime(compEntry.Properties["ms-Mcs-AdmPwdExpirationTime"].Value);
                    return new LapsPasswordResult
                    {
                        DeviceName = computerName,
                        DistinguishedName = dn,
                        LapsType = "Legacy Microsoft LAPS",
                        AccountName = "Local Administrator",
                        Password = legacyPwd!,
                        ExpirationTime = legacyExp
                    };
                }

                return new LapsPasswordResult
                {
                    DeviceName = computerName,
                    DistinguishedName = dn,
                    LapsType = "None",
                    ErrorMessage = "No LAPS password attribute was found (or current account lacks Read permission on ms-Mcs-AdmPwd / msLAPS-Password)."
                };
            }, ct);
        }

        public Task<IReadOnlyList<BitLockerRecoveryKeyInfo>> GetBitLockerRecoveryKeysAsync(string computerName, CancellationToken ct = default)
        {
            return Task.Run<IReadOnlyList<BitLockerRecoveryKeyInfo>>(() =>
            {
                ct.ThrowIfCancellationRequested();
                using var compEntry = FindComputerEntry(computerName);
                using var searcher = new DirectorySearcher(compEntry)
                {
                    Filter = "(objectClass=msFVE-RecoveryInformation)",
                    SearchScope = SearchScope.OneLevel
                };
                searcher.PropertiesToLoad.AddRange(new[] { "name", "msFVE-RecoveryPassword", "msFVE-RecoveryGuid", "msFVE-VolumeGuid", "whenCreated" });

                var keys = new List<BitLockerRecoveryKeyInfo>();
                using var found = searcher.FindAll();
                foreach (SearchResult sr in found)
                {
                    var pwd = GetPropString(sr, "msFVE-RecoveryPassword");
                    if (string.IsNullOrWhiteSpace(pwd)) continue;

                    var guidBytes = sr.Properties["msFVE-RecoveryGuid"].Count > 0 ? sr.Properties["msFVE-RecoveryGuid"][0] as byte[] : null;
                    var volBytes = sr.Properties["msFVE-VolumeGuid"].Count > 0 ? sr.Properties["msFVE-VolumeGuid"][0] as byte[] : null;

                    keys.Add(new BitLockerRecoveryKeyInfo
                    {
                        DeviceName = computerName,
                        Source = "Active Directory",
                        RecoveryKeyId = guidBytes != null && guidBytes.Length == 16 ? new Guid(guidBytes).ToString("B").ToUpperInvariant() : GetPropString(sr, "name"),
                        RecoveryPassword = pwd,
                        VolumeGuid = volBytes != null && volBytes.Length == 16 ? new Guid(volBytes).ToString("B").ToUpperInvariant() : string.Empty,
                        CreatedUtc = GetPropDateTime(sr, "whenCreated")
                    });
                }

                return keys.OrderByDescending(k => k.CreatedUtc).ToList();
            }, ct);
        }

        private static DirectoryEntry FindComputerEntry(string computerName)
        {
            var shortName = computerName.Contains('.') ? computerName.Substring(0, computerName.IndexOf('.')) : computerName;
            var safe = EscapeLdapFilter(shortName.Trim().TrimEnd('$'));
            using var rootDse = new DirectoryEntry("LDAP://RootDSE");
            var defaultNc = rootDse.Properties["defaultNamingContext"].Value?.ToString();
            using var baseEntry = new DirectoryEntry($"LDAP://{defaultNc}");
            using var searcher = new DirectorySearcher(baseEntry)
            {
                Filter = $"(&(objectCategory=computer)(|(name={safe})(sAMAccountName={safe}$)(dNSHostName={EscapeLdapFilter(computerName.Trim())})))",
                SearchScope = SearchScope.Subtree
            };
            var result = searcher.FindOne()
                ?? throw new InvalidOperationException($"Computer '{computerName}' was not found in Active Directory.");
            return result.GetDirectoryEntry();
        }

        private static DirectoryEntry FindGroupEntry(string groupNameOrDn)
        {
            if (groupNameOrDn.IndexOf("CN=", StringComparison.OrdinalIgnoreCase) >= 0 && groupNameOrDn.Contains(","))
            {
                return new DirectoryEntry($"LDAP://{groupNameOrDn}");
            }

            var safe = EscapeLdapFilter(groupNameOrDn.Trim());
            using var rootDse = new DirectoryEntry("LDAP://RootDSE");
            var defaultNc = rootDse.Properties["defaultNamingContext"].Value?.ToString();
            using var baseEntry = new DirectoryEntry($"LDAP://{defaultNc}");
            using var searcher = new DirectorySearcher(baseEntry)
            {
                Filter = $"(&(objectCategory=group)(|(cn={safe})(sAMAccountName={safe})))",
                SearchScope = SearchScope.Subtree
            };
            var result = searcher.FindOne()
                ?? throw new InvalidOperationException($"AD Group '{groupNameOrDn}' was not found.");
            return result.GetDirectoryEntry();
        }

        private static (string Account, string Password) ParseWindowsLapsJson(string rawJson)
        {
            // Windows LAPS stores JSON like: {"n":"Administrator","t":"1d9...","p":"secret"}
            string account = "Administrator";
            string password = rawJson;
            try
            {
                var pIdx = rawJson.IndexOf("\"p\":\"", StringComparison.Ordinal);
                if (pIdx >= 0)
                {
                    var start = pIdx + 5;
                    var end = rawJson.IndexOf('"', start);
                    if (end > start) password = rawJson.Substring(start, end - start);
                }
                var nIdx = rawJson.IndexOf("\"n\":\"", StringComparison.Ordinal);
                if (nIdx >= 0)
                {
                    var start = nIdx + 5;
                    var end = rawJson.IndexOf('"', start);
                    if (end > start) account = rawJson.Substring(start, end - start);
                }
            }
            catch { }
            return (account, password);
        }

        private static string GetPropString(SearchResult sr, string prop) =>
            sr.Properties[prop].Count > 0 ? sr.Properties[prop][0]?.ToString() ?? string.Empty : string.Empty;

        private static int GetPropInt(SearchResult sr, string prop) =>
            sr.Properties[prop].Count > 0 ? Convert.ToInt32(sr.Properties[prop][0]) : 0;

        private static DateTime? GetPropDateTime(SearchResult sr, string prop) =>
            sr.Properties[prop].Count > 0 && sr.Properties[prop][0] is DateTime dt ? dt : (DateTime?)null;

        private static DateTime? GetPropFileTime(SearchResult sr, string prop)
        {
            if (sr.Properties[prop].Count == 0) return null;
            try
            {
                long ft = Convert.ToInt64(sr.Properties[prop][0]);
                return ft > 0 ? DateTime.FromFileTime(ft) : (DateTime?)null;
            }
            catch { return null; }
        }

        private static DateTime? ParseLargeIntegerFileTime(object? val)
        {
            if (val == null) return null;
            if (val is long l && l > 0) return DateTime.FromFileTime(l);
            try
            {
                var type = val.GetType();
                var high = Convert.ToInt64(type.InvokeMember("HighPart", System.Reflection.BindingFlags.GetProperty, null, val, null));
                var low = Convert.ToUInt32(type.InvokeMember("LowPart", System.Reflection.BindingFlags.GetProperty, null, val, null));
                long ft = (high << 32) | low;
                return ft > 0 ? DateTime.FromFileTime(ft) : (DateTime?)null;
            }
            catch { return null; }
        }

        private static string ExtractCn(string dn)
        {
            var first = dn.Split(',')[0];
            return first.StartsWith("CN=", StringComparison.OrdinalIgnoreCase) ? first.Substring(3) : first;
        }

        private static string EscapeLdapFilter(string value) =>
            value.Replace("\\", "\\5c").Replace("*", "\\2a").Replace("(", "\\28").Replace(")", "\\29").Replace("\0", "\\00");
    }
}
