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
    public partial class BitLockerComplianceDashboardView : UserControl
    {
        private readonly IActiveDirectoryClient _adClient;
        private readonly ISmsProviderClient _smsClient;
        private readonly string _smsServer;
        private readonly string _siteCode;
        private readonly OpenRctSettings _settings;
        private readonly ObservableCollection<DeviceErrorEntry> _errors = new ObservableCollection<DeviceErrorEntry>();

        private List<BitLockerComplianceRow> _allRows = new List<BitLockerComplianceRow>();
        private string? _selectedKeyBucket;
        private string? _selectedCompliance;

        public BitLockerComplianceDashboardView(
            IActiveDirectoryClient adClient,
            ISmsProviderClient smsClient,
            string smsServer,
            string siteCode,
            OpenRctSettings settings,
            bool isPoppedOut = false)
        {
            ThemeManager.ApplyTheme(this, settings.Theme);
            InitializeComponent();
            _adClient = adClient;
            _smsClient = smsClient;
            _smsServer = smsServer;
            _siteCode = siteCode;
            _settings = settings;

            ErrorDrawer.Errors = _errors;
            if (isPoppedOut) PopOutButton.Visibility = Visibility.Collapsed;

            OuDropdown.PlaceholderText = "Select Organizational Unit(s) (blank = entire domain)";
            CollectionDropdown.PlaceholderText = "Select ConfigMgr Collection(s) (blank = all devices)";

            Loaded += async (_, __) => await LoadSelectorsAsync();
        }

        private async Task LoadSelectorsAsync()
        {
            try
            {
                var ous = await _adClient.GetOrganizationalUnitsAsync();
                OuDropdown.SetItems(ous.Select(o => new SelectableItem { Key = o.DistinguishedName, DisplayText = o.CanonicalPath }));
            }
            catch (Exception ex)
            {
                _errors.Add(new DeviceErrorEntry { Source = "Active Directory (OUs)", Message = ex.Message });
            }

            if (!string.IsNullOrWhiteSpace(_smsServer) && !string.IsNullOrWhiteSpace(_siteCode))
            {
                try
                {
                    var cols = await _smsClient.GetDeviceCollectionsAsync(_smsServer, _siteCode);
                    CollectionDropdown.SetItems(cols.Select(c => new SelectableItem { Key = c.CollectionId, DisplayText = $"{c.Name} ({c.CollectionId})" }));
                }
                catch (Exception ex)
                {
                    _errors.Add(new DeviceErrorEntry { Source = _smsServer, Message = ex.Message });
                }
            }
        }

        private void SearchMode_Changed(object sender, RoutedEventArgs e)
        {
            if (OuDropdown == null || CollectionDropdown == null) return;
            bool byOu = SearchByOuRadio.IsChecked == true;
            OuDropdown.IsEnabled = byOu;
            CollectionDropdown.IsEnabled = !byOu;
        }

        private async void Scan_Click(object sender, RoutedEventArgs e)
        {
            ScanButton.IsEnabled = false;
            ScanButton.Content = "Scanning...";
            _errors.Clear();
            _selectedKeyBucket = null;
            _selectedCompliance = null;
            KeyStorageChart.SelectedSliceKey = null;
            ComplianceChart.SelectedSliceKey = null;

            bool byCollection = SearchByCollectionRadio.IsChecked == true;
            var selectedOus = byCollection ? Array.Empty<string>() : OuDropdown.GetSelectedKeys();
            var selectedCols = byCollection ? CollectionDropdown.GetSelectedKeys() : Array.Empty<string>();

            IReadOnlyList<AdComputerRecord> adComputers = Array.Empty<AdComputerRecord>();
            HashSet<string> adKeyHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IReadOnlyList<ConfigMgrBitLockerRecord> cmRecords = Array.Empty<ConfigMgrBitLockerRecord>();

            try
            {
                adComputers = await _adClient.GetComputersInOusAsync(selectedOus);
            }
            catch (Exception ex)
            {
                _errors.Add(new DeviceErrorEntry { Source = "Active Directory (Computers)", Message = ex.Message });
            }

            try
            {
                adKeyHosts = await _adClient.GetComputersWithBitLockerKeysAsync(selectedOus);
            }
            catch (Exception ex)
            {
                _errors.Add(new DeviceErrorEntry { Source = "Active Directory (BitLocker Keys)", Message = ex.Message });
            }

            if (!string.IsNullOrWhiteSpace(_smsServer) && !string.IsNullOrWhiteSpace(_siteCode))
            {
                try
                {
                    cmRecords = await _smsClient.GetBitLockerComplianceRecordsAsync(_smsServer, _siteCode, selectedCols);
                }
                catch (Exception ex)
                {
                    _errors.Add(new DeviceErrorEntry { Source = _smsServer, Message = ex.Message });
                }
            }
            else if (byCollection)
            {
                _errors.Add(new DeviceErrorEntry { Source = "SMS Provider", Message = "SMS Provider server / site code not configured." });
            }

            var result = BitLockerComplianceAnalyzer.Analyze(adComputers, adKeyHosts, cmRecords, primarySourceIsCollection: byCollection);
            _allRows = result.Rows;
            KeyStorageChart.Slices = result.KeyStorageSlices;
            ComplianceChart.Slices = result.UnifiedComplianceSlices;
            ApplyFilter();

            ScanButton.Content = "Scan";
            ScanButton.IsEnabled = true;
        }

        private void KeyStorageChart_SliceClicked(object? sender, PieSliceData? slice)
        {
            _selectedKeyBucket = slice?.Key;
            ApplyFilter();
        }

        private void ComplianceChart_SliceClicked(object? sender, PieSliceData? slice)
        {
            _selectedCompliance = slice?.Key;
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            IEnumerable<BitLockerComplianceRow> filtered = _allRows;
            if (!string.IsNullOrWhiteSpace(_selectedKeyBucket))
            {
                filtered = filtered.Where(r => string.Equals(r.KeyStorageBucket.ToString(), _selectedKeyBucket, StringComparison.OrdinalIgnoreCase));
            }
            if (!string.IsNullOrWhiteSpace(_selectedCompliance))
            {
                filtered = filtered.Where(r => string.Equals(r.UnifiedComplianceState.ToString(), _selectedCompliance, StringComparison.OrdinalIgnoreCase));
            }

            var list = filtered.ToList();
            ResultsGrid.ItemsSource = list;

            var activeFilters = new List<string>();
            if (!string.IsNullOrWhiteSpace(_selectedKeyBucket)) activeFilters.Add($"Storage={_selectedKeyBucket}");
            if (!string.IsNullOrWhiteSpace(_selectedCompliance)) activeFilters.Add($"Compliance={_selectedCompliance}");
            FilterStatusText.Text = activeFilters.Count == 0
                ? $"Showing all {list.Count} device(s)"
                : $"Filtered ({string.Join(", ", activeFilters)}): {list.Count} of {_allRows.Count} device(s)";
        }

        private TargetDevice[] GetSelectedDevices() =>
            ResultsGrid.SelectedItems.OfType<BitLockerComplianceRow>()
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

        private void ViewBitLockerKey_Click(object sender, RoutedEventArgs e) => DispatchSelected(ToolActionKeys.ViewBitLockerRecoveryKey);
        private void SystemInfo_Click(object sender, RoutedEventArgs e) => DispatchSelected(ToolActionKeys.SystemInformation);
        private void ClientInfo_Click(object sender, RoutedEventArgs e) => DispatchSelected(ToolActionKeys.ClientInformation);
        private void MachinePolicy_Click(object sender, RoutedEventArgs e) => DispatchSelected(ClientActionRegistry.MachinePolicyRetrievalAndEval);
        private void PingSystems_Click(object sender, RoutedEventArgs e) => DispatchSelected(ToolActionKeys.PingSystems);

        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var rows = (ResultsGrid.ItemsSource as IEnumerable<BitLockerComplianceRow>)?.ToList() ?? _allRows;
            var dlg = new SaveFileDialog { Filter = "CSV Files (*.csv)|*.csv", FileName = $"OpenRCT_BitLockerCompliance_{DateTime.Now:yyyyMMdd_HHmm}.csv" };
            if (dlg.ShowDialog() == true)
            {
                CsvExporter.WriteToFile(dlg.FileName, rows, new[]
                {
                    new CsvColumn<BitLockerComplianceRow>("Name", r => r.Name),
                    new CsvColumn<BitLockerComplianceRow>("Key Storage", r => r.KeyStorage),
                    new CsvColumn<BitLockerComplianceRow>("Unified Compliance", r => r.UnifiedCompliance),
                    new CsvColumn<BitLockerComplianceRow>("CM BitLocker Compliance", r => r.CmBitLockerCompliance),
                    new CsvColumn<BitLockerComplianceRow>("Encryption Method", r => r.EncryptionMethod),
                    new CsvColumn<BitLockerComplianceRow>("Protection Status", r => r.ProtectionStatus),
                    new CsvColumn<BitLockerComplianceRow>("Distinguished Name", r => r.DistinguishedName),
                    new CsvColumn<BitLockerComplianceRow>("Password Last Set", r => r.PasswordLastSet),
                    new CsvColumn<BitLockerComplianceRow>("OS", r => r.OperatingSystem),
                    new CsvColumn<BitLockerComplianceRow>("OS Version", r => r.OperatingSystemVersion)
                });
            }
        }

        private void PopOut_Click(object sender, RoutedEventArgs e)
        {
            var popped = new BitLockerComplianceDashboardView(_adClient, _smsClient, _smsServer, _siteCode, _settings, isPoppedOut: true);
            new DashboardHostWindow("BitLocker Compliance", popped, _settings).Show();
        }
    }
}
