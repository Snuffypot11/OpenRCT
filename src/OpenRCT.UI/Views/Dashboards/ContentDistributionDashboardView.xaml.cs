using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OpenRCT.Core.Analytics;
using OpenRCT.Core.Export;
using OpenRCT.Core.Interfaces;
using OpenRCT.Core.Models;
using OpenRCT.Core.Settings;
using OpenRCT.UI.Themes;

namespace OpenRCT.UI.Views.Dashboards
{
    public partial class ContentDistributionDashboardView : UserControl
    {
        private readonly ISmsProviderClient _smsClient;
        private readonly string _smsServer;
        private readonly string _siteCode;
        private readonly OpenRctSettings _settings;
        private readonly ObservableCollection<DeviceErrorEntry> _errors = new ObservableCollection<DeviceErrorEntry>();

        private ContentDistributionSnapshot _snapshot = new ContentDistributionSnapshot();
        private DistributionPointSummary? _selectedDp;

        public ContentDistributionDashboardView(
            ISmsProviderClient smsClient,
            string smsServer,
            string siteCode,
            OpenRctSettings settings,
            bool isPoppedOut = false)
        {
            ThemeManager.ApplyTheme(this, settings.Theme);
            InitializeComponent();
            _smsClient = smsClient;
            _smsServer = smsServer;
            _siteCode = siteCode;
            _settings = settings;

            ErrorDrawer.Errors = _errors;
            if (isPoppedOut) PopOutButton.Visibility = Visibility.Collapsed;

            Loaded += async (_, __) => await LoadSnapshotAsync();
        }

        private async Task LoadSnapshotAsync()
        {
            if (string.IsNullOrWhiteSpace(_smsServer) || string.IsNullOrWhiteSpace(_siteCode))
            {
                _errors.Clear();
                _errors.Add(new DeviceErrorEntry { Source = "SMS Provider", Message = "SMS Provider server / site code not configured. Set defaults in OpenRCT Settings or open from the MECM Console." });
                FilterStatusText.Text = "Not connected to SMS Provider";
                return;
            }

            RefreshButton.IsEnabled = false;
            RedistributeFailedButton.IsEnabled = false;
            RefreshButton.Content = "Loading...";
            _errors.Clear();

            try
            {
                _snapshot = await _smsClient.GetContentDistributionSnapshotAsync(_smsServer, _siteCode);
                foreach (var err in _snapshot.Errors) _errors.Add(err);

                DpChart.SetDistributionPoints(_snapshot.DistributionPoints);

                if (_selectedDp != null)
                {
                    _selectedDp = _snapshot.DistributionPoints.FirstOrDefault(d =>
                        string.Equals(d.ServerName, _selectedDp.ServerName, StringComparison.OrdinalIgnoreCase));
                }
                if (_selectedDp == null && _snapshot.DistributionPoints.Count > 0)
                {
                    UpdateDpDetailsPanel(_snapshot.DistributionPoints[0], isSelected: false);
                }
                else
                {
                    UpdateDpDetailsPanel(_selectedDp, isSelected: _selectedDp != null);
                }

                ApplyFilters();
            }
            catch (Exception ex)
            {
                _errors.Add(new DeviceErrorEntry { Source = _smsServer, Message = ex.Message, Details = ex.ToString() });
            }
            finally
            {
                RefreshButton.Content = "Refresh";
                RefreshButton.IsEnabled = true;
                RedistributeFailedButton.IsEnabled = true;
            }
        }

