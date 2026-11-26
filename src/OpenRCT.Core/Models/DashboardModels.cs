using System;
using System.Collections.Generic;

namespace OpenRCT.Core.Models
{
    public sealed class PieSliceData
    {
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public int Value { get; set; }
        public string ColorHex { get; set; } = "#63B3ED";
    }

    public sealed class OrganizationalUnitInfo
    {
        public string Name { get; set; } = string.Empty;
        public string DistinguishedName { get; set; } = string.Empty;
        public string CanonicalPath { get; set; } = string.Empty;
        public override string ToString() => string.IsNullOrWhiteSpace(CanonicalPath) ? DistinguishedName : CanonicalPath;
    }

    // ---- Dashboard 1: Active Directory Cleanup Tool ----

    public enum DevicePresenceBucket
    {
        ConfigMgrOnly,
        Both,
        ActiveDirectoryOnly
    }

    public sealed class AdComputerRecord
    {
        public string Name { get; set; } = string.Empty;
        public string DistinguishedName { get; set; } = string.Empty;
        public DateTime? PasswordLastSet { get; set; }
        public string OperatingSystem { get; set; } = string.Empty;
        public string OperatingSystemVersion { get; set; } = string.Empty;
        public DateTime? Created { get; set; }
        public DateTime? LastLogonTimestamp { get; set; }
        public bool Enabled { get; set; } = true;
        public bool HasLegacyLapsPassword { get; set; }
        public DateTime? LegacyLapsExpiration { get; set; }
        public bool HasWindowsLapsPassword { get; set; }
        public DateTime? WindowsLapsExpiration { get; set; }
    }

    public sealed class ConfigMgrDeviceRecord
    {
        public string ResourceId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string OperatingSystem { get; set; } = string.Empty;
        public string LastLogonUser { get; set; } = string.Empty;
        public string LastLogonDomain { get; set; } = string.Empty;
        public DateTime? LastLogonTime { get; set; }
        public string Domain { get; set; } = string.Empty;
        public string DistinguishedName { get; set; } = string.Empty;
        public DateTime? LastCheckInTime { get; set; }
        public bool ClientInstalled { get; set; }
        public string LapsClientArchitecture { get; set; } = string.Empty;
    }

    public sealed class AdCleanupRow
    {
        public DevicePresenceBucket Bucket { get; set; }
        public string BucketLabel => Bucket switch
        {
            DevicePresenceBucket.ConfigMgrOnly => "ConfigMgr",
            DevicePresenceBucket.Both => "Both",
            DevicePresenceBucket.ActiveDirectoryOnly => "Active Directory",
            _ => "Unknown"
        };

        public string Name { get; set; } = string.Empty;
        public string AdDistinguishedName { get; set; } = string.Empty;
        public DateTime? AdPasswordLastSet { get; set; }
        public string AdOs { get; set; } = string.Empty;
        public string AdOsVersion { get; set; } = string.Empty;
        public DateTime? AdCreated { get; set; }
        public DateTime? AdLastLogonTimestamp { get; set; }
        public string SccmOperatingSystem { get; set; } = string.Empty;
        public string SccmLastLogonUser { get; set; } = string.Empty;
        public string SccmLastLogonDomain { get; set; } = string.Empty;
        public DateTime? SccmLastLogonTime { get; set; }
        public string SccmDomain { get; set; } = string.Empty;
        public string SccmDistinguishedName { get; set; } = string.Empty;
        public DateTime? SccmLastCheckInTime { get; set; }
        public bool? SccmClientInstalled { get; set; }
        public string? SccmResourceId { get; set; }
    }

    public sealed class AdCleanupScanResult
    {
        public List<AdCleanupRow> Rows { get; set; } = new List<AdCleanupRow>();
        public List<PieSliceData> ChartSlices { get; set; } = new List<PieSliceData>();
        public List<DeviceErrorEntry> Errors { get; set; } = new List<DeviceErrorEntry>();
    }

    // ---- Dashboard 2: LAPS Dashboard ----

    public enum LapsClientInstallState
    {
        X64Installed,
        X86Installed,
        WindowsBuiltIn,
        NotInstalled
    }

    public sealed class LapsDashboardRow
    {
        public string Name { get; set; } = string.Empty;
        public string DistinguishedName { get; set; } = string.Empty;
        public DateTime? PasswordLastSet { get; set; }
        public string Os { get; set; } = string.Empty;
        public string OsVersion { get; set; } = string.Empty;
        public DateTime? LapsPasswordExpiration { get; set; }
        public bool HasLapsPassword { get; set; }
        public LapsClientInstallState ClientInstallState { get; set; }
        public string ClientInstallStateLabel => ClientInstallState switch
        {
            LapsClientInstallState.X64Installed => "x64 LAPS Client Installed",
            LapsClientInstallState.X86Installed => "x86 LAPS Client Installed",
            LapsClientInstallState.WindowsBuiltIn => "Windows Built-in LAPS",
            _ => "No LAPS Client Installed"
        };
        public DateTime? Created { get; set; }
    }

