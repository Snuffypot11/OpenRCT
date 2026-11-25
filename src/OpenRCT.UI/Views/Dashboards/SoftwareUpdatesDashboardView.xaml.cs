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
using OpenRCT.UI.Controls;
using OpenRCT.UI.Themes;

namespace OpenRCT.UI.Views.Dashboards
{
    public partial class SoftwareUpdatesDashboardView : UserControl
    {
        private readonly ISmsProviderClient _smsClient;
        private readonly string _smsServer;
        private readonly string _siteCode;
        private readonly OpenRctSettings _settings;
        private readonly ObservableCollection<DeviceErrorEntry> _errors = new ObservableCollection<DeviceErrorEntry>();

        private IReadOnlyList<TargetDevice> _collectionMembers = Array.Empty<TargetDevice>();
        private IReadOnlyList<RawDeviceUpdateRecord> _rawUpdateRecords = Array.Empty<RawDeviceUpdateRecord>();
        private List<DeviceUpdateComplianceRow> _currentRows = new List<DeviceUpdateComplianceRow>();
        private string? _complianceSliceFilter;
        private string? _categorySliceFilter;

        public SoftwareUpdatesDashboardView(
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

            SugDropdown.PlaceholderText = "All Software Update Groups";
            SugDropdown.SelectionChanged += (_, __) => RecomputeAnalysis();

            Loaded += async (_, __) => await LoadSelectorsAsync();
        }

        private async Task LoadSelectorsAsync()
        {
            if (string.IsNullOrWhiteSpace(_smsServer) || string.IsNullOrWhiteSpace(_siteCode))
            {
                _errors.Add(new DeviceErrorEntry { Source = "ConfigMgr", Message = "SMS Provider Server/Site Code not configured." });
                return;
            }

            try
            {
                var cols = await _smsClient.GetDeviceCollectionsAsync(_smsServer, _siteCode);
                CollectionCombo.ItemsSource = cols;
                if (cols.Count > 0) CollectionCombo.SelectedIndex = 0;

                var sugs = await _smsClient.GetSoftwareUpdateGroupsAsync(_smsServer, _siteCode);
                SugDropdown.SetItems(sugs.Select(s => new SelectableItem { Key = s.CiUniqueId, DisplayText = s.Title, Tag = s }));
            }
            catch (Exception ex)
            {
                _errors.Add(new DeviceErrorEntry { Source = "ConfigMgr", Message = ex.Message, Details = ex.ToString() });
            }
        }

