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
    public partial class LapsDashboardView : UserControl
    {
        private readonly IActiveDirectoryClient _adClient;
        private readonly ISmsProviderClient _smsClient;
        private readonly string _smsServer;
        private readonly string _siteCode;
        private readonly OpenCMToolsSettings _settings;
        private readonly ObservableCollection<DeviceErrorEntry> _errors = new ObservableCollection<DeviceErrorEntry>();

        private List<LapsDashboardRow> _allRows = new List<LapsDashboardRow>();
        private string? _passwordFilter;
        private string? _clientStateFilter;

        public LapsDashboardView(
            IActiveDirectoryClient adClient,
            ISmsProviderClient smsClient,
            string smsServer,
            string siteCode,
            OpenCMToolsSettings settings,
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
            Loaded += async (_, __) => await LoadOusAsync();
        }

        private async Task LoadOusAsync()
        {
            try
            {
                var ous = await _adClient.GetOrganizationalUnitsAsync();
                OuDropdown.SetItems(ous.Select(o => new SelectableItem { Key = o.DistinguishedName, DisplayText = o.CanonicalPath, Tag = o }));
            }
            catch (Exception ex)
            {
                _errors.Add(new DeviceErrorEntry { Source = "Active Directory", Message = $"Failed to load OUs: {ex.Message}", Details = ex.ToString() });
            }
        }

        private async void Scan_Click(object sender, RoutedEventArgs e)
        {
            ScanButton.IsEnabled = false;
            FilterStatusText.Text = "Scanning LAPS compliance...";
            _errors.Clear();

            try
            {
                var adComputers = await _adClient.GetComputersInOusAsync(OuDropdown.GetSelectedKeys());
                IReadOnlyList<ConfigMgrDeviceRecord> cmDevices = Array.Empty<ConfigMgrDeviceRecord>();
                if (!string.IsNullOrWhiteSpace(_smsServer) && !string.IsNullOrWhiteSpace(_siteCode))
                {
                    try
                    {
                        cmDevices = await _smsClient.GetCollectionDeviceRecordsAsync(_smsServer, _siteCode, Array.Empty<string>());
                    }
                    catch (Exception ex)
                    {
                        _errors.Add(new DeviceErrorEntry { Source = "ConfigMgr", Message = ex.Message, Details = ex.ToString() });
                    }
                }

                var result = LapsComplianceAnalyzer.Analyze(adComputers, cmDevices, _errors);
                _allRows = result.Rows;
                _passwordFilter = null;
                _clientStateFilter = null;
                PasswordChart.SelectedSliceKey = null;
                ClientStateChart.SelectedSliceKey = null;
                PasswordChart.Slices = result.PasswordInAdSlices;
                ClientStateChart.Slices = result.ClientInstallStateSlices;
                ApplyFilters();
            }
            catch (Exception ex)
            {
                _errors.Add(new DeviceErrorEntry { Source = "LAPS Scan", Message = ex.Message, Details = ex.ToString() });
            }
            finally
            {
                ScanButton.IsEnabled = true;
            }
        }

        private void PasswordChart_SliceClicked(object? sender, PieSliceData? slice)
        {
            _passwordFilter = slice?.Key;
            ApplyFilters();
        }

        private void ClientStateChart_SliceClicked(object? sender, PieSliceData? slice)
        {
            _clientStateFilter = slice?.Key;
            ApplyFilters();
        }

        private void ApplyFilters()
        {
            IEnumerable<LapsDashboardRow> query = _allRows;
            if (_passwordFilter == "WithPassword") query = query.Where(r => r.HasLapsPassword);
            else if (_passwordFilter == "WithoutPassword") query = query.Where(r => !r.HasLapsPassword);

            if (!string.IsNullOrEmpty(_clientStateFilter))
            {
                query = query.Where(r => string.Equals(r.ClientInstallState.ToString(), _clientStateFilter, StringComparison.OrdinalIgnoreCase));
            }

            var list = query.ToList();
            ResultsGrid.ItemsSource = list;
            FilterStatusText.Text = $"Showing {list.Count} of {_allRows.Count} computer(s)";
        }

        private void ViewLapsPassword_Click(object sender, RoutedEventArgs e)
        {
            var targets = ResultsGrid.SelectedItems.OfType<LapsDashboardRow>()
                .Select(r => new TargetDevice { Name = r.Name, SiteCode = _siteCode, SmsProviderServer = _smsServer })
                .ToArray();
            if (targets.Length == 0) return;

            ToolDispatcher.Dispatch(new ToolInvocationContext
            {
                ActionKey = ToolActionKeys.ViewLapsPassword,
                SmsProviderServer = _smsServer,
                SiteCode = _siteCode,
                Devices = targets
            });
        }

        private void SystemInfo_Click(object sender, RoutedEventArgs e)
        {
            var targets = ResultsGrid.SelectedItems.OfType<LapsDashboardRow>()
                .Select(r => new TargetDevice { Name = r.Name, SiteCode = _siteCode, SmsProviderServer = _smsServer })
                .ToArray();
            if (targets.Length == 0) return;

            ToolDispatcher.Dispatch(new ToolInvocationContext
            {
                ActionKey = ToolActionKeys.SystemInformation,
                SmsProviderServer = _smsServer,
                SiteCode = _siteCode,
                Devices = targets
            });
        }

        private void PopOut_Click(object sender, RoutedEventArgs e)
        {
            var host = new DashboardHostWindow("LAPS Dashboard",
                new LapsDashboardView(_adClient, _smsClient, _smsServer, _siteCode, _settings, isPoppedOut: true),
                _settings);
            host.Show();
        }

        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var rows = ResultsGrid.ItemsSource as IEnumerable<LapsDashboardRow> ?? _allRows;
            var dlg = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = $"OpenCMTools_LAPS_{DateTime.Now:yyyyMMdd_HHmm}.csv" };
            if (dlg.ShowDialog() != true) return;

            CsvExporter.WriteToFile(dlg.FileName, rows, new[]
            {
                new CsvColumn<LapsDashboardRow>("Name", r => r.Name),
                new CsvColumn<LapsDashboardRow>("Distinguished Name", r => r.DistinguishedName),
                new CsvColumn<LapsDashboardRow>("Password Last Set", r => r.PasswordLastSet),
                new CsvColumn<LapsDashboardRow>("OS", r => r.Os),
                new CsvColumn<LapsDashboardRow>("OS Version", r => r.OsVersion),
                new CsvColumn<LapsDashboardRow>("LAPS Password Expiration", r => r.LapsPasswordExpiration),
                new CsvColumn<LapsDashboardRow>("Laps Client Install State", r => r.ClientInstallStateLabel),
                new CsvColumn<LapsDashboardRow>("Created", r => r.Created)
            });
        }
    }
}