        private void UpdateDpDetailsPanel(DistributionPointSummary? dp, bool isSelected)
        {
            if (dp == null)
            {
                DpHeaderText.Text = "Select a Distribution Point";
                DpDescText.Text = string.Empty;
                DpHttpsText.Text = string.Empty;
                DpLedbatText.Text = string.Empty;
                DpInternetText.Text = string.Empty;
                DpMulticastText.Text = string.Empty;
                DpPeerText.Text = string.Empty;
                DpPullText.Text = string.Empty;
                DpMaintText.Text = string.Empty;
                DpPxeText.Text = string.Empty;
                DpFreeSpaceText.Text = string.Empty;
                return;
            }

            DpHeaderText.Text = isSelected ? dp.ServerName : $"{dp.ServerName} (click bar to filter)";
            DpDescText.Text = dp.Description;
            DpHttpsText.Text = dp.HttpsEnabled.ToString();
            DpLedbatText.Text = dp.LedbatEnabled.ToString();
            DpInternetText.Text = dp.InternetFacing.ToString();
            DpMulticastText.Text = dp.MulticastEnabled.ToString();
            DpPeerText.Text = dp.PeerDp.ToString();
            DpPullText.Text = dp.PullDp.ToString();
            DpMaintText.Text = dp.MaintenanceModeEnabled.ToString();
            DpPxeText.Text = dp.PxeEnabled.ToString();
            DpFreeSpaceText.Text = string.IsNullOrWhiteSpace(dp.PercentFreeSpace) ? "N/A" : dp.PercentFreeSpace;
        }

        private void DpChart_DistributionPointSelected(object? sender, DistributionPointSummary? dp)
        {
            _selectedDp = dp;
            UpdateDpDetailsPanel(dp ?? _snapshot.DistributionPoints.FirstOrDefault(), isSelected: dp != null);
            ApplyFilters();
        }

