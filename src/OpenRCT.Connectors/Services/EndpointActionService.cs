using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using OpenRCT.Connectors.Wmi;
using OpenRCT.Core.Interfaces;
using OpenRCT.Core.Models;
using OpenRCT.Core.Settings;

namespace OpenRCT.Connectors.Services
{
    /// <summary>
    /// Executes all MECM Client Actions, Client Tools, Console Tools, and Security Tools
    /// against single or multiple target endpoints using native Windows APIs (plus optional user-configured PsExec).
    /// </summary>
    public sealed class EndpointActionService : IEndpointActionService
    {
        private readonly WmiClient _wmi;

        public EndpointActionService(WmiClient? wmi = null)
        {
            _wmi = wmi ?? new WmiClient();
        }

        public async Task<ActionExecutionResult> RunClientActionAsync(TargetDevice device, ClientActionDefinition action, CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            var host = device.ConnectAddress;

            if (action.IsStateMessageCacheCleanup)
            {
                // Resets state message cache by forcing a full resync via RefreshServerComplianceState + Send Unsent State Messages
                await RefreshUpdatesComplianceStateAsync(device, ct).ConfigureAwait(false);
            }

            foreach (var scheduleId in action.ScheduleIds)
            {
                using var _ = await _wmi.InvokeStaticMethodAsync(
                    host,
                    @"root\ccm",
                    "SMS_Client",
                    "TriggerSchedule",
                    new Dictionary<string, object?> { ["sScheduleID"] = scheduleId },
                    ct: ct).ConfigureAwait(false);
            }

            return ActionExecutionResult.Success(
                device.DisplayName,
                action.DisplayName,
                $"Triggered {action.DisplayName}.",
                sw.Elapsed);
        }

