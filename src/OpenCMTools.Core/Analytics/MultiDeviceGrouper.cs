using System;
using System.Collections.Generic;
using System.Linq;
using OpenCMTools.Core.Models;

namespace OpenCMTools.Core.Analytics
{
    public sealed class GroupedChildData<T>
    {
        public string DeviceName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string SecondaryDetail { get; set; } = string.Empty;
        public string ExtraDetail { get; set; } = string.Empty;
        public DateTime? Timestamp { get; set; }
        public string? TimestampText { get; set; }
        public string ErrorCode { get; set; } = string.Empty;
        public T Payload { get; set; } = default!;
    }

    public sealed class GroupedItemData<T>
    {
        public string GroupKey { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string SecondaryInfo { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string StatusSummary { get; set; } = string.Empty;
        public string ExtraInfo { get; set; } = string.Empty;
        public List<GroupedChildData<T>> Children { get; set; } = new List<GroupedChildData<T>>();
        public int DeviceCount => Children
            .Select(c => c.DeviceName)
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    /// <summary>
    /// Pure grouping and status-aggregation helpers for multi-device client tools
    /// (Remote Software Center, System Information, and Client Information).
    /// </summary>
    public static class MultiDeviceGrouper
    {
        public static List<GroupedItemData<RemoteSoftwareCenterItem>> GroupSoftwareCenterItems(
            IEnumerable<RemoteSoftwareCenterItem> items,
            SoftwareCenterItemType itemType)
        {
            if (items == null) return new List<GroupedItemData<RemoteSoftwareCenterItem>>();

            var filtered = items.Where(i => i != null && i.ItemType == itemType).ToList();
            if (filtered.Count == 0) return new List<GroupedItemData<RemoteSoftwareCenterItem>>();

            string BuildKey(RemoteSoftwareCenterItem i)
            {
                var name = (i.Name ?? string.Empty).Trim();
                var ver = (i.SoftwareVersion ?? string.Empty).Trim();
                var pub = (i.Publisher ?? string.Empty).Trim();
                var id = (i.Id ?? string.Empty).Trim();
                switch (itemType)
                {
                    case SoftwareCenterItemType.SoftwareUpdate:
                        return $"{ver}|{name}".ToUpperInvariant();
                    case SoftwareCenterItemType.PackageOrTaskSequence:
                        return $"{id}|{name}".ToUpperInvariant();
                    case SoftwareCenterItemType.ComplianceBaseline:
                        return $"{name}|{ver}".ToUpperInvariant();
                    default:
                        return string.IsNullOrEmpty(name)
                            ? id.ToUpperInvariant()
                            : $"{name}|{ver}|{pub}".ToUpperInvariant();
                }
            }

            var result = new List<GroupedItemData<RemoteSoftwareCenterItem>>();
            foreach (var grp in filtered.GroupBy(BuildKey, StringComparer.OrdinalIgnoreCase))
            {
                var first = grp.First();
                var children = grp
                    .OrderBy(i => i.DeviceName, StringComparer.OrdinalIgnoreCase)
                    .Select(i => new GroupedChildData<RemoteSoftwareCenterItem>
                    {
                        DeviceName = i.DeviceName,
                        Status = GetSoftwareCenterChildStatus(i, itemType),
                        SecondaryDetail = itemType == SoftwareCenterItemType.PackageOrTaskSequence
                            ? i.PackageName
                            : i.EvaluationState,
                        ExtraDetail = i.AllowedActions,
                        Timestamp = i.LastRunTime ?? i.Deadline,
                        ErrorCode = i.ErrorCodeHex,
                        Payload = i
                    })
                    .ToList();

                result.Add(new GroupedItemData<RemoteSoftwareCenterItem>
                {
                    GroupKey = grp.Key,
                    Name = string.IsNullOrWhiteSpace(first.Name) ? first.Id : first.Name,
                    SecondaryInfo = itemType == SoftwareCenterItemType.PackageOrTaskSequence
                        ? (string.IsNullOrWhiteSpace(first.PackageName) ? first.Id : $"{first.PackageName} ({first.Id})")
                        : first.Publisher,
                    Version = first.SoftwareVersion,
                    StatusSummary = BuildSoftwareCenterStatusSummary(grp, itemType),
                    ExtraInfo = itemType == SoftwareCenterItemType.PackageOrTaskSequence
                        ? (first.IsTaskSequence ? "Task Sequence" : "Package / Program")
                        : first.AllowedActions,
                    Children = children
                });
            }

            return result
                .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(g => g.Version, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string GetSoftwareCenterChildStatus(RemoteSoftwareCenterItem item, SoftwareCenterItemType itemType)
        {
            switch (itemType)
            {
                case SoftwareCenterItemType.ComplianceBaseline:
                    return string.IsNullOrWhiteSpace(item.ComplianceState) ? item.InstallState : item.ComplianceState;
                case SoftwareCenterItemType.SoftwareUpdate:
                    return !string.IsNullOrWhiteSpace(item.ComplianceState) ? item.ComplianceState : item.InstallState;
                default:
                    return string.IsNullOrWhiteSpace(item.InstallState) ? item.EvaluationState : item.InstallState;
            }
        }

        private static string BuildSoftwareCenterStatusSummary(
            IEnumerable<RemoteSoftwareCenterItem> group,
            SoftwareCenterItemType itemType)
        {
            var list = group.ToList();
            var total = list.Count;
            if (total == 0) return string.Empty;

            switch (itemType)
            {
                case SoftwareCenterItemType.Application:
                {
                    var installed = list.Count(i => string.Equals(i.InstallState, "Installed", StringComparison.OrdinalIgnoreCase));
                    var failed = list.Count(i => i.ErrorCode != 0 ||
                        (i.EvaluationState != null && i.EvaluationState.IndexOf("Fail", StringComparison.OrdinalIgnoreCase) >= 0));
                    return failed > 0
                        ? $"{installed} / {total} installed ({failed} failed)"
                        : $"{installed} / {total} installed";
                }
                case SoftwareCenterItemType.SoftwareUpdate:
                {
                    var installed = list.Count(i =>
                        string.Equals(i.ComplianceState, "Installed", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(i.InstallState, "Installed", StringComparison.OrdinalIgnoreCase));
                    var failed = list.Count(i => i.ErrorCode != 0 ||
                        (i.EvaluationState != null && i.EvaluationState.IndexOf("Fail", StringComparison.OrdinalIgnoreCase) >= 0));
                    return failed > 0
                        ? $"{installed} / {total} installed ({failed} failed)"
                        : $"{installed} / {total} installed";
                }
                case SoftwareCenterItemType.ComplianceBaseline:
                {
                    var compliant = list.Count(i =>
                        string.Equals(i.ComplianceState, "Compliant", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(i.InstallState, "Compliant", StringComparison.OrdinalIgnoreCase));
                    var errors = list.Count(i =>
                        string.Equals(i.ComplianceState, "Error", StringComparison.OrdinalIgnoreCase) ||
                        i.ErrorCode != 0);
                    return errors > 0
                        ? $"{compliant} / {total} compliant ({errors} error)"
                        : $"{compliant} / {total} compliant";
                }
                case SoftwareCenterItemType.PackageOrTaskSequence:
                {
                    var succeeded = list.Count(i =>
                        (i.InstallState != null && (i.InstallState.IndexOf("Succeed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                    i.InstallState.IndexOf("Installed", StringComparison.OrdinalIgnoreCase) >= 0)));
                    if (succeeded > 0)
                    {
                        return $"{succeeded} / {total} succeeded";
                    }
                    return BuildStateBreakdown(list.Select(i => string.IsNullOrWhiteSpace(i.InstallState) ? "Available" : i.InstallState));
                }
                default:
                    return $"{total} device(s)";
            }
        }

        public static List<GroupedItemData<InstalledApplicationInfo>> GroupInstalledApplications(
            IEnumerable<InstalledApplicationInfo> items)
        {
            if (items == null) return new List<GroupedItemData<InstalledApplicationInfo>>();
            var result = new List<GroupedItemData<InstalledApplicationInfo>>();

            foreach (var grp in items.Where(i => i != null)
                .GroupBy(i => $"{(i.Name ?? string.Empty).Trim()}|{(i.Version ?? string.Empty).Trim()}|{(i.Architecture ?? string.Empty).Trim()}", StringComparer.OrdinalIgnoreCase))
            {
                var first = grp.First();
                var children = grp
                    .OrderBy(i => i.DeviceName, StringComparer.OrdinalIgnoreCase)
                    .Select(i => new GroupedChildData<InstalledApplicationInfo>
                    {
                        DeviceName = i.DeviceName,
                        Status = string.IsNullOrWhiteSpace(i.Architecture) ? "Installed" : $"Installed ({i.Architecture})",
                        SecondaryDetail = i.InstalledOn,
                        ExtraDetail = i.UninstallString,
                        Payload = i
                    })
                    .ToList();

                var devCount = children.Select(c => c.DeviceName).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                result.Add(new GroupedItemData<InstalledApplicationInfo>
                {
                    GroupKey = grp.Key,
                    Name = first.Name,
                    SecondaryInfo = first.Publisher,
                    Version = first.Version,
                    ExtraInfo = first.Architecture,
                    StatusSummary = $"Installed on {devCount} device(s)",
                    Children = children
                });
            }

            return result
                .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(g => g.Version, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static List<GroupedItemData<WindowsUpdateInfo>> GroupWindowsUpdates(
            IEnumerable<WindowsUpdateInfo> items)
        {
            if (items == null) return new List<GroupedItemData<WindowsUpdateInfo>>();
            var result = new List<GroupedItemData<WindowsUpdateInfo>>();

            foreach (var grp in items.Where(i => i != null)
                .GroupBy(i => (i.HotFixId ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase))
            {
                var first = grp.First();
                var children = grp
                    .OrderBy(i => i.DeviceName, StringComparer.OrdinalIgnoreCase)
                    .Select(i => new GroupedChildData<WindowsUpdateInfo>
                    {
                        DeviceName = i.DeviceName,
                        Status = string.IsNullOrWhiteSpace(i.Status) ? "Installed" : i.Status,
                        SecondaryDetail = i.InstalledOn,
                        ExtraDetail = i.InstalledBy,
                        Payload = i
                    })
                    .ToList();

                var devCount = children.Select(c => c.DeviceName).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                result.Add(new GroupedItemData<WindowsUpdateInfo>
                {
                    GroupKey = grp.Key,
                    Name = first.HotFixId,
                    SecondaryInfo = first.Description,
                    Version = first.Status,
                    StatusSummary = $"Installed on {devCount} device(s)",
                    Children = children
                });
            }

            return result.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static List<GroupedItemData<WindowsServiceInfo>> GroupWindowsServices(
            IEnumerable<WindowsServiceInfo> items)
        {
            if (items == null) return new List<GroupedItemData<WindowsServiceInfo>>();
            var result = new List<GroupedItemData<WindowsServiceInfo>>();

            foreach (var grp in items.Where(i => i != null)
                .GroupBy(i => (i.Name ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase))
            {
                var first = grp.First();
                var children = grp
                    .OrderBy(i => i.DeviceName, StringComparer.OrdinalIgnoreCase)
                    .Select(i => new GroupedChildData<WindowsServiceInfo>
                    {
                        DeviceName = i.DeviceName,
                        Status = i.State,
                        SecondaryDetail = i.StartMode,
                        ExtraDetail = i.StartName,
                        Payload = i
                    })
                    .ToList();

                var distinctStartModes = grp.Select(i => i.StartMode).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                result.Add(new GroupedItemData<WindowsServiceInfo>
                {
                    GroupKey = grp.Key,
                    Name = string.IsNullOrWhiteSpace(first.DisplayName) ? first.Name : first.DisplayName,
                    SecondaryInfo = first.Name,
                    Version = distinctStartModes.Count == 1 ? distinctStartModes[0] : "Mixed",
                    StatusSummary = BuildStateBreakdown(grp.Select(i => i.State)),
                    Children = children
                });
            }

            return result.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static List<GroupedItemData<DriverInfo>> GroupDrivers(
            IEnumerable<DriverInfo> items)
        {
            if (items == null) return new List<GroupedItemData<DriverInfo>>();
            var result = new List<GroupedItemData<DriverInfo>>();

            foreach (var grp in items.Where(i => i != null)
                .GroupBy(i => $"{(i.FriendlyName ?? string.Empty).Trim()}|{(i.DriverVersion ?? string.Empty).Trim()}", StringComparer.OrdinalIgnoreCase))
            {
                var first = grp.First();
                var children = grp
                    .OrderBy(i => i.DeviceName, StringComparer.OrdinalIgnoreCase)
                    .Select(i => new GroupedChildData<DriverInfo>
                    {
                        DeviceName = i.DeviceName,
                        Status = i.IsSigned ? "Signed" : "Unsigned",
                        SecondaryDetail = i.InfName,
                        ExtraDetail = i.DeviceClass,
                        Timestamp = i.DriverDate,
                        Payload = i
                    })
                    .ToList();

                var signed = grp.Count(i => i.IsSigned);
                var total = children.Count;
                result.Add(new GroupedItemData<DriverInfo>
                {
                    GroupKey = grp.Key,
                    Name = first.FriendlyName,
                    SecondaryInfo = first.Manufacturer,
                    Version = first.DriverVersion,
                    ExtraInfo = first.DeviceClass,
                    StatusSummary = signed == total ? $"Signed ({total})" : $"{signed} / {total} signed",
                    Children = children
                });
            }

            return result
                .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(g => g.Version, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static List<GroupedItemData<UserProfileInfo>> GroupUserProfiles(
            IEnumerable<UserProfileInfo> items)
        {
            if (items == null) return new List<GroupedItemData<UserProfileInfo>>();
            var result = new List<GroupedItemData<UserProfileInfo>>();

            foreach (var grp in items.Where(i => i != null)
                .GroupBy(i => string.IsNullOrWhiteSpace(i.AccountName) ? (i.Sid ?? string.Empty).Trim() : i.AccountName.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                var first = grp.First();
                var children = grp
                    .OrderBy(i => i.DeviceName, StringComparer.OrdinalIgnoreCase)
                    .Select(i => new GroupedChildData<UserProfileInfo>
                    {
                        DeviceName = i.DeviceName,
                        Status = i.Loaded ? "Loaded" : "Unloaded",
                        SecondaryDetail = i.LocalPath,
                        ExtraDetail = i.Special ? "Special" : "Standard",
                        Timestamp = i.LastUseTime,
                        Payload = i
                    })
                    .ToList();

                var loaded = grp.Count(i => i.Loaded);
                result.Add(new GroupedItemData<UserProfileInfo>
                {
                    GroupKey = grp.Key,
                    Name = string.IsNullOrWhiteSpace(first.AccountName) ? first.Sid : first.AccountName,
                    SecondaryInfo = first.Sid,
                    Version = first.LocalPath,
                    StatusSummary = loaded > 0
                        ? $"Loaded: {loaded}, Unloaded: {children.Count - loaded}"
                        : $"Unloaded ({children.Count})",
                    Children = children
                });
            }

            return result.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static List<GroupedItemData<LocalGroupMemberInfo>> GroupLocalGroupMembers(
            IEnumerable<LocalGroupMemberInfo> items)
        {
            if (items == null) return new List<GroupedItemData<LocalGroupMemberInfo>>();
            var result = new List<GroupedItemData<LocalGroupMemberInfo>>();

            foreach (var grp in items.Where(i => i != null)
                .GroupBy(i => $"{(i.GroupName ?? string.Empty).Trim()}|{i.FullName.Trim()}", StringComparer.OrdinalIgnoreCase))
            {
                var first = grp.First();
                var children = grp
                    .OrderBy(i => i.DeviceName, StringComparer.OrdinalIgnoreCase)
                    .Select(i => new GroupedChildData<LocalGroupMemberInfo>
                    {
                        DeviceName = i.DeviceName,
                        Status = i.MemberType,
                        SecondaryDetail = i.GroupName,
                        ExtraDetail = i.Domain,
                        Payload = i
                    })
                    .ToList();

                var devCount = children.Select(c => c.DeviceName).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                result.Add(new GroupedItemData<LocalGroupMemberInfo>
                {
                    GroupKey = grp.Key,
                    Name = first.FullName,
                    SecondaryInfo = first.GroupName,
                    Version = first.MemberType,
                    StatusSummary = $"Member on {devCount} device(s)",
                    Children = children
                });
            }

            return result.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static List<GroupedItemData<ClientComponentInfo>> GroupClientComponents(
            IEnumerable<ClientComponentInfo> items)
        {
            if (items == null) return new List<GroupedItemData<ClientComponentInfo>>();
            var result = new List<GroupedItemData<ClientComponentInfo>>();

            foreach (var grp in items.Where(i => i != null)
                .GroupBy(i => (i.Name ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase))
            {
                var first = grp.First();
                var children = grp
                    .OrderBy(i => i.DeviceName, StringComparer.OrdinalIgnoreCase)
                    .Select(i => new GroupedChildData<ClientComponentInfo>
                    {
                        DeviceName = i.DeviceName,
                        Status = i.State,
                        SecondaryDetail = i.Version,
                        ExtraDetail = i.Name,
                        Payload = i
                    })
                    .ToList();

                var versions = grp.Select(i => i.Version).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                result.Add(new GroupedItemData<ClientComponentInfo>
                {
                    GroupKey = grp.Key,
                    Name = string.IsNullOrWhiteSpace(first.DisplayName) ? first.Name : first.DisplayName,
                    SecondaryInfo = first.Name,
                    Version = versions.Count == 1 ? versions[0] : (versions.Count > 1 ? "Multiple" : string.Empty),
                    StatusSummary = BuildStateBreakdown(grp.Select(i => i.State)),
                    Children = children
                });
            }

            return result.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static List<GroupedItemData<ClientCacheItem>> GroupClientCacheItems(
            IEnumerable<ClientCacheItem> items)
        {
            if (items == null) return new List<GroupedItemData<ClientCacheItem>>();
            var result = new List<GroupedItemData<ClientCacheItem>>();

            foreach (var grp in items.Where(i => i != null)
                .GroupBy(i => $"{(i.ContentId ?? string.Empty).Trim()}|{(i.ContentVersion ?? string.Empty).Trim()}", StringComparer.OrdinalIgnoreCase))
            {
                var first = grp.First();
                var children = grp
                    .OrderBy(i => i.DeviceName, StringComparer.OrdinalIgnoreCase)
                    .Select(i => new GroupedChildData<ClientCacheItem>
                    {
                        DeviceName = i.DeviceName,
                        Status = $"{Math.Round(i.SizeMb, 1)} MB",
                        SecondaryDetail = i.Location,
                        ExtraDetail = i.CacheId,
                        Timestamp = i.LastReferenced,
                        Payload = i
                    })
                    .ToList();

                var totalMb = grp.Sum(i => i.SizeMb);
                var devCount = children.Select(c => c.DeviceName).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                result.Add(new GroupedItemData<ClientCacheItem>
                {
                    GroupKey = grp.Key,
                    Name = first.ContentId,
                    SecondaryInfo = string.IsNullOrWhiteSpace(first.ContentVersion) ? string.Empty : $"v{first.ContentVersion}",
                    Version = $"{Math.Round(totalMb, 1)} MB total",
                    StatusSummary = $"Cached on {devCount} device(s)",
                    Children = children
                });
            }

            return result.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static List<GroupedItemData<BitLockerVolumeInfo>> GroupBitLockerVolumes(IEnumerable<BitLockerVolumeInfo> items)
        {
            if (items == null) return new List<GroupedItemData<BitLockerVolumeInfo>>();
            var result = new List<GroupedItemData<BitLockerVolumeInfo>>();

            foreach (var grp in items.Where(i => i != null)
                .GroupBy(i => $"{i.DriveLetter}|{i.ProtectionStatus}|{i.EncryptionMethod}|{i.ConversionStatus}", StringComparer.OrdinalIgnoreCase))
            {
                var first = grp.First();
                var children = grp.OrderBy(i => i.DeviceName, StringComparer.OrdinalIgnoreCase)
                    .Select(i => new GroupedChildData<BitLockerVolumeInfo>
                    {
                        DeviceName = i.DeviceName,
                        Status = i.VolumeType,
                        SecondaryDetail = i.LockStatus,
                        ExtraDetail = $"{i.EncryptionPercentage}%",
                        TimestampText = i.KeyProtectors,
                        Payload = i
                    }).ToList();

                result.Add(new GroupedItemData<BitLockerVolumeInfo>
                {
                    GroupKey = grp.Key,
                    Name = first.DriveLetter,
                    SecondaryInfo = first.ProtectionStatus,
                    Version = first.EncryptionMethod,
                    StatusSummary = first.ConversionStatus,
                    Children = children
                });
            }

            return result.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static List<GroupedItemData<TpmStatusInfo>> GroupTpmStatuses(IEnumerable<TpmStatusInfo> items)
        {
            if (items == null) return new List<GroupedItemData<TpmStatusInfo>>();
            var result = new List<GroupedItemData<TpmStatusInfo>>();

            foreach (var grp in items.Where(i => i != null)
                .GroupBy(i => $"{i.SpecVersion}|{i.IsActivated}|{i.IsEnabled}|{i.IsOwned}|{i.IsReady}", StringComparer.OrdinalIgnoreCase))
            {
                var first = grp.First();
                var children = grp.OrderBy(i => i.DeviceName, StringComparer.OrdinalIgnoreCase)
                    .Select(i => new GroupedChildData<TpmStatusInfo>
                    {
                        DeviceName = i.DeviceName,
                        Status = i.Manufacturer,
                        SecondaryDetail = i.ManufacturerVersion,
                        ExtraDetail = i.FipsReady.ToString(),
                        TimestampText = i.LockedOut.ToString(),
                        Payload = i
                    }).ToList();

                result.Add(new GroupedItemData<TpmStatusInfo>
                {
                    GroupKey = grp.Key,
                    Name = first.SpecVersion,
                    SecondaryInfo = first.IsActivated.ToString(),
                    Version = first.IsEnabled.ToString(),
                    ExtraInfo = first.IsOwned.ToString(),
                    StatusSummary = first.IsReady.ToString(),
                    Children = children
                });
            }

            return result.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static List<GroupedItemData<FirmwareSecurityInfo>> GroupFirmwareSecurity(IEnumerable<FirmwareSecurityInfo> items)
        {
            if (items == null) return new List<GroupedItemData<FirmwareSecurityInfo>>();
            var result = new List<GroupedItemData<FirmwareSecurityInfo>>();

            foreach (var grp in items.Where(i => i != null)
                .GroupBy(i => $"{i.UefiEnabled}|{i.SecureBootEnabled}|{i.CertificateStatus}", StringComparer.OrdinalIgnoreCase))
            {
                var first = grp.First();
                var children = grp.OrderBy(i => i.DeviceName, StringComparer.OrdinalIgnoreCase)
                    .Select(i => new GroupedChildData<FirmwareSecurityInfo>
                    {
                        DeviceName = i.DeviceName,
                        Status = i.BiosVersion,
                        SecondaryDetail = i.CertificateUpdateReadiness,
                        ExtraDetail = i.CertificateError,
                        TimestampText = i.CertificateErrorEvent,
                        ErrorCode = i.ConfidenceLevel,
                        Payload = i
                    }).ToList();

                result.Add(new GroupedItemData<FirmwareSecurityInfo>
                {
                    GroupKey = grp.Key,
                    Name = first.UefiEnabled.ToString(),
                    SecondaryInfo = first.SecureBootEnabled.ToString(),
                    StatusSummary = first.CertificateStatus,
                    Children = children
                });
            }

            return result.OrderByDescending(g => g.DeviceCount).ThenBy(g => g.StatusSummary, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static List<GroupedItemData<VbsStatusInfo>> GroupVbsStatuses(IEnumerable<VbsStatusInfo> items)
        {
            if (items == null) return new List<GroupedItemData<VbsStatusInfo>>();
            var result = new List<GroupedItemData<VbsStatusInfo>>();

            foreach (var grp in items.Where(i => i != null)
                .GroupBy(i => $"{i.VbsStatus}|{i.CredentialGuardStatus}|{i.HvciStatus}", StringComparer.OrdinalIgnoreCase))
            {
                var first = grp.First();
                var children = grp.OrderBy(i => i.DeviceName, StringComparer.OrdinalIgnoreCase)
                    .Select(i => new GroupedChildData<VbsStatusInfo>
                    {
                        DeviceName = i.DeviceName,
                        Status = i.VbsStatus,
                        SecondaryDetail = i.RunningServices,
                        ExtraDetail = i.ConfiguredServices,
                        TimestampText = i.RequiredProperties,
                        Payload = i
                    }).ToList();

                result.Add(new GroupedItemData<VbsStatusInfo>
                {
                    GroupKey = grp.Key,
                    Name = first.VbsStatus,
                    SecondaryInfo = first.CredentialGuardStatus,
                    StatusSummary = first.HvciStatus,
                    Children = children
                });
            }

            return result.OrderByDescending(g => g.DeviceCount).ToList();
        }

        public static List<GroupedItemData<DefenderStatusInfo>> GroupDefenderStatuses(IEnumerable<DefenderStatusInfo> items)
        {
            if (items == null) return new List<GroupedItemData<DefenderStatusInfo>>();
            var result = new List<GroupedItemData<DefenderStatusInfo>>();

            static string Fmt(DateTime? dt) => dt.HasValue ? dt.Value.ToString("yyyy-MM-dd HH:mm") : "Never";

            foreach (var grp in items.Where(i => i != null)
                .GroupBy(i => $"{i.AntivirusEnabled}|{i.RealTimeProtectionEnabled}|{i.SignatureVersion}|{i.ClientVersion}|{i.EngineVersion}", StringComparer.OrdinalIgnoreCase))
            {
                var first = grp.First();
                var children = grp.OrderBy(i => i.DeviceName, StringComparer.OrdinalIgnoreCase)
                    .Select(i => new GroupedChildData<DefenderStatusInfo>
                    {
                        DeviceName = i.DeviceName,
                        Status = Fmt(i.LastSignatureUpdate),
                        SecondaryDetail = Fmt(i.LastFullScan),
                        ExtraDetail = Fmt(i.LastQuickScan),
                        Payload = i
                    }).ToList();

                result.Add(new GroupedItemData<DefenderStatusInfo>
                {
                    GroupKey = grp.Key,
                    Name = first.AntivirusEnabled ? "Enabled" : "Disabled",
                    SecondaryInfo = first.RealTimeProtectionEnabled ? "Enabled" : "Disabled",
                    Version = first.SignatureVersion,
                    ExtraInfo = first.ClientVersion,
                    StatusSummary = first.EngineVersion,
                    Children = children
                });
            }

            return result.OrderByDescending(g => g.DeviceCount).ToList();
        }

        public static List<GroupedItemData<DefenderExclusionInfo>> GroupDefenderExclusions(IEnumerable<DefenderExclusionInfo> items)
        {
            if (items == null) return new List<GroupedItemData<DefenderExclusionInfo>>();
            var result = new List<GroupedItemData<DefenderExclusionInfo>>();

            foreach (var grp in items.Where(i => i != null)
                .GroupBy(i => $"{i.ExclusionType}|{i.Value}", StringComparer.OrdinalIgnoreCase))
            {
                var first = grp.First();
                var children = grp.OrderBy(i => i.DeviceName, StringComparer.OrdinalIgnoreCase)
                    .Select(i => new GroupedChildData<DefenderExclusionInfo>
                    {
                        DeviceName = i.DeviceName,
                        Status = i.ExclusionType,
                        SecondaryDetail = i.Value,
                        Payload = i
                    }).ToList();

                result.Add(new GroupedItemData<DefenderExclusionInfo>
                {
                    GroupKey = grp.Key,
                    Name = first.Value,
                    StatusSummary = first.ExclusionType,
                    Children = children
                });
            }

            return result.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static List<GroupedItemData<FirewallProfileInfo>> GroupFirewallProfiles(IEnumerable<FirewallProfileInfo> items)
        {
            if (items == null) return new List<GroupedItemData<FirewallProfileInfo>>();
            var result = new List<GroupedItemData<FirewallProfileInfo>>();

            foreach (var grp in items.Where(i => i != null)
                .GroupBy(i => $"{i.ProfileName}|{i.Enabled}", StringComparer.OrdinalIgnoreCase))
            {
                var first = grp.First();
                var children = grp.OrderBy(i => i.DeviceName, StringComparer.OrdinalIgnoreCase)
                    .Select(i => new GroupedChildData<FirewallProfileInfo>
                    {
                        DeviceName = i.DeviceName,
                        Status = i.Enabled ? "Enabled" : "Disabled",
                        SecondaryDetail = i.DefaultInboundAction,
                        ExtraDetail = i.DefaultOutboundAction,
                        Payload = i
                    }).ToList();

                result.Add(new GroupedItemData<FirewallProfileInfo>
                {
                    GroupKey = grp.Key,
                    Name = first.ProfileName,
                    StatusSummary = first.Enabled ? "Enabled" : "Disabled",
                    Children = children
                });
            }

            return result.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static List<GroupedItemData<FirewallRuleInfo>> GroupFirewallRules(IEnumerable<FirewallRuleInfo> items)
        {
            if (items == null) return new List<GroupedItemData<FirewallRuleInfo>>();
            var result = new List<GroupedItemData<FirewallRuleInfo>>();

            foreach (var grp in items.Where(i => i != null)
                .GroupBy(i => $"{i.DisplayName}|{i.Action}|{i.Direction}|{i.Profiles}|{i.Enabled}", StringComparer.OrdinalIgnoreCase))
            {
                var first = grp.First();
                var children = grp.OrderBy(i => i.DeviceName, StringComparer.OrdinalIgnoreCase)
                    .Select(i => new GroupedChildData<FirewallRuleInfo>
                    {
                        DeviceName = i.DeviceName,
                        Status = i.Enabled ? "Enabled" : "Disabled",
                        SecondaryDetail = i.Group,
                        ExtraDetail = $"{i.Direction} / {i.Action}",
                        Payload = i
                    }).ToList();

                result.Add(new GroupedItemData<FirewallRuleInfo>
                {
                    GroupKey = grp.Key,
                    Name = first.DisplayName,
                    SecondaryInfo = first.Action,
                    Version = first.Direction,
                    ExtraInfo = first.Profiles,
                    StatusSummary = first.Enabled ? "Enabled" : "Disabled",
                    Children = children
                });
            }

            return result.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static string BuildStateBreakdown(IEnumerable<string?> states)
        {
            var counts = states
                .Select(s => string.IsNullOrWhiteSpace(s) ? "Unknown" : s!.Trim())
                .GroupBy(s => s, StringComparer.OrdinalIgnoreCase)
                .Select(g => new { State = g.First(), Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.State, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return counts.Count == 0
                ? string.Empty
                : string.Join(", ", counts.Select(c => $"{c.State}: {c.Count}"));
        }
    }
}
