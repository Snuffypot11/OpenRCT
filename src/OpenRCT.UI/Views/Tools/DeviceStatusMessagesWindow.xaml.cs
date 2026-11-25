using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OpenRCT.Core.Export;
using OpenRCT.Core.Interfaces;
using OpenRCT.Core.Models;
using OpenRCT.Core.Settings;
using OpenRCT.UI.Themes;

namespace OpenRCT.UI.Views.Tools
{
    public partial class DeviceStatusMessagesWindow : Window
    {
        private readonly IReadOnlyList<TargetDevice> _devices;
        private readonly ISmsProviderClient _smsClient;
        private readonly string _smsServer;
        private readonly string _siteCode;
        private readonly ObservableCollection<DeviceErrorEntry> _errors = new ObservableCollection<DeviceErrorEntry>();
        private List<StatusMessageEntry> _allMessages = new List<StatusMessageEntry>();

        public DeviceStatusMessagesWindow(IReadOnlyList<TargetDevice> devices, ISmsProviderClient smsClient, string smsServer, string siteCode, OpenRctSettings settings)
        {
            InitializeComponent();
            _devices = devices;
            _smsClient = smsClient;
            _smsServer = smsServer;
            _siteCode = siteCode;
            ThemeManager.ApplyTheme(this, settings.Theme);
            ErrorDrawer.Errors = _errors;
            Loaded += async (_, __) => await LoadAsync();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

        private async void Filter_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            if (sender == DaysCombo) await LoadAsync();
            else ApplyFilter();
        }

        private async Task LoadAsync()
        {
            int days = 7;
            if (DaysCombo.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out var d)) days = d;

            _allMessages.Clear();
            foreach (var dev in _devices)
            {
                try
                {
                    _allMessages.AddRange(await _smsClient.GetDeviceStatusMessagesAsync(_smsServer, _siteCode, dev, days));
                }
                catch (Exception ex)
                {
                    _errors.Add(new DeviceErrorEntry { Source = dev.DisplayName, Message = ex.Message, Details = ex.ToString() });
                }
            }
            ApplyFilter();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

        private void ApplyFilter()
        {
            var term = SearchBox.Text?.Trim() ?? string.Empty;
            var sev = (SeverityCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Severities";

            IEnumerable<StatusMessageEntry> q = _allMessages;
            if (!string.Equals(sev, "All Severities", StringComparison.OrdinalIgnoreCase))
            {
                q = q.Where(m => string.Equals(m.Severity, sev, StringComparison.OrdinalIgnoreCase));
            }
            if (!string.IsNullOrEmpty(term))
            {
                q = q.Where(m => m.Component.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 m.Description.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 m.MessageId.ToString().Contains(term));
            }
            MessagesGrid.ItemsSource = q.ToList();
        }

        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var rows = MessagesGrid.ItemsSource as IEnumerable<StatusMessageEntry> ?? _allMessages;
            var dlg = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = $"OpenRCT_StatusMessages_{DateTime.Now:yyyyMMdd_HHmm}.csv" };
            if (dlg.ShowDialog(this) != true) return;
            CsvExporter.WriteToFile(dlg.FileName, rows, new[]
            {
                new CsvColumn<StatusMessageEntry>("Device", m => m.DeviceName),
                new CsvColumn<StatusMessageEntry>("Time", m => m.Time),
                new CsvColumn<StatusMessageEntry>("Severity", m => m.Severity),
                new CsvColumn<StatusMessageEntry>("MessageID", m => m.MessageId),
                new CsvColumn<StatusMessageEntry>("Component", m => m.Component),
                new CsvColumn<StatusMessageEntry>("Site", m => m.SiteCode),
                new CsvColumn<StatusMessageEntry>("Description", m => m.Description)
            });
        }
    }
}