        public async Task<ClientInfoSummary> GetClientInfoAsync(TargetDevice device, CancellationToken ct = default)
        {
            var host = device.ConnectAddress;
            var summary = new ClientInfoSummary { DeviceName = device.DisplayName };

            var clientRows = await _wmi.QueryAsync(host, @"root\ccm", "SELECT ClientVersion, ClientID FROM SMS_Client", ct: ct).ConfigureAwait(false);
            var c = clientRows.FirstOrDefault();
            if (c != null)
            {
                summary.ClientVersion = WmiClient.GetString(c, "ClientVersion");
                summary.ClientGuid = WmiClient.GetString(c, "ClientID");
            }

            try
            {
                using var siteOut = await _wmi.InvokeStaticMethodAsync(host, @"root\ccm", "SMS_Client", "GetAssignedSite", ct: ct).ConfigureAwait(false);
                summary.AssignedSiteCode = siteOut?["sSiteCode"]?.ToString() ?? string.Empty;
            }
            catch { }

            try
            {
                var mpRows = await _wmi.QueryAsync(host, @"root\ccm", "SELECT Name FROM SMS_Authority", ct: ct).ConfigureAwait(false);
                summary.ManagementPoint = mpRows.Select(r => WmiClient.GetString(r, "Name")).FirstOrDefault() ?? string.Empty;
            }
            catch { }

            try
            {
                var svcRows = await _wmi.QueryAsync(host, @"root\cimv2", "SELECT State FROM Win32_Service WHERE Name = 'CcmExec'", ct: ct).ConfigureAwait(false);
                summary.CcmExecStatus = svcRows.Select(r => WmiClient.GetString(r, "State")).FirstOrDefault() ?? "Not Found";
            }
            catch { }

            try
            {
                var compRows = await _wmi.QueryAsync(host, @"root\ccm", "SELECT Name, DisplayName, Version, Enabled FROM SMS_ClientComponent", ct: ct).ConfigureAwait(false);
                summary.Components = compRows.Select(r => new ClientComponentInfo
                {
                    Name = WmiClient.GetString(r, "Name"),
                    DisplayName = WmiClient.GetString(r, "DisplayName"),
                    Version = WmiClient.GetString(r, "Version"),
                    State = WmiClient.GetBool(r, "Enabled") ? "Enabled" : "Disabled"
                }).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch { }

            try
            {
                var cacheCfg = await _wmi.QueryAsync(host, @"root\ccm\SoftMgmtAgent", "SELECT Location, Size FROM CacheConfig", ct: ct).ConfigureAwait(false);
                var cfg = cacheCfg.FirstOrDefault();
                if (cfg != null)
                {
                    summary.CacheLocation = WmiClient.GetString(cfg, "Location");
                    summary.CacheSizeMb = WmiClient.GetInt32(cfg, "Size");
                }

                var cacheItems = await _wmi.QueryAsync(host, @"root\ccm\SoftMgmtAgent",
                    "SELECT CacheElementID, ContentID, ContentVersion, Location, ContentSize, LastReferenceTime FROM CacheInfoEx", ct: ct).ConfigureAwait(false);

                summary.CacheItems = cacheItems.Select(r => new ClientCacheItem
                {
                    CacheId = WmiClient.GetString(r, "CacheElementID"),
                    ContentId = WmiClient.GetString(r, "ContentID"),
                    ContentVersion = WmiClient.GetString(r, "ContentVersion"),
                    Location = WmiClient.GetString(r, "Location"),
                    SizeMb = Math.Round(WmiClient.GetInt64(r, "ContentSize") / 1024.0, 2),
                    LastReferenced = WmiClient.GetDmtfDateTime(r, "LastReferenceTime")
                }).OrderByDescending(i => i.SizeMb).ToList();

                summary.CacheUsedMb = (int)Math.Round(summary.CacheItems.Sum(i => i.SizeMb));
            }
            catch { }

            return summary;
        }

        public Task<ActionExecutionResult> DeleteClientCacheItemAsync(TargetDevice device, string cacheId, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                ct.ThrowIfCancellationRequested();
                var scope = _wmi.CreateScope(device.ConnectAddress, @"root\ccm\SoftMgmtAgent");
                var safeId = cacheId.Replace("'", "\\'");
                using var item = new ManagementObject(scope, new ManagementPath($"CacheInfoEx.CacheElementID='{safeId}'"), null);
                item.Delete();
                return ActionExecutionResult.Success(device.DisplayName, "Delete Cache Item", $"Deleted cache item '{cacheId}'.", sw.Elapsed);
            }, ct);
        }

