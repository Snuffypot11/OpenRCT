using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using OpenRCT.Connectors.ActiveDirectory;
using OpenRCT.Connectors.Services;
using OpenRCT.Connectors.SmsProvider;
using OpenRCT.Connectors.SystemInfo;
using OpenRCT.Connectors.Wmi;
using OpenRCT.Core.Interfaces;
using OpenRCT.Core.Models;
using OpenRCT.Core.Settings;
using OpenRCT.UI.Views;
using OpenRCT.UI.Views.Dashboards;
using OpenRCT.UI.Views.Tools;

namespace OpenRCT.UI
{
    /// <summary>
    /// Central UI router invoked by OpenRCT.ConsoleExtension (in-process MECM Console)
    /// and OpenRCT.App (standalone / Executable fallback).
    /// Handles single assets, multi-selected assets, and Device Collection expansion seamlessly.
    /// </summary>
    public static class ToolDispatcher
    {
        public static UserControl CreateDashboardView(string dashboardKey, string? smsServer, string? siteCode)
        {
            var settingsService = new SettingsService();
            var settings = settingsService.Load();
            var smsWmi = new WmiClient
            {
                DefaultTimeout = TimeSpan.FromSeconds(Math.Max(90, settings.ConnectionTimeoutSeconds * 4)),
                EnableFastReachabilityCheck = false
            };
            var smsClient = new SmsProviderClient(smsWmi);
            var adClient = new ActiveDirectoryClient();
            ResolveSmsConnection(smsClient, settings, ref smsServer, ref siteCode);

            return dashboardKey switch
            {
                ToolActionKeys.DashboardHome => new OpenRctHomeDashboardView(smsServer, siteCode, settings),
                ToolActionKeys.DashboardAdCleanup => new AdCleanupDashboardView(adClient, smsClient, smsServer ?? string.Empty, siteCode ?? string.Empty, settings),
                ToolActionKeys.DashboardLaps => new LapsDashboardView(adClient, smsClient, smsServer ?? string.Empty, siteCode ?? string.Empty, settings),
                ToolActionKeys.DashboardSoftwareUpdates => new SoftwareUpdatesDashboardView(smsClient, smsServer ?? string.Empty, siteCode ?? string.Empty, settings),
                ToolActionKeys.DashboardBitLockerCompliance => new BitLockerComplianceDashboardView(adClient, smsClient, smsServer ?? string.Empty, siteCode ?? string.Empty, settings),
                ToolActionKeys.DashboardContentDistribution => new ContentDistributionDashboardView(smsClient, smsServer ?? string.Empty, siteCode ?? string.Empty, settings),
                ToolActionKeys.DashboardHardwareFirmwareAudit => new HardwareFirmwareAuditDashboardView(smsClient, smsServer ?? string.Empty, siteCode ?? string.Empty, settings),
                _ => new OpenRctHomeDashboardView(smsServer, siteCode, settings)
            };
        }

