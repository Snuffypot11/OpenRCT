using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using OpenRCT.Core.Analytics;
using OpenRCT.Core.Interfaces;
using OpenRCT.Core.Models;
using OpenRCT.Core.Settings;
using OpenRCT.UI.Controls;
using OpenRCT.UI.Mvvm;
using OpenRCT.UI.Themes;

namespace OpenRCT.UI.Views.Tools
{
    public partial class ClientInfoWindow : Window
    {
        private readonly IReadOnlyList<TargetDevice> _devices;
        private readonly Dictionary<string, TargetDevice> _deviceMap;
        private readonly IEndpointActionService _service;
        private readonly OpenRctSettings _settings;
        private readonly ObservableCollection<DeviceErrorEntry> _errors = new ObservableCollection<DeviceErrorEntry>();

        private List<ClientInfoSummary> _summaries = new List<ClientInfoSummary>();
        private List<ClientComponentInfo> _allComponents = new List<ClientComponentInfo>();
        private List<ClientCacheItem> _allCacheItems = new List<ClientCacheItem>();

        public ClientInfoWindow(IReadOnlyList<TargetDevice> devices, IEndpointActionService service, OpenRctSettings settings)
        {
            _devices = devices ?? Array.Empty<TargetDevice>();
            _deviceMap = new Dictionary<string, TargetDevice>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in _devices)
            {
                if (!string.IsNullOrWhiteSpace(d.DisplayName) && !_deviceMap.ContainsKey(d.DisplayName))
                    _deviceMap[d.DisplayName] = d;
                if (!string.IsNullOrWhiteSpace(d.ConnectAddress) && !_deviceMap.ContainsKey(d.ConnectAddress))
                    _deviceMap[d.ConnectAddress] = d;
            }

            _service = service;
            _settings = settings;

            ThemeManager.ApplyTheme(this, settings.Theme);
            InitializeComponent();
            ErrorDrawer.Errors = _errors;

            Title = _devices.Count == 1
                ? $"Client Information - {_devices[0].DisplayName}"
                : $"Client Information - {_devices.Count} Computers";

            ComponentsGroupedGrid.ConfigureColumns(
                nameHeader: "Component",
                secondaryHeader: "Internal Name",
                versionHeader: "Version",
                statusHeader: "State Summary",
                childStatusHeader: "State",
                childSecondaryHeader: "Version",
                showSecondary: true,
                showVersion: true,
                showChildTimestamp: false,
                showChildError: false);

            CacheGroupedGrid.ConfigureColumns(
                nameHeader: "Content ID",
                secondaryHeader: "Version",
                versionHeader: "Total Size",
                statusHeader: "Summary",
                childStatusHeader: "Size",
                childSecondaryHeader: "Cache Location",
                childTimestampHeader: "Last Referenced",
                showSecondary: true,
                showVersion: true,
                showChildTimestamp: true,
                showChildError: false);

            var filterOptions = DeviceFilterOption.BuildOptions(_devices);
            DeviceFilterCombo.ItemsSource = filterOptions;
            if (filterOptions.Count > 0) DeviceFilterCombo.SelectedIndex = 0;

            Loaded += async (_, __) => await LoadAsync();
        }

        private DeviceFilterOption? ActiveDeviceFilter => DeviceFilterCombo.SelectedItem as DeviceFilterOption;

        private bool IsMultiDeviceView => _devices.Count > 1 && ActiveDeviceFilter?.Device == null;

