using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using OpenRCT.Core.Settings;
using OpenRCT.Core.Updates;
using OpenRCT.UI.Themes;

namespace OpenRCT.UI.Views.Tools
{
    public partial class SettingsWindow : Window
    {
        private readonly SettingsService _settingsService;
        private readonly OpenRctSettings _settings;
        private readonly AppThemePreference _initialTheme;
        private IconStylePreference _initialIconStyle;

        public SettingsWindow(SettingsService settingsService)
        {
            _settingsService = settingsService ?? new SettingsService();
            _settings = _settingsService.Load();
            _initialTheme = _settings.Theme;
            _initialIconStyle = _settings.IconStyle;

            ThemeManager.ApplyTheme(this, _settings.Theme);
            InitializeComponent();

            TransportCombo.ItemsSource = Enum.GetValues(typeof(RemoteTransportPreference));
            ThemeCombo.ItemsSource = Enum.GetValues(typeof(AppThemePreference));
            IconStyleCombo.ItemsSource = new[]
            {
                new IconStyleOption(IconStylePreference.FluentDuotone, "Fluent Duotone (Default)"),
                new IconStyleOption(IconStylePreference.CrispOutline, "Crisp Monochrome Outline")
            };

            PsExecPathBox.Text = _settings.PsExecPath ?? string.Empty;
            PsExecSystemCheck.IsChecked = _settings.PsExecRunAsSystem;
            PsExecEulaCheck.IsChecked = _settings.PsExecAcceptEula;
            PsExecArgsBox.Text = _settings.PsExecAdditionalArgs;

            TransportCombo.SelectedItem = _settings.TransportPreference;
            TimeoutBox.Text = _settings.ConnectionTimeoutSeconds.ToString();
            ConcurrencyBox.Text = _settings.MaxConcurrentDevices.ToString();
            SmsServerBox.Text = _settings.DefaultSmsProviderServer ?? string.Empty;
            SiteCodeBox.Text = _settings.DefaultSiteCode ?? string.Empty;
            FastOfflineCheckBox.IsChecked = _settings.EnableFastOfflineCheck;
            OfflineTimeoutMsBox.Text = _settings.OfflineCheckTimeoutMs.ToString();

            ThemeCombo.SelectedItem = _settings.Theme;
            IconStyleCombo.SelectedIndex = _settings.IconStyle == IconStylePreference.CrispOutline ? 1 : 0;
            ConfirmDestructiveCheck.IsChecked = _settings.ConfirmDestructiveActions;
            AutoClearClipboardCheck.IsChecked = _settings.AutoClearClipboardSecrets;
            ClipboardSecondsBox.Text = _settings.ClipboardClearSeconds.ToString();

            EnableUpdateChecksCheck.IsChecked = _settings.EnableUpdateChecks;
            UpdateRepoBox.Text = _settings.UpdateRepository ?? string.Empty;
            InstalledVersionText.Text = $"Installed: v{GitHubUpdateChecker.GetCurrentVersionString(typeof(SettingsWindow).Assembly)}";

            try { UpdatePsExecValidation(); } catch { }
            try { UpdateRepoValidation(); } catch { }

            Loaded += (_, __) => UpdateConsoleIconSyncHintAsync();
        }

        private static void LogSettingsDiagnostic(string message)
        {
            try
            {
                var logDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OpenRCT");
                System.IO.Directory.CreateDirectory(logDir);
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(logDir, "openrct.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [SettingsWindow] {message}\r\n");
            }
            catch { }
        }