        public async Task<IReadOnlyList<RemoteSoftwareCenterItem>> GetRemoteSoftwareCenterItemsAsync(TargetDevice device, CancellationToken ct = default)
        {
            var host = device.ConnectAddress;
            var items = new List<RemoteSoftwareCenterItem>();

            try
            {
                var apps = await _wmi.QueryAsync(host, @"root\ccm\ClientSDK",
                    "SELECT Id, Revision, Name, Publisher, SoftwareVersion, InstallState, EvaluationState, IsMachineTarget, Deadline FROM CCM_Application",
                    ct: ct).ConfigureAwait(false);
                foreach (var a in apps)
                {
                    items.Add(new RemoteSoftwareCenterItem
                    {
                        DeviceName = device.DisplayName,
                        ItemType = SoftwareCenterItemType.Application,
                        Id = WmiClient.GetString(a, "Id"),
                        Revision = WmiClient.GetString(a, "Revision"),
                        Name = WmiClient.GetString(a, "Name"),
                        Publisher = WmiClient.GetString(a, "Publisher"),
                        SoftwareVersion = WmiClient.GetString(a, "SoftwareVersion"),
                        InstallState = WmiClient.GetString(a, "InstallState"),
                        EvaluationState = MapAppEvalState(WmiClient.GetInt32(a, "EvaluationState")),
                        IsMachineTarget = WmiClient.GetBool(a, "IsMachineTarget"),
                        Deadline = WmiClient.GetDmtfDateTime(a, "Deadline")
                    });
                }
            }
            catch { }

            try
            {
                var updates = await _wmi.QueryAsync(host, @"root\ccm\ClientSDK",
                    "SELECT UpdateID, ArticleID, Name, Publisher, EvaluationState, Deadline FROM CCM_SoftwareUpdate",
                    ct: ct).ConfigureAwait(false);
                foreach (var u in updates)
                {
                    items.Add(new RemoteSoftwareCenterItem
                    {
                        DeviceName = device.DisplayName,
                        ItemType = SoftwareCenterItemType.SoftwareUpdate,
                        Id = WmiClient.GetString(u, "UpdateID"),
                        Name = WmiClient.GetString(u, "Name"),
                        Publisher = WmiClient.GetString(u, "Publisher"),
                        SoftwareVersion = WmiClient.GetString(u, "ArticleID"),
                        InstallState = "Available/Required",
                        EvaluationState = MapUpdateEvalState(WmiClient.GetInt32(u, "EvaluationState")),
                        IsMachineTarget = true,
                        Deadline = WmiClient.GetDmtfDateTime(u, "Deadline")
                    });
                }
            }
            catch { }

            try
            {
                var progs = await _wmi.QueryAsync(host, @"root\ccm\ClientSDK",
                    "SELECT PackageID, ProgramID, Name, Publisher, LastRunStatus FROM CCM_Program",
                    ct: ct).ConfigureAwait(false);
                foreach (var p in progs)
                {
                    items.Add(new RemoteSoftwareCenterItem
                    {
                        DeviceName = device.DisplayName,
                        ItemType = SoftwareCenterItemType.PackageOrTaskSequence,
                        Id = WmiClient.GetString(p, "PackageID"),
                        Revision = WmiClient.GetString(p, "ProgramID"),
                        Name = WmiClient.GetString(p, "Name"),
                        Publisher = WmiClient.GetString(p, "Publisher"),
                        InstallState = WmiClient.GetString(p, "LastRunStatus"),
                        EvaluationState = WmiClient.GetString(p, "LastRunStatus"),
                        IsMachineTarget = true
                    });
                }
            }
            catch { }

            return items.OrderBy(i => i.ItemType).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public async Task<ActionExecutionResult> InvokeRemoteSoftwareCenterItemAsync(
            TargetDevice device,
            RemoteSoftwareCenterItem item,
            bool uninstall,
            CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            var host = device.ConnectAddress;

            if (item.ItemType == SoftwareCenterItemType.Application)
            {
                var method = uninstall ? "Uninstall" : "Install";
                await _wmi.InvokeStaticMethodAsync(host, @"root\ccm\ClientSDK", "CCM_Application", method,
                    new Dictionary<string, object?>
                    {
                        ["Id"] = item.Id,
                        ["Revision"] = item.Revision,
                        ["IsMachineTarget"] = item.IsMachineTarget,
                        ["EnforcePreference"] = 0u,
                        ["Priority"] = "Normal",
                        ["IsRebootIfNeeded"] = false
                    }, ct: ct).ConfigureAwait(false);
                return ActionExecutionResult.Success(device.DisplayName, $"Software Center {method}", $"{method} invoked for '{item.Name}'.", sw.Elapsed);
            }

            if (item.ItemType == SoftwareCenterItemType.SoftwareUpdate)
            {
                var scope = _wmi.CreateScope(host, @"root\ccm\ClientSDK");
                var safeId = item.Id.Replace("'", "\\'");
                using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery($"SELECT * FROM CCM_SoftwareUpdate WHERE UpdateID = '{safeId}'"));
                var matches = searcher.Get().Cast<ManagementBaseObject>().ToArray();
                if (matches.Length == 0)
                {
                    return ActionExecutionResult.Failure(device.DisplayName, "Install Update", $"Update '{item.Name}' not found on client.", null, sw.Elapsed);
                }
                await _wmi.InvokeStaticMethodAsync(host, @"root\ccm\ClientSDK", "CCM_SoftwareUpdatesManager", "InstallUpdates",
                    new Dictionary<string, object?> { ["CCMUpdates"] = matches }, ct: ct).ConfigureAwait(false);
                return ActionExecutionResult.Success(device.DisplayName, "Install Update", $"Install invoked for '{item.Name}'.", sw.Elapsed);
            }

            await _wmi.InvokeStaticMethodAsync(host, @"root\ccm\ClientSDK", "CCM_ProgramsManager", "ExecuteProgram",
                new Dictionary<string, object?> { ["PackageID"] = item.Id, ["ProgramID"] = item.Revision }, ct: ct).ConfigureAwait(false);
            return ActionExecutionResult.Success(device.DisplayName, "Run Package/Task Sequence", $"Started '{item.Name}'.", sw.Elapsed);
        }

