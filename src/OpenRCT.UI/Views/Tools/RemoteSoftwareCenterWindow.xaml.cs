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
    public partial class RemoteSoftwareCenterWindow : Window
    {
        private readonly IEndpointActionService _service;
        private readonly ObservableCollection<DeviceErrorEntry> _errors = new ObservableCollection<DeviceErrorEntry>();
        private List<RemoteSoftwareCenterItem> _allItems = new List<RemoteSoftwareCenterItem>();

        public RemoteSoftwareCenterWindow(IReadOnlyList<TargetDevice> devices, IEndpointActionService service, OpenRctSettings settings)
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
            Title = $"Remote Software Center - {dev.DisplayName}";
            try
            {
                _allItems = (await _service.GetRemoteSoftwareCenterItemsAsync(dev)).ToList();
                ApplyFilter();
            }
            catch (Exception ex)
            {
                _errors.Add(new DeviceErrorEntry { Source = dev.DisplayName, Message = ex.Message, Details = ex.ToString() });
            }
        }

        private void FilterBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => ApplyFilter();

        private void ApplyFilter()
        {
            var term = FilterBox.Text?.Trim() ?? string.Empty;
            ItemsGrid.ItemsSource = string.IsNullOrEmpty(term)
                ? _allItems
                : _allItems.Where(i => i.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       i.Publisher.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }

        private async void Install_Click(object sender, RoutedEventArgs e) => await InvokeSelectedAsync(uninstall: false);
        private async void Uninstall_Click(object sender, RoutedEventArgs e) => await InvokeSelectedAsync(uninstall: true);

        private async Task InvokeSelectedAsync(bool uninstall)
        {
            var dev = SelectedDevice;
            if (dev == null) return;
            var selected = ItemsGrid.SelectedItems.OfType<RemoteSoftwareCenterItem>().ToList();
            foreach (var item in selected)
            {
                try
                {
                    var res = await _service.InvokeRemoteSoftwareCenterItemAsync(dev, item, uninstall);
                    if (!res.IsSuccess)
                    {
                        _errors.Add(new DeviceErrorEntry { Source = dev.DisplayName, Message = res.Message, Details = res.ErrorDetails });
                    }
                }
                catch (Exception ex)
                {
                    _errors.Add(new DeviceErrorEntry { Source = dev.DisplayName, Message = ex.Message, Details = ex.ToString() });
                }
            }
            await LoadAsync();
        }
    }
}
