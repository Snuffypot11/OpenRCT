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
    public partial class HardwareFirmwareAuditDashboardView : UserControl
    {
        private readonly ISmsProviderClient _smsClient;
        private readonly string _smsServer;
        private readonly string _siteCode;
        private readonly OpenRctSettings _settings;
        private readonly ObservableCollection<DeviceErrorEntry> _errors = new ObservableCollection<DeviceErrorEntry>();

        private List<HardwareFirmwareAuditRow> _allRows = new List<HardwareFirmwareAuditRow>();
        private List<HardwareAuditDimension> _selectedDimensions = new List<HardwareAuditDimension>
        {
            HardwareAuditDimension.Chassis,
            HardwareAuditDimension.Manufacturer,
            HardwareAuditDimension.Model
        };
        private readonly Dictionary<HardwareAuditDimension, string> _activeFilters = new Dictionary<HardwareAuditDimension, string>();
        private readonly Dictionary<HardwareAuditDimension, InteractivePieChart> _chartControls = new Dictionary<HardwareAuditDimension, InteractivePieChart>();

        public HardwareFirmwareAuditDashboardView(
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

            CollectionDropdown.PlaceholderText = "Select ConfigMgr Collection(s) (blank = all devices)";
            RebuildChartStrip();

            Loaded += async (_, __) => await LoadCollectionsAsync();
        }

        private async Task LoadCollectionsAsync()
        {
            if (string.IsNullOrWhiteSpace(_smsServer) || string.IsNullOrWhiteSpace(_siteCode))
            {
                _errors.Add(new DeviceErrorEntry { Source = "SMS Provider", Message = "SMS Provider server / site code not configured." });
                return;
            }

            try
            {
                var cols = await _smsClient.GetDeviceCollectionsAsync(_smsServer, _siteCode);
                CollectionDropdown.SetItems(cols.Select(c => new MultiSelectOption(c.CollectionId, $"{c.Name} ({c.CollectionId})")));
            }
            catch (Exception ex)
            {
                _errors.Add(new DeviceErrorEntry { Source = _smsServer, Message = ex.Message });
            }
        }

        private async void Scan_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_smsServer) || string.IsNullOrWhiteSpace(_siteCode))
            {
                _errors.Add(new DeviceErrorEntry { Source = "SMS Provider", Message = "SMS Provider server / site code not configured." });
                return;
            }

            ScanButton.IsEnabled = false;
            ScanButton.Content = "Scanning...";
            _errors.Clear();
            _activeFilters.Clear();

            try
            {
                var selectedCols = CollectionDropdown.GetSelectedIds();
                var rows = await _smsClient.GetHardwareFirmwareInventoryAsync(_smsServer, _siteCode, selectedCols);
                _allRows = rows.ToList();
                RefreshChartsAndGrid();
            }
            catch (Exception ex)
            {
                _errors.Add(new DeviceErrorEntry { Source = _smsServer, Message = ex.Message, Details = ex.ToString() });
            }
            finally
            {
                ScanButton.Content = "Scan";
                ScanButton.IsEnabled = true;
            }
        }

        private void SelectCharts_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SelectChartsDialog(_selectedDimensions, _settings);
            var owner = Window.GetWindow(this);
            if (owner != null) dlg.Owner = owner;

            if (dlg.ShowDialog() == true)
            {
                _selectedDimensions = dlg.SelectedDimensions;
                foreach (var key in _activeFilters.Keys.Where(k => !_selectedDimensions.Contains(k)).ToList())
                {
                    _activeFilters.Remove(key);
                }
                RebuildChartStrip();
                RefreshChartsAndGrid();
            }
        }

        private void NestedFiltering_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            if (NestedFilteringCheck.IsChecked != true && _activeFilters.Count > 1)
            {
                var last = _activeFilters.Last();
                _activeFilters.Clear();
                _activeFilters[last.Key] = last.Value;
            }
            RefreshChartsAndGrid();
        }

        private void ClearFilters_Click(object sender, RoutedEventArgs e)
        {
            _activeFilters.Clear();
            RefreshChartsAndGrid();
        }

        private void RebuildChartStrip()
        {
            ChartsStripPanel.Children.Clear();
            _chartControls.Clear();

            for (int i = 0; i < _selectedDimensions.Count; i++)
            {
                var dim = _selectedDimensions[i];
                var chart = new InteractivePieChart
                {
                    ChartTitle = HardwareFirmwareAnalyzer.GetDimensionDisplayName(dim),
                    Width = 390
                };
                chart.SliceClicked += (_, slice) => OnChartSliceClicked(dim, slice);

                var border = new Border
                {
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(8),
                    Margin = new Thickness(0, 0, i < _selectedDimensions.Count - 1 ? 10 : 0, 0),
                    Child = chart
                };
                border.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
                border.SetResourceReference(Border.BorderBrushProperty, "BorderBrushSubtle");

                _chartControls[dim] = chart;
                ChartsStripPanel.Children.Add(border);
            }
        }

        private void OnChartSliceClicked(HardwareAuditDimension dim, PieSliceData? slice)
        {
            bool nested = NestedFilteringCheck.IsChecked == true;
            if (!nested)
            {
                _activeFilters.Clear();
            }

            if (slice == null)
            {
                _activeFilters.Remove(dim);
            }
            else
            {
                _activeFilters[dim] = slice.Key;
            }

            RefreshChartsAndGrid();
        }

        private void RefreshChartsAndGrid()
        {
            bool nested = NestedFilteringCheck.IsChecked == true;
            var filteredRows = HardwareFirmwareAnalyzer.ApplyFilters(_allRows, _activeFilters);

            foreach (var kv in _chartControls)
            {
                var dim = kv.Key;
                var chart = kv.Value;

                // When nested filtering is enabled, each chart reflects rows filtered by all OTHER active filters
                List<HardwareFirmwareAuditRow> sourceForChart;
                if (nested && _activeFilters.Count > 0)
                {
                    var otherFilters = _activeFilters
                        .Where(f => f.Key != dim)
                        .ToDictionary(f => f.Key, f => f.Value);
                    sourceForChart = HardwareFirmwareAnalyzer.ApplyFilters(_allRows, otherFilters);
                }
                else
                {
                    sourceForChart = _allRows;
                }

                chart.SelectedSliceKey = _activeFilters.TryGetValue(dim, out var sel) ? sel : null;
                chart.Slices = HardwareFirmwareAnalyzer.BuildSlices(sourceForChart, dim);
            }

            ResultsGrid.ItemsSource = filteredRows;
            ClearFiltersButton.Visibility = _activeFilters.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            if (_activeFilters.Count == 0)
            {
                FilterStatusText.Text = $"Showing all {filteredRows.Count} device(s)";
            }
            else
            {
                var summary = string.Join(" AND ", _activeFilters.Select(kv => $"{HardwareFirmwareAnalyzer.GetDimensionDisplayName(kv.Key)}={kv.Value}"));
                FilterStatusText.Text = $"Filtered ({summary}): {filteredRows.Count} of {_allRows.Count} device(s)";
            }
        }

        private TargetDevice[] GetSelectedDevices() =>
            ResultsGrid.SelectedItems.OfType<HardwareFirmwareAuditRow>()
                .Select(r => new TargetDevice
                {
                    Name = r.Name,
                    ResourceId = r.ResourceId,
                    SmsProviderServer = _smsServer,
                    SiteCode = _siteCode
                }).ToArray();

        private void DispatchSelected(string actionKey)
        {
            var targets = GetSelectedDevices();
            if (targets.Length == 0) return;
            ToolDispatcher.Dispatch(new ToolInvocationContext
            {
                ActionKey = actionKey,
                SmsProviderServer = _smsServer,
                SiteCode = _siteCode,
                Devices = targets
            });
        }

        private void SystemInfo_Click(object sender, RoutedEventArgs e) => DispatchSelected(ToolActionKeys.SystemInformation);
        private void ClientInfo_Click(object sender, RoutedEventArgs e) => DispatchSelected(ToolActionKeys.ClientInformation);
        private void HardwareInventoryCycle_Click(object sender, RoutedEventArgs e) => DispatchSelected("ClientAction.HardwareInventoryCycle");
        private void PingSystems_Click(object sender, RoutedEventArgs e) => DispatchSelected(ToolActionKeys.PingSystems);

        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var rows = (ResultsGrid.ItemsSource as IEnumerable<HardwareFirmwareAuditRow>)?.ToList() ?? _allRows;
            var dlg = new SaveFileDialog { Filter = "CSV Files (*.csv)|*.csv", FileName = $"OpenRCT_HardwareFirmwareAudit_{DateTime.Now:yyyyMMdd_HHmm}.csv" };
            if (dlg.ShowDialog() == true)
            {
                CsvExporter.ExportToFile(dlg.FileName, rows, new (string, Func<HardwareFirmwareAuditRow, object?>)[]
                {
                    ("Name", r => r.Name),
                    ("Distinguished Name", r => r.DistinguishedName),
                    ("Manufacturer", r => r.Manufacturer),
                    ("Model", r => r.Model),
                    ("Chassis Type", r => r.ChassisType),
                    ("UEFI Status", r => r.UefiStatus),
                    ("SecureBoot Status", r => r.SecureBootStatus),
                    ("TPM Status", r => r.TpmStatus),
                    ("BIOS Version", r => r.BiosVersion),
                    ("BIOS Release Date", r => r.BiosReleaseDate),
                    ("OS", r => r.OperatingSystem),
                    ("OS Version", r => r.OsVersion)
                });
            }
        }

        private void PopOut_Click(object sender, RoutedEventArgs e)
        {
            var popped = new HardwareFirmwareAuditDashboardView(_smsClient, _smsServer, _siteCode, _settings, isPoppedOut: true);
            new DashboardHostWindow("Hardware and Firmware Audit", popped, _settings).Show();
        }
    }
}