        private async void Start_Click(object sender, RoutedEventArgs e)
        {
            if (!(CollectionCombo.SelectedItem is SmsCollectionSummary selectedCol))
            {
                MessageBox.Show("Select a Device Collection first.", "OpenRCT", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            StartButton.IsEnabled = false;
            FilterStatusText.Text = $"Querying update compliance for '{selectedCol.Name}'...";
            _errors.Clear();

            try
            {
                _collectionMembers = await _smsClient.GetCollectionMembersAsync(_smsServer, _siteCode, selectedCol.CollectionId);
                _rawUpdateRecords = await _smsClient.GetUpdateComplianceForCollectionAsync(_smsServer, _siteCode, selectedCol.CollectionId);
                _complianceSliceFilter = null;
                _categorySliceFilter = null;
                CompliancePieChart.SelectedSliceKey = null;
                CategoryPieChart.SelectedSliceKey = null;
                RecomputeAnalysis();
            }
            catch (Exception ex)
            {
                _errors.Add(new DeviceErrorEntry { Source = selectedCol.Name, Message = ex.Message, Details = ex.ToString() });
            }
            finally
            {
                StartButton.IsEnabled = true;
            }
        }

        private void AgeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (AgeLabel != null)
            {
                AgeLabel.Text = $"Updates older than {(int)AgeSlider.Value} days:";
            }
            if (IsLoaded && _collectionMembers.Count > 0) RecomputeAnalysis();
        }

        private void FilterControlChanged(object sender, RoutedEventArgs e)
        {
            if (IsLoaded && _collectionMembers.Count > 0) RecomputeAnalysis();
        }

        private void RecomputeAnalysis()
        {
            var filter = new SoftwareUpdatesFilter
            {
                LimitToDeployedUpdates = LimitDeployedCheck.IsChecked == true,
                SelectedUpdateGroupIds = new HashSet<string>(SugDropdown.GetSelectedKeys(), StringComparer.OrdinalIgnoreCase),
                UpdatesOlderThanDays = (int)AgeSlider.Value,
                IncludeCriticalUpdates = ChkCritical.IsChecked == true,
                IncludeDefinitionUpdates = ChkDefinition.IsChecked == true,
                IncludeOtherClassifications = ChkOther.IsChecked == true,
                IncludeSecurityUpdates = ChkSecurity.IsChecked == true,
                IncludeUpdateRollups = ChkRollups.IsChecked == true,
                IncludeUpdates = ChkUpdates.IsChecked == true
            };

            var analysis = SoftwareUpdatesAnalyzer.Analyze(_collectionMembers, _rawUpdateRecords, filter, errors: _errors);
            _currentRows = analysis.DeviceRows;
            CompliancePieChart.Slices = analysis.ComplianceSlices;
            CategoryPieChart.Slices = analysis.MissingByCategorySlices;
            ApplySliceFilters();
        }

        private void CompliancePieChart_SliceClicked(object? sender, PieSliceData? slice)
        {
            _complianceSliceFilter = slice?.Key;
            ApplySliceFilters();
        }

        private void CategoryPieChart_SliceClicked(object? sender, PieSliceData? slice)
        {
            _categorySliceFilter = slice?.Key;
            ApplySliceFilters();
        }

        private void ApplySliceFilters()
        {
            IEnumerable<DeviceUpdateComplianceRow> query = _currentRows;
            if (!string.IsNullOrEmpty(_complianceSliceFilter))
            {
                query = query.Where(r => string.Equals(r.ComplianceState.ToString(), _complianceSliceFilter, StringComparison.OrdinalIgnoreCase));
            }
            if (!string.IsNullOrEmpty(_categorySliceFilter))
            {
                query = query.Where(r => r.MissingUpdates.Any(m =>
                    string.Equals(SoftwareUpdatesAnalyzer.MapCategoryBucket(m.Classification), _categorySliceFilter, StringComparison.OrdinalIgnoreCase)));
            }

            var list = query.ToList();
            ResultsGrid.ItemsSource = list;
            FilterStatusText.Text = $"Showing {list.Count} of {_currentRows.Count} device(s)";
        }

        private TargetDevice[] GetSelectedTargets() =>
            ResultsGrid.SelectedItems.OfType<DeviceUpdateComplianceRow>().Select(r => new TargetDevice
            {
                Name = r.DeviceName,
                ResourceId = r.ResourceId,
                SiteCode = _siteCode,
                SmsProviderServer = _smsServer
            }).ToArray();

        private void TriggerScan_Click(object sender, RoutedEventArgs e)
        {
            var targets = GetSelectedTargets();
            if (targets.Length == 0) return;
            ToolDispatcher.Dispatch(new ToolInvocationContext { ActionKey = ClientActionRegistry.SoftwareUpdatesScan, SmsProviderServer = _smsServer, SiteCode = _siteCode, Devices = targets });
        }

        private void TriggerEval_Click(object sender, RoutedEventArgs e)
        {
            var targets = GetSelectedTargets();
            if (targets.Length == 0) return;
            ToolDispatcher.Dispatch(new ToolInvocationContext { ActionKey = ClientActionRegistry.SoftwareUpdatesDeploymentEval, SmsProviderServer = _smsServer, SiteCode = _siteCode, Devices = targets });
        }

        private void OpenSoftwareCenter_Click(object sender, RoutedEventArgs e)
        {
            var targets = GetSelectedTargets();
            if (targets.Length == 0) return;
            ToolDispatcher.Dispatch(new ToolInvocationContext { ActionKey = ToolActionKeys.RemoteSoftwareCenter, SmsProviderServer = _smsServer, SiteCode = _siteCode, Devices = targets });
        }

        private void PopOut_Click(object sender, RoutedEventArgs e)
        {
            var host = new DashboardHostWindow("Software Updates Deployment Status",
                new SoftwareUpdatesDashboardView(_smsClient, _smsServer, _siteCode, _settings, isPoppedOut: true),
                _settings);
            host.Show();
        }

        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var rows = ResultsGrid.ItemsSource as IEnumerable<DeviceUpdateComplianceRow> ?? _currentRows;
            var dlg = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = $"OpenRCT_SoftwareUpdates_{DateTime.Now:yyyyMMdd_HHmm}.csv" };
            if (dlg.ShowDialog() != true) return;

            CsvExporter.WriteToFile(dlg.FileName, rows, new[]
            {
                new CsvColumn<DeviceUpdateComplianceRow>("Device Name", r => r.DeviceName),
                new CsvColumn<DeviceUpdateComplianceRow>("Compliance Status", r => r.ComplianceLabel),
                new CsvColumn<DeviceUpdateComplianceRow>("Missing Updates", r => r.MissingCount),
                new CsvColumn<DeviceUpdateComplianceRow>("Installed Updates", r => r.InstalledCount),
                new CsvColumn<DeviceUpdateComplianceRow>("Unknown", r => r.UnknownCount),
                new CsvColumn<DeviceUpdateComplianceRow>("Missing Categories", r => r.MissingCategories)
            });
        }
    }
}
