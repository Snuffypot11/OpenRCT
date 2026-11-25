using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenRCT.Core.Models;
using OpenRCT.Core.Settings;

namespace OpenRCT.Core.Interfaces
{
    public interface ISmsProviderClient
    {
        bool TryAutoDiscover(out string server, out string siteCode);
        Task<IReadOnlyList<SmsCollectionSummary>> GetDeviceCollectionsAsync(string server, string siteCode, CancellationToken ct = default);
        Task<IReadOnlyList<TargetDevice>> GetCollectionMembersAsync(string server, string siteCode, string collectionId, CancellationToken ct = default);
        Task<IReadOnlyList<ConfigMgrDeviceRecord>> GetCollectionDeviceRecordsAsync(string server, string siteCode, IEnumerable<string> collectionIds, CancellationToken ct = default);
        Task<IReadOnlyList<DeviceCollectionMembershipInfo>> GetDeviceCollectionsForAssetAsync(string server, string siteCode, TargetDevice device, CancellationToken ct = default);
        Task AddDevicesToCollectionAsync(string server, string siteCode, string collectionId, IEnumerable<TargetDevice> devices, CancellationToken ct = default);
        Task DeleteDeviceAsync(string server, string siteCode, TargetDevice device, CancellationToken ct = default);
        Task<IReadOnlyList<SoftwareUpdateGroupInfo>> GetSoftwareUpdateGroupsAsync(string server, string siteCode, CancellationToken ct = default);
        Task<IReadOnlyList<RawDeviceUpdateRecord>> GetUpdateComplianceForCollectionAsync(string server, string siteCode, string collectionId, CancellationToken ct = default);
        Task<IReadOnlyList<StatusMessageEntry>> GetDeviceStatusMessagesAsync(string server, string siteCode, TargetDevice device, int daysBack = 7, CancellationToken ct = default);
        Task<IReadOnlyList<string>> GetDeviceMacAddressesAsync(string server, string siteCode, TargetDevice device, CancellationToken ct = default);
        Task<IReadOnlyList<BitLockerRecoveryKeyInfo>> GetConfigMgrBitLockerKeysAsync(string server, string siteCode, TargetDevice device, CancellationToken ct = default);
    }

    public interface IActiveDirectoryClient
    {
        Task<IReadOnlyList<OrganizationalUnitInfo>> GetOrganizationalUnitsAsync(CancellationToken ct = default);
        Task<IReadOnlyList<AdComputerRecord>> GetComputersInOusAsync(IEnumerable<string> ouDistinguishedNames, CancellationToken ct = default);
        Task<IReadOnlyList<AdGroupMembershipInfo>> GetComputerGroupsAsync(string computerName, CancellationToken ct = default);
        Task AddComputerToGroupAsync(string computerName, string groupNameOrDn, CancellationToken ct = default);
        Task RemoveComputerFromGroupAsync(string computerName, string groupDistinguishedName, CancellationToken ct = default);
        Task SetComputerEnabledAsync(string computerName, bool enabled, CancellationToken ct = default);
        Task DeleteComputerAsync(string computerName, CancellationToken ct = default);
        Task<LapsPasswordResult> GetLapsPasswordAsync(string computerName, CancellationToken ct = default);
        Task<IReadOnlyList<BitLockerRecoveryKeyInfo>> GetBitLockerRecoveryKeysAsync(string computerName, CancellationToken ct = default);
    }

    public interface ISystemInfoCollector
    {
        Task<OperatingSystemInfo> GetOperatingSystemAsync(TargetDevice device, CancellationToken ct = default);
        Task<HardwareInfo> GetHardwareAsync(TargetDevice device, CancellationToken ct = default);
        Task<IReadOnlyList<DiskInfo>> GetDisksAsync(TargetDevice device, CancellationToken ct = default);
        Task<IReadOnlyList<InstalledApplicationInfo>> GetApplicationsAsync(TargetDevice device, CancellationToken ct = default);
        Task<ActionExecutionResult> UninstallApplicationAsync(TargetDevice device, InstalledApplicationInfo app, CancellationToken ct = default);
        Task<IReadOnlyList<WindowsUpdateInfo>> GetWindowsUpdatesAsync(TargetDevice device, CancellationToken ct = default);
        Task<IReadOnlyList<WindowsServiceInfo>> GetServicesAsync(TargetDevice device, CancellationToken ct = default);
        Task<ActionExecutionResult> ControlServiceAsync(TargetDevice device, string serviceName, string operation, string? newStartMode = null, CancellationToken ct = default);
        Task<IReadOnlyList<DriverInfo>> GetDriversAsync(TargetDevice device, CancellationToken ct = default);
        Task<IReadOnlyList<UserProfileInfo>> GetUserProfilesAsync(TargetDevice device, CancellationToken ct = default);
        Task<ActionExecutionResult> DeleteUserProfileAsync(TargetDevice device, string sid, CancellationToken ct = default);
        Task<IReadOnlyList<LocalGroupMemberInfo>> GetLocalGroupMembersAsync(TargetDevice device, string groupName, CancellationToken ct = default);
        Task<ActionExecutionResult> ModifyLocalGroupMemberAsync(TargetDevice device, string groupName, string accountName, bool add, CancellationToken ct = default);
        Task<IReadOnlyList<BatteryInfo>> GetBatteriesAsync(TargetDevice device, CancellationToken ct = default);
    }

