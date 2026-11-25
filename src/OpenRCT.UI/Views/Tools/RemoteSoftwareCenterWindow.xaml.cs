using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using OpenRCT.Core.Export;
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
            _service = service;
            ThemeManager.ApplyTheme(this, settings.Theme);
            InitializeComponent();
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
            StatusText.Text = $"Querying Software Center on {dev.DisplayName}...";
            _errors.Clear();
            try
            {
                _allItems = (await _service.GetRemoteSoftwareCenterItemsAsync(dev)).ToList();
                ApplyFilter();
            }
            catch (Exception ex)
            {
                _errors.Add(new DeviceErrorEntry { Source = dev.DisplayName, Message = ex.Message, Details = ex.ToString() });
                StatusText.Text = "Query failed (see Errors drawer).";
            }
        }

        private void FilterBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => ApplyFilter();

        private void ApplyFilter()
        {
            var term = FilterBox.Text?.Trim() ?? string.Empty;
            bool Matches(RemoteSoftwareCenterItem i) =>
                string.IsNullOrEmpty(term) ||
                i.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                i.Publisher.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                i.SoftwareVersion.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                i.PackageName.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;

            var apps = _allItems.Where(i => i.ItemType == SoftwareCenterItemType.Application && Matches(i)).ToList();
            var updates = _allItems.Where(i => i.ItemType == SoftwareCenterItemType.SoftwareUpdate && Matches(i)).ToList();
            var packages = _allItems.Where(i => i.ItemType == SoftwareCenterItemType.PackageOrTaskSequence && Matches(i)).ToList();

            AppsGrid.ItemsSource = apps;
            UpdatesGrid.ItemsSource = updates;
            PackagesGrid.ItemsSource = packages;

            AppsTab.Header = $"Applications ({apps.Count})";
            UpdatesTab.Header = $"Software Updates ({updates.Count})";
            PackagesTab.Header = $"Packages & Task Sequences ({packages.Count})";
            StatusText.Text = $"{apps.Count} app(s), {updates.Count} update(s), {packages.Count} package(s)/TS";
        }

        private async void InstallApp_Click(object sender, RoutedEventArgs e) => await InvokeGridItemsAsync(AppsGrid.SelectedItems.OfType<RemoteSoftwareCenterItem>(), uninstall: false);
        private async void UninstallApp_Click(object sender, RoutedEventArgs e) => await InvokeGridItemsAsync(AppsGrid.SelectedItems.OfType<RemoteSoftwareCenterItem>(), uninstall: true);
        private async void InstallUpdate_Click(object sender, RoutedEventArgs e) => await InvokeGridItemsAsync(UpdatesGrid.SelectedItems.OfType<RemoteSoftwareCenterItem>(), uninstall: false);
        private async void RunPackage_Click(object sender, RoutedEventArgs e) => await InvokeGridItemsAsync(PackagesGrid.SelectedItems.OfType<RemoteSoftwareCenterItem>(), uninstall: false);

        private async void RepairApp_Click(object sender, RoutedEventArgs e)
        {
            var dev = SelectedDevice;
            if (dev == null) return;
            var selected = AppsGrid.SelectedItems.OfType<RemoteSoftwareCenterItem>().ToList();
            foreach (var item in selected)
            {
                try
                {
                    var res = await _service.RepairRemoteSoftwareCenterApplicationAsync(dev, item);
                    StatusText.Text = res.Message;
                }
                catch (Exception ex)
                {
                    _errors.Add(new DeviceErrorEntry { Source = dev.DisplayName, Message = ex.Message, Details = ex.ToString() });
                }
            }
            await LoadAsync();
        }

        private async void InstallAllUpdates_Click(object sender, RoutedEventArgs e)
        {
            var dev = SelectedDevice;
            if (dev == null) return;
            try
            {
                var res = await _service.InstallAllSoftwareUpdatesAsync(dev);
                StatusText.Text = res.Message;
                await LoadAsync();
            }
            catch (Exception ex)
            {
                _errors.Add(new DeviceErrorEntry { Source = dev.DisplayName, Message = ex.Message, Details = ex.ToString() });
            }
        }

        private async void TriggerAppEval_Click(object sender, RoutedEventArgs e) =>
            await TriggerClientCycleAsync(ClientActionRegistry.AppDeploymentEval);

        private async void TriggerUpdateScan_Click(object sender, RoutedEventArgs e) =>
            await TriggerClientCycleAsync(ClientActionRegistry.SoftwareUpdatesScan);

        private async void TriggerUpdateEval_Click(object sender, RoutedEventArgs e) =>
            await TriggerClientCycleAsync(ClientActionRegistry.SoftwareUpdatesDeploymentEval);

        private async Task TriggerClientCycleAsync(string actionKey)
        {
            var dev = SelectedDevice;
            if (dev == null) return;
            try
            {
                var action = ClientActionRegistry.GetRequired(actionKey);
                var res = await _service.RunClientActionAsync(dev, action);
                StatusText.Text = res.Message;
            }
            catch (Exception ex)
            {
                _errors.Add(new DeviceErrorEntry { Source = dev.DisplayName, Message = ex.Message, Details = ex.ToString() });
            }
        }

        private async Task InvokeGridItemsAsync(IEnumerable<RemoteSoftwareCenterItem> items, bool uninstall)
        {
            var dev = SelectedDevice;
            if (dev == null) return;
            var list = items.ToList();
            if (list.Count == 0) return;

            foreach (var item in list)
            {
                try
                {
                    var res = await _service.InvokeRemoteSoftwareCenterItemAsync(dev, item, uninstall);
                    StatusText.Text = res.Message;
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

        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = $"OpenRCT_SoftwareCenter_{DateTime.Now:yyyyMMdd_HHmm}.csv" };
            if (dlg.ShowDialog(this) != true) return;

            CsvExporter.WriteToFile(dlg.FileName, _allItems, new[]
            {
                new CsvColumn<RemoteSoftwareCenterItem>("Device", i => i.DeviceName),
                new CsvColumn<RemoteSoftwareCenterItem>("Type", i => i.ItemType),
                new CsvColumn<RemoteSoftwareCenterItem>("Name", i => i.Name),
                new CsvColumn<RemoteSoftwareCenterItem>("Publisher", i => i.Publisher),
                new CsvColumn<RemoteSoftwareCenterItem>("Version / KB", i => i.SoftwareVersion),
                new CsvColumn<RemoteSoftwareCenterItem>("Install / Compliance State", i => i.InstallState),
                new CsvColumn<RemoteSoftwareCenterItem>("Evaluation State", i => i.EvaluationState),
                new CsvColumn<RemoteSoftwareCenterItem>("Error Code", i => i.ErrorCodeHex),
                new CsvColumn<RemoteSoftwareCenterItem>("Deadline", i => i.Deadline)
            });
        }
    }
}
