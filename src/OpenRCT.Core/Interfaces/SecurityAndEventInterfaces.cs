using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenRCT.Core.Models;

namespace OpenRCT.Core.Interfaces
{
    public interface IRemoteWindowsSecurityService
    {
        Task<IReadOnlyList<BitLockerVolumeInfo>> GetBitLockerVolumesAsync(TargetDevice device, CancellationToken ct = default);
        Task<ActionExecutionResult> SetBitLockerProtectionAsync(TargetDevice device, string driveLetter, bool suspend, CancellationToken ct = default);

        Task<TpmStatusInfo> GetTpmStatusAsync(TargetDevice device, CancellationToken ct = default);
        Task<ActionExecutionResult> ClearTpmAsync(TargetDevice device, CancellationToken ct = default);

        Task<FirmwareSecurityInfo> GetFirmwareSecurityAsync(TargetDevice device, CancellationToken ct = default);
        Task<ActionExecutionResult> RunSecureBootTaskAsync(TargetDevice device, CancellationToken ct = default);

        Task<VbsStatusInfo> GetVbsStatusAsync(TargetDevice device, CancellationToken ct = default);

        Task<DefenderStatusInfo> GetDefenderStatusAsync(TargetDevice device, CancellationToken ct = default);
        Task<IReadOnlyList<DefenderExclusionInfo>> GetDefenderExclusionsAsync(TargetDevice device, CancellationToken ct = default);

        Task<IReadOnlyList<FirewallProfileInfo>> GetFirewallProfilesAsync(TargetDevice device, CancellationToken ct = default);
        Task<IReadOnlyList<FirewallRuleInfo>> GetFirewallRulesAsync(TargetDevice device, CancellationToken ct = default);
        Task<ActionExecutionResult> SetFirewallRuleEnabledAsync(TargetDevice device, string ruleId, bool enabled, CancellationToken ct = default);
    }

    public interface IRemoteEventViewerService
    {
        Task<IReadOnlyList<string>> GetLogNamesAsync(TargetDevice device, CancellationToken ct = default);
        Task<RemoteEventPageResult> QueryEventsAsync(TargetDevice device, RemoteEventLogQuery query, CancellationToken ct = default);
    }
}