        public async Task<ActionExecutionResult> RefreshUpdatesComplianceStateAsync(TargetDevice device, CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            const string cmd = "powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"(New-Object -ComObject Microsoft.CCM.UpdatesStore).RefreshServerComplianceState()\"";
            await _wmi.CreateRemoteProcessAsync(device.ConnectAddress, cmd, ct: ct).ConfigureAwait(false);
            return ActionExecutionResult.Success(device.DisplayName, "Refresh Updates Compliance State", "Invoked UpdatesStore.RefreshServerComplianceState().", sw.Elapsed);
        }

        public async Task<ActionExecutionResult> RerunDeploymentAsync(TargetDevice device, CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            // Trigger Application Deployment Evaluation + Machine Policy Evaluation so required deployments re-evaluate cleanly
            await RunClientActionAsync(device, ClientActionRegistry.GetRequired(ClientActionRegistry.MachinePolicyRetrievalAndEval), ct).ConfigureAwait(false);
            await RunClientActionAsync(device, ClientActionRegistry.GetRequired(ClientActionRegistry.AppDeploymentEval), ct).ConfigureAwait(false);
            return ActionExecutionResult.Success(device.DisplayName, "Rerun Deployment", "Triggered policy & deployment re-evaluation.", sw.Elapsed);
        }