    public sealed class LapsDashboardScanResult
    {
        public List<LapsDashboardRow> Rows { get; set; } = new List<LapsDashboardRow>();
        public List<PieSliceData> PasswordInAdSlices { get; set; } = new List<PieSliceData>();
        public List<PieSliceData> ClientInstallStateSlices { get; set; } = new List<PieSliceData>();
        public List<DeviceErrorEntry> Errors { get; set; } = new List<DeviceErrorEntry>();
    }

    // ---- Dashboard 3: Software Updates Deployment Status ----

    public enum UpdateComplianceState
    {
        Compliant,
        NonCompliant,
        Unknown
    }

    public sealed class SoftwareUpdateGroupInfo
    {
        public string CiUniqueId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public int UpdateCount { get; set; }
        public override string ToString() => Title;
    }

    public sealed class RawDeviceUpdateRecord
    {
        public string ResourceId { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public string UpdateCiId { get; set; } = string.Empty;
        public string ArticleId { get; set; } = string.Empty;
        public string UpdateTitle { get; set; } = string.Empty;
        public string Classification { get; set; } = string.Empty;
        public DateTime? DatePosted { get; set; }
        public bool IsDeployed { get; set; }
        public List<string> UpdateGroupIds { get; set; } = new List<string>();
        /// <summary>
        /// 0 = Unknown, 1 = Not Applicable, 2 = Missing/Required, 3 = Installed
        /// </summary>
        public int StatusCode { get; set; }
    }

    public sealed class SoftwareUpdatesFilter
    {
        public bool LimitToDeployedUpdates { get; set; }
        public HashSet<string> SelectedUpdateGroupIds { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public int UpdatesOlderThanDays { get; set; }
        public bool IncludeCriticalUpdates { get; set; } = true;
        public bool IncludeDefinitionUpdates { get; set; } = true;
        public bool IncludeOtherClassifications { get; set; } = true;
        public bool IncludeSecurityUpdates { get; set; } = true;
        public bool IncludeUpdateRollups { get; set; } = true;
        public bool IncludeUpdates { get; set; } = true;
    }

    public sealed class DeviceUpdateComplianceRow
    {
        public string ResourceId { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public UpdateComplianceState ComplianceState { get; set; }
        public string ComplianceLabel => ComplianceState.ToString();
        public int MissingCount { get; set; }
        public int InstalledCount { get; set; }
        public int UnknownCount { get; set; }
        public string MissingCategories { get; set; } = string.Empty;
        public List<RawDeviceUpdateRecord> MissingUpdates { get; set; } = new List<RawDeviceUpdateRecord>();
    }

    public sealed class SoftwareUpdatesAnalysisResult
    {
        public List<DeviceUpdateComplianceRow> DeviceRows { get; set; } = new List<DeviceUpdateComplianceRow>();
        public List<PieSliceData> ComplianceSlices { get; set; } = new List<PieSliceData>();
        public List<PieSliceData> MissingByCategorySlices { get; set; } = new List<PieSliceData>();
        public List<DeviceErrorEntry> Errors { get; set; } = new List<DeviceErrorEntry>();
    }

    // ---- Dashboard 4: BitLocker Compliance (AD + ConfigMgr, no Entra ID) ----

    public enum BitLockerKeyStorageBucket
    {
        None,
        ActiveDirectory,
        ConfigMgr,
        MultipleLocations
    }

    public enum BitLockerUnifiedComplianceState
    {
        CmCompliant,
        EncryptedUnmanaged,
        NonCompliant,
        Unknown
    }

    public sealed class ConfigMgrBitLockerRecord
    {
        public string ResourceId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string DistinguishedName { get; set; } = string.Empty;
        public string OperatingSystem { get; set; } = string.Empty;
        public string OperatingSystemVersion { get; set; } = string.Empty;
        public bool HasConfigMgrRecoveryKey { get; set; }
        public string CmBitLockerCompliance { get; set; } = "Unknown";
        public string EncryptionMethod { get; set; } = "Unknown";
        public string ProtectionStatus { get; set; } = "Unknown";
        public int? ConversionStatus { get; set; }
    }

    public sealed class BitLockerComplianceRow
    {
        public string Name { get; set; } = string.Empty;
        public string ResourceId { get; set; } = string.Empty;
        public BitLockerKeyStorageBucket KeyStorageBucket { get; set; }
        public string KeyStorage { get; set; } = "None";
        public BitLockerUnifiedComplianceState UnifiedComplianceState { get; set; }
        public string UnifiedCompliance => UnifiedComplianceState switch
        {
            BitLockerUnifiedComplianceState.CmCompliant => "CM Compliant",
            BitLockerUnifiedComplianceState.EncryptedUnmanaged => "Encrypted (Unmanaged)",
            BitLockerUnifiedComplianceState.NonCompliant => "Non-Compliant",
            _ => "Unknown"
        };
        public string CmBitLockerCompliance { get; set; } = "Unknown";
        public string EncryptionMethod { get; set; } = "Unknown";
        public string ProtectionStatus { get; set; } = "Unknown";
        public string DistinguishedName { get; set; } = string.Empty;
        public DateTime? PasswordLastSet { get; set; }
        public string OperatingSystem { get; set; } = string.Empty;
        public string OperatingSystemVersion { get; set; } = string.Empty;
    }

