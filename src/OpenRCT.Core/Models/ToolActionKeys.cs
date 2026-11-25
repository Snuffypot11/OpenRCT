using System;
using System.Collections.Generic;

namespace OpenRCT.Core.Models
{
    /// <summary>
    /// Canonical action keys referenced by MECM Console Extension XML manifests, OpenRCT.ConsoleExtension,
    /// OpenRCT.App CLI/named-pipe arguments, and OpenRCT.UI ToolDispatcher.
    /// </summary>
    public static class ToolActionKeys
    {
        // Client Tools (13)
        public const string AddDevicesToCollection = "ClientTool.AddDevicesToCollection";
        public const string ClientInformation = "ClientTool.ClientInformation";
        public const string OpenClientInstallLogs = "ClientTool.OpenClientInstallLogs";
        public const string OpenClientLogs = "ClientTool.OpenClientLogs";
        public const string RefreshUpdatesComplianceState = "ClientTool.RefreshUpdatesComplianceState";
        public const string RemoteSoftwareCenter = "ClientTool.RemoteSoftwareCenter";
        public const string RerunDeployment = "ClientTool.RerunDeployment";
        public const string RestartSmsAgentHost = "ClientTool.RestartSmsAgentHost";
        public const string RunClientCheck = "ClientTool.RunClientCheck";
        public const string StopConfigMgrClientActivities = "ClientTool.StopConfigMgrClientActivities";
        public const string ShowCollectionsAdvanced = "ClientTool.ShowCollectionsAdvanced";
        public const string RepairClient = "ClientTool.RepairClient";
        public const string UninstallSccmClient = "ClientTool.UninstallSccmClient";

        // Console Tools (21)
        public const string AdSecurityGroups = "ConsoleTool.AdSecurityGroups";
        public const string ConnectToCShare = "ConsoleTool.ConnectToCShare";
        public const string DeleteFromActiveDirectory = "ConsoleTool.DeleteFromActiveDirectory";
        public const string DeleteFromConfigMgr = "ConsoleTool.DeleteFromConfigMgr";
        public const string DisableInActiveDirectory = "ConsoleTool.DisableInActiveDirectory";
        public const string EnableInActiveDirectory = "ConsoleTool.EnableInActiveDirectory";
        public const string GroupPolicyUpdate = "ConsoleTool.GroupPolicyUpdate";
        public const string InteractiveCommandPrompt = "ConsoleTool.InteractiveCommandPrompt";
        public const string InteractivePowerShellPrompt = "ConsoleTool.InteractivePowerShellPrompt";
        public const string ManageComputer = "ConsoleTool.ManageComputer";
        public const string OpenEventViewer = "ConsoleTool.OpenEventViewer";
        public const string OpenRegedit = "ConsoleTool.OpenRegedit";
        public const string PingSystems = "ConsoleTool.PingSystems";
        public const string RunningProcesses = "ConsoleTool.RunningProcesses";
        public const string SendNotification = "ConsoleTool.SendNotification";
        public const string SystemInformation = "ConsoleTool.SystemInformation";
        public const string WakeOnLan = "ConsoleTool.WakeOnLan";
        public const string CancelPendingRestart = "ConsoleTool.CancelPendingRestart";
        public const string ScheduleRestartOrShutdown = "ConsoleTool.ScheduleRestartOrShutdown";
        public const string RestartSystems = "ConsoleTool.RestartSystems";
        public const string ShutdownSystems = "ConsoleTool.ShutdownSystems";

        // Security Tools (5)
        public const string ViewLapsPassword = "SecurityTool.ViewLapsPassword";
        public const string ViewBitLockerRecoveryKey = "SecurityTool.ViewBitLockerRecoveryKey";
        public const string DefenderQuickScan = "SecurityTool.DefenderQuickScan";
        public const string DefenderFullScan = "SecurityTool.DefenderFullScan";
        public const string DefenderUpdateDefinitions = "SecurityTool.DefenderUpdateDefinitions";

        // Other top-level tools
        public const string DeviceStatusMessages = "Tool.DeviceStatusMessages";
        public const string OpenSettings = "Tool.Settings";

