using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OpenCMTools.Core.Analytics;
using OpenCMTools.Core.Export;
using OpenCMTools.Core.Interfaces;
using OpenCMTools.Core.Models;
using OpenCMTools.Core.Settings;
using OpenCMTools.UI.Controls;
using OpenCMTools.UI.Themes;

namespace OpenCMTools.UI.Views.Dashboards
{
    public partial class SoftwareUpdatesDashboardView : UserControl
    {
        private readonly ISmsProviderClient _smsClient;
        private readonly string _smsServer;
        private readonly string _siteCode;
        private readonly OpenCMToolsSettings _settings;
        private readonly ObservableCollection<DeviceErrorEntry> _errors = new ObservableCollection<DeviceErrorEntry>();

        private IReadOnlyList<TargetDevice> _collectionMembers = Array.Empty<TargetDevice>();
        private IReadOnlyList<RawDeviceUpdateRecord> _rawUpdateRecords = Array.Empty<RawDeviceUpdateRecord>();
        private List<DeviceUpdateComplianceRow> _currentRows = new List<DeviceUpdateComplianceRow>();
        private string? _complianceSliceFilter;
        private string? _categorySliceFilter;
        private System.Threading.CancellationTokenSource? _scanCts;
        private bool _lastQueryWasDeployedOnly;
        private HashSet<string> _lastQuerySugIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public SoftwareUpdatesDashboardView(
            ISmsProviderClient smsClient,
            string smsServer,
            string siteCode,
            OpenCMToolsSettings settings,
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
            SugDropdown.SelectionChanged += (_, __) => HandleScopeOrFilterChanged();

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
                MessageBox.Show("Select a Device Collection first.", "OpenCMTools", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _scanCts?.Cancel();
            _scanCts?.Dispose();
            _scanCts = new System.Threading.CancellationTokenSource();
            var ct = _scanCts.Token;

            StartButton.IsEnabled = false;
            CancelButton.Visibility = Visibility.Visible;
            FilterStatusText.Text = $"Querying MECM update compliance for '{selectedCol.Name}'...";
            _errors.Clear();

            var deployedOnly = LimitDeployedCheck.IsChecked == true;
            var selectedSugs = SugDropdown.GetSelectedKeys().ToList();
            var progress = new Progress<string>(msg => FilterStatusText.Text = msg);

            try
            {
                _collectionMembers = await _smsClient.GetCollectionMembersAsync(_smsServer, _siteCode, selectedCol.CollectionId, ct);
                _rawUpdateRecords = await _smsClient.GetUpdateComplianceForCollectionAsync(
                    _smsServer,
                    _siteCode,
                    selectedCol.CollectionId,
                    limitToDeployedUpdates: deployedOnly,
                    selectedUpdateGroupIds: selectedSugs,
                    progress: progress,
                    ct: ct);

                _lastQueryWasDeployedOnly = deployedOnly;
                _lastQuerySugIds = new HashSet<string>(selectedSugs, StringComparer.OrdinalIgnoreCase);
                _complianceSliceFilter = null;
                _categorySliceFilter = null;
                CompliancePieChart.SelectedSliceKey = null;
                CategoryPieChart.SelectedSliceKey = null;
                RecomputeAnalysis();
            }
            catch (OperationCanceledException)
            {
                FilterStatusText.Text = "Query cancelled.";
            }
            catch (Exception ex)
            {
                FilterStatusText.Text = $"Query failed: {ex.Message}";
                _errors.Add(new DeviceErrorEntry { Source = selectedCol.Name, Message = ex.Message, Details = ex.ToString() });
            }
            finally
            {
                StartButton.IsEnabled = true;
                CancelButton.Visibility = Visibility.Collapsed;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _scanCts?.Cancel();
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
            HandleScopeOrFilterChanged();
        }

        private void HandleScopeOrFilterChanged()
        {
            if (!IsLoaded || _collectionMembers.Count == 0) return;
            RecomputeAnalysis();

            var nowDeployedOnly = LimitDeployedCheck.IsChecked == true;
            var nowSugs = new HashSet<string>(SugDropdown.GetSelectedKeys(), StringComparer.OrdinalIgnoreCase);
            bool broadenedDeployed = _lastQueryWasDeployedOnly && !nowDeployedOnly;
            bool broadenedSug = _lastQuerySugIds.Count > 0 && (nowSugs.Count == 0 || nowSugs.Any(s => !_lastQuerySugIds.Contains(s)));
            if (broadenedDeployed || broadenedSug)
            {
                FilterStatusText.Text += " — Click Start to load broadened MECM scope";
            }
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
            var dlg = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = $"OpenCMTools_SoftwareUpdates_{DateTime.Now:yyyyMMdd_HHmm}.csv" };
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
