using System;
using System.Collections.Generic;

namespace OpenRCT.Core.Models
{
    public sealed class ClientInfoSummary
    {
        public string DeviceName { get; set; } = string.Empty;
        public string ClientVersion { get; set; } = string.Empty;
        public string AssignedSiteCode { get; set; } = string.Empty;
        public string ManagementPoint { get; set; } = string.Empty;
        public string ClientGuid { get; set; } = string.Empty;
        public string CacheLocation { get; set; } = string.Empty;
        public int CacheSizeMb { get; set; }
        public int CacheUsedMb { get; set; }
        public string CcmExecStatus { get; set; } = string.Empty;
        public List<ClientComponentInfo> Components { get; set; } = new List<ClientComponentInfo>();
        public List<ClientCacheItem> CacheItems { get; set; } = new List<ClientCacheItem>();
    }

    public sealed class ClientComponentInfo
    {
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
    }

    public sealed class ClientCacheItem
    {
        public string CacheId { get; set; } = string.Empty;
        public string ContentId { get; set; } = string.Empty;
        public string ContentVersion { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public double SizeMb { get; set; }
        public DateTime? LastReferenced { get; set; }
    }

    public enum SoftwareCenterItemType
    {
        Application,
        SoftwareUpdate,
        PackageOrTaskSequence
    }

    public sealed class RemoteSoftwareCenterItem
    {
        public string DeviceName { get; set; } = string.Empty;
        public SoftwareCenterItemType ItemType { get; set; }
        public string Id { get; set; } = string.Empty;
        public string Revision { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Publisher { get; set; } = string.Empty;
        public string SoftwareVersion { get; set; } = string.Empty;
        public string InstallState { get; set; } = string.Empty;
        public string EvaluationState { get; set; } = string.Empty;
        public bool IsMachineTarget { get; set; }
        public DateTime? Deadline { get; set; }
    }

    public sealed class DeviceCollectionMembershipInfo
    {
        public string DeviceName { get; set; } = string.Empty;
        public string CollectionId { get; set; } = string.Empty;
        public string CollectionName { get; set; } = string.Empty;
        public string MembershipType { get; set; } = string.Empty;
        public int MemberCount { get; set; }
        public string Comment { get; set; } = string.Empty;
        public List<MaintenanceWindowInfo> MaintenanceWindows { get; set; } = new List<MaintenanceWindowInfo>();
    }

    public sealed class MaintenanceWindowInfo
    {
        public string CollectionId { get; set; } = string.Empty;
        public string CollectionName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ServiceWindowType { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public DateTime? StartTime { get; set; }
        public int DurationMinutes { get; set; }
    }

    public sealed class SmsCollectionSummary
    {
        public string CollectionId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int MemberCount { get; set; }
        public string Comment { get; set; } = string.Empty;
        public override string ToString() => $"{Name} ({CollectionId})";
    }

    public sealed class RemoteProcessInfo
    {
        public string DeviceName { get; set; } = string.Empty;
        public uint ProcessId { get; set; }
        public uint ParentProcessId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Owner { get; set; } = string.Empty;
        public double WorkingSetMb { get; set; }
        public DateTime? CreationDate { get; set; }
        public string CommandLine { get; set; } = string.Empty;
    }

    public sealed class PingResultEntry
    {
        public string DeviceName { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public bool IsOnline { get; set; }
        public long? RoundtripMs { get; set; }
        public string StatusText { get; set; } = string.Empty;
        public int SentCount { get; set; }
        public int SuccessCount { get; set; }
        public DateTime LastChecked { get; set; } = DateTime.Now;
    }

    public sealed class AdGroupMembershipInfo
    {
        public string DeviceName { get; set; } = string.Empty;
        public string GroupName { get; set; } = string.Empty;
        public string DistinguishedName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public sealed class LapsPasswordResult
    {
        public string DeviceName { get; set; } = string.Empty;
        public string DistinguishedName { get; set; } = string.Empty;
        public string LapsType { get; set; } = "None";
        public string AccountName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public DateTime? ExpirationTime { get; set; }
        public bool HasPassword => !string.IsNullOrEmpty(Password);
        public string? ErrorMessage { get; set; }
    }

    public sealed class BitLockerRecoveryKeyInfo
    {
        public string DeviceName { get; set; } = string.Empty;
        public string Source { get; set; } = "Active Directory";
        public string RecoveryKeyId { get; set; } = string.Empty;
        public string RecoveryPassword { get; set; } = string.Empty;
        public string VolumeGuid { get; set; } = string.Empty;
        public DateTime? CreatedUtc { get; set; }
    }

    public sealed class StatusMessageEntry
    {
        public string DeviceName { get; set; } = string.Empty;
        public DateTime Time { get; set; }
        public string Severity { get; set; } = string.Empty;
        public uint MessageId { get; set; }
        public string Component { get; set; } = string.Empty;
        public string SiteCode { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public sealed class SchedulePowerOptions
    {
        public bool IsShutdown { get; set; }
        public int DelaySeconds { get; set; } = 60;
        public bool ForceCloseApps { get; set; } = true;
        public bool SkipIfUserLoggedOn { get; set; }
        public string Message { get; set; } = "Your IT administrator has scheduled a system restart.";
    }
}