        // Dashboards (Phase 1 + Phase 2)
        public const string DashboardHome = "Dashboard.Home";
        public const string DashboardAdCleanup = "Dashboard.AdCleanup";
        public const string DashboardLaps = "Dashboard.Laps";
        public const string DashboardSoftwareUpdates = "Dashboard.SoftwareUpdates";
        public const string DashboardBitLockerCompliance = "Dashboard.BitLockerCompliance";
        public const string DashboardContentDistribution = "Dashboard.ContentDistribution";
        public const string DashboardHardwareFirmwareAudit = "Dashboard.HardwareFirmwareAudit";

        private static readonly Dictionary<string, string> DisplayNameToKey = BuildDisplayNameMap();

        private static Dictionary<string, string> BuildDisplayNameMap()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var action in ClientActionRegistry.All)
            {
                map[action.DisplayName] = action.Key;
            }

            // Client Tools
            map["Add Device(s) to Collection"] = AddDevicesToCollection;
            map["Client Information"] = ClientInformation;
            map["Open Client Installation Log File Folder"] = OpenClientInstallLogs;
            map["Open Client Log File Folder"] = OpenClientLogs;
            map["Refresh Updates Compliance State"] = RefreshUpdatesComplianceState;
            map["Remote Software Center"] = RemoteSoftwareCenter;
            map["Rerun Deployment"] = RerunDeployment;
            map["Restart SMS Agent Host Service"] = RestartSmsAgentHost;
            map["Run Client Check"] = RunClientCheck;
            map["Stop ConfigMgr Client Activities"] = StopConfigMgrClientActivities;
            map["Show Collections - Advanced"] = ShowCollectionsAdvanced;
            map["Repair Client"] = RepairClient;
            map["Uninstall SCCM Client"] = UninstallSccmClient;

            // Console Tools
            map["AD Security Groups"] = AdSecurityGroups;
            map["Connect to C$"] = ConnectToCShare;
            map["Delete Device(s) from Active Directory"] = DeleteFromActiveDirectory;
            map["Delete Device(s) From ConfigMgr"] = DeleteFromConfigMgr;
            map["Disable Device(s) in Active Directory"] = DisableInActiveDirectory;
            map["Enable Device(s) in Active Directory"] = EnableInActiveDirectory;
            map["Group Policy Update"] = GroupPolicyUpdate;
            map["Interactive Command Prompt"] = InteractiveCommandPrompt;
            map["Interactive PowerShell Prompt"] = InteractivePowerShellPrompt;
            map["Manage Computer"] = ManageComputer;
            map["Open Event Viewer"] = OpenEventViewer;
            map["Open Regedit"] = OpenRegedit;
            map["Ping System(s)"] = PingSystems;
            map["Running Processes"] = RunningProcesses;
            map["Send Notification"] = SendNotification;
            map["System Information"] = SystemInformation;
            map["Wake On LAN"] = WakeOnLan;
            map["Cancel Pending Restart or Shutdown"] = CancelPendingRestart;
            map["Schedule Restart or Shutdown"] = ScheduleRestartOrShutdown;
            map["Restart System(s)"] = RestartSystems;
            map["Shutdown System(s)"] = ShutdownSystems;

            // Security Tools (pure ASCII hyphen names + legacy em-dash aliases)
            map["View LAPS Password"] = ViewLapsPassword;
            map["View BitLocker Recovery Key"] = ViewBitLockerRecoveryKey;
            map["Windows Defender - Quick Scan"] = DefenderQuickScan;
            map["Windows Defender - Full Scan"] = DefenderFullScan;
            map["Windows Defender - Update Definitions"] = DefenderUpdateDefinitions;
            map["Windows Defender — Quick Scan"] = DefenderQuickScan;
            map["Windows Defender — Full Scan"] = DefenderFullScan;
            map["Windows Defender — Update Definitions"] = DefenderUpdateDefinitions;

            // Other
            map["Device Status Messages"] = DeviceStatusMessages;
            map["OpenRCT Settings"] = OpenSettings;

            return map;
        }

        public static string? ResolveByDisplayName(string? displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName)) return null;
            return DisplayNameToKey.TryGetValue(displayName!.Trim(), out var key) ? key : null;
        }
    }
}
