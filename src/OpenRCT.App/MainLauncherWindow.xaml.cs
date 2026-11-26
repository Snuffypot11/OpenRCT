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

            // Quick Access / Core Tools
            void Add(string cat, string name, string key, bool quick = false, bool primary = false) =>
                list.Add(new ToolCatalogEntry { Category = cat, DisplayName = name, ActionKey = key, ShowInQuickAccess = quick, IsPrimary = primary });

            // Client Actions (12)
            foreach (var action in ClientActionRegistry.All)
            {
                bool quick = string.Equals(action.Key, ClientActionRegistry.MachinePolicyRetrievalAndEval, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(action.Key, ClientActionRegistry.AppDeploymentEval, StringComparison.OrdinalIgnoreCase);
                Add("Client Actions", action.DisplayName, action.Key, quick: quick);
            }

            // Client Tools (13)
            Add("Client Tools", "Client Information", ToolActionKeys.ClientInformation, quick: true);
            Add("Client Tools", "Remote Software Center", ToolActionKeys.RemoteSoftwareCenter, quick: true);
            Add("Client Tools", "Show Collections - Advanced", ToolActionKeys.ShowCollectionsAdvanced);
            Add("Client Tools", "Add Device(s) to Collection", ToolActionKeys.AddDevicesToCollection);
            Add("Client Tools", "Open Client Log File Folder", ToolActionKeys.OpenClientLogs);
            Add("Client Tools", "Open Client Installation Log File Folder", ToolActionKeys.OpenClientInstallLogs);
            Add("Client Tools", "Refresh Updates Compliance State", ToolActionKeys.RefreshUpdatesComplianceState);
            Add("Client Tools", "Rerun Deployment", ToolActionKeys.RerunDeployment);
            Add("Client Tools", "Restart SMS Agent Host Service", ToolActionKeys.RestartSmsAgentHost);
            Add("Client Tools", "Run Client Check", ToolActionKeys.RunClientCheck);
            Add("Client Tools", "Stop ConfigMgr Client Activities", ToolActionKeys.StopConfigMgrClientActivities);
            Add("Client Tools", "Repair Client", ToolActionKeys.RepairClient);
            Add("Client Tools", "Uninstall SCCM Client", ToolActionKeys.UninstallSccmClient);

            // Console Tools (19 + Device Status Messages)
            Add("Console Tools", "System Information", ToolActionKeys.SystemInformation, quick: true, primary: true);
            Add("Console Tools", "Ping System(s)", ToolActionKeys.PingSystems, quick: true);
            Add("Console Tools", "Running Processes", ToolActionKeys.RunningProcesses, quick: true);
            Add("Console Tools", "Open Regedit", ToolActionKeys.OpenRegedit, quick: true);
            Add("Console Tools", "Open Event Viewer", ToolActionKeys.OpenEventViewer, quick: true);
            Add("Console Tools", "Schedule Restart or Shutdown", ToolActionKeys.ScheduleRestartOrShutdown, quick: true);
            Add("Console Tools", "Cancel Pending Restart or Shutdown", ToolActionKeys.CancelPendingRestart);
            Add("Console Tools", "Interactive PowerShell Prompt", ToolActionKeys.InteractivePowerShellPrompt, quick: true);
            Add("Console Tools", "Interactive Command Prompt (PsExec)", ToolActionKeys.InteractiveCommandPrompt, quick: true);
            Add("Console Tools", "Manage Computer", ToolActionKeys.ManageComputer);
            Add("Console Tools", "Connect to C$", ToolActionKeys.ConnectToCShare);
            Add("Console Tools", "Group Policy Update", ToolActionKeys.GroupPolicyUpdate);
            Add("Console Tools", "AD Security Groups", ToolActionKeys.AdSecurityGroups);
            Add("Console Tools", "Send Notification", ToolActionKeys.SendNotification);
            Add("Console Tools", "Wake On LAN", ToolActionKeys.WakeOnLan);
            Add("Console Tools", "Device Status Messages", ToolActionKeys.DeviceStatusMessages, quick: true);
            Add("Console Tools", "Enable Device(s) in Active Directory", ToolActionKeys.EnableInActiveDirectory);
            Add("Console Tools", "Disable Device(s) in Active Directory", ToolActionKeys.DisableInActiveDirectory);
            Add("Console Tools", "Delete Device(s) from Active Directory", ToolActionKeys.DeleteFromActiveDirectory);
            Add("Console Tools", "Delete Device(s) From ConfigMgr", ToolActionKeys.DeleteFromConfigMgr);

            // Security Tools (5)
            Add("Security Tools", "View LAPS Password", ToolActionKeys.ViewLapsPassword, quick: true);
            Add("Security Tools", "View BitLocker Recovery Key", ToolActionKeys.ViewBitLockerRecoveryKey, quick: true);
            Add("Security Tools", "Windows Defender - Quick Scan", ToolActionKeys.DefenderQuickScan);
            Add("Security Tools", "Windows Defender - Full Scan", ToolActionKeys.DefenderFullScan);
            Add("Security Tools", "Windows Defender - Update Definitions", ToolActionKeys.DefenderUpdateDefinitions);

            return list;
        }

        private void PopulateToolTabs()
        {
            QuickAccessPanel.Children.Clear();
            ClientActionsPanel.Children.Clear();
            ClientToolsPanel.Children.Clear();
            ConsoleToolsPanel.Children.Clear();
            SecurityToolsPanel.Children.Clear();

            // Put System Information first in Quick Access
            foreach (var entry in _catalog.Where(c => c.ShowInQuickAccess).OrderByDescending(c => c.IsPrimary))
            {
                QuickAccessPanel.Children.Add(CreateToolButton(entry));
            }
            foreach (var entry in _catalog.Where(c => c.Category == "Client Actions"))
            {
                ClientActionsPanel.Children.Add(CreateToolButton(entry));
            }
            foreach (var entry in _catalog.Where(c => c.Category == "Client Tools"))
            {
                ClientToolsPanel.Children.Add(CreateToolButton(entry));
            }
            foreach (var entry in _catalog.Where(c => c.Category == "Console Tools"))
            {
                ConsoleToolsPanel.Children.Add(CreateToolButton(entry));
            }
            foreach (var entry in _catalog.Where(c => c.Category == "Security Tools"))
            {
                SecurityToolsPanel.Children.Add(CreateToolButton(entry));
            }
        }

        private Button CreateToolButton(ToolCatalogEntry entry)
        {
            var btn = new Button
            {
                Content = entry.DisplayName,
                Tag = entry.ActionKey,
                Width = 235,
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