    public sealed class BitLockerComplianceScanResult
    {
        public List<BitLockerComplianceRow> Rows { get; set; } = new List<BitLockerComplianceRow>();
        public List<PieSliceData> KeyStorageSlices { get; set; } = new List<PieSliceData>();
        public List<PieSliceData> UnifiedComplianceSlices { get; set; } = new List<PieSliceData>();
        public List<DeviceErrorEntry> Errors { get; set; } = new List<DeviceErrorEntry>();
    }

    // ---- Dashboard 5: Content Distribution Monitor ----

    public enum ContentDistributionState
    {
        Success,
        InProgress,
        Error,
        Unknown
    }

    public sealed class DistributionPointSummary
    {
        public string ServerName { get; set; } = string.Empty;
        public string NalPath { get; set; } = string.Empty;
        public string SiteCode { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool HttpsEnabled { get; set; }
        public bool LedbatEnabled { get; set; }
        public bool InternetFacing { get; set; }
        public bool MulticastEnabled { get; set; }
        public bool PeerDp { get; set; }
        public bool PullDp { get; set; }
        public bool MaintenanceModeEnabled { get; set; }
        public bool PxeEnabled { get; set; }
        public string PercentFreeSpace { get; set; } = string.Empty;

        public int SuccessCount { get; set; }
        public int InProgressCount { get; set; }
        public int ErrorCount { get; set; }
        public int TotalCount => SuccessCount + InProgressCount + ErrorCount;
    }

    public sealed class ContentDistributionItem
    {
        public string ContentName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public string DistributionPoint { get; set; } = string.Empty;
        public string ServerNalPath { get; set; } = string.Empty;
        public int SourceVersion { get; set; }
        public string PackageId { get; set; } = string.Empty;
        public string ObjectId { get; set; } = string.Empty;
        public double SourceSizeMb { get; set; }
        public ContentDistributionState State { get; set; }
        public string DistributionStatus => State switch
        {
            ContentDistributionState.Success => "Success",
            ContentDistributionState.InProgress => "In Progress",
            ContentDistributionState.Error => "Error",
            _ => "Unknown"
        };
        public int MessageState { get; set; }
        public uint MessageId { get; set; }
        public DateTime? LastUpdateDate { get; set; }
        public string Description { get; set; } = string.Empty;
        public string StatusDetails { get; set; } = string.Empty;
    }

    public sealed class ContentDpStatusDetail
    {
        public string PackageId { get; set; } = string.Empty;
        public string DistributionPoint { get; set; } = string.Empty;
        public string ServerNalPath { get; set; } = string.Empty;
        public string SiteCode { get; set; } = string.Empty;
        public ContentDistributionState State { get; set; }
        public string StatusLabel => State switch
        {
            ContentDistributionState.Success => "Success",
            ContentDistributionState.InProgress => "In Progress",
            ContentDistributionState.Error => "Error",
            _ => "Unknown"
        };
        public uint MessageId { get; set; }
        public string MessageSummary { get; set; } = string.Empty;
        public DateTime? LastUpdateDate { get; set; }
    }

    public sealed class ContentDistributionSnapshot
    {
        public List<DistributionPointSummary> DistributionPoints { get; set; } = new List<DistributionPointSummary>();
        public List<ContentDistributionItem> Items { get; set; } = new List<ContentDistributionItem>();
        public List<DeviceErrorEntry> Errors { get; set; } = new List<DeviceErrorEntry>();
    }

    // ---- Dashboard 6: Hardware and Firmware Audit ----

    public enum HardwareAuditDimension
    {
        Chassis,
        Manufacturer,
        Model,
        UefiStatus,
        SecureBootStatus,
        TpmStatus,
        BiosVersion,
        OsVersion
    }

    public sealed class HardwareFirmwareAuditRow
    {
        public string ResourceId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string DistinguishedName { get; set; } = string.Empty;
        public string Manufacturer { get; set; } = "Unknown";
        public string Model { get; set; } = "Unknown";
        public string ChassisType { get; set; } = "Unknown";
        public string UefiStatus { get; set; } = "Unknown";
        public string SecureBootStatus { get; set; } = "Unknown";
        public string TpmStatus { get; set; } = "Unknown";
        public string TpmVersion { get; set; } = string.Empty;
        public string BiosVersion { get; set; } = "Unknown";
        public DateTime? BiosReleaseDate { get; set; }
        public string OperatingSystem { get; set; } = string.Empty;
        public string OsVersion { get; set; } = "Unknown";
    }
}
