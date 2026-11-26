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

        public SettingsWindow(SettingsService settingsService)
        {
            InitializeComponent();
            _settingsService = settingsService;
            _settings = _settingsService.Load();

            ThemeManager.ApplyTheme(this, _settings.Theme);

            TransportCombo.ItemsSource = Enum.GetValues(typeof(RemoteTransportPreference));
            ThemeCombo.ItemsSource = Enum.GetValues(typeof(AppThemePreference));

            PsExecPathBox.Text = _settings.PsExecPath ?? string.Empty;
            PsExecSystemCheck.IsChecked = _settings.PsExecRunAsSystem;
            PsExecEulaCheck.IsChecked = _settings.PsExecAcceptEula;
            PsExecArgsBox.Text = _settings.PsExecAdditionalArgs;

            TransportCombo.SelectedItem = _settings.TransportPreference;
            TimeoutBox.Text = _settings.ConnectionTimeoutSeconds.ToString();
            ConcurrencyBox.Text = _settings.MaxConcurrentDevices.ToString();
            SmsServerBox.Text = _settings.DefaultSmsProviderServer ?? string.Empty;
            SiteCodeBox.Text = _settings.DefaultSiteCode ?? string.Empty;

            ThemeCombo.SelectedItem = _settings.Theme;
            ConfirmDestructiveCheck.IsChecked = _settings.ConfirmDestructiveActions;
            AutoClearClipboardCheck.IsChecked = _settings.AutoClearClipboardSecrets;
            ClipboardSecondsBox.Text = _settings.ClipboardClearSeconds.ToString();

            EnableUpdateChecksCheck.IsChecked = _settings.EnableUpdateChecks;
            UpdateRepoBox.Text = _settings.UpdateRepository ?? string.Empty;
            InstalledVersionText.Text = $"Installed: v{GitHubUpdateChecker.GetCurrentVersionString(typeof(SettingsWindow).Assembly)}";

            UpdatePsExecValidation();
            UpdateRepoValidation();
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

        private void PsExecPathBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
            UpdatePsExecValidation();

        private void UpdateRepoBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
            UpdateRepoValidation();

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
            _settings.PsExecPath = PsExecPathBox.Text;
            _settings.PsExecRunAsSystem = PsExecSystemCheck.IsChecked == true;
            _settings.PsExecAcceptEula = PsExecEulaCheck.IsChecked == true;
            _settings.PsExecAdditionalArgs = PsExecArgsBox.Text ?? string.Empty;

            if (TransportCombo.SelectedItem is RemoteTransportPreference tp) _settings.TransportPreference = tp;
            if (int.TryParse(TimeoutBox.Text, out var timeout)) _settings.ConnectionTimeoutSeconds = timeout;
            if (int.TryParse(ConcurrencyBox.Text, out var conc)) _settings.MaxConcurrentDevices = conc;
            _settings.DefaultSmsProviderServer = SmsServerBox.Text;
            _settings.DefaultSiteCode = SiteCodeBox.Text;

            if (ThemeCombo.SelectedItem is AppThemePreference theme) _settings.Theme = theme;
            _settings.ConfirmDestructiveActions = ConfirmDestructiveCheck.IsChecked == true;
            _settings.AutoClearClipboardSecrets = AutoClearClipboardCheck.IsChecked == true;
            if (int.TryParse(ClipboardSecondsBox.Text, out var clipSec)) _settings.ClipboardClearSeconds = clipSec;

            ApplyPendingUpdateSettings();

            _settingsService.Save(_settings);
            ThemeManager.ApplyApplicationTheme(_settings.Theme);
            DialogResult = true;
            Close();
        }
    }
}
