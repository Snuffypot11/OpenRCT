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
}
