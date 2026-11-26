using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using OpenRCT.Connectors.SmsProvider;
using OpenRCT.Connectors.Wmi;
using OpenRCT.Core.Models;
using OpenRCT.Core.Settings;
using OpenRCT.Core.Updates;
using OpenRCT.UI;
using OpenRCT.UI.Controls;
using OpenRCT.UI.Themes;
using OpenRCT.UI.Views.Tools;

namespace OpenRCT.App
{
    public partial class MainLauncherWindow : Window
    {
        private sealed class ToolCatalogEntry
        {
            public string Category { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public string ActionKey { get; set; } = string.Empty;
            public string IconKey { get; set; } = "ToolSystemInfo";
            public bool IsPrimary { get; set; }
            public bool ShowInQuickAccess { get; set; }
        }

        private readonly SettingsService _settingsService = new SettingsService();
        private readonly List<ToolCatalogEntry> _catalog;
        private string? _smsServer;
        private string? _siteCode;
        private List<SmsCollectionSummary> _collections = new List<SmsCollectionSummary>();
        private bool _collectionsLoaded;

        public MainLauncherWindow()
        {
            var settings = _settingsService.Load();
            ThemeManager.SetIconStyle(settings.IconStyle);
            ThemeManager.ApplyTheme(this, settings.Theme);
            InitializeComponent();

            _catalog = BuildCatalog();
            PopulateToolTabs();
            ApplyUpdateCheckVisibility(settings);
            ResolveSmsConnectionAndFooter(settings);
        }

