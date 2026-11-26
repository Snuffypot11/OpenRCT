using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using OpenRCT.Core.Interfaces;
using OpenRCT.Core.Models;
using OpenRCT.Core.Settings;
using OpenRCT.UI.Themes;

namespace OpenRCT.UI.Views.Dashboards
{
    public partial class ContentStatusDetailWindow : Window
    {
        private readonly ISmsProviderClient _smsClient;
        private readonly string _smsServer;
        private readonly string _siteCode;
        private readonly string _packageId;
        private readonly string _contentName;

        public ContentStatusDetailWindow(
            ISmsProviderClient smsClient,
            string smsServer,
            string siteCode,
            string packageId,
            string contentName,
            OpenRctSettings settings)
        {
            ThemeManager.ApplyTheme(this, settings.Theme);
            InitializeComponent();

            _smsClient = smsClient;
            _smsServer = smsServer;
            _siteCode = siteCode;
            _packageId = packageId;
            _contentName = contentName;

            Title = $"Content Status — {contentName} ({packageId})";
            HeaderTitleText.Text = string.IsNullOrWhiteSpace(contentName) ? packageId : contentName;
            HeaderSubtitleText.Text = $"Package ID: {packageId}  |  Site: {siteCode} ({smsServer})";

            Loaded += async (_, __) => await LoadAsync();
        }

        private async Task LoadAsync()
        {
            RefreshButton.IsEnabled = false;
            ActionStatusText.Text = "Loading distribution point status...";
            try
            {
                var details = await _smsClient.GetContentStatusDetailsAsync(_smsServer, _siteCode, _packageId);
                StatusGrid.ItemsSource = details;

                int ok = details.Count(d => d.State == ContentDistributionState.Success);
                int prog = details.Count(d => d.State == ContentDistributionState.InProgress);
                int err = details.Count(d => d.State == ContentDistributionState.Error);
                ActionStatusText.Text = $"Targeted DPs: {details.Count}  |  Success: {ok}  |  In Progress: {prog}  |  Error: {err}";
            }
            catch (Exception ex)
            {
                ActionStatusText.Text = $"Failed to query content status: {ex.Message}";
            }
            finally
            {
                RefreshButton.IsEnabled = true;
            }
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

        private async void RedistributeSelected_Click(object sender, RoutedEventArgs e)
        {
            await RunSelectedActionAsync("Redistribute", (d) =>
                _smsClient.RedistributeContentAsync(_smsServer, _siteCode, d.PackageId, d.ServerNalPath, d.DistributionPoint));
        }

        private async void ValidateSelected_Click(object sender, RoutedEventArgs e)
        {
            await RunSelectedActionAsync("Validate", (d) =>
                _smsClient.ValidateContentAsync(_smsServer, _siteCode, d.PackageId, d.ServerNalPath, d.DistributionPoint));
        }

        private async Task RunSelectedActionAsync(string label, Func<ContentDpStatusDetail, Task<ActionExecutionResult>> op)
        {
            var selected = StatusGrid.SelectedItems.OfType<ContentDpStatusDetail>().ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show(this, "Select one or more Distribution Point rows first.", "OpenRCT", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            RedistributeButton.IsEnabled = false;
            ValidateButton.IsEnabled = false;
            int succeeded = 0;
            var failures = new List<string>();

            foreach (var dp in selected)
            {
                ActionStatusText.Text = $"{label}: {dp.DistributionPoint}...";
                var res = await op(dp);
                if (res.IsSuccess) succeeded++;
                else failures.Add($"{dp.DistributionPoint}: {res.Message}");
            }

            ActionStatusText.Text = failures.Count == 0
                ? $"{label} triggered on {succeeded} distribution point(s)."
                : $"{label}: {succeeded} succeeded, {failures.Count} failed ({failures[0]}).";

            RedistributeButton.IsEnabled = true;
            ValidateButton.IsEnabled = true;
        }
    }
}