        public static void Dispatch(ToolInvocationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var settingsService = new SettingsService();
            var settings = settingsService.Load();

            if (string.Equals(context.ActionKey, ToolActionKeys.OpenSettings, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    new SettingsWindow(settingsService).ShowDialog();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not open OpenRCT Settings:\n\n{ex.Message}", "OpenRCT Settings Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                return;
            }

            var smsWmi = new WmiClient
            {
                DefaultTimeout = TimeSpan.FromSeconds(Math.Max(60, settings.ConnectionTimeoutSeconds * 4)),
                EnableFastReachabilityCheck = false
            };
            var endpointWmi = new WmiClient
            {
                DefaultTimeout = TimeSpan.FromSeconds(settings.ConnectionTimeoutSeconds),
                EnableFastReachabilityCheck = settings.EnableFastOfflineCheck,
                ReachabilityTimeoutMs = settings.OfflineCheckTimeoutMs
            };
            var smsClient = new SmsProviderClient(smsWmi);
            var adClient = new ActiveDirectoryClient();
            var sysInfoCollector = new SystemInfoCollector(endpointWmi);
            var endpointService = new EndpointActionService(endpointWmi);

            var smsServer = context.SmsProviderServer;
            var siteCode = context.SiteCode;
            ResolveSmsConnection(smsClient, settings, ref smsServer, ref siteCode);

            // Dashboard pop-out dispatch
            if (context.ActionKey.StartsWith("Dashboard.", StringComparison.OrdinalIgnoreCase))
            {
                var view = CreateDashboardView(context.ActionKey, smsServer, siteCode);
                var title = context.ActionKey.Replace("Dashboard.", string.Empty);
                new DashboardHostWindow(title, view, settings).Show();
                return;
            }

            // Resolve targets (either directly selected 1..N devices or expanded from a Device Collection)
            var devices = ResolveTargetDevices(context, smsClient, smsServer, siteCode);
            if (devices.Count == 0)
            {
                MessageBox.Show("No target devices were selected or found in the specified collection.", "OpenRCT", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 1. Client Actions (all 12 Schedule IDs)
            if (ClientActionRegistry.TryGet(context.ActionKey, out var clientAction) && clientAction != null)
            {
                new BulkActionResultWindow(
                    clientAction.DisplayName,
                    devices,
                    (dev, ct) => endpointService.RunClientActionAsync(dev, clientAction, ct),
                    settings).Show();
                return;
            }

            // 2. Client Tools, Console Tools, Security Tools, Status Messages
            switch (context.ActionKey)
            {
                // ---- Client Tools ----
                case ToolActionKeys.AddDevicesToCollection:
                    new AddToCollectionWindow(devices, smsClient, smsServer ?? string.Empty, siteCode ?? string.Empty, settings).Show();
                    break;

                case ToolActionKeys.ClientInformation:
                    new ClientInfoWindow(devices, endpointService, settings).Show();
                    break;

                case ToolActionKeys.OpenClientInstallLogs:
                    LaunchForAll(devices, d => endpointService.OpenRemoteShareAsync(d, @"C$\Windows\ccmsetup\Logs"));
                    break;

                case ToolActionKeys.OpenClientLogs:
                    LaunchForAll(devices, d => endpointService.OpenRemoteShareAsync(d, @"C$\Windows\CCM\Logs"));
                    break;

                case ToolActionKeys.RefreshUpdatesComplianceState:
                    new BulkActionResultWindow("Refresh Updates Compliance State", devices, endpointService.RefreshUpdatesComplianceStateAsync, settings).Show();
                    break;

                case ToolActionKeys.RemoteSoftwareCenter:
                    new RemoteSoftwareCenterWindow(devices, endpointService, settings).Show();
                    break;

                case ToolActionKeys.RerunDeployment:
                    new BulkActionResultWindow("Rerun Deployment", devices, endpointService.RerunDeploymentAsync, settings).Show();
                    break;

                case ToolActionKeys.RestartSmsAgentHost:
                    new BulkActionResultWindow("Restart SMS Agent Host Service", devices, endpointService.RestartSmsAgentHostAsync, settings).Show();
                    break;

                case ToolActionKeys.RunClientCheck:
                    new BulkActionResultWindow("Run Client Check", devices, endpointService.RunClientCheckAsync, settings).Show();
                    break;

                case ToolActionKeys.StopConfigMgrClientActivities:
                    if (ConfirmIfNeeded(settings, $"Stop ConfigMgr Client Activities (CcmExec) on {devices.Count} device(s)?"))
                    {
                        new BulkActionResultWindow("Stop ConfigMgr Client Activities", devices, endpointService.StopConfigMgrClientActivitiesAsync, settings).Show();
                    }
                    break;

                case ToolActionKeys.ShowCollectionsAdvanced:
                    new ShowCollectionsWindow(devices, smsClient, smsServer ?? string.Empty, siteCode ?? string.Empty, settings).Show();
                    break;

                case ToolActionKeys.RepairClient:
                    if (ConfirmIfNeeded(settings, $"Initiate ConfigMgr Client Repair on {devices.Count} device(s)?"))
                    {
                        new BulkActionResultWindow("Repair Client", devices, endpointService.RepairClientAsync, settings).Show();
                    }
                    break;

                case ToolActionKeys.UninstallSccmClient:
                    if (ConfirmIfNeeded(settings, $"Uninstall the ConfigMgr Client on {devices.Count} device(s)? This cannot be undone remotely."))
                    {
                        new BulkActionResultWindow("Uninstall SCCM Client", devices, endpointService.UninstallSccmClientAsync, settings).Show();
                    }
                    break;

                // ---- Console Tools ----
                case ToolActionKeys.AdSecurityGroups:
                    new AdSecurityGroupsWindow(devices, adClient, settings).Show();
                    break;

                case ToolActionKeys.ConnectToCShare:
                    LaunchForAll(devices, d => endpointService.OpenRemoteShareAsync(d, "C$"));
                    break;

                case ToolActionKeys.DeleteFromActiveDirectory:
                    if (ConfirmIfNeeded(settings, $"Permanently DELETE {devices.Count} computer account(s) from Active Directory?"))
                    {
                        new BulkActionResultWindow("Delete Device(s) from Active Directory", devices, async (d, ct) =>
                        {
                            var sw = Stopwatch.StartNew();
                            await adClient.DeleteComputerAsync(d.DisplayName, ct).ConfigureAwait(false);
                            return ActionExecutionResult.Success(d.DisplayName, "Delete from AD", "Deleted computer object from Active Directory.", sw.Elapsed);
                        }, settings).Show();
                    }
                    break;

                case ToolActionKeys.DeleteFromConfigMgr:
                    if (ConfirmIfNeeded(settings, $"Permanently DELETE {devices.Count} device record(s) from ConfigMgr?"))
                    {
                        new BulkActionResultWindow("Delete Device(s) from ConfigMgr", devices, async (d, ct) =>
                        {
                            var sw = Stopwatch.StartNew();
                            await smsClient.DeleteDeviceAsync(smsServer ?? string.Empty, siteCode ?? string.Empty, d, ct).ConfigureAwait(false);
                            return ActionExecutionResult.Success(d.DisplayName, "Delete from ConfigMgr", "Deleted device from ConfigMgr.", sw.Elapsed);
                        }, settings).Show();
                    }
                    break;

                case ToolActionKeys.DisableInActiveDirectory:
                    if (ConfirmIfNeeded(settings, $"Disable {devices.Count} computer account(s) in Active Directory?"))
                    {
                        new BulkActionResultWindow("Disable Device(s) in Active Directory", devices, async (d, ct) =>
                        {
                            var sw = Stopwatch.StartNew();
                            await adClient.SetComputerEnabledAsync(d.DisplayName, enabled: false, ct).ConfigureAwait(false);
                            return ActionExecutionResult.Success(d.DisplayName, "Disable in AD", "Disabled computer account in Active Directory.", sw.Elapsed);
                        }, settings).Show();
                    }
                    break;

                case ToolActionKeys.EnableInActiveDirectory:
                    new BulkActionResultWindow("Enable Device(s) in Active Directory", devices, async (d, ct) =>
                    {
                        var sw = Stopwatch.StartNew();
                        await adClient.SetComputerEnabledAsync(d.DisplayName, enabled: true, ct).ConfigureAwait(false);
                        return ActionExecutionResult.Success(d.DisplayName, "Enable in AD", "Enabled computer account in Active Directory.", sw.Elapsed);
                    }, settings).Show();
                    break;

                case ToolActionKeys.GroupPolicyUpdate:
                    new BulkActionResultWindow("Group Policy Update", devices, endpointService.GroupPolicyUpdateAsync, settings).Show();
                    break;

                case ToolActionKeys.InteractiveCommandPrompt:
                    HandleInteractiveCommandPrompt(devices, endpointService, settingsService, settings);
                    break;

                case ToolActionKeys.InteractivePowerShellPrompt:
                    LaunchForAll(devices, d => endpointService.LaunchInteractivePowerShellAsync(d));
                    break;

                case ToolActionKeys.ManageComputer:
                    LaunchForAll(devices, d => endpointService.LaunchLocalMmcToolAsync(d, "compmgmt.msc", "/computer:\\\\{host}"));
                    break;

                case ToolActionKeys.OpenEventViewer:
                case ToolActionKeys.RemoteEventViewer:
                    new RemoteEventViewerWindow(devices, new RemoteEventViewerService(), settings).Show();
                    break;

                case ToolActionKeys.RemoteWindowsSecurity:
                    new RemoteWindowsSecurityWindow(devices, new RemoteWindowsSecurityService(endpointWmi), endpointService, adClient, smsClient, smsServer ?? string.Empty, siteCode ?? string.Empty, settings).Show();
                    break;

                case ToolActionKeys.OpenRegedit:
                    new RemoteRegistryWindow(devices, sysInfoCollector, settings).Show();
                    break;

                case ToolActionKeys.PingSystems:
                    new PingSystemsWindow(devices, endpointService, settings).Show();
                    break;

                case ToolActionKeys.RunningProcesses:
                    new RunningProcessesWindow(devices, endpointService, settings).Show();
                    break;

                case ToolActionKeys.SendNotification:
                    var notifWin = new SendNotificationWindow(devices.Count, settings);
                    if (notifWin.ShowDialog() == true)
                    {
                        var msg = notifWin.NotificationMessage;
                        var dur = notifWin.DisplaySeconds;
                        new BulkActionResultWindow("Send Notification", devices, (d, ct) => endpointService.SendNotificationAsync(d, msg, dur, ct), settings).Show();
                    }
                    break;

                case ToolActionKeys.SystemInformation:
                    new SystemInfoWindow(devices, sysInfoCollector, settings).Show();
                    break;

                case ToolActionKeys.WakeOnLan:
                    new BulkActionResultWindow("Wake On LAN", devices, async (d, ct) =>
                    {
                        var macs = await smsClient.GetDeviceMacAddressesAsync(smsServer ?? string.Empty, siteCode ?? string.Empty, d, ct).ConfigureAwait(false);
                        return await endpointService.SendWakeOnLanAsync(d, macs, ct).ConfigureAwait(false);
                    }, settings).Show();
                    break;

                case ToolActionKeys.CancelPendingRestart:
                    new BulkActionResultWindow("Cancel Pending Restart or Shutdown", devices, endpointService.CancelPendingRestartAsync, settings).Show();
                    break;

                case ToolActionKeys.ScheduleRestartOrShutdown:
                    ShowPowerActionDialog(devices, endpointService, settings, defaultShutdown: false);
                    break;

                case ToolActionKeys.RestartSystems:
                    ShowPowerActionDialog(devices, endpointService, settings, defaultShutdown: false);
                    break;

                case ToolActionKeys.ShutdownSystems:
                    ShowPowerActionDialog(devices, endpointService, settings, defaultShutdown: true);
                    break;

                // ---- Security Tools ----
                case ToolActionKeys.ViewLapsPassword:
                    new LapsPasswordWindow(devices, adClient, settings).Show();
                    break;

                case ToolActionKeys.ViewBitLockerRecoveryKey:
                    new BitLockerKeyWindow(devices, adClient, smsClient, smsServer ?? string.Empty, siteCode ?? string.Empty, settings).Show();
                    break;

                case ToolActionKeys.DefenderQuickScan:
                    new BulkActionResultWindow("Windows Defender Quick Scan", devices, (d, ct) => endpointService.RunDefenderActionAsync(d, "quickscan", ct), settings).Show();
                    break;

                case ToolActionKeys.DefenderFullScan:
                    new BulkActionResultWindow("Windows Defender Full Scan", devices, (d, ct) => endpointService.RunDefenderActionAsync(d, "fullscan", ct), settings).Show();
                    break;

                case ToolActionKeys.DefenderUpdateDefinitions:
                    new BulkActionResultWindow("Windows Defender Update Definitions", devices, (d, ct) => endpointService.RunDefenderActionAsync(d, "updatesignatures", ct), settings).Show();
                    break;

                // ---- Device Status Messages ----
                case ToolActionKeys.DeviceStatusMessages:
                    new DeviceStatusMessagesWindow(devices, smsClient, smsServer ?? string.Empty, siteCode ?? string.Empty, settings).Show();
                    break;

                default:
                    MessageBox.Show($"Unknown OpenRCT action '{context.ActionKey}'.", "OpenRCT", MessageBoxButton.OK, MessageBoxImage.Warning);
                    break;
            }
        }

        private static void ShowPowerActionDialog(
            IReadOnlyList<TargetDevice> devices,
            IEndpointActionService endpointService,
            OpenRctSettings settings,
            bool defaultShutdown)
        {
            var schedWin = new ScheduleRestartWindow(devices.Count, settings, defaultShutdown);
            if (schedWin.ShowDialog() == true)
            {
                var opts = schedWin.Options;
                var label = opts.IsShutdown ? "Shutdown System(s)" : "Restart System(s)";
                new BulkActionResultWindow(label, devices, (d, ct) => endpointService.SchedulePowerActionAsync(d, opts, ct), settings).Show();
            }
        }

        private static void HandleInteractiveCommandPrompt(
            IReadOnlyList<TargetDevice> devices,
            IEndpointActionService service,
            SettingsService settingsService,
            OpenRctSettings settings)
        {
            var validation = SettingsService.ValidatePsExecPath(settings.PsExecPath);
            if (!validation.IsValid)
            {
                var result = MessageBox.Show(
                    $"{validation.Message}\n\nPsExec is the only optional external tool used by OpenRCT. Would you like to open OpenRCT Settings now to set your PsExec path?",
                    "PsExec Path Not Configured",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                {
                    var settingsWin = new SettingsWindow(settingsService);
                    if (settingsWin.ShowDialog() == true)
                    {
                        settings = settingsService.Load();
                        validation = SettingsService.ValidatePsExecPath(settings.PsExecPath);
                    }
                }
                if (!validation.IsValid) return;
            }

            if (devices.Count > 5 &&
                MessageBox.Show($"Launch Interactive Command Prompt windows for {devices.Count} devices?", "OpenRCT", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            LaunchForAll(devices, d => service.LaunchInteractiveCommandPromptAsync(d, settings));
        }

        private static IReadOnlyList<TargetDevice> ResolveTargetDevices(
            ToolInvocationContext context,
            ISmsProviderClient smsClient,
            string? smsServer,
            string? siteCode)
        {
            if (context.Devices != null && context.Devices.Length > 0)
            {
                return context.Devices;
            }

            if (!string.IsNullOrWhiteSpace(context.CollectionId) &&
                !string.IsNullOrWhiteSpace(smsServer) &&
                !string.IsNullOrWhiteSpace(siteCode))
            {
                try
                {
                    var members = smsClient.GetCollectionMembersAsync(smsServer!, siteCode!, context.CollectionId!).GetAwaiter().GetResult();
                    if (members.Count > 25)
                    {
                        var label = string.IsNullOrWhiteSpace(context.CollectionName) ? context.CollectionId : context.CollectionName;
                        if (MessageBox.Show(
                            $"Collection '{label}' contains {members.Count} devices. Continue running '{context.ActionKey}' against all {members.Count} devices?",
                            "Confirm Collection Action",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question) != MessageBoxResult.Yes)
                        {
                            return Array.Empty<TargetDevice>();
                        }
                    }
                    return members;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to expand collection '{context.CollectionId}': {ex.Message}", "OpenRCT", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            return Array.Empty<TargetDevice>();
        }

        private static void ResolveSmsConnection(ISmsProviderClient smsClient, OpenRctSettings settings, ref string? smsServer, ref string? siteCode)
        {
            if (!string.IsNullOrWhiteSpace(smsServer) && !string.IsNullOrWhiteSpace(siteCode)) return;

            if (!string.IsNullOrWhiteSpace(settings.DefaultSmsProviderServer) && !string.IsNullOrWhiteSpace(settings.DefaultSiteCode))
            {
                smsServer ??= settings.DefaultSmsProviderServer;
                siteCode ??= settings.DefaultSiteCode;
                return;
            }

            if (smsClient.TryAutoDiscover(out var discoveredServer, out var discoveredSite))
            {
                if (string.IsNullOrWhiteSpace(smsServer)) smsServer = discoveredServer;
                if (string.IsNullOrWhiteSpace(siteCode)) siteCode = discoveredSite;
            }
        }

        private static bool ConfirmIfNeeded(OpenRctSettings settings, string prompt) =>
            !settings.ConfirmDestructiveActions ||
            MessageBox.Show(prompt, "OpenRCT — Confirm Action", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

        private static void LaunchForAll(IReadOnlyList<TargetDevice> devices, Func<TargetDevice, Task<ActionExecutionResult>> launcher)
        {
            foreach (var d in devices)
            {
                try
                {
                    launcher(d).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed for {d.DisplayName}: {ex.Message}", "OpenRCT", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