        private static List<ToolCatalogEntry> BuildCatalog()
        {
            var list = new List<ToolCatalogEntry>();

            void Add(string cat, string name, string key, string iconKey, bool quick = false, bool primary = false) =>
                list.Add(new ToolCatalogEntry { Category = cat, DisplayName = name, ActionKey = key, IconKey = iconKey, ShowInQuickAccess = quick, IsPrimary = primary });

            // 1. Remote Tools (10)
            Add("Remote Tools", "System Information", ToolActionKeys.SystemInformation, "ToolSystemInfo", quick: true, primary: true);
            Add("Remote Tools", "Remote Software Center", ToolActionKeys.RemoteSoftwareCenter, "ToolSoftwareCenter", quick: true);
            Add("Remote Tools", "Remote Windows Security", ToolActionKeys.RemoteWindowsSecurity, "ToolWindowsSecurity", quick: true);
            Add("Remote Tools", "Remote Registry Editor", ToolActionKeys.OpenRegedit, "ToolRemoteRegistry", quick: true);
            Add("Remote Tools", "Remote Event Viewer", ToolActionKeys.RemoteEventViewer, "ToolRemoteEventViewer", quick: true);
            Add("Remote Tools", "Running Processes", ToolActionKeys.RunningProcesses, "ToolRunningProcesses", quick: true);
            Add("Remote Tools", "Connect to C$", ToolActionKeys.ConnectToCShare, "ToolFolderShare");
            Add("Remote Tools", "Interactive PowerShell Prompt", ToolActionKeys.InteractivePowerShellPrompt, "ToolPowerShell", quick: true);
            Add("Remote Tools", "Interactive Command Prompt (PsExec)", ToolActionKeys.InteractiveCommandPrompt, "ToolCommandPrompt", quick: true);
            Add("Remote Tools", "Manage Computer", ToolActionKeys.ManageComputer, "ToolManageComputer");

            // 2. Client Actions (12)
            foreach (var action in ClientActionRegistry.All)
            {
                bool quick = string.Equals(action.Key, ClientActionRegistry.MachinePolicyRetrievalAndEval, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(action.Key, ClientActionRegistry.AppDeploymentEval, StringComparison.OrdinalIgnoreCase);
                Add("Client Actions", action.DisplayName, action.Key, "ToolClientAction", quick: quick);
            }

            // 3. Client Tools (13)
            Add("Client Tools", "Client Information", ToolActionKeys.ClientInformation, "ToolClientInfo", quick: true);
            Add("Client Tools", "Show Collections - Advanced", ToolActionKeys.ShowCollectionsAdvanced, "ToolCollections");
            Add("Client Tools", "Add Device(s) to Collection", ToolActionKeys.AddDevicesToCollection, "ToolCollections");
            Add("Client Tools", "Device Status Messages", ToolActionKeys.DeviceStatusMessages, "ToolStatusMessages", quick: true);
            Add("Client Tools", "Open Client Log File Folder", ToolActionKeys.OpenClientLogs, "ToolFolderShare");
            Add("Client Tools", "Open Client Installation Log File Folder", ToolActionKeys.OpenClientInstallLogs, "ToolFolderShare");
            Add("Client Tools", "Refresh Updates Compliance State", ToolActionKeys.RefreshUpdatesComplianceState, "DashSoftwareUpdates");
            Add("Client Tools", "Rerun Deployment", ToolActionKeys.RerunDeployment, "ToolRestartService");
            Add("Client Tools", "Restart SMS Agent Host Service", ToolActionKeys.RestartSmsAgentHost, "ToolRestartService");
            Add("Client Tools", "Run Client Check", ToolActionKeys.RunClientCheck, "ToolRepair");
            Add("Client Tools", "Stop ConfigMgr Client Activities", ToolActionKeys.StopConfigMgrClientActivities, "ToolPowerSchedule");
            Add("Client Tools", "Repair Client", ToolActionKeys.RepairClient, "ToolRepair");
            Add("Client Tools", "Uninstall SCCM Client", ToolActionKeys.UninstallSccmClient, "ToolUninstall");

            // 4. Device & Power Tools (6)
            Add("Device & Power", "Ping System(s)", ToolActionKeys.PingSystems, "ToolPing", quick: true);
            Add("Device & Power", "Wake On LAN", ToolActionKeys.WakeOnLan, "ToolWakeOnLan");
            Add("Device & Power", "Group Policy Update", ToolActionKeys.GroupPolicyUpdate, "ToolGroupPolicy");
            Add("Device & Power", "Send Notification", ToolActionKeys.SendNotification, "ToolNotification");
            Add("Device & Power", "Schedule Restart or Shutdown", ToolActionKeys.ScheduleRestartOrShutdown, "ToolPowerSchedule", quick: true);
            Add("Device & Power", "Cancel Pending Restart or Shutdown", ToolActionKeys.CancelPendingRestart, "ToolPowerSchedule");

            // 5. Security Tools (5)
            Add("Security Tools", "View LAPS Password", ToolActionKeys.ViewLapsPassword, "ToolKey", quick: true);
            Add("Security Tools", "View BitLocker Recovery Key", ToolActionKeys.ViewBitLockerRecoveryKey, "DashBitLocker", quick: true);
            Add("Security Tools", "Windows Defender - Quick Scan", ToolActionKeys.DefenderQuickScan, "ToolDefender");
            Add("Security Tools", "Windows Defender - Full Scan", ToolActionKeys.DefenderFullScan, "ToolDefender");
            Add("Security Tools", "Windows Defender - Update Definitions", ToolActionKeys.DefenderUpdateDefinitions, "ToolDefender");

            // 6. Active Directory & ConfigMgr (5)
            Add("AD & ConfigMgr", "AD Security Groups", ToolActionKeys.AdSecurityGroups, "ToolAdGroups");
            Add("AD & ConfigMgr", "Enable Device(s) in Active Directory", ToolActionKeys.EnableInActiveDirectory, "ToolAdGroups");
            Add("AD & ConfigMgr", "Disable Device(s) in Active Directory", ToolActionKeys.DisableInActiveDirectory, "ToolAdGroups");
            Add("AD & ConfigMgr", "Delete Device(s) from Active Directory", ToolActionKeys.DeleteFromActiveDirectory, "ToolUninstall");
            Add("AD & ConfigMgr", "Delete Device(s) From ConfigMgr", ToolActionKeys.DeleteFromConfigMgr, "ToolUninstall");

            return list;
        }

        private void PopulateToolTabs()
        {
            QuickAccessPanel.Children.Clear();
            RemoteToolsPanel.Children.Clear();
            ClientActionsPanel.Children.Clear();
            ClientToolsPanel.Children.Clear();
            DevicePowerPanel.Children.Clear();
            SecurityToolsPanel.Children.Clear();
            AdConfigMgrPanel.Children.Clear();

            foreach (var entry in _catalog.Where(c => c.ShowInQuickAccess).OrderByDescending(c => c.IsPrimary))
            {
                QuickAccessPanel.Children.Add(CreateToolButton(entry));
            }
            foreach (var entry in _catalog.Where(c => c.Category == "Remote Tools"))
            {
                RemoteToolsPanel.Children.Add(CreateToolButton(entry));
            }
            foreach (var entry in _catalog.Where(c => c.Category == "Client Actions"))
            {
                ClientActionsPanel.Children.Add(CreateToolButton(entry));
            }
            foreach (var entry in _catalog.Where(c => c.Category == "Client Tools"))
            {
                ClientToolsPanel.Children.Add(CreateToolButton(entry));
            }
            foreach (var entry in _catalog.Where(c => c.Category == "Device & Power"))
            {
                DevicePowerPanel.Children.Add(CreateToolButton(entry));
            }
            foreach (var entry in _catalog.Where(c => c.Category == "Security Tools"))
            {
                SecurityToolsPanel.Children.Add(CreateToolButton(entry));
            }
            foreach (var entry in _catalog.Where(c => c.Category == "AD & ConfigMgr"))
            {
                AdConfigMgrPanel.Children.Add(CreateToolButton(entry));
            }
        }

        private Button CreateToolButton(ToolCatalogEntry entry)
        {
            var contentPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            contentPanel.Children.Add(new VectorIcon
            {
                IconKey = entry.IconKey,
                Width = 16,
                Height = 16,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            contentPanel.Children.Add(new TextBlock
            {
                Text = entry.DisplayName,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            var btn = new Button
            {
                Content = contentPanel,
                Tag = entry.ActionKey,
                Width = 242,
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(10, 7, 10, 7),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                ToolTip = $"{entry.Category}: {entry.DisplayName}"
            };
            btn.SetResourceReference(StyleProperty, entry.IsPrimary ? "PrimaryButton" : "StandardButton");
            btn.Click += RunTool_Click;
            return btn;
        }

        private void ApplyUpdateCheckVisibility(OpenRctSettings settings)
        {
            CheckUpdatesButton.Visibility = settings.EnableUpdateChecks ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ResolveSmsConnectionAndFooter(OpenRctSettings settings)
        {
            var ver = GitHubUpdateChecker.GetCurrentVersionString(typeof(MainLauncherWindow).Assembly);
            _smsServer = settings.DefaultSmsProviderServer;
            _siteCode = settings.DefaultSiteCode;

            if (string.IsNullOrWhiteSpace(_smsServer) || string.IsNullOrWhiteSpace(_siteCode))
            {
                try
                {
                    var sms = new SmsProviderClient(new WmiClient { DefaultTimeout = TimeSpan.FromSeconds(5) });
                    if (sms.TryAutoDiscover(out var srv, out var site))
                    {
                        if (string.IsNullOrWhiteSpace(_smsServer)) _smsServer = srv;
                        if (string.IsNullOrWhiteSpace(_siteCode)) _siteCode = site;
                    }
                }
                catch
                {
                }
            }

            var smsStatus = !string.IsNullOrWhiteSpace(_siteCode) && !string.IsNullOrWhiteSpace(_smsServer)
                ? $"Site: {_siteCode} ({_smsServer})"
                : "SMS Provider: Auto-discover / Configure in Settings";
            FooterStatusText.Text = $"OpenRCT v{ver} • MIT License • {smsStatus}";
        }

        private async Task LoadCollectionsAsync()
        {
            if (string.IsNullOrWhiteSpace(_smsServer) || string.IsNullOrWhiteSpace(_siteCode))
            {
                CollectionCombo.ItemsSource = null;
                TargetCountText.Text = "Configure SMS Provider in Settings";
                return;
            }

            RefreshCollectionsButton.IsEnabled = false;
            TargetCountText.Text = "Loading collections...";
            try
            {
                var settings = _settingsService.Load();
                var sms = new SmsProviderClient(new WmiClient { DefaultTimeout = TimeSpan.FromSeconds(settings.ConnectionTimeoutSeconds) });
                var cols = await sms.GetDeviceCollectionsAsync(_smsServer!, _siteCode!);
                _collections = cols.ToList();
                _collectionsLoaded = true;
                CollectionCombo.ItemsSource = _collections;
                TargetCountText.Text = $"{_collections.Count} collection(s) available";
            }
            catch (Exception ex)
            {
                TargetCountText.Text = $"Collection load failed: {ex.Message}";
            }
            finally
            {
                RefreshCollectionsButton.IsEnabled = true;
            }
        }

        private async void TargetMode_Changed(object sender, RoutedEventArgs e)
        {
            if (ManualTargetsPanel == null || CollectionTargetPanel == null) return;
            bool isCollection = TargetCollectionRadio.IsChecked == true;
            ManualTargetsPanel.Visibility = isCollection ? Visibility.Collapsed : Visibility.Visible;
            CollectionTargetPanel.Visibility = isCollection ? Visibility.Visible : Visibility.Collapsed;
            ClearTargetsButton.Visibility = isCollection ? Visibility.Collapsed : Visibility.Visible;

            if (isCollection)
            {
                if (!_collectionsLoaded) await LoadCollectionsAsync();
                UpdateCollectionCountText();
            }
            else
            {
                UpdateManualTargetCountText();
            }
        }

        private async void RefreshCollections_Click(object sender, RoutedEventArgs e)
        {
            _collectionsLoaded = false;
            await LoadCollectionsAsync();
        }

        private void CollectionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateCollectionCountText();

        private void UpdateCollectionCountText()
        {
            if (CollectionCombo.SelectedItem is SmsCollectionSummary col)
            {
                TargetCountText.Text = $"{col.MemberCount} member(s) in {col.CollectionId}";
            }
        }

        private void TargetsBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateManualTargetCountText();

        private void UpdateManualTargetCountText()
        {
            var count = ParseTargets().Length;
            TargetCountText.Text = count == 1 ? "1 target" : $"{count} targets";
        }

        private void ClearTargets_Click(object sender, RoutedEventArgs e)
        {
            TargetsBox.Clear();
            TargetsBox.Focus();
        }

        private void ToolSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var term = ToolSearchBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(term))
            {
                SearchResultsScroll.Visibility = Visibility.Collapsed;
                ToolCategoryTabs.Visibility = Visibility.Visible;
                return;
            }

            SearchResultsPanel.Children.Clear();
            var matches = _catalog.Where(c =>
                c.DisplayName.IndexOf(term!, StringComparison.OrdinalIgnoreCase) >= 0 ||
                c.Category.IndexOf(term!, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            if (matches.Count == 0)
            {
                SearchResultsPanel.Children.Add(new TextBlock
                {
                    Text = $"No tools match '{term}'.",
                    Margin = new Thickness(4, 12, 4, 4),
                    Foreground = (System.Windows.Media.Brush)(TryFindResource("SecondaryTextBrush") ?? System.Windows.Media.Brushes.Gray)
                });
            }
            else
            {
                foreach (var entry in matches)
                {
                    SearchResultsPanel.Children.Add(CreateToolButton(entry));
                }
            }

            ToolCategoryTabs.Visibility = Visibility.Collapsed;
            SearchResultsScroll.Visibility = Visibility.Visible;
        }

        private TargetDevice[] ParseTargets()
        {
            var raw = TargetsBox.Text ?? string.Empty;
            return raw.Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(name => new TargetDevice { Name = name, SmsProviderServer = _smsServer, SiteCode = _siteCode })
                .ToArray();
        }

        private void RunTool_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn) || !(btn.Tag is string actionKey)) return;

            if (TargetCollectionRadio.IsChecked == true)
            {
                var selectedCol = CollectionCombo.SelectedItem as SmsCollectionSummary;
                if (selectedCol == null)
                {
                    MessageBox.Show(this, "Select a ConfigMgr Device Collection first.", "OpenRCT", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                ToolDispatcher.Dispatch(new ToolInvocationContext
                {
                    ActionKey = actionKey,
                    SmsProviderServer = _smsServer,
                    SiteCode = _siteCode,
                    CollectionId = selectedCol.CollectionId,
                    CollectionName = selectedCol.Name
                });
                return;
            }

            var targets = ParseTargets();
            if (targets.Length == 0)
            {
                MessageBox.Show(this, "Enter at least one target device hostname or FQDN (or switch to ConfigMgr Device Collection).", "OpenRCT", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ToolDispatcher.Dispatch(new ToolInvocationContext
            {
                ActionKey = actionKey,
                SmsProviderServer = _smsServer,
                SiteCode = _siteCode,
                Devices = targets
            });
        }

        private void OpenDashboard_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn) || !(btn.Tag is string dashboardKey)) return;
            ToolDispatcher.Dispatch(new ToolInvocationContext
            {
                ActionKey = dashboardKey,
                SmsProviderServer = _smsServer,
                SiteCode = _siteCode
            });
        }

        private void CheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            var win = new UpdateCheckWindow(_settingsService, typeof(MainLauncherWindow).Assembly) { Owner = this };
            win.ShowDialog();
            var refreshed = _settingsService.Load();
            ThemeManager.ApplyTheme(this, refreshed.Theme);
            ApplyUpdateCheckVisibility(refreshed);
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            ToolDispatcher.Dispatch(new ToolInvocationContext { ActionKey = ToolActionKeys.OpenSettings });
            var updated = _settingsService.Load();
            ThemeManager.SetIconStyle(updated.IconStyle);
            ThemeManager.ApplyTheme(this, updated.Theme);
            ApplyUpdateCheckVisibility(updated);
            ResolveSmsConnectionAndFooter(updated);
        }

        private void ViewInstallLog_Click(object sender, RoutedEventArgs e)
        {
            var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OpenRCT", "install.log");
            if (!File.Exists(logPath))
            {
                MessageBox.Show(this, $"No install log was found at:\n{logPath}", "OpenRCT Diagnostics", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Process.Start(new ProcessStartInfo { FileName = "notepad.exe", Arguments = $"\"{logPath}\"", UseShellExecute = true });
        }

        private void RepairConsoleExtension_Click(object sender, RoutedEventArgs e)
        {
            var installDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            var scriptPath = Path.Combine(installDir, "scripts", "Install-OpenRCT.ps1");
            if (!File.Exists(scriptPath))
            {
                MessageBox.Show(this, $"Installer script not found at:\n{scriptPath}", "OpenRCT", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoExit -NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -InstallDir \"{installDir}\" -SkipBinaryCopy",
                    Verb = "runas",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not launch repair script: {ex.Message}", "OpenRCT", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