        public Task<ActionExecutionResult> RestartSmsAgentHostAsync(TargetDevice device, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                ct.ThrowIfCancellationRequested();
                var scope = _wmi.CreateScope(device.ConnectAddress, @"root\cimv2");
                using var svc = new ManagementObject(scope, new ManagementPath("Win32_Service.Name='CcmExec'"), null);
                svc.InvokeMethod("StopService", null);
                Thread.Sleep(2500);
                svc.InvokeMethod("StartService", null);
                return ActionExecutionResult.Success(device.DisplayName, "Restart SMS Agent Host", "Restarted CcmExec service.", sw.Elapsed);
            }, ct);
        }

        public async Task<ActionExecutionResult> RunClientCheckAsync(TargetDevice device, CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            var pid = await _wmi.CreateRemoteProcessAsync(device.ConnectAddress, @"C:\Windows\CCM\ccmeval.exe", ct: ct).ConfigureAwait(false);
            return ActionExecutionResult.Success(device.DisplayName, "Run Client Check", $"Started ccmeval.exe (PID {pid}).", sw.Elapsed);
        }

        public Task<ActionExecutionResult> StopConfigMgrClientActivitiesAsync(TargetDevice device, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                ct.ThrowIfCancellationRequested();
                var scope = _wmi.CreateScope(device.ConnectAddress, @"root\cimv2");
                using var svc = new ManagementObject(scope, new ManagementPath("Win32_Service.Name='CcmExec'"), null);
                svc.InvokeMethod("StopService", null);
                return ActionExecutionResult.Success(device.DisplayName, "Stop ConfigMgr Client Activities", "Stopped SMS Agent Host (CcmExec).", sw.Elapsed);
            }, ct);
        }

        public async Task<ActionExecutionResult> RepairClientAsync(TargetDevice device, CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            await _wmi.InvokeStaticMethodAsync(device.ConnectAddress, @"root\ccm", "SMS_Client", "RepairClient", ct: ct).ConfigureAwait(false);
            return ActionExecutionResult.Success(device.DisplayName, "Repair Client", "Initiated ConfigMgr client repair.", sw.Elapsed);
        }

        public async Task<ActionExecutionResult> UninstallSccmClientAsync(TargetDevice device, CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            var pid = await _wmi.CreateRemoteProcessAsync(device.ConnectAddress, @"C:\Windows\ccmsetup\ccmsetup.exe /uninstall", ct: ct).ConfigureAwait(false);
            return ActionExecutionResult.Success(device.DisplayName, "Uninstall SCCM Client", $"Started ccmsetup.exe /uninstall (PID {pid}).", sw.Elapsed);
        }

        public async Task<ActionExecutionResult> GroupPolicyUpdateAsync(TargetDevice device, CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            var pid = await _wmi.CreateRemoteProcessAsync(device.ConnectAddress, "gpupdate.exe /force", ct: ct).ConfigureAwait(false);
            return ActionExecutionResult.Success(device.DisplayName, "Group Policy Update", $"Started gpupdate /force (PID {pid}).", sw.Elapsed);
        }

        public Task<ActionExecutionResult> LaunchInteractiveCommandPromptAsync(TargetDevice device, OpenRctSettings settings, CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            var validation = SettingsService.ValidatePsExecPath(settings.PsExecPath);
            if (!validation.IsValid)
            {
                return Task.FromResult(ActionExecutionResult.Failure(
                    device.DisplayName,
                    "Interactive Command Prompt",
                    validation.Message,
                    "PsExec not configured",
                    sw.Elapsed));
            }

            var args = SettingsService.BuildPsExecArguments(settings, device.ConnectAddress, "cmd.exe");
            Process.Start(new ProcessStartInfo
            {
                FileName = settings.PsExecPath!.Trim().Trim('"'),
                Arguments = args,
                UseShellExecute = true
            });

            return Task.FromResult(ActionExecutionResult.Success(
                device.DisplayName,
                "Interactive Command Prompt",
                $"Launched PsExec cmd.exe against {device.ConnectAddress}.",
                sw.Elapsed));
        }

        public Task<ActionExecutionResult> LaunchInteractivePowerShellAsync(TargetDevice device, CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            var safeHost = device.ConnectAddress.Replace("'", "''");
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoExit -Command \"$host.UI.RawUI.WindowTitle = 'OpenRCT PowerShell - {safeHost}'; Enter-PSSession -ComputerName '{safeHost}'\"",
                UseShellExecute = true
            });
            return Task.FromResult(ActionExecutionResult.Success(device.DisplayName, "Interactive PowerShell Prompt", $"Opened WinRM session to {device.ConnectAddress}.", sw.Elapsed));
        }

        public Task<ActionExecutionResult> OpenRemoteShareAsync(TargetDevice device, string relativeSharePath, CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            var unc = $@"\\{device.ConnectAddress}\{relativeSharePath.TrimStart('\\')}";
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{unc}\"",
                UseShellExecute = true
            });
            return Task.FromResult(ActionExecutionResult.Success(device.DisplayName, "Open Remote Share", $"Opened {unc}.", sw.Elapsed));
        }

        public Task<ActionExecutionResult> LaunchLocalMmcToolAsync(TargetDevice device, string toolExe, string argumentsTemplate, CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            var args = argumentsTemplate.Replace("{host}", device.ConnectAddress);
            Process.Start(new ProcessStartInfo
            {
                FileName = toolExe,
                Arguments = args,
                UseShellExecute = true
            });
            return Task.FromResult(ActionExecutionResult.Success(device.DisplayName, toolExe, $"Launched {toolExe} {args}.", sw.Elapsed));
        }

        public async Task<PingResultEntry> PingDeviceAsync(TargetDevice device, int timeoutMs = 2000, CancellationToken ct = default)
        {
            using var ping = new Ping();
            try
            {
                var reply = await ping.SendPingAsync(device.ConnectAddress, timeoutMs).ConfigureAwait(false);
                var online = reply.Status == IPStatus.Success;
                return new PingResultEntry
                {
                    DeviceName = device.DisplayName,
                    IpAddress = reply.Address?.ToString() ?? string.Empty,
                    IsOnline = online,
                    RoundtripMs = online ? reply.RoundtripTime : (long?)null,
                    StatusText = online ? $"Online ({reply.RoundtripTime} ms)" : reply.Status.ToString(),
                    SentCount = 1,
                    SuccessCount = online ? 1 : 0,
                    LastChecked = DateTime.Now
                };
            }
            catch (Exception ex)
            {
                return new PingResultEntry
                {
                    DeviceName = device.DisplayName,
                    IsOnline = false,
                    StatusText = ex.InnerException?.Message ?? ex.Message,
                    SentCount = 1,
                    SuccessCount = 0,
                    LastChecked = DateTime.Now
                };
            }
        }

        public async Task<IReadOnlyList<RemoteProcessInfo>> GetRunningProcessesAsync(TargetDevice device, CancellationToken ct = default)
        {
            var rows = await _wmi.QueryAsync(device.ConnectAddress, @"root\cimv2",
                "SELECT ProcessId, ParentProcessId, Name, WorkingSetSize, CreationDate, CommandLine FROM Win32_Process", ct: ct).ConfigureAwait(false);

            return rows.Select(r => new RemoteProcessInfo
            {
                DeviceName = device.DisplayName,
                ProcessId = WmiClient.GetUInt32(r, "ProcessId"),
                ParentProcessId = WmiClient.GetUInt32(r, "ParentProcessId"),
                Name = WmiClient.GetString(r, "Name"),
                WorkingSetMb = Math.Round(WmiClient.GetInt64(r, "WorkingSetSize") / (1024.0 * 1024.0), 2),
                CreationDate = WmiClient.GetDmtfDateTime(r, "CreationDate"),
                CommandLine = WmiClient.GetString(r, "CommandLine")
            }).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public Task<ActionExecutionResult> TerminateProcessAsync(TargetDevice device, uint processId, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                ct.ThrowIfCancellationRequested();
                var scope = _wmi.CreateScope(device.ConnectAddress, @"root\cimv2");
                using var proc = new ManagementObject(scope, new ManagementPath($"Win32_Process.Handle='{processId}'"), null);
                var ret = proc.InvokeMethod("Terminate", new object[] { 1u });
                var code = ret != null ? Convert.ToUInt32(ret) : 0u;
                if (code != 0)
                {
                    return ActionExecutionResult.Failure(device.DisplayName, "Terminate Process", $"Terminate returned error code {code}.", null, sw.Elapsed);
                }
                return ActionExecutionResult.Success(device.DisplayName, "Terminate Process", $"Terminated PID {processId}.", sw.Elapsed);
            }, ct);
        }

        public Task<ActionExecutionResult> SendNotificationAsync(TargetDevice device, string message, int displaySeconds = 300, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                ct.ThrowIfCancellationRequested();
                var safeMsg = (message ?? string.Empty).Replace("\"", "'");
                var psi = new ProcessStartInfo
                {
                    FileName = "msg.exe",
                    Arguments = $"* /SERVER:{device.ConnectAddress} /TIME:{Math.Max(10, displaySeconds)} \"{safeMsg}\"",
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start msg.exe.");
                proc.WaitForExit(10000);
                var err = proc.StandardError.ReadToEnd().Trim();
                if (proc.ExitCode != 0)
                {
                    return ActionExecutionResult.Failure(device.DisplayName, "Send Notification",
                        string.IsNullOrWhiteSpace(err) ? $"msg.exe exited with code {proc.ExitCode}." : err, err, sw.Elapsed);
                }
                return ActionExecutionResult.Success(device.DisplayName, "Send Notification", "Notification sent to logged-on session(s).", sw.Elapsed);
            }, ct);
        }

        public async Task<ActionExecutionResult> SendWakeOnLanAsync(TargetDevice device, IEnumerable<string> macAddresses, CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            var validMacs = (macAddresses ?? Enumerable.Empty<string>())
                .Select(ParseMacBytes)
                .Where(b => b != null)
                .Cast<byte[]>()
                .ToList();

            if (validMacs.Count == 0)
            {
                return ActionExecutionResult.Failure(device.DisplayName, "Wake On LAN",
                    "No valid MAC address found in ConfigMgr inventory for this device.", null, sw.Elapsed);
            }

            using var udp = new UdpClient { EnableBroadcast = true };
            foreach (var mac in validMacs)
            {
                ct.ThrowIfCancellationRequested();
                var packet = BuildMagicPacket(mac);
                await udp.SendAsync(packet, packet.Length, new IPEndPoint(IPAddress.Broadcast, 9)).ConfigureAwait(false);
                await udp.SendAsync(packet, packet.Length, new IPEndPoint(IPAddress.Broadcast, 7)).ConfigureAwait(false);
            }

            return ActionExecutionResult.Success(device.DisplayName, "Wake On LAN",
                $"Sent Wake-on-LAN magic packet(s) to {validMacs.Count} adapter(s).", sw.Elapsed);
        }

        public async Task<ActionExecutionResult> SchedulePowerActionAsync(TargetDevice device, SchedulePowerOptions options, CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            var host = device.ConnectAddress;

            if (options.SkipIfUserLoggedOn)
            {
                var csRows = await _wmi.QueryAsync(host, @"root\cimv2", "SELECT UserName FROM Win32_ComputerSystem", ct: ct).ConfigureAwait(false);
                var user = csRows.Select(r => WmiClient.GetString(r, "UserName")).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(user))
                {
                    return ActionExecutionResult.Warning(device.DisplayName,
                        options.IsShutdown ? "Shutdown" : "Restart",
                        $"Skipped because user '{user}' is logged on.",
                        sw.Elapsed);
                }
            }

            var modeFlag = options.IsShutdown ? "/s" : "/r";
            var forceFlag = options.ForceCloseApps ? "/f" : string.Empty;
            var delay = Math.Max(0, options.DelaySeconds);
            var comment = string.IsNullOrWhiteSpace(options.Message)
                ? string.Empty
                : $"/c \"{options.Message.Replace("\"", "'")}\"";

            var psi = new ProcessStartInfo
            {
                FileName = "shutdown.exe",
                Arguments = $"{modeFlag} {forceFlag} /t {delay} /m \\\\{host} {comment}".Trim(),
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start shutdown.exe.");
            proc.WaitForExit(15000);
            var err = proc.StandardError.ReadToEnd().Trim();
            if (proc.ExitCode != 0)
            {
                return ActionExecutionResult.Failure(device.DisplayName,
                    options.IsShutdown ? "Shutdown" : "Restart",
                    string.IsNullOrWhiteSpace(err) ? $"shutdown.exe failed with code {proc.ExitCode}." : err,
                    err, sw.Elapsed);
            }

            var actionLabel = options.IsShutdown ? "Shutdown" : "Restart";
            return ActionExecutionResult.Success(device.DisplayName, actionLabel,
                delay == 0 ? $"{actionLabel} initiated immediately." : $"{actionLabel} scheduled in {delay}s.", sw.Elapsed);
        }

        public Task<ActionExecutionResult> CancelPendingRestartAsync(TargetDevice device, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();
                ct.ThrowIfCancellationRequested();
                var psi = new ProcessStartInfo
                {
                    FileName = "shutdown.exe",
                    Arguments = $"/a /m \\\\{device.ConnectAddress}",
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start shutdown.exe.");
                proc.WaitForExit(10000);
                var err = proc.StandardError.ReadToEnd().Trim();
                if (proc.ExitCode != 0)
                {
                    return ActionExecutionResult.Failure(device.DisplayName, "Cancel Pending Restart/Shutdown",
                        string.IsNullOrWhiteSpace(err) ? $"shutdown.exe /a failed with code {proc.ExitCode}." : err, err, sw.Elapsed);
                }
                return ActionExecutionResult.Success(device.DisplayName, "Cancel Pending Restart/Shutdown", "Cancelled pending restart or shutdown.", sw.Elapsed);
            }, ct);
        }

        public async Task<ActionExecutionResult> RunDefenderActionAsync(TargetDevice device, string actionType, CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            var host = device.ConnectAddress;

            switch (actionType.ToLowerInvariant())
            {
                case "quickscan":
                    await _wmi.InvokeStaticMethodAsync(host, @"root\Microsoft\Windows\Defender", "MSFT_MpScan", "Start",
                        new Dictionary<string, object?> { ["ScanType"] = 1u }, ct: ct).ConfigureAwait(false);
                    return ActionExecutionResult.Success(device.DisplayName, "Defender Quick Scan", "Started Windows Defender Quick Scan.", sw.Elapsed);
                case "fullscan":
                    await _wmi.InvokeStaticMethodAsync(host, @"root\Microsoft\Windows\Defender", "MSFT_MpScan", "Start",
                        new Dictionary<string, object?> { ["ScanType"] = 2u }, ct: ct).ConfigureAwait(false);
                    return ActionExecutionResult.Success(device.DisplayName, "Defender Full Scan", "Started Windows Defender Full Scan.", sw.Elapsed);
                case "updatesignatures":
                    await _wmi.InvokeStaticMethodAsync(host, @"root\Microsoft\Windows\Defender", "MSFT_MpSignature", "Update", ct: ct).ConfigureAwait(false);
                    return ActionExecutionResult.Success(device.DisplayName, "Defender Update Definitions", "Updated Windows Defender definitions.", sw.Elapsed);
                default:
                    throw new ArgumentException($"Unknown Defender action '{actionType}'.", nameof(actionType));
            }
        }

        public static byte[] BuildMagicPacket(byte[] macBytes)
        {
            if (macBytes == null || macBytes.Length != 6)
            {
                throw new ArgumentException("MAC address must be exactly 6 bytes.", nameof(macBytes));
            }
            var packet = new byte[6 + 16 * 6];
            for (int i = 0; i < 6; i++) packet[i] = 0xFF;
            for (int i = 0; i < 16; i++) Buffer.BlockCopy(macBytes, 0, packet, 6 + i * 6, 6);
            return packet;
        }

        public static byte[]? ParseMacBytes(string? mac)
        {
            if (string.IsNullOrWhiteSpace(mac)) return null;
            var hex = Regex.Replace(mac!, "[^0-9A-Fa-f]", string.Empty);
            if (hex.Length != 12) return null;
            var bytes = new byte[6];
            for (int i = 0; i < 6; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return bytes;
        }

        private static string MapAppEvalState(int state) => state switch
        {
            0 => "No State Information",
            1 => "Application is enforced to desired state",
            2 => "Application is not required",
            3 => "Available for Enforcement",
            4 => "Not required",
            5 => "Waiting for Content",
            6 => "Waiting for Dependency",
            7 => "Waiting for Service Window",
            8 => "Waiting for Restart",
            9 => "Waiting for User Logoff",
            10 => "Waiting for User Logon",
            11 => "Verifying Before Install",
            12 => "Install Complete",
            13 => "Error",
            14 => "Installing",
            16 => "Pending Soft Reboot",
            17 => "Pending Hard Reboot",
            _ => $"State {state}"
        };

        private static string MapUpdateEvalState(int state) => state switch
        {
            0 => "None",
            1 => "Available",
            2 => "Submitted",
            3 => "Detecting",
            4 => "PreDownload",
            5 => "Downloading",
            6 => "WaitInstall",
            7 => "Installing",
            8 => "PendingSoftReboot",
            9 => "PendingHardReboot",
            10 => "WaitReboot",
            11 => "Verifying",
            12 => "InstallComplete",
            13 => "Error",
            14 => "WaitServiceWindow",
            _ => $"State {state}"
        };
    }
}
