using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OpenCMTools.Core.Models
{
    // =========================================================================
    // Remote Windows Security Models (8 tabs)
    // =========================================================================

    public sealed class BitLockerVolumeInfo
    {
        public string DeviceName { get; set; } = string.Empty;
        public string DriveLetter { get; set; } = string.Empty;
        public string VolumeType { get; set; } = string.Empty;
        public string ProtectionStatus { get; set; } = "Unknown";
        public string ConversionStatus { get; set; } = "Unknown";
        public int EncryptionPercentage { get; set; }
        public string EncryptionMethod { get; set; } = "None";
        public string LockStatus { get; set; } = "Unknown";
        public string KeyProtectors { get; set; } = string.Empty;
    }

    public sealed class TpmStatusInfo
    {
        public string DeviceName { get; set; } = string.Empty;
        public string SpecVersion { get; set; } = "Not Present";
        public bool IsActivated { get; set; }
        public bool IsEnabled { get; set; }
        public bool IsOwned { get; set; }
        public bool IsReady { get; set; }
        public string Manufacturer { get; set; } = string.Empty;
        public string ManufacturerVersion { get; set; } = string.Empty;
        public bool FipsReady { get; set; }
        public bool LockedOut { get; set; }
    }

    public sealed class FirmwareSecurityInfo
    {
        public string DeviceName { get; set; } = string.Empty;
        public bool UefiEnabled { get; set; }
        public bool SecureBootEnabled { get; set; }
        public string BiosVersion { get; set; } = string.Empty;
        public string CertificateStatus { get; set; } = "Unknown";
        public string CertificateUpdateReadiness { get; set; } = "Unknown";
        public string CertificateError { get; set; } = string.Empty;
        public string CertificateErrorEvent { get; set; } = string.Empty;
        public string ConfidenceLevel { get; set; } = string.Empty;
    }

    public sealed class VbsStatusInfo
    {
        public string DeviceName { get; set; } = string.Empty;
        public string VbsStatus { get; set; } = "Not Enabled";
        public string CredentialGuardStatus { get; set; } = "Not Running";
        public string HvciStatus { get; set; } = "Not Running";
        public string RunningServices { get; set; } = string.Empty;
        public string ConfiguredServices { get; set; } = string.Empty;
        public string RequiredProperties { get; set; } = string.Empty;
    }

    public sealed class DefenderStatusInfo
    {
        public string DeviceName { get; set; } = string.Empty;
        public bool AntivirusEnabled { get; set; }
        public bool RealTimeProtectionEnabled { get; set; }
        public string SignatureVersion { get; set; } = string.Empty;
        public string ClientVersion { get; set; } = string.Empty;
        public string EngineVersion { get; set; } = string.Empty;
        public DateTime? LastSignatureUpdate { get; set; }
        public DateTime? LastFullScan { get; set; }
        public DateTime? LastQuickScan { get; set; }
    }

    public sealed class DefenderExclusionInfo
    {
        public string DeviceName { get; set; } = string.Empty;
        public string ExclusionType { get; set; } = "Path";
        public string Value { get; set; } = string.Empty;
    }

    public sealed class FirewallProfileInfo
    {
        public string DeviceName { get; set; } = string.Empty;
        public string ProfileName { get; set; } = string.Empty;
        public bool Enabled { get; set; }
        public string DefaultInboundAction { get; set; } = string.Empty;
        public string DefaultOutboundAction { get; set; } = string.Empty;
        public string LogFileName { get; set; } = string.Empty;
    }

    public sealed class FirewallRuleInfo
    {
        public string DeviceName { get; set; } = string.Empty;
        public string RuleId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Group { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string Direction { get; set; } = string.Empty;
        public string Profiles { get; set; } = string.Empty;
        public bool Enabled { get; set; }
    }

    // =========================================================================
    // Remote Event Viewer Models & XPath Query Builder
    // =========================================================================

    public sealed class RemoteEventLogQuery
    {
        public string LogName { get; set; } = "Application";
        public string Severity { get; set; } = "All"; // All, Critical, Error, Warning, Information, Verbose
        public string Source { get; set; } = string.Empty;
        public string EventIdsText { get; set; } = string.Empty; // single or comma-separated IDs
        public string Keyword { get; set; } = string.Empty;
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public int MaxEntries { get; set; } = 200;
        public int PageIndex { get; set; }
    }

    public sealed class RemoteEventEntry
    {
        public long RecordId { get; set; }
        public string Level { get; set; } = string.Empty;
        public DateTime? TimeCreated { get; set; }
        public string TimeCreatedDisplay => TimeCreated.HasValue ? TimeCreated.Value.ToString("yyyy-MM-dd HH:mm:ss") : string.Empty;
        public string Source { get; set; } = string.Empty;
        public int EventId { get; set; }
        public string TaskCategory { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string MessagePreview
        {
            get
            {
                if (string.IsNullOrEmpty(Message)) return string.Empty;
                var oneLine = Message.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ").Trim();
                return oneLine.Length > 220 ? oneLine.Substring(0, 220) + "..." : oneLine;
            }
        }
        public string MachineName { get; set; } = string.Empty;
        public string UserSid { get; set; } = string.Empty;
        public string RawXml { get; set; } = string.Empty;
    }

    public sealed class RemoteEventPageResult
    {
        public string LogName { get; set; } = string.Empty;
        public List<RemoteEventEntry> Entries { get; set; } = new List<RemoteEventEntry>();
        public int PageIndex { get; set; }
        public int PageSize { get; set; } = 200;
        public long? TotalEntriesInLog { get; set; }
        public bool HasMorePages { get; set; }
    }

    /// <summary>
    /// Builds safe Windows Event Log XPath 1.0 queries from RemoteEventLogQuery filters
    /// for server-side filtering via EventLogQuery.
    /// </summary>
    public static class EventLogXPathBuilder
    {
        public static string BuildXPath(RemoteEventLogQuery query)
        {
            if (query == null) return "*";

            var clauses = new List<string>();

            // Level filter (Windows Event Log Levels: 1=Critical, 2=Error, 3=Warning, 4=Information/0=LogAlways, 5=Verbose)
            switch ((query.Severity ?? "All").Trim().ToLowerInvariant())
            {
                case "critical":
                    clauses.Add("Level=1");
                    break;
                case "error":
                    clauses.Add("Level=2");
                    break;
                case "critical & error":
                case "critical+error":
                    clauses.Add("(Level=1 or Level=2)");
                    break;
                case "warning":
                    clauses.Add("Level=3");
                    break;
                case "information":
                case "informational":
                    clauses.Add("(Level=0 or Level=4)");
                    break;
                case "verbose":
                    clauses.Add("Level=5");
                    break;
            }

            // Event ID filter (supports single ID or comma/space-separated IDs, e.g. "1000, 1001, 7036")
            if (!string.IsNullOrWhiteSpace(query.EventIdsText))
            {
                var ids = query.EventIdsText
                    .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => int.TryParse(s.Trim(), out var id) && id >= 0 ? (int?)id : null)
                    .Where(id => id.HasValue)
                    .Select(id => id!.Value)
                    .Distinct()
                    .ToList();

                if (ids.Count == 1)
                {
                    clauses.Add($"EventID={ids[0]}");
                }
                else if (ids.Count > 1)
                {
                    clauses.Add("(" + string.Join(" or ", ids.Select(id => $"EventID={id}")) + ")");
                }
            }

            // Date range filter (TimeCreated[@SystemTime >= '...' and @SystemTime <= '...'])
            var timeParts = new List<string>();
            if (query.StartDate.HasValue)
            {
                var startUtc = query.StartDate.Value.Date.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.000Z");
                timeParts.Add($"@SystemTime>='{startUtc}'");
            }
            if (query.EndDate.HasValue)
            {
                var endUtc = query.EndDate.Value.Date.AddDays(1).AddSeconds(-1).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.999Z");
                timeParts.Add($"@SystemTime<='{endUtc}'");
            }
            if (timeParts.Count > 0)
            {
                clauses.Add("TimeCreated[" + string.Join(" and ", timeParts) + "]");
            }

            if (clauses.Count == 0)
            {
                return "*";
            }

            var sb = new StringBuilder("*[System[");
            sb.Append(string.Join(" and ", clauses));
            sb.Append("]]");
            return sb.ToString();
        }
    }
}