        private void DeviceFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded) ApplyFilterAndBind();
        }

        private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (IsLoaded) ApplyFilterAndBind();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

        private async Task LoadAsync()
        {
            if (_devices.Count == 0) return;

            _errors.Clear();
            LoadingOverlay.Visibility = Visibility.Visible;
            LoadingText.Text = _devices.Count == 1
                ? $"Querying client information on {_devices[0].DisplayName}..."
                : $"Querying client information across 0 / {_devices.Count} computers...";

            var summaries = new ConcurrentBag<ClientInfoSummary>();
            var errs = new ConcurrentBag<DeviceErrorEntry>();
            var completed = 0;

            try
            {
                using (var gate = new SemaphoreSlim(Math.Max(1, _settings.MaxConcurrentDevices)))
                {
                    var tasks = _devices.Select(async dev =>
                    {
                        await gate.WaitAsync();
                        try
                        {
                            var info = await _service.GetClientInfoAsync(dev);
                            if (string.IsNullOrWhiteSpace(info.DeviceName))
                            {
                                info.DeviceName = dev.DisplayName;
                            }
                            foreach (var c in info.Components)
                            {
                                if (string.IsNullOrWhiteSpace(c.DeviceName)) c.DeviceName = dev.DisplayName;
                            }
                            foreach (var cache in info.CacheItems)
                            {
                                if (string.IsNullOrWhiteSpace(cache.DeviceName)) cache.DeviceName = dev.DisplayName;
                            }
                            foreach (var w in info.Warnings)
                            {
                                errs.Add(new DeviceErrorEntry { Source = dev.DisplayName, Message = w });
                            }
                            summaries.Add(info);
                        }
                        catch (Exception ex)
                        {
                            errs.Add(new DeviceErrorEntry { Source = dev.DisplayName, Message = ex.Message, Details = ex.ToString() });
                        }
                        finally
                        {
                            gate.Release();
                            var done = Interlocked.Increment(ref completed);
                            if (_devices.Count > 1)
                            {
                                Dispatcher.InvokeAsync(() =>
                                    LoadingText.Text = $"Querying client information across {done} / {_devices.Count} computers...");
                            }
                        }
                    }).ToList();

                    await Task.WhenAll(tasks);
                }

                _summaries = summaries.OrderBy(s => s.DeviceName, StringComparer.OrdinalIgnoreCase).ToList();
                _allComponents = _summaries.SelectMany(s => s.Components).ToList();
                _allCacheItems = _summaries.SelectMany(s => s.CacheItems).ToList();

                foreach (var err in errs.OrderBy(e => e.Source, StringComparer.OrdinalIgnoreCase))
                {
                    _errors.Add(err);
                }

                ApplyFilterAndBind();
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void ApplyFilterAndBind()
        {
            var term = FilterBox.Text?.Trim() ?? string.Empty;
            var filterDev = ActiveDeviceFilter?.Device;
            var multi = IsMultiDeviceView;

            bool MatchesDevice(string? deviceName) =>
                filterDev == null ||
                string.Equals(deviceName, filterDev.DisplayName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(deviceName, filterDev.ConnectAddress, StringComparison.OrdinalIgnoreCase);

            bool MatchesText(params string?[] values) =>
                string.IsNullOrEmpty(term) ||
                values.Any(v => v != null && v.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);

            var visibleSummaries = _summaries
                .Where(s => MatchesDevice(s.DeviceName) && MatchesText(s.DeviceName, s.ClientVersion, s.AssignedSiteCode, s.ManagementPoint, s.CcmExecStatus, s.ClientGuid))
                .ToList();

            var visibleComponents = _allComponents
                .Where(c => MatchesDevice(c.DeviceName) && MatchesText(c.DeviceName, c.Name, c.DisplayName, c.Version, c.State))
                .ToList();

            var visibleCache = _allCacheItems
                .Where(c => MatchesDevice(c.DeviceName) && MatchesText(c.DeviceName, c.ContentId, c.ContentVersion, c.Location))
                .ToList();

            // Client Details tab
            if (multi)
            {
                SingleDeviceDetailsPanel.Visibility = Visibility.Collapsed;
                ClientDetailsGrid.Visibility = Visibility.Visible;
                ClientDetailsGrid.ItemsSource = visibleSummaries;
            }
            else
            {
                ClientDetailsGrid.Visibility = Visibility.Collapsed;
                SingleDeviceDetailsPanel.Visibility = Visibility.Visible;
                var info = visibleSummaries.FirstOrDefault();
                if (info != null)
                {
                    VersionText.Text = info.ClientVersion;
                    SiteCodeText.Text = info.AssignedSiteCode;
                    MpText.Text = info.ManagementPoint;
                    GuidText.Text = info.ClientGuid;
                    ServiceText.Text = info.CcmExecStatus;
                    CacheLocText.Text = info.CacheLocation;
                    CacheUsageText.Text = $"{info.CacheUsedMb} MB / {info.CacheSizeMb} MB ({info.CacheItems.Count} items)";
                }
                else
                {
                    VersionText.Text = SiteCodeText.Text = MpText.Text = GuidText.Text = ServiceText.Text = CacheLocText.Text = CacheUsageText.Text = string.Empty;
                }
            }

            // Components tab
            if (multi)
            {
                ComponentsFlatGrid.Visibility = Visibility.Collapsed;
                ComponentsGroupedGrid.Visibility = Visibility.Visible;
                var compGroups = ExpandableItemGroup.FromGroupedData(MultiDeviceGrouper.GroupClientComponents(visibleComponents));
                ComponentsGroupedGrid.SetGroups(compGroups);
                ComponentsTab.Header = $"Components ({compGroups.Count})";
            }
            else
            {
                ComponentsGroupedGrid.Visibility = Visibility.Collapsed;
                ComponentsFlatGrid.Visibility = Visibility.Visible;
                ComponentsFlatGrid.ItemsSource = visibleComponents;
                ComponentsTab.Header = $"Components ({visibleComponents.Count})";
            }

            // Cache tab
            if (multi)
            {
                CacheFlatGrid.Visibility = Visibility.Collapsed;
                CacheGroupedGrid.Visibility = Visibility.Visible;
                var cacheGroups = ExpandableItemGroup.FromGroupedData(MultiDeviceGrouper.GroupClientCacheItems(visibleCache));
                CacheGroupedGrid.SetGroups(cacheGroups);
                CacheTab.Header = $"Cache ({cacheGroups.Count})";
                CacheHintText.Text = "Check cached content items (or expand to select individual devices) to delete.";
            }
            else
            {
                CacheGroupedGrid.Visibility = Visibility.Collapsed;
                CacheFlatGrid.Visibility = Visibility.Visible;
                CacheFlatGrid.ItemsSource = visibleCache;
                CacheTab.Header = $"Cache ({visibleCache.Count})";
                CacheHintText.Text = "Select one or more cache rows to delete.";
            }

            StatusText.Text = $"{visibleSummaries.Count} device(s) loaded — {visibleCache.Count} cache item(s)";
        }

        private TargetDevice? ResolveDevice(string deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName)) return _devices.FirstOrDefault();
            if (_deviceMap.TryGetValue(deviceName, out var found)) return found;
            return _devices.FirstOrDefault(d =>
                string.Equals(d.DisplayName, deviceName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(d.ConnectAddress, deviceName, StringComparison.OrdinalIgnoreCase));
        }

        private async void DeleteCacheItem_Click(object sender, RoutedEventArgs e)
        {
            List<CheckedPayloadEntry<ClientCacheItem>> targets;
            if (IsMultiDeviceView)
            {
                targets = CacheGroupedGrid.GetCheckedOrSelectedPayloads<ClientCacheItem>();
            }
            else
            {
                targets = CacheFlatGrid.SelectedItems.OfType<ClientCacheItem>()
                    .Select(c => new CheckedPayloadEntry<ClientCacheItem> { DeviceName = c.DeviceName, Payload = c })
                    .ToList();
            }

            if (targets.Count == 0)
            {
                MessageBox.Show(this, "Select one or more cache items to delete.", "OpenRCT", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var devCount = targets.Select(t => t.DeviceName).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            if (MessageBox.Show(this, $"Delete {targets.Count} cache item(s) across {devCount} device(s)?",
                "Confirm Delete Cache", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            LoadingOverlay.Visibility = Visibility.Visible;
            LoadingText.Text = $"Deleting {targets.Count} cache item(s)...";
            var errs = new ConcurrentBag<DeviceErrorEntry>();

            try
            {
                using (var gate = new SemaphoreSlim(Math.Max(1, _settings.MaxConcurrentDevices)))
                {
                    var tasks = targets.Select(async entry =>
                    {
                        await gate.WaitAsync();
                        try
                        {
                            var dev = ResolveDevice(entry.DeviceName);
                            if (dev == null)
                            {
                                errs.Add(new DeviceErrorEntry { Source = entry.DeviceName, Message = "Unable to resolve target device." });
                                return;
                            }
                            await _service.DeleteClientCacheItemAsync(dev, entry.Payload.CacheId);
                        }
                        catch (Exception ex)
                        {
                            errs.Add(new DeviceErrorEntry { Source = entry.DeviceName, Message = ex.Message, Details = ex.ToString() });
                        }
                        finally
                        {
                            gate.Release();
                        }
                    }).ToList();

                    await Task.WhenAll(tasks);
                }

                foreach (var err in errs) _errors.Add(err);
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }

            await LoadAsync();
        }
    }
}
