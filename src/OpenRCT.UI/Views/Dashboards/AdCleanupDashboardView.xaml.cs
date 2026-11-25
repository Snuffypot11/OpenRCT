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
    public partial class AdCleanupDashboardView : UserControl
    {
        private readonly IActiveDirectoryClient _adClient;
        private readonly ISmsProviderClient _smsClient;
        private readonly string _smsServer;
        private readonly string _siteCode;
        private readonly OpenRctSettings _settings;
        private readonly ObservableCollection<DeviceErrorEntry> _errors = new ObservableCollection<DeviceErrorEntry>();

        private List<AdCleanupRow> _allRows = new List<AdCleanupRow>();
        private string? _activeBucketFilter;

        public AdCleanupDashboardView(
            IActiveDirectoryClient adClient,
            ISmsProviderClient smsClient,
            string smsServer,
            string siteCode,
            OpenRctSettings settings,
            bool isPoppedOut = false)
        {
            InitializeComponent();
            _adClient = adClient;
            _smsClient = smsClient;
            _smsServer = smsServer;
            _siteCode = siteCode;
            _settings = settings;

            ThemeManager.ApplyTheme(this, settings.Theme);
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
                OuDropdown.SetItems(ous.Select(o => new SelectableItem { Key = o.DistinguishedName, DisplayText = o.CanonicalPath, Tag = o }));
            }
            catch (Exception ex)
            {
                _errors.Add(new DeviceErrorEntry { Source = "Active Directory", Message = $"Failed to load OUs: {ex.Message}", Details = ex.ToString() });
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(_smsServer) && !string.IsNullOrWhiteSpace(_siteCode))
                {
                    var cols = await _smsClient.GetDeviceCollectionsAsync(_smsServer, _siteCode);
                    CollectionDropdown.SetItems(cols.Select(c => new SelectableItem { Key = c.CollectionId, DisplayText = $"{c.Name} ({c.CollectionId})", Tag = c }));
                }
            }
            catch (Exception ex)
            {
                _errors.Add(new DeviceErrorEntry { Source = "ConfigMgr", Message = $"Failed to load Collections: {ex.Message}", Details = ex.ToString() });
            }
        }

        private async void Scan_Click(object sender, RoutedEventArgs e)
        {
            ScanButton.IsEnabled = false;
            FilterStatusText.Text = "Scanning AD & ConfigMgr...";
            _errors.Clear();

            try
            {
                IReadOnlyList<AdComputerRecord> adDevices = Array.Empty<AdComputerRecord>();
                IReadOnlyList<ConfigMgrDeviceRecord> cmDevices = Array.Empty<ConfigMgrDeviceRecord>();

                try
                {
                    adDevices = await _adClient.GetComputersInOusAsync(OuDropdown.GetSelectedKeys());
                }
                catch (Exception ex)
                {
                    _errors.Add(new DeviceErrorEntry { Source = "Active Directory", Message = ex.Message, Details = ex.ToString() });
                }

                try
                {
                    if (!string.IsNullOrWhiteSpace(_smsServer) && !string.IsNullOrWhiteSpace(_siteCode))
                    {
                        cmDevices = await _smsClient.GetCollectionDeviceRecordsAsync(_smsServer, _siteCode, CollectionDropdown.GetSelectedKeys());
                    }
                    else
                    {
                        _errors.Add(new DeviceErrorEntry { Source = "ConfigMgr", Message = "SMS Provider Server/Site Code not configured." });
                    }
                }
                catch (Exception ex)
                {
                    _errors.Add(new DeviceErrorEntry { Source = "ConfigMgr", Message = ex.Message, Details = ex.ToString() });
                }

                var result = AdCleanupReconciler.Reconcile(adDevices, cmDevices, _errors);
                _allRows = result.Rows;
                _activeBucketFilter = null;
                PresencePieChart.SelectedSliceKey = null;
                PresencePieChart.Slices = result.ChartSlices;
                ApplyBucketFilter();
            }
            finally
            {
                ScanButton.IsEnabled = true;
            }
        }

        private void PresencePieChart_SliceClicked(object? sender, PieSliceData? slice)
        {
            _activeBucketFilter = slice?.Key;
            ApplyBucketFilter();
        }

        private void ApplyBucketFilter()
        {
            var filtered = string.IsNullOrEmpty(_activeBucketFilter)
                ? _allRows
                : _allRows.Where(r => string.Equals(r.Bucket.ToString(), _activeBucketFilter, StringComparison.OrdinalIgnoreCase)).ToList();

            ResultsGrid.ItemsSource = filtered;
            FilterStatusText.Text = string.IsNullOrEmpty(_activeBucketFilter)
                ? $"Showing all {filtered.Count} device(s)"
                : $"Filtered ({_activeBucketFilter}): {filtered.Count} of {_allRows.Count} device(s)";
        }

        private IReadOnlyList<TargetDevice> GetSelectedTargets() =>
            ResultsGrid.SelectedItems.OfType<AdCleanupRow>().Select(r => new TargetDevice
            {
                Name = r.Name,
                ResourceId = r.SccmResourceId,
                SiteCode = _siteCode,
                SmsProviderServer = _smsServer
            }).ToList();

        private void DeleteFromAd_Click(object sender, RoutedEventArgs e)
        {
            var targets = GetSelectedTargets();
            if (targets.Count == 0) return;
            ToolDispatcher.Dispatch(new ToolInvocationContext
            {
                ActionKey = ToolActionKeys.DeleteFromActiveDirectory,
                SmsProviderServer = _smsServer,
                SiteCode = _siteCode,
                Devices = targets.ToArray()
            });
        }

        private void DeleteFromConfigMgr_Click(object sender, RoutedEventArgs e)
        {
            var targets = GetSelectedTargets();
            if (targets.Count == 0) return;
            ToolDispatcher.Dispatch(new ToolInvocationContext
            {
                ActionKey = ToolActionKeys.DeleteFromConfigMgr,
                SmsProviderServer = _smsServer,
                SiteCode = _siteCode,
                Devices = targets.ToArray()
            });
        }

        private void PingSelected_Click(object sender, RoutedEventArgs e)
        {
            var targets = GetSelectedTargets();
            if (targets.Count == 0) return;
            ToolDispatcher.Dispatch(new ToolInvocationContext
            {
                ActionKey = ToolActionKeys.PingSystems,
                SmsProviderServer = _smsServer,
                SiteCode = _siteCode,
                Devices = targets.ToArray()
            });
        }

        private void SystemInfoSelected_Click(object sender, RoutedEventArgs e)
        {
            var targets = GetSelectedTargets();
            if (targets.Count == 0) return;
            ToolDispatcher.Dispatch(new ToolInvocationContext
            {
                ActionKey = ToolActionKeys.SystemInformation,
                SmsProviderServer = _smsServer,
                SiteCode = _siteCode,
                Devices = targets.ToArray()
            });
        }

        private void PopOut_Click(object sender, RoutedEventArgs e)
        {
            var host = new DashboardHostWindow("Active Directory Cleanup Tool",
                new AdCleanupDashboardView(_adClient, _smsClient, _smsServer, _siteCode, _settings, isPoppedOut: true),
                _settings);
            host.Show();
        }

        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var rows = ResultsGrid.ItemsSource as IEnumerable<AdCleanupRow> ?? _allRows;
            var dlg = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = $"OpenRCT_ADCleanup_{DateTime.Now:yyyyMMdd_HHmm}.csv" };
            if (dlg.ShowDialog() != true) return;

            CsvExporter.WriteToFile(dlg.FileName, rows, new[]
            {
                new CsvColumn<AdCleanupRow>("Bucket", r => r.BucketLabel),
                new CsvColumn<AdCleanupRow>("Name", r => r.Name),
                new CsvColumn<AdCleanupRow>("AD Distinguished Name", r => r.AdDistinguishedName),
                new CsvColumn<AdCleanupRow>("AD Password Last Set", r => r.AdPasswordLastSet),
                new CsvColumn<AdCleanupRow>("AD OS", r => r.AdOs),
                new CsvColumn<AdCleanupRow>("AD OS Version", r => r.AdOsVersion),
                new CsvColumn<AdCleanupRow>("AD Created", r => r.AdCreated),
                new CsvColumn<AdCleanupRow>("AD Last Logon Timestamp", r => r.AdLastLogonTimestamp),
                new CsvColumn<AdCleanupRow>("SCCM Operating System", r => r.SccmOperatingSystem),
                new CsvColumn<AdCleanupRow>("SCCM Last Logon User", r => r.SccmLastLogonUser),
                new CsvColumn<AdCleanupRow>("SCCM Last Logon Domain", r => r.SccmLastLogonDomain),
                new CsvColumn<AdCleanupRow>("SCCM Last Logon Time", r => r.SccmLastLogonTime),
                new CsvColumn<AdCleanupRow>("SCCM Domain", r => r.SccmDomain),
                new CsvColumn<AdCleanupRow>("SCCM Distinguished Name", r => r.SccmDistinguishedName),
                new CsvColumn<AdCleanupRow>("SCCM Last Check in Time", r => r.SccmLastCheckInTime),
                new CsvColumn<AdCleanupRow>("SCCM Client Installed", r => r.SccmClientInstalled)
            });
        }
    }
}