        private void UpdateConsoleIconSyncHintAsync()
        {
            var currentStyle = _settings.IconStyle;
            System.Threading.Tasks.Task.Run(() =>
            {
                bool outOfSync = false;
                try
                {
                    outOfSync = ConsoleIconSyncService.IsConsoleInstalledAndOutOfSync(currentStyle);
                }
                catch { }

                Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        if (outOfSync)
                        {
                            IconStyleStatusText.Foreground = (Brush)(TryFindResource("WarningBrush") ?? Brushes.DarkOrange);
                            IconStyleStatusText.Text =
                                "MECM Console icons do not yet match your saved Icon Style. Click 'Apply to MECM Console' and restart the console to update tree and right-click icons.";
                        }
                        else
                        {
                            IconStyleStatusText.Foreground = (Brush)(TryFindResource("SecondaryTextBrush") ?? Brushes.Gray);
                            IconStyleStatusText.Text =
                                "Changes apply immediately to OpenRCT windows. MECM Console tree and right-click icons update after restarting the console.";
                        }
                    }
                    catch { }
                });
            });
        }

        private void ApplyConsoleIcons_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedStyle = IconStyleCombo.SelectedItem is IconStyleOption iconOpt
                    ? iconOpt.Value
                    : _settings.IconStyle;

                _settings.IconStyle = selectedStyle;
                _settingsService.Save(_settings);
                ThemeManager.SetIconStyle(selectedStyle);

                var sync = ConsoleIconSyncService.ApplyConsoleIconStyle(selectedStyle, this, allowElevationPrompt: true);
                ApplySyncStatusVisual(sync);

                if (sync.Status == ConsoleIconSyncStatus.Updated)
                {
                    _initialIconStyle = selectedStyle;
                    MessageBox.Show(this, sync.Message, "MECM Console Icons Updated", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else if (sync.Status == ConsoleIconSyncStatus.AlreadyUpToDate)
                {
                    _initialIconStyle = selectedStyle;
                    MessageBox.Show(this, sync.Message, "MECM Console Icons", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else if (sync.Status == ConsoleIconSyncStatus.Failed || sync.Status == ConsoleIconSyncStatus.SourceDllNotFound)
                {
                    MessageBox.Show(this, sync.Message, "MECM Console Icons", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    MessageBox.Show(this, sync.Message, "MECM Console Icons", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                LogSettingsDiagnostic($"ApplyConsoleIcons_Click error: {ex}");
                MessageBox.Show(this, $"Could not apply MECM Console icon style:\n\n{ex.Message}", "MECM Console Icons", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ApplySyncStatusVisual(ConsoleIconSyncResult sync)
        {
            IconStyleStatusText.Text = sync.Message;
            switch (sync.Status)
            {
                case ConsoleIconSyncStatus.Updated:
                case ConsoleIconSyncStatus.AlreadyUpToDate:
                    IconStyleStatusText.Foreground = (Brush)(TryFindResource("SuccessBrush") ?? Brushes.ForestGreen);
                    break;
                case ConsoleIconSyncStatus.CancelledByUser:
                case ConsoleIconSyncStatus.SourceDllNotFound:
                    IconStyleStatusText.Foreground = (Brush)(TryFindResource("WarningBrush") ?? Brushes.DarkOrange);
                    break;
                case ConsoleIconSyncStatus.Failed:
                    IconStyleStatusText.Foreground = (Brush)(TryFindResource("DangerBrush") ?? Brushes.Crimson);
                    break;
                default:
                    IconStyleStatusText.Foreground = (Brush)(TryFindResource("SecondaryTextBrush") ?? Brushes.Gray);
                    break;
            }
        }

        private void BrowsePsExec_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select Sysinternals PsExec Executable",
                Filter = "PsExec Executable (PsExec*.exe)|PsExec*.exe|All Executables (*.exe)|*.exe"
            };
            if (dlg.ShowDialog(this) == true)
            {
                PsExecPathBox.Text = dlg.FileName;
            }
        }

        private void PsExecPathBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (PsExecStatusText != null) UpdatePsExecValidation();
        }

        private void UpdateRepoBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (UpdateRepoStatusText != null) UpdateRepoValidation();
        }

        private void UpdatePsExecValidation()
        {
            var validation = SettingsService.ValidatePsExecPath(PsExecPathBox.Text);
            PsExecStatusText.Text = validation.Message;
            if (!validation.IsConfigured)
            {
                PsExecStatusText.Foreground = (Brush)(TryFindResource("SecondaryTextBrush") ?? Brushes.Gray);
            }
            else if (!validation.IsValid)
            {
                PsExecStatusText.Foreground = (Brush)(TryFindResource("DangerBrush") ?? Brushes.Crimson);
            }
            else if (validation.IsWarning)
            {
                PsExecStatusText.Foreground = (Brush)(TryFindResource("WarningBrush") ?? Brushes.DarkOrange);
            }
            else
            {
                PsExecStatusText.Foreground = (Brush)(TryFindResource("SuccessBrush") ?? Brushes.ForestGreen);
            }
        }

        private void UpdateRepoValidation()
        {
            var buildDefault = GitHubUpdateChecker.GetBuildDefaultRepository(typeof(SettingsWindow).Assembly);
            var raw = UpdateRepoBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                UpdateRepoStatusText.Foreground = (Brush)(TryFindResource("SecondaryTextBrush") ?? Brushes.Gray);
                UpdateRepoStatusText.Text = string.IsNullOrWhiteSpace(buildDefault)
                    ? "No build-time default repository embedded (local build). Enter owner/repo or a GitHub URL to enable update checks."
                    : $"Using build-time default repository: github.com/{buildDefault}";
                return;
            }

            if (GitHubUpdateChecker.TryNormalizeRepository(raw, out var normalized))
            {
                UpdateRepoStatusText.Foreground = (Brush)(TryFindResource("SuccessBrush") ?? Brushes.ForestGreen);
                UpdateRepoStatusText.Text = $"Will check GitHub Releases at: https://github.com/{normalized}/releases";
            }
            else
            {
                UpdateRepoStatusText.Foreground = (Brush)(TryFindResource("DangerBrush") ?? Brushes.Crimson);
                UpdateRepoStatusText.Text = "Invalid format. Enter 'owner/repo' or a valid 'https://github.com/owner/repo' URL.";
            }
        }

        private void CheckUpdatesNow_Click(object sender, RoutedEventArgs e)
        {
            ApplyPendingUpdateSettings();
            _settingsService.Save(_settings);
            new UpdateCheckWindow(_settingsService, typeof(SettingsWindow).Assembly) { Owner = this }.ShowDialog();
        }

        private void ApplyPendingUpdateSettings()
        {
            _settings.EnableUpdateChecks = EnableUpdateChecksCheck.IsChecked == true;
            var rawRepo = UpdateRepoBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(rawRepo))
            {
                _settings.UpdateRepository = null;
            }
            else if (GitHubUpdateChecker.TryNormalizeRepository(rawRepo, out var normalized))
            {
                _settings.UpdateRepository = normalized;
            }
            else
            {
                _settings.UpdateRepository = rawRepo;
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _settings.PsExecPath = PsExecPathBox.Text;
                _settings.PsExecRunAsSystem = PsExecSystemCheck.IsChecked == true;
                _settings.PsExecAcceptEula = PsExecEulaCheck.IsChecked == true;
                _settings.PsExecAdditionalArgs = PsExecArgsBox.Text ?? string.Empty;

                if (TransportCombo.SelectedItem is RemoteTransportPreference tp) _settings.TransportPreference = tp;
                if (int.TryParse(TimeoutBox.Text, out var timeout)) _settings.ConnectionTimeoutSeconds = timeout;
                if (int.TryParse(ConcurrencyBox.Text, out var conc)) _settings.MaxConcurrentDevices = conc;
                _settings.DefaultSmsProviderServer = SmsServerBox.Text;
                _settings.DefaultSiteCode = SiteCodeBox.Text;
                _settings.EnableFastOfflineCheck = FastOfflineCheckBox.IsChecked == true;
                if (int.TryParse(OfflineTimeoutMsBox.Text, out var offlineMs)) _settings.OfflineCheckTimeoutMs = offlineMs;

                if (ThemeCombo.SelectedItem is AppThemePreference theme) _settings.Theme = theme;
                if (IconStyleCombo.SelectedItem is IconStyleOption iconOpt) _settings.IconStyle = iconOpt.Value;
                _settings.ConfirmDestructiveActions = ConfirmDestructiveCheck.IsChecked == true;
                _settings.AutoClearClipboardSecrets = AutoClearClipboardCheck.IsChecked == true;
                if (int.TryParse(ClipboardSecondsBox.Text, out var clipSec)) _settings.ClipboardClearSeconds = clipSec;

                ApplyPendingUpdateSettings();

                _settingsService.Save(_settings);

                var themeChanged = _settings.Theme != _initialTheme;
                var iconStyleChanged = _settings.IconStyle != _initialIconStyle;

                if (iconStyleChanged)
                {
                    var sync = ConsoleIconSyncService.ApplyConsoleIconStyle(_settings.IconStyle, this, allowElevationPrompt: true);
                    if (sync.Status == ConsoleIconSyncStatus.Updated)
                    {
                        MessageBox.Show(this, sync.Message, "MECM Console Icons Updated", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else if (sync.Status == ConsoleIconSyncStatus.Failed || sync.Status == ConsoleIconSyncStatus.SourceDllNotFound)
                    {
                        MessageBox.Show(this, sync.Message, "MECM Console Icons", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }

                // Defer live theme/icon updates onto Background priority AFTER SettingsWindow closes
                // so we never mutate ResourceDictionaries on a modal Window mid-Click / mid-Close.
                var savedTheme = _settings.Theme;
                var savedIconStyle = _settings.IconStyle;
                var closingWindow = this;
                if (themeChanged || iconStyleChanged)
                {
                    Dispatcher.BeginInvoke(
                        System.Windows.Threading.DispatcherPriority.Background,
                        new Action(() =>
                        {
                            try
                            {
                                if (themeChanged) ThemeManager.ApplyApplicationTheme(savedTheme, excludeWindow: closingWindow);
                                if (iconStyleChanged) ThemeManager.SetIconStyle(savedIconStyle);
                            }
                            catch (Exception ex)
                            {
                                LogSettingsDiagnostic($"Deferred appearance update error: {ex}");
                            }
                        }));
                }

                try
                {
                    DialogResult = true;
                }
                catch
                {
                    Close();
                }
            }
            catch (Exception ex)
            {
                LogSettingsDiagnostic($"Save_Click error: {ex}");
                MessageBox.Show(this, $"Could not save OpenRCT Settings:\n\n{ex.Message}", "OpenRCT Settings Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private sealed class IconStyleOption
        {
            public IconStyleOption(IconStylePreference value, string label)
            {
                Value = value;
                Label = label;
            }

            public IconStylePreference Value { get; }
            public string Label { get; }
            public override string ToString() => Label;
        }
    }
}
