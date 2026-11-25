using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using OpenRCT.Core.Interfaces;
using OpenRCT.Core.Models;
using OpenRCT.Core.Settings;
using OpenRCT.UI.Themes;

namespace OpenRCT.UI.Views.Tools
{
    public partial class ClientInfoWindow : Window
    {
        private readonly IEndpointActionService _service;
        private readonly ObservableCollection<DeviceErrorEntry> _errors = new ObservableCollection<DeviceErrorEntry>();

        public ClientInfoWindow(IReadOnlyList<TargetDevice> devices, IEndpointActionService service, OpenRctSettings settings)
        {
            InitializeComponent();
            _service = service;
            ThemeManager.ApplyTheme(this, settings.Theme);
            ErrorDrawer.Errors = _errors;
            DeviceCombo.ItemsSource = devices;
            if (devices.Count > 0) DeviceCombo.SelectedIndex = 0;
            Loaded += async (_, __) => await LoadAsync();
        }

        private TargetDevice? SelectedDevice => DeviceCombo.SelectedItem as TargetDevice;

        private async void DeviceCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (IsLoaded) await LoadAsync();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

        private async Task LoadAsync()
        {
            var dev = SelectedDevice;
            if (dev == null) return;
            Title = $"Client Information - {dev.DisplayName}";
            try
            {
                var info = await _service.GetClientInfoAsync(dev);
                VersionText.Text = info.ClientVersion;
                SiteCodeText.Text = info.AssignedSiteCode;
                MpText.Text = info.ManagementPoint;
                GuidText.Text = info.ClientGuid;
                ServiceText.Text = info.CcmExecStatus;
                CacheLocText.Text = info.CacheLocation;
                CacheUsageText.Text = $"{info.CacheUsedMb} MB / {info.CacheSizeMb} MB ({info.CacheItems.Count} items)";
                ComponentsGrid.ItemsSource = info.Components;
                CacheGrid.ItemsSource = info.CacheItems;
            }
            catch (Exception ex)
            {
                _errors.Add(new DeviceErrorEntry { Source = dev.DisplayName, Message = ex.Message, Details = ex.ToString() });
            }
        }

        private async void DeleteCacheItem_Click(object sender, RoutedEventArgs e)
        {
            var dev = SelectedDevice;
            if (dev == null || !(CacheGrid.SelectedItem is ClientCacheItem item)) return;
            if (MessageBox.Show(this, $"Delete cache item '{item.ContentId}' on {dev.DisplayName}?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            try
            {
                await _service.DeleteClientCacheItemAsync(dev, item.CacheId);
                await LoadAsync();
            }
            catch (Exception ex)
            {
                _errors.Add(new DeviceErrorEntry { Source = dev.DisplayName, Message = ex.Message, Details = ex.ToString() });
            }
        }
    }
}