        private void Filter_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded) ApplyFilters();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (IsLoaded) ApplyFilters();
        }

        private void ApplyFilters()
        {
            var statusFilter = (StatusFilterCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All";
            var filtered = ContentDistributionAnalyzer.FilterItems(
                _snapshot.Items,
                _selectedDp?.ServerName,
                statusFilter,
                SearchBox?.Text);

            ContentGrid.ItemsSource = filtered;

            int errCount = filtered.Count(i => i.State == ContentDistributionState.Error);
            var dpLabel = _selectedDp != null ? $"DP: {_selectedDp.ServerName} | " : "All DPs | ";
            FilterStatusText.Text = $"{dpLabel}Showing {filtered.Count} of {_snapshot.Items.Count} content item(s) ({errCount} error)";
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadSnapshotAsync();

        private void ContentStatus_Click(object sender, RoutedEventArgs e)
        {
            var item = ContentGrid.SelectedItem as ContentDistributionItem;
            if (item == null)
            {
                MessageBox.Show("Select a content item to inspect its Distribution Point status.", "OpenRCT", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            new ContentStatusDetailWindow(_smsClient, _smsServer, _siteCode, item.PackageId, item.ContentName, _settings).Show();
        }

        private async void RedistributeSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = ContentGrid.SelectedItems.OfType<ContentDistributionItem>().ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Select one or more content rows to redistribute.", "OpenRCT", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            await ExecuteDpBatchAsync(
                "Redistribute Content",
                selected,
                item => _smsClient.RedistributeContentAsync(_smsServer, _siteCode, item.PackageId, item.ServerNalPath, item.DistributionPoint));
        }

        private async void ValidateSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = ContentGrid.SelectedItems.OfType<ContentDistributionItem>().ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Select one or more content rows to validate.", "OpenRCT", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            await ExecuteDpBatchAsync(
                "Validate Content",
                selected,
                item => _smsClient.ValidateContentAsync(_smsServer, _siteCode, item.PackageId, item.ServerNalPath, item.DistributionPoint));
        }

        private async void RemoveSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = ContentGrid.SelectedItems.OfType<ContentDistributionItem>().ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("Select one or more content rows to remove from their Distribution Point.", "OpenRCT", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_settings.ConfirmDestructiveActions)
            {
                var confirm = MessageBox.Show(
                    $"Remove {selected.Count} selected content assignment(s) from their Distribution Point(s)?",
                    "OpenRCT — Confirm Content Removal",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (confirm != MessageBoxResult.Yes) return;
            }

            await ExecuteDpBatchAsync(
                "Remove Content from DP",
                selected,
                item => _smsClient.RemoveContentFromDpAsync(_smsServer, _siteCode, item.PackageId, item.ServerNalPath, item.DistributionPoint));

            await LoadSnapshotAsync();
        }

        private async void RedistributeFailed_Click(object sender, RoutedEventArgs e)
        {
            var scopeItems = _selectedDp != null
                ? _snapshot.Items.Where(i => string.Equals(i.DistributionPoint, _selectedDp.ServerName, StringComparison.OrdinalIgnoreCase))
                : _snapshot.Items;

            var failed = scopeItems.Where(i => i.State == ContentDistributionState.Error).ToList();
            if (failed.Count == 0)
            {
                MessageBox.Show("No failed content transfers were found in the current scope.", "OpenRCT", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var scopeDesc = _selectedDp != null ? $"on {_selectedDp.ServerName}" : "across all Distribution Points";
            if (_settings.ConfirmDestructiveActions)
            {
                var confirm = MessageBox.Show(
                    $"Trigger redistribution for {failed.Count} failed content transfer(s) {scopeDesc}?",
                    "OpenRCT — Redistribute Failed Content",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (confirm != MessageBoxResult.Yes) return;
            }

            await ExecuteDpBatchAsync(
                "Redistribute Failed Content",
                failed,
                item => _smsClient.RedistributeContentAsync(_smsServer, _siteCode, item.PackageId, item.ServerNalPath, item.DistributionPoint));
        }

        private async Task ExecuteDpBatchAsync(
            string actionTitle,
            List<ContentDistributionItem> items,
            Func<ContentDistributionItem, Task<ActionExecutionResult>> operation)
        {
            RefreshButton.IsEnabled = false;
            RedistributeFailedButton.IsEnabled = false;
            int succeeded = 0;
            int failed = 0;

            foreach (var item in items)
            {
                FilterStatusText.Text = $"{actionTitle}: {item.ContentName} ({item.PackageId}) on {item.DistributionPoint}...";
                var res = await operation(item);
                if (res.IsSuccess)
                {
                    succeeded++;
                }
                else
                {
                    failed++;
                    _errors.Add(new DeviceErrorEntry
                    {
                        Source = $"{item.PackageId} @ {item.DistributionPoint}",
                        Message = res.Message,
                        Details = res.ErrorDetails
                    });
                }
            }

            RefreshButton.IsEnabled = true;
            RedistributeFailedButton.IsEnabled = true;
            FilterStatusText.Text = $"{actionTitle} completed: {succeeded} succeeded, {failed} failed.";
        }

        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var rows = (ContentGrid.ItemsSource as IEnumerable<ContentDistributionItem>)?.ToList() ?? _snapshot.Items;
            var dlg = new SaveFileDialog { Filter = "CSV Files (*.csv)|*.csv", FileName = $"OpenRCT_ContentDistribution_{DateTime.Now:yyyyMMdd_HHmm}.csv" };
            if (dlg.ShowDialog() == true)
            {
                CsvExporter.ExportToFile(dlg.FileName, rows, new (string, Func<ContentDistributionItem, object?>)[]
                {
                    ("Content Name", r => r.ContentName),
                    ("Content Type", r => r.ContentType),
                    ("Distribution Point", r => r.DistributionPoint),
                    ("Source Version", r => r.SourceVersion),
                    ("Package ID", r => r.PackageId),
                    ("Object ID", r => r.ObjectId),
                    ("Source Size (MB)", r => r.SourceSizeMb),
                    ("Distribution Status", r => r.DistributionStatus),
                    ("Last Update Date", r => r.LastUpdateDate),
                    ("Description", r => r.Description)
                });
            }
        }

        private void PopOut_Click(object sender, RoutedEventArgs e)
        {
            var popped = new ContentDistributionDashboardView(_smsClient, _smsServer, _siteCode, _settings, isPoppedOut: true);
            new DashboardHostWindow("Content Distribution Monitor", popped, _settings).Show();
        }
    }
}
