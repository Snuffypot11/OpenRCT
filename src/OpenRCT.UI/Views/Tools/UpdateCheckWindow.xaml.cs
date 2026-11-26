using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using OpenRCT.Core.Settings;
using OpenRCT.Core.Updates;
using OpenRCT.UI.Themes;

namespace OpenRCT.UI.Views.Tools
{
    public partial class UpdateCheckWindow : Window
    {
        private readonly SettingsService _settingsService;
        private readonly Assembly _appAssembly;
        private string? _releaseUrl;

        public UpdateCheckWindow(SettingsService settingsService, Assembly? appAssembly = null)
        {
            _settingsService = settingsService;
            _appAssembly = appAssembly ?? typeof(UpdateCheckWindow).Assembly;
            var settings = _settingsService.Load();

            ThemeManager.ApplyTheme(this, settings.Theme);
            InitializeComponent();

            Loaded += async (_, __) => await RunCheckAsync();
        }

        private async Task RunCheckAsync()
        {
            RecheckButton.IsEnabled = false;
            OpenReleaseButton.Visibility = Visibility.Collapsed;
            _releaseUrl = null;

            var settings = _settingsService.Load();
            var current = GitHubUpdateChecker.GetCurrentVersionString(_appAssembly);
            var effectiveRepo = GitHubUpdateChecker.ResolveEffectiveRepository(settings, _appAssembly);

            StatusHeadingText.Text = "Checking GitHub Releases...";
            RepoInfoText.Text = string.IsNullOrWhiteSpace(effectiveRepo)
                ? "Source repository: (not configured)"
                : $"Source repository: github.com/{effectiveRepo}";
            CurrentVersionText.Text = $"v{current}";
            LatestVersionText.Text = "...";
            PublishedDateText.Text = "...";
            ReleaseNotesBox.Text = "Querying GitHub Releases API...";

            var result = await GitHubUpdateChecker.CheckForUpdatesAsync(settings, _appAssembly);

            CurrentVersionText.Text = $"v{result.CurrentVersion}";
            LatestVersionText.Text = string.IsNullOrWhiteSpace(result.LatestVersion) ? "N/A" : result.LatestVersion;
            PublishedDateText.Text = result.PublishedAtUtc.HasValue
                ? result.PublishedAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                : "N/A";

            if (!result.IsConfigured)
            {
                StatusHeadingText.Text = "Update Source Not Configured";
                ReleaseNotesBox.Text = result.Message;
            }
            else if (!result.IsSuccess)
            {
                StatusHeadingText.Text = "Unable to Check for Updates";
                ReleaseNotesBox.Text = result.Message;
            }
            else if (result.IsUpdateAvailable)
            {
                StatusHeadingText.Text = $"Update Available: {result.LatestVersion}";
                ReleaseNotesBox.Text = string.IsNullOrWhiteSpace(result.ReleaseNotes)
                    ? result.Message
                    : $"{result.ReleaseName}\n\n{result.ReleaseNotes}";
                _releaseUrl = result.ReleaseUrl;
                OpenReleaseButton.Visibility = Visibility.Visible;
            }
            else
            {
                StatusHeadingText.Text = "You Are Up to Date";
                ReleaseNotesBox.Text = string.IsNullOrWhiteSpace(result.ReleaseNotes)
                    ? result.Message
                    : $"{result.Message}\n\nLatest Release Notes ({result.LatestVersion}):\n{result.ReleaseNotes}";
                if (!string.IsNullOrWhiteSpace(result.ReleaseUrl))
                {
                    _releaseUrl = result.ReleaseUrl;
                    OpenReleaseButton.Content = "View Releases on GitHub";
                    OpenReleaseButton.Visibility = Visibility.Visible;
                }
            }

            RecheckButton.IsEnabled = true;
        }

        private async void Recheck_Click(object sender, RoutedEventArgs e) => await RunCheckAsync();

        private void OpenRelease_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_releaseUrl)) return;
            try
            {
                Process.Start(new ProcessStartInfo { FileName = _releaseUrl, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not open browser: {ex.Message}", "OpenRCT", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            var win = new SettingsWindow(_settingsService) { Owner = this };
            if (win.ShowDialog() == true)
            {
                ThemeManager.ApplyTheme(this, _settingsService.Load().Theme);
                await RunCheckAsync();
            }
        }
    }
}