    public interface IEndpointActionService
    {
        Task<ActionExecutionResult> RunClientActionAsync(TargetDevice device, ClientActionDefinition action, CancellationToken ct = default);
        Task<ClientInfoSummary> GetClientInfoAsync(TargetDevice device, CancellationToken ct = default);
        Task<ActionExecutionResult> DeleteClientCacheItemAsync(TargetDevice device, string cacheId, CancellationToken ct = default);
        Task<IReadOnlyList<RemoteSoftwareCenterItem>> GetRemoteSoftwareCenterItemsAsync(TargetDevice device, CancellationToken ct = default);
        Task<ActionExecutionResult> InvokeRemoteSoftwareCenterItemAsync(TargetDevice device, RemoteSoftwareCenterItem item, bool uninstall, CancellationToken ct = default);
        Task<ActionExecutionResult> RefreshUpdatesComplianceStateAsync(TargetDevice device, CancellationToken ct = default);
        Task<ActionExecutionResult> RerunDeploymentAsync(TargetDevice device, CancellationToken ct = default);
        Task<ActionExecutionResult> RestartSmsAgentHostAsync(TargetDevice device, CancellationToken ct = default);
        Task<ActionExecutionResult> RunClientCheckAsync(TargetDevice device, CancellationToken ct = default);
        Task<ActionExecutionResult> StopConfigMgrClientActivitiesAsync(TargetDevice device, CancellationToken ct = default);
        Task<ActionExecutionResult> RepairClientAsync(TargetDevice device, CancellationToken ct = default);
        Task<ActionExecutionResult> UninstallSccmClientAsync(TargetDevice device, CancellationToken ct = default);
        Task<ActionExecutionResult> GroupPolicyUpdateAsync(TargetDevice device, CancellationToken ct = default);
        Task<ActionExecutionResult> LaunchInteractiveCommandPromptAsync(TargetDevice device, OpenRctSettings settings, CancellationToken ct = default);
        Task<ActionExecutionResult> LaunchInteractivePowerShellAsync(TargetDevice device, CancellationToken ct = default);
        Task<ActionExecutionResult> OpenRemoteShareAsync(TargetDevice device, string relativeSharePath, CancellationToken ct = default);
        Task<ActionExecutionResult> LaunchLocalMmcToolAsync(TargetDevice device, string toolExe, string argumentsTemplate, CancellationToken ct = default);
        Task<PingResultEntry> PingDeviceAsync(TargetDevice device, int timeoutMs = 2000, CancellationToken ct = default);
        Task<IReadOnlyList<RemoteProcessInfo>> GetRunningProcessesAsync(TargetDevice device, CancellationToken ct = default);
        Task<ActionExecutionResult> TerminateProcessAsync(TargetDevice device, uint processId, CancellationToken ct = default);
        Task<ActionExecutionResult> SendNotificationAsync(TargetDevice device, string message, int displaySeconds = 300, CancellationToken ct = default);
        Task<ActionExecutionResult> SendWakeOnLanAsync(TargetDevice device, IEnumerable<string> macAddresses, CancellationToken ct = default);
        Task<ActionExecutionResult> SchedulePowerActionAsync(TargetDevice device, SchedulePowerOptions options, CancellationToken ct = default);
        Task<ActionExecutionResult> CancelPendingRestartAsync(TargetDevice device, CancellationToken ct = default);
        Task<ActionExecutionResult> RunDefenderActionAsync(TargetDevice device, string actionType, CancellationToken ct = default);
    }
}
