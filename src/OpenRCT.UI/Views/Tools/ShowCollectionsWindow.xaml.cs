using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using OpenRCT.Core.Interfaces;
using OpenRCT.Core.Models;
using OpenRCT.Core.Settings;
using OpenRCT.UI.Themes;

namespace OpenRCT.UI.Views.Tools
{
    public partial class ShowCollectionsWindow : Window
    {
        private readonly IReadOnlyList<TargetDevice> _devices;
        private readonly ISmsProviderClient _smsClient;
        private readonly string _smsServer;
        private readonly string _siteCode;
        private readonly ObservableCollection<DeviceErrorEntry> _errors = new ObservableCollection<DeviceErrorEntry>();
        private List<DeviceCollectionMembershipInfo> _allCollections = new List<DeviceCollectionMembershipInfo>();

        public ShowCollectionsWindow(IReadOnlyList<TargetDevice> devices, ISmsProviderClient smsClient, string smsServer, string siteCode, OpenRctSettings settings)
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

        private async Task LoadAsync()
        {
            _allCollections.Clear();
            var mws = new List<MaintenanceWindowInfo>();
            foreach (var dev in _devices)
            {
                try
                {
                    var items = await _smsClient.GetDeviceCollectionsForAssetAsync(_smsServer, _siteCode, dev);
                    _allCollections.AddRange(items);
                    foreach (var c in items) mws.AddRange(c.MaintenanceWindows);
                }
                catch (Exception ex)
                {
                    _errors.Add(new DeviceErrorEntry { Source = dev.DisplayName, Message = ex.Message, Details = ex.ToString() });
                }
            }
            MaintenanceWindowsGrid.ItemsSource = mws;
            ApplyFilter();
        }

        private void FilterBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => ApplyFilter();

        private void ApplyFilter()
        {
            var term = FilterBox.Text?.Trim() ?? string.Empty;
            CollectionsGrid.ItemsSource = string.IsNullOrEmpty(term)
                ? _allCollections.ToList()
                : _allCollections.Where(c => c.CollectionName.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                             c.CollectionId.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }
    }
}
