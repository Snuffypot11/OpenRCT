using System;

namespace OpenCMTools.Core.Models
{
    public sealed class OperatingSystemInfo
    {
        public string DeviceName { get; set; } = string.Empty;
        public string Caption { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string BuildNumber { get; set; } = string.Empty;
        public string Architecture { get; set; } = string.Empty;
        public DateTime? InstallDate { get; set; }
        public DateTime? LastBootUpTime { get; set; }
        public TimeSpan? Uptime => LastBootUpTime.HasValue ? DateTime.Now - LastBootUpTime.Value : null;
        public string RegisteredUser { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
        public string WindowsDirectory { get; set; } = string.Empty;
        public string Domain { get; set; } = string.Empty;
        public string LoggedOnUser { get; set; } = string.Empty;
    }

    public sealed class HardwareInfo
    {
        public string DeviceName { get; set; } = string.Empty;
        public string Manufacturer { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
        public string BiosVersion { get; set; } = string.Empty;
        public DateTime? BiosReleaseDate { get; set; }
        public string ProcessorName { get; set; } = string.Empty;
        public int PhysicalCores { get; set; }
        public int LogicalProcessors { get; set; }
        public double TotalMemoryGb { get; set; }
        public double FreeMemoryGb { get; set; }
        public string SystemType { get; set; } = string.Empty;
        public string TpmVersion { get; set; } = "Unknown";
        public string SecureBootState { get; set; } = "Unknown";
    }

    public sealed class DiskInfo
    {
        public string DeviceName { get; set; } = string.Empty;
        public string DriveLetter { get; set; } = string.Empty;
        public string VolumeName { get; set; } = string.Empty;
        public string FileSystem { get; set; } = string.Empty;
        public string DriveType { get; set; } = string.Empty;
        public double TotalSizeGb { get; set; }
        public double FreeSpaceGb { get; set; }
        public double UsedSpaceGb => Math.Max(0, TotalSizeGb - FreeSpaceGb);
        public double PercentFree => TotalSizeGb > 0 ? Math.Round((FreeSpaceGb / TotalSizeGb) * 100.0, 1) : 0;
        public double PercentUsed => TotalSizeGb > 0 ? Math.Round((UsedSpaceGb / TotalSizeGb) * 100.0, 1) : 0;
    }

    public sealed class InstalledApplicationInfo
    {
        public string DeviceName { get; set; } = string.Empty;
        public bool IsSelected { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Architecture { get; set; } = string.Empty;
        public string Publisher { get; set; } = string.Empty;
        public string InstalledOn { get; set; } = "n/a";
        public string Version { get; set; } = string.Empty;
        public string UninstallString { get; set; } = string.Empty;
        public string QuietUninstallString { get; set; } = string.Empty;
        public string InstallLocation { get; set; } = string.Empty;
        public string RegistryKeyPath { get; set; } = string.Empty;
    }

    public sealed class WindowsUpdateInfo
    {
        public string DeviceName { get; set; } = string.Empty;
        public string HotFixId { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string InstalledBy { get; set; } = string.Empty;
        public string InstalledOn { get; set; } = string.Empty;
        public string Status { get; set; } = "Installed";
    }

    public sealed class WindowsServiceInfo
    {
        public string DeviceName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string StartMode { get; set; } = string.Empty;
        public string StartName { get; set; } = string.Empty;
        public string PathName { get; set; } = string.Empty;
    }

    public sealed class DriverInfo
    {
        public string DeviceName { get; set; } = string.Empty;
        public string FriendlyName { get; set; } = string.Empty;
        public string DeviceClass { get; set; } = string.Empty;
        public string Manufacturer { get; set; } = string.Empty;
        public string DriverVersion { get; set; } = string.Empty;
        public DateTime? DriverDate { get; set; }
        public bool IsSigned { get; set; }
        public string InfName { get; set; } = string.Empty;
    }

    public sealed class UserProfileInfo
    {
        public string DeviceName { get; set; } = string.Empty;
        public string Sid { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string LocalPath { get; set; } = string.Empty;
        public DateTime? LastUseTime { get; set; }
        public bool Loaded { get; set; }
        public bool Special { get; set; }
        public bool RoamingConfigured { get; set; }
    }

    public sealed class LocalGroupMemberInfo
    {
        public string DeviceName { get; set; } = string.Empty;
        public string GroupName { get; set; } = string.Empty;
        public string Domain { get; set; } = string.Empty;
        public string MemberName { get; set; } = string.Empty;
        public string MemberType { get; set; } = string.Empty;
        public string FullName => string.IsNullOrWhiteSpace(Domain) ? MemberName : $"{Domain}\\{MemberName}";
    }

    public sealed class BatteryInfo
    {
        public string DeviceName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int EstimatedChargeRemaining { get; set; }
        public int EstimatedRunTimeMinutes { get; set; }
        public string BatteryStatusDescription { get; set; } = string.Empty;
        public int? DesignCapacityMwh { get; set; }
        public int? FullChargeCapacityMwh { get; set; }
        public double? HealthPercent =>
            DesignCapacityMwh.HasValue && DesignCapacityMwh.Value > 0 && FullChargeCapacityMwh.HasValue
                ? Math.Round((FullChargeCapacityMwh.Value / (double)DesignCapacityMwh.Value) * 100.0, 1)
                : null;
    }
}
