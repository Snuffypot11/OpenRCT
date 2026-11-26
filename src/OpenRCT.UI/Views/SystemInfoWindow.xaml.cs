using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
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
using OpenRCT.UI.Mvvm;
using OpenRCT.UI.Themes;

namespace OpenRCT.UI.Views
{
    public partial class SystemInfoWindow : Window
    {
        private static readonly HashSet<string> GroupableTabs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Applications",
            "WindowsUpdates",
            "Services",
            "Drivers",
            "UserProfiles",
            "LocalGroups"
        };

        private readonly IReadOnlyList<TargetDevice> _devices;
        private readonly Dictionary<string, TargetDevice> _deviceMap;
        private readonly ISystemInfoCollector _collector;
        private readonly OpenRctSettings _settings;
        private readonly ObservableCollection<DeviceErrorEntry> _errors = new ObservableCollection<DeviceErrorEntry>();
        private readonly HashSet<string> _loadedTabs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private string _activeTab = "OS";
        private string _loadedLocalGroup = "Administrators";
        private List<OperatingSystemInfo> _osItems = new List<OperatingSystemInfo>();
        private List<HardwareInfo> _hwItems = new List<HardwareInfo>();
        private List<DiskInfo> _diskItems = new List<DiskInfo>();
        private List<InstalledApplicationInfo> _appItems = new List<InstalledApplicationInfo>();
        private List<WindowsUpdateInfo> _updateItems = new List<WindowsUpdateInfo>();
        private List<WindowsServiceInfo> _serviceItems = new List<WindowsServiceInfo>();
        private List<DriverInfo> _driverItems = new List<DriverInfo>();
        private List<UserProfileInfo> _profileItems = new List<UserProfileInfo>();
        private List<LocalGroupMemberInfo> _groupItems = new List<LocalGroupMemberInfo>();
        private List<BatteryInfo> _batteryItems = new List<BatteryInfo>();

        public SystemInfoWindow(IReadOnlyList<TargetDevice> devices, ISystemInfoCollector collector, OpenRctSettings settings)
        {
            InitializeComponent();
            _devices = devices ?? Array.Empty<TargetDevice>();
            _deviceMap = new Dictionary<string, TargetDevice>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in _devices)
            {
                if (!string.IsNullOrWhiteSpace(d.DisplayName) && !_deviceMap.ContainsKey(d.DisplayName))
                    _deviceMap[d.DisplayName] = d;
                if (!string.IsNullOrWhiteSpace(d.TargetHost) && !_deviceMap.ContainsKey(d.TargetHost))
                    _deviceMap[d.TargetHost] = d;
            }

            _collector = collector;
            _settings = settings;

            ThemeManager.ApplyTheme(this, settings.Theme);
            ErrorDrawer.Errors = _errors;

            Title = _devices.Count == 1
                ? $"System Information - {_devices[0].DisplayName}"
                : $"System Information - {_devices.Count} Computers";
            ScopeHeaderText.Text = _devices.Count == 1
                ? "Target Device"
                : $"Device Filter ({_devices.Count} total)";

            var filterOptions = DeviceFilterOption.BuildOptions(_devices);
            DeviceFilterCombo.ItemsSource = filterOptions;
            if (filterOptions.Count > 0) DeviceFilterCombo.SelectedIndex = 0;

            GroupByItemCheckBox.IsChecked = _devices.Count > 1;

            LocalGroupCombo.ItemsSource = new[] { "Administrators", "Remote Desktop Users", "Users", "Power Users", "Hyper-V Administrators" };
            LocalGroupCombo.SelectedIndex = 0;

            UpdateVisiblePane();
            Loaded += async (_, __) => await LoadActiveTabAsync(forceRefresh: true);
        }

        private DeviceFilterOption? ActiveDeviceFilter => DeviceFilterCombo.SelectedItem as DeviceFilterOption;

        private IReadOnlyList<TargetDevice> ActiveScopeDevices
        {
            get
            {
                var filter = ActiveDeviceFilter;
                if (filter != null && filter.Device != null)
                {
                    return new[] { filter.Device };
                }
                return _devices;
            }
        }

        private bool IsGroupedModeActive =>
            GroupableTabs.Contains(_activeTab) &&
            GroupByItemCheckBox.IsChecked == true &&
            ActiveDeviceFilter?.Device == null &&
            _devices.Count > 1;

        private void DeviceFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            UpdateVisiblePane();
            ApplySearchFilter();
        }

        private void GroupByItemCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            UpdateVisiblePane();
            ApplySearchFilter();
        }

        private async void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            if (NavList.SelectedItem is ListBoxItem item && item.Tag is string tag)
            {
                _activeTab = tag;
                UpdateVisiblePane();
                if (_loadedTabs.Contains(_activeTab))
                {
                    ApplySearchFilter();
                }
                else
                {
                    await LoadActiveTabAsync(forceRefresh: false);
                }
            }
        }

        private void UpdateVisiblePane()
        {
            var canGroup = GroupableTabs.Contains(_activeTab) && _devices.Count > 1 && ActiveDeviceFilter?.Device == null;
            GroupByItemCheckBox.Visibility = canGroup ? Visibility.Visible : Visibility.Collapsed;

            var useGrouped = IsGroupedModeActive;
            GroupedGrid.Visibility = useGrouped ? Visibility.Visible : Visibility.Collapsed;

            OsGrid.Visibility = !useGrouped && _activeTab == "OS" ? Visibility.Visible : Visibility.Collapsed;
            HardwareGrid.Visibility = !useGrouped && _activeTab == "Hardware" ? Visibility.Visible : Visibility.Collapsed;
            DisksGrid.Visibility = !useGrouped && _activeTab == "Disks" ? Visibility.Visible : Visibility.Collapsed;
            ApplicationsGrid.Visibility = !useGrouped && _activeTab == "Applications" ? Visibility.Visible : Visibility.Collapsed;
            UpdatesGrid.Visibility = !useGrouped && _activeTab == "WindowsUpdates" ? Visibility.Visible : Visibility.Collapsed;
            ServicesGrid.Visibility = !useGrouped && _activeTab == "Services" ? Visibility.Visible : Visibility.Collapsed;
            DriversGrid.Visibility = !useGrouped && _activeTab == "Drivers" ? Visibility.Visible : Visibility.Collapsed;
            ProfilesGrid.Visibility = !useGrouped && _activeTab == "UserProfiles" ? Visibility.Visible : Visibility.Collapsed;
            LocalGroupsGrid.Visibility = !useGrouped && _activeTab == "LocalGroups" ? Visibility.Visible : Visibility.Collapsed;
            BatteriesGrid.Visibility = !useGrouped && _activeTab == "Batteries" ? Visibility.Visible : Visibility.Collapsed;

            UninstallAppButton.Visibility = _activeTab == "Applications" ? Visibility.Visible : Visibility.Collapsed;
            var isSvc = _activeTab == "Services" ? Visibility.Visible : Visibility.Collapsed;
            StartServiceButton.Visibility = isSvc;
            StopServiceButton.Visibility = isSvc;
            RestartServiceButton.Visibility = isSvc;
            DeleteProfileButton.Visibility = _activeTab == "UserProfiles" ? Visibility.Visible : Visibility.Collapsed;

            var isGrp = _activeTab == "LocalGroups" ? Visibility.Visible : Visibility.Collapsed;
            LocalGroupCombo.Visibility = isGrp;
            AddLocalMemberButton.Visibility = isGrp;
            RemoveLocalMemberButton.Visibility = isGrp;

            if (useGrouped)
            {
                ConfigureGroupedColumnsForTab(_activeTab);
            }
        }

        private void ConfigureGroupedColumnsForTab(string tab)
        {
            switch (tab)
            {
                case "Applications":
                    GroupedGrid.ConfigureColumns(
                        nameHeader: "Application Name",
                        secondaryHeader: "Publisher",
                        versionHeader: "Version",
                        statusHeader: "Summary",
                        childStatusHeader: "Install State",
                        childSecondaryHeader: "Installed On",
                        showSecondary: true,
                        showVersion: true,
                        showChildTimestamp: false,
                        showChildError: false);
                    break;
                case "WindowsUpdates":
                    GroupedGrid.ConfigureColumns(
                        nameHeader: "KB / HotFix ID",
                        secondaryHeader: "Description",
                        versionHeader: "Status",
                        statusHeader: "Summary",
                        childStatusHeader: "Status",
                        childSecondaryHeader: "Installed On",
                        showSecondary: true,
                        showVersion: false,
                        showChildTimestamp: false,
                        showChildError: false);
                    break;
                case "Services":
                    GroupedGrid.ConfigureColumns(
                        nameHeader: "Display Name",
                        secondaryHeader: "Service Name",
                        versionHeader: "Startup Type",
                        statusHeader: "State Summary",
                        childStatusHeader: "State",
                        childSecondaryHeader: "Startup Type",
                        showSecondary: true,
                        showVersion: true,
                        showChildTimestamp: false,
                        showChildError: false);
                    break;
                case "Drivers":
                    GroupedGrid.ConfigureColumns(
                        nameHeader: "Driver / Hardware",
                        secondaryHeader: "Manufacturer",
                        versionHeader: "Version",
                        statusHeader: "Signing",
                        childStatusHeader: "Signed",
                        childSecondaryHeader: "INF File",
                        childTimestampHeader: "Driver Date",
                        showSecondary: true,
                        showVersion: true,
                        showChildTimestamp: true,
                        showChildError: false);
                    break;
                case "UserProfiles":
                    GroupedGrid.ConfigureColumns(
                        nameHeader: "Account",
                        secondaryHeader: "SID",
                        versionHeader: "Local Path",
                        statusHeader: "Profile State",
                        childStatusHeader: "State",
                        childSecondaryHeader: "Local Path",
                        childTimestampHeader: "Last Used",
                        showSecondary: true,
                        showVersion: false,
                        showChildTimestamp: true,
                        showChildError: false);
                    break;
                case "LocalGroups":
                    GroupedGrid.ConfigureColumns(
                        nameHeader: "Member",
                        secondaryHeader: "Local Group",
                        versionHeader: "Type",
                        statusHeader: "Summary",
                        childStatusHeader: "Member Type",
                        childSecondaryHeader: "Local Group",
                        showSecondary: true,
                        showVersion: true,
                        showChildTimestamp: false,
                        showChildError: false);
                    break;
            }
        }

        private async Task LoadActiveTabAsync(bool forceRefresh)
        {
            if (_devices.Count == 0) return;

            var groupName = LocalGroupCombo.SelectedItem as string ?? "Administrators";
            if (!forceRefresh && _loadedTabs.Contains(_activeTab) && (_activeTab != "LocalGroups" || string.Equals(_loadedLocalGroup, groupName, StringComparison.OrdinalIgnoreCase)))
            {
                ApplySearchFilter();
                return;
            }

            LoadingText.Text = _devices.Count == 1
                ? $"Querying {_activeTab} on {_devices[0].DisplayName}..."
                : $"Querying {_activeTab} across 0 / {_devices.Count} computers...";
            LoadingOverlay.Visibility = Visibility.Visible;

            var maxConcurrency = Math.Max(1, _settings.MaxConcurrentDevices);
            var completed = 0;
            var errs = new ConcurrentBag<DeviceErrorEntry>();

            try
            {
                async Task<List<T>> QueryAllAsync<T>(Func<TargetDevice, Task<IEnumerable<T>>> fetch)
                {
                    var bag = new ConcurrentBag<T>();
                    using (var gate = new SemaphoreSlim(maxConcurrency))
                    {
                        var tasks = _devices.Select(async dev =>
                        {
                            await gate.WaitAsync();
                            try
                            {
                                var items = await fetch(dev);
                                foreach (var item in items)
                                {
                                    bag.Add(item);
                                }
                            }
                            catch (Exception ex)
                            {
                                errs.Add(new DeviceErrorEntry
                                {
                                    Source = dev.DisplayName,
                                    Message = $"{_activeTab}: {ex.Message}",
                                    Details = ex.ToString()
                                });
                            }
                            finally
                            {
                                gate.Release();
                                var done = Interlocked.Increment(ref completed);
                                if (_devices.Count > 1)
                                {
                                    Dispatcher.InvokeAsync(() =>
                                        LoadingText.Text = $"Querying {_activeTab} across {done} / {_devices.Count} computers...");
                                }
                            }
                        }).ToList();

                        await Task.WhenAll(tasks);
                    }
                    return bag.ToList();
                }

                switch (_activeTab)
                {
                    case "OS":
                        _osItems = (await QueryAllAsync(async d => new[] { await _collector.GetOperatingSystemAsync(d) }))
                            .OrderBy(x => x.DeviceName, StringComparer.OrdinalIgnoreCase).ToList();
                        break;
                    case "Hardware":
                        _hwItems = (await QueryAllAsync(async d => new[] { await _collector.GetHardwareAsync(d) }))
                            .OrderBy(x => x.DeviceName, StringComparer.OrdinalIgnoreCase).ToList();
                        break;
                    case "Disks":
                        _diskItems = (await QueryAllAsync(async d => (IEnumerable<DiskInfo>)await _collector.GetDisksAsync(d)))
                            .OrderBy(x => x.DeviceName, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.DriveLetter, StringComparer.OrdinalIgnoreCase).ToList();
                        break;
                    case "Applications":
                        _appItems = (await QueryAllAsync(async d => (IEnumerable<InstalledApplicationInfo>)await _collector.GetApplicationsAsync(d)))
                            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.DeviceName, StringComparer.OrdinalIgnoreCase).ToList();
                        break;
                    case "WindowsUpdates":
                        _updateItems = (await QueryAllAsync(async d => (IEnumerable<WindowsUpdateInfo>)await _collector.GetWindowsUpdatesAsync(d)))
                            .OrderBy(x => x.HotFixId, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.DeviceName, StringComparer.OrdinalIgnoreCase).ToList();
                        break;
                    case "Services":
                        _serviceItems = (await QueryAllAsync(async d => (IEnumerable<WindowsServiceInfo>)await _collector.GetServicesAsync(d)))
                            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.DeviceName, StringComparer.OrdinalIgnoreCase).ToList();
                        break;
                    case "Drivers":
                        _driverItems = (await QueryAllAsync(async d => (IEnumerable<DriverInfo>)await _collector.GetDriversAsync(d)))
                            .OrderBy(x => x.FriendlyName, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.DeviceName, StringComparer.OrdinalIgnoreCase).ToList();
                        break;
                    case "UserProfiles":
                        _profileItems = (await QueryAllAsync(async d => (IEnumerable<UserProfileInfo>)await _collector.GetUserProfilesAsync(d)))
                            .OrderBy(x => x.AccountName, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.DeviceName, StringComparer.OrdinalIgnoreCase).ToList();
                        break;
                    case "LocalGroups":
                        _loadedLocalGroup = groupName;
                        _groupItems = (await QueryAllAsync(async d => (IEnumerable<LocalGroupMemberInfo>)await _collector.GetLocalGroupMembersAsync(d, groupName)))
                            .OrderBy(x => x.FullName, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.DeviceName, StringComparer.OrdinalIgnoreCase).ToList();
                        break;
                    case "Batteries":
                        _batteryItems = (await QueryAllAsync(async d => (IEnumerable<BatteryInfo>)await _collector.GetBatteriesAsync(d)))
                            .OrderBy(x => x.DeviceName, StringComparer.OrdinalIgnoreCase).ToList();
                        break;
                }

                _loadedTabs.Add(_activeTab);
                foreach (var err in errs.OrderBy(e => e.Source, StringComparer.OrdinalIgnoreCase))
                {
                    _errors.Add(err);
                }

                UpdateVisiblePane();
                ApplySearchFilter();
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void ApplySearchFilter()
        {
            var term = SearchBox.Text?.Trim() ?? string.Empty;
            var filterDev = ActiveDeviceFilter?.Device;
            var useGrouped = IsGroupedModeActive;

            bool MatchesDevice(string? deviceName) =>
                filterDev == null ||
                string.Equals(deviceName, filterDev.DisplayName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(deviceName, filterDev.TargetHost, StringComparison.OrdinalIgnoreCase);

            bool MatchesText(params string?[] values) =>
                string.IsNullOrEmpty(term) ||
                values.Any(v => v != null && v.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);

            switch (_activeTab)
            {
                case "OS":
                {
                    var rows = _osItems.Where(x => MatchesDevice(x.DeviceName) && MatchesText(x.DeviceName, x.Caption, x.Version, x.BuildNumber, x.LoggedOnUser)).ToList();
                    OsGrid.ItemsSource = rows;
                    StatusSummaryText.Text = $"{rows.Count} OS record(s)";
                    break;
                }
                case "Hardware":
                {
                    var rows = _hwItems.Where(x => MatchesDevice(x.DeviceName) && MatchesText(x.DeviceName, x.Manufacturer, x.Model, x.SerialNumber, x.BiosVersion, x.ProcessorName)).ToList();
                    HardwareGrid.ItemsSource = rows;
                    StatusSummaryText.Text = $"{rows.Count} hardware record(s)";
                    break;
                }
                case "Disks":
                {
                    var rows = _diskItems.Where(x => MatchesDevice(x.DeviceName) && MatchesText(x.DeviceName, x.DriveLetter, x.VolumeName, x.FileSystem)).ToList();
                    DisksGrid.ItemsSource = rows;
                    StatusSummaryText.Text = $"{rows.Count} disk(s)";
                    break;
                }
                case "Applications":
                {
                    var rows = _appItems.Where(x => MatchesDevice(x.DeviceName) && MatchesText(x.DeviceName, x.Name, x.Publisher, x.Version, x.Architecture)).ToList();
                    if (useGrouped)
                    {
                        var groups = ExpandableItemGroup.FromGroupedData(MultiDeviceGrouper.GroupInstalledApplications(rows));
                        GroupedGrid.SetGroups(groups);
                        StatusSummaryText.Text = $"{groups.Count} unique application(s) across {rows.Count} installation(s)";
                    }
                    else
                    {
                        ApplicationsGrid.ItemsSource = rows;
                        StatusSummaryText.Text = $"{rows.Count} application(s)";
                    }
                    break;
                }
                case "WindowsUpdates":
                {
                    var rows = _updateItems.Where(x => MatchesDevice(x.DeviceName) && MatchesText(x.DeviceName, x.HotFixId, x.Description, x.InstalledOn)).ToList();
                    if (useGrouped)
                    {
                        var groups = ExpandableItemGroup.FromGroupedData(MultiDeviceGrouper.GroupWindowsUpdates(rows));
                        GroupedGrid.SetGroups(groups);
                        StatusSummaryText.Text = $"{groups.Count} unique update(s) across {rows.Count} record(s)";
                    }
                    else
                    {
                        UpdatesGrid.ItemsSource = rows;
                        StatusSummaryText.Text = $"{rows.Count} update(s)";
                    }
                    break;
                }
                case "Services":
                {
                    var rows = _serviceItems.Where(x => MatchesDevice(x.DeviceName) && MatchesText(x.DeviceName, x.Name, x.DisplayName, x.State, x.StartMode)).ToList();
                    if (useGrouped)
                    {
                        var groups = ExpandableItemGroup.FromGroupedData(MultiDeviceGrouper.GroupWindowsServices(rows));
                        GroupedGrid.SetGroups(groups);
                        StatusSummaryText.Text = $"{groups.Count} unique service(s) across {rows.Count} instance(s)";
                    }
                    else
                    {
                        ServicesGrid.ItemsSource = rows;
                        StatusSummaryText.Text = $"{rows.Count} service(s)";
                    }
                    break;
                }
                case "Drivers":
                {
                    var rows = _driverItems.Where(x => MatchesDevice(x.DeviceName) && MatchesText(x.DeviceName, x.FriendlyName, x.Manufacturer, x.DeviceClass, x.DriverVersion, x.InfName)).ToList();
                    if (useGrouped)
                    {
                        var groups = ExpandableItemGroup.FromGroupedData(MultiDeviceGrouper.GroupDrivers(rows));
                        GroupedGrid.SetGroups(groups);
                        StatusSummaryText.Text = $"{groups.Count} unique driver(s) across {rows.Count} record(s)";
                    }
                    else
                    {
                        DriversGrid.ItemsSource = rows;
                        StatusSummaryText.Text = $"{rows.Count} driver(s)";
                    }
                    break;
                }
                case "UserProfiles":
                {
                    var rows = _profileItems.Where(x => MatchesDevice(x.DeviceName) && MatchesText(x.DeviceName, x.AccountName, x.LocalPath, x.Sid)).ToList();
                    if (useGrouped)
                    {
                        var groups = ExpandableItemGroup.FromGroupedData(MultiDeviceGrouper.GroupUserProfiles(rows));
                        GroupedGrid.SetGroups(groups);
                        StatusSummaryText.Text = $"{groups.Count} unique account(s) across {rows.Count} profile(s)";
                    }
                    else
                    {
                        ProfilesGrid.ItemsSource = rows;
                        StatusSummaryText.Text = $"{rows.Count} profile(s)";
                    }
                    break;
                }
                case "LocalGroups":
                {
                    var rows = _groupItems.Where(x => MatchesDevice(x.DeviceName) && MatchesText(x.DeviceName, x.FullName, x.GroupName, x.MemberType)).ToList();
                    if (useGrouped)
                    {
                        var groups = ExpandableItemGroup.FromGroupedData(MultiDeviceGrouper.GroupLocalGroupMembers(rows));
                        GroupedGrid.SetGroups(groups);
                        StatusSummaryText.Text = $"{groups.Count} unique member(s) across {rows.Count} membership(s)";
                    }
                    else
                    {
                        LocalGroupsGrid.ItemsSource = rows;
                        StatusSummaryText.Text = $"{rows.Count} member(s)";
                    }
                    break;
                }
                case "Batteries":
                {
                    var rows = _batteryItems.Where(x => MatchesDevice(x.DeviceName) && MatchesText(x.DeviceName, x.Name, x.BatteryStatusDescription)).ToList();
                    BatteriesGrid.ItemsSource = rows;
                    StatusSummaryText.Text = $"{rows.Count} battery record(s)";
                    break;
                }
            }
        }

        private TargetDevice? ResolveDevice(string deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName)) return _devices.FirstOrDefault();
            if (_deviceMap.TryGetValue(deviceName, out var found)) return found;
            return _devices.FirstOrDefault(d =>
                string.Equals(d.DisplayName, deviceName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(d.TargetHost, deviceName, StringComparison.OrdinalIgnoreCase));
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplySearchFilter();

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadActiveTabAsync(forceRefresh: true);

        private async void LocalGroupCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded && _activeTab == "LocalGroups")
            {
                await LoadActiveTabAsync(forceRefresh: true);
            }
        }

        private async void UninstallApp_Click(object sender, RoutedEventArgs e)
        {
            List<CheckedPayloadEntry<InstalledApplicationInfo>> targets;
            if (IsGroupedModeActive)
            {
                targets = GroupedGrid.GetCheckedOrSelectedPayloads<InstalledApplicationInfo>();
            }
            else
            {
                var checkedApps = _appItems.Where(a => a.IsSelected).ToList();
                if (checkedApps.Count == 0 && ApplicationsGrid.SelectedItem is InstalledApplicationInfo single)
                {
                    checkedApps.Add(single);
                }
                targets = checkedApps.Select(a => new CheckedPayloadEntry<InstalledApplicationInfo>
                {
                    DeviceName = a.DeviceName,
                    Payload = a
                }).ToList();
            }

            if (targets.Count == 0)
            {
                MessageBox.Show(this, "Select one or more applications (or expand an item to select specific devices) to uninstall.", "OpenRCT", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var devCount = targets.Select(t => t.DeviceName).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            if (_settings.ConfirmDestructiveActions &&
                MessageBox.Show(this, $"Uninstall {targets.Count} application instance(s) across {devCount} device(s)?", "Confirm Uninstall", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            await RunBulkTargetActionAsync("Uninstalling applications", targets, async (dev, app) =>
            {
                var res = await _collector.UninstallApplicationAsync(dev, app);
                if (!res.IsSuccess)
                {
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(res.ErrorDetails) ? res.Message : $"{res.Message} ({res.ErrorDetails})");
                }
            });
            await LoadActiveTabAsync(forceRefresh: true);
        }

        private async void StartService_Click(object sender, RoutedEventArgs e) => await ControlSelectedServiceAsync("start");
        private async void StopService_Click(object sender, RoutedEventArgs e) => await ControlSelectedServiceAsync("stop");
        private async void RestartService_Click(object sender, RoutedEventArgs e) => await ControlSelectedServiceAsync("restart");

        private async Task ControlSelectedServiceAsync(string operation)
        {
            List<CheckedPayloadEntry<WindowsServiceInfo>> targets;
            if (IsGroupedModeActive)
            {
                targets = GroupedGrid.GetCheckedOrSelectedPayloads<WindowsServiceInfo>();
            }
            else
            {
                targets = ServicesGrid.SelectedItems.OfType<WindowsServiceInfo>()
                    .Select(s => new CheckedPayloadEntry<WindowsServiceInfo> { DeviceName = s.DeviceName, Payload = s })
                    .ToList();
            }

            if (targets.Count == 0)
            {
                MessageBox.Show(this, "Select a service (or expand a service and check devices) first.", "OpenRCT", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (targets.Count > 1 && _settings.ConfirmDestructiveActions)
            {
                var devCount = targets.Select(t => t.DeviceName).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                if (MessageBox.Show(this, $"{char.ToUpperInvariant(operation[0])}{operation.Substring(1)} {targets.Count} service instance(s) across {devCount} device(s)?",
                    "Confirm Service Action", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            await RunBulkTargetActionAsync($"Service '{operation}'", targets, async (dev, svc) =>
            {
                await _collector.ControlServiceAsync(dev, svc.Name, operation);
            });
            await LoadActiveTabAsync(forceRefresh: true);
        }

        private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            List<CheckedPayloadEntry<UserProfileInfo>> targets;
            if (IsGroupedModeActive)
            {
                targets = GroupedGrid.GetCheckedOrSelectedPayloads<UserProfileInfo>();
            }
            else
            {
                targets = ProfilesGrid.SelectedItems.OfType<UserProfileInfo>()
                    .Select(p => new CheckedPayloadEntry<UserProfileInfo> { DeviceName = p.DeviceName, Payload = p })
                    .ToList();
            }

            if (targets.Count == 0) return;

            var devCount = targets.Select(t => t.DeviceName).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            if (MessageBox.Show(this, $"Permanently delete {targets.Count} user profile(s) across {devCount} device(s)?",
                "Confirm Delete Profile", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            await RunBulkTargetActionAsync("Deleting user profiles", targets, async (dev, prof) =>
            {
                await _collector.DeleteUserProfileAsync(dev, prof.Sid);
            });
            await LoadActiveTabAsync(forceRefresh: true);
        }

        private async void AddLocalMember_Click(object sender, RoutedEventArgs e)
        {
            var scope = ActiveScopeDevices;
            var groupName = LocalGroupCombo.SelectedItem as string ?? "Administrators";
            if (scope.Count == 0) return;

            var scopeLabel = scope.Count == 1 ? scope[0].DisplayName : $"{scope.Count} computers";
            var prompt = new InputPromptWindow($"Add Member to {groupName} ({scopeLabel})", "Enter Domain\\Username or LocalAccount:");
            prompt.Owner = this;
            if (prompt.ShowDialog() != true || string.IsNullOrWhiteSpace(prompt.ResponseText)) return;

            var member = prompt.ResponseText.Trim();
            var targets = scope.Select(d => new CheckedPayloadEntry<string> { DeviceName = d.DisplayName, Payload = member }).ToList();
            await RunBulkTargetActionAsync($"Adding {member} to {groupName}", targets, async (dev, acct) =>
            {
                await _collector.ModifyLocalGroupMemberAsync(dev, groupName, acct, add: true);
            });
            await LoadActiveTabAsync(forceRefresh: true);
        }

        private async void RemoveLocalMember_Click(object sender, RoutedEventArgs e)
        {
            var groupName = LocalGroupCombo.SelectedItem as string ?? "Administrators";
            List<CheckedPayloadEntry<LocalGroupMemberInfo>> targets;
            if (IsGroupedModeActive)
            {
                targets = GroupedGrid.GetCheckedOrSelectedPayloads<LocalGroupMemberInfo>();
            }
            else
            {
                targets = LocalGroupsGrid.SelectedItems.OfType<LocalGroupMemberInfo>()
                    .Select(m => new CheckedPayloadEntry<LocalGroupMemberInfo> { DeviceName = m.DeviceName, Payload = m })
                    .ToList();
            }

            if (targets.Count == 0) return;

            var devCount = targets.Select(t => t.DeviceName).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            if (MessageBox.Show(this, $"Remove {targets.Count} local group membership(s) from '{groupName}' across {devCount} device(s)?",
                "Confirm Remove Member", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            await RunBulkTargetActionAsync($"Removing members from {groupName}", targets, async (dev, m) =>
            {
                await _collector.ModifyLocalGroupMemberAsync(dev, string.IsNullOrWhiteSpace(m.GroupName) ? groupName : m.GroupName, m.FullName, add: false);
            });
            await LoadActiveTabAsync(forceRefresh: true);
        }

        private async Task RunBulkTargetActionAsync<T>(
            string label,
            IReadOnlyList<CheckedPayloadEntry<T>> targets,
            Func<TargetDevice, T, Task> action)
        {
            LoadingOverlay.Visibility = Visibility.Visible;
            LoadingText.Text = $"{label} (0 / {targets.Count})...";
            var done = 0;
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
                            await action(dev, entry.Payload);
                        }
                        catch (Exception ex)
                        {
                            errs.Add(new DeviceErrorEntry { Source = entry.DeviceName, Message = ex.Message, Details = ex.ToString() });
                        }
                        finally
                        {
                            gate.Release();
                            var c = Interlocked.Increment(ref done);
                            Dispatcher.InvokeAsync(() => LoadingText.Text = $"{label} ({c} / {targets.Count})...");
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
        }

        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv",
                FileName = $"OpenRCT_SystemInfo_{_activeTab}_{DateTime.Now:yyyyMMdd_HHmm}.csv"
            };
            if (dlg.ShowDialog(this) != true) return;

            switch (_activeTab)
            {
                case "Hardware":
                    CsvExporter.WriteToFile(dlg.FileName, _hwItems, new[]
                    {
                        new CsvColumn<HardwareInfo>("Device", x => x.DeviceName),
                        new CsvColumn<HardwareInfo>("Manufacturer", x => x.Manufacturer),
                        new CsvColumn<HardwareInfo>("Model", x => x.Model),
                        new CsvColumn<HardwareInfo>("Serial Number", x => x.SerialNumber),
                        new CsvColumn<HardwareInfo>("BIOS", x => x.BiosVersion),
                        new CsvColumn<HardwareInfo>("CPU", x => x.ProcessorName),
                        new CsvColumn<HardwareInfo>("RAM (GB)", x => x.TotalMemoryGb),
                        new CsvColumn<HardwareInfo>("TPM", x => x.TpmVersion)
                    });
                    break;
                case "Disks":
                    CsvExporter.WriteToFile(dlg.FileName, _diskItems, new[]
                    {
                        new CsvColumn<DiskInfo>("Device", x => x.DeviceName),
                        new CsvColumn<DiskInfo>("Drive", x => x.DriveLetter),
                        new CsvColumn<DiskInfo>("Volume", x => x.VolumeName),
                        new CsvColumn<DiskInfo>("FileSystem", x => x.FileSystem),
                        new CsvColumn<DiskInfo>("Total GB", x => x.TotalSizeGb),
                        new CsvColumn<DiskInfo>("Free GB", x => x.FreeSpaceGb),
                        new CsvColumn<DiskInfo>("% Free", x => x.PercentFree)
                    });
                    break;
                case "Applications":
                    CsvExporter.WriteToFile(dlg.FileName, _appItems, new[]
                    {
                        new CsvColumn<InstalledApplicationInfo>("Device", x => x.DeviceName),
                        new CsvColumn<InstalledApplicationInfo>("Name", x => x.Name),
                        new CsvColumn<InstalledApplicationInfo>("Architecture", x => x.Architecture),
                        new CsvColumn<InstalledApplicationInfo>("Publisher", x => x.Publisher),
                        new CsvColumn<InstalledApplicationInfo>("Installed On", x => x.InstalledOn),
                        new CsvColumn<InstalledApplicationInfo>("Version", x => x.Version),
                        new CsvColumn<InstalledApplicationInfo>("Uninstall String", x => x.UninstallString)
                    });
                    break;
                case "WindowsUpdates":
                    CsvExporter.WriteToFile(dlg.FileName, _updateItems, new[]
                    {
                        new CsvColumn<WindowsUpdateInfo>("Device", x => x.DeviceName),
                        new CsvColumn<WindowsUpdateInfo>("KB / HotFix ID", x => x.HotFixId),
                        new CsvColumn<WindowsUpdateInfo>("Description", x => x.Description),
                        new CsvColumn<WindowsUpdateInfo>("Installed On", x => x.InstalledOn),
                        new CsvColumn<WindowsUpdateInfo>("Installed By", x => x.InstalledBy),
                        new CsvColumn<WindowsUpdateInfo>("Status", x => x.Status)
                    });
                    break;
                case "Services":
                    CsvExporter.WriteToFile(dlg.FileName, _serviceItems, new[]
                    {
                        new CsvColumn<WindowsServiceInfo>("Device", x => x.DeviceName),
                        new CsvColumn<WindowsServiceInfo>("Name", x => x.Name),
                        new CsvColumn<WindowsServiceInfo>("Display Name", x => x.DisplayName),
                        new CsvColumn<WindowsServiceInfo>("State", x => x.State),
                        new CsvColumn<WindowsServiceInfo>("Start Mode", x => x.StartMode),
                        new CsvColumn<WindowsServiceInfo>("Log On As", x => x.StartName)
                    });
                    break;
                case "Drivers":
                    CsvExporter.WriteToFile(dlg.FileName, _driverItems, new[]
                    {
                        new CsvColumn<DriverInfo>("Device", x => x.DeviceName),
                        new CsvColumn<DriverInfo>("Driver / Hardware", x => x.FriendlyName),
                        new CsvColumn<DriverInfo>("Class", x => x.DeviceClass),
                        new CsvColumn<DriverInfo>("Manufacturer", x => x.Manufacturer),
                        new CsvColumn<DriverInfo>("Version", x => x.DriverVersion),
                        new CsvColumn<DriverInfo>("Date", x => x.DriverDate),
                        new CsvColumn<DriverInfo>("Signed", x => x.IsSigned),
                        new CsvColumn<DriverInfo>("INF", x => x.InfName)
                    });
                    break;
                case "UserProfiles":
                    CsvExporter.WriteToFile(dlg.FileName, _profileItems, new[]
                    {
                        new CsvColumn<UserProfileInfo>("Device", x => x.DeviceName),
                        new CsvColumn<UserProfileInfo>("Account", x => x.AccountName),
                        new CsvColumn<UserProfileInfo>("Local Path", x => x.LocalPath),
                        new CsvColumn<UserProfileInfo>("Last Used", x => x.LastUseTime),
                        new CsvColumn<UserProfileInfo>("Loaded", x => x.Loaded),
                        new CsvColumn<UserProfileInfo>("Special", x => x.Special),
                        new CsvColumn<UserProfileInfo>("SID", x => x.Sid)
                    });
                    break;
                case "LocalGroups":
                    CsvExporter.WriteToFile(dlg.FileName, _groupItems, new[]
                    {
                        new CsvColumn<LocalGroupMemberInfo>("Device", x => x.DeviceName),
                        new CsvColumn<LocalGroupMemberInfo>("Group", x => x.GroupName),
                        new CsvColumn<LocalGroupMemberInfo>("Member", x => x.FullName),
                        new CsvColumn<LocalGroupMemberInfo>("Domain", x => x.Domain),
                        new CsvColumn<LocalGroupMemberInfo>("Type", x => x.MemberType)
                    });
                    break;
                case "Batteries":
                    CsvExporter.WriteToFile(dlg.FileName, _batteryItems, new[]
                    {
                        new CsvColumn<BatteryInfo>("Device", x => x.DeviceName),
                        new CsvColumn<BatteryInfo>("Battery Name", x => x.Name),
                        new CsvColumn<BatteryInfo>("Status", x => x.BatteryStatusDescription),
                        new CsvColumn<BatteryInfo>("Charge %", x => x.EstimatedChargeRemaining),
                        new CsvColumn<BatteryInfo>("Design (mWh)", x => x.DesignCapacityMwh),
                        new CsvColumn<BatteryInfo>("Full Charge (mWh)", x => x.FullChargeCapacityMwh),
                        new CsvColumn<BatteryInfo>("Health %", x => x.HealthPercent)
                    });
                    break;
                default:
                    CsvExporter.WriteToFile(dlg.FileName, _osItems, new[]
                    {
                        new CsvColumn<OperatingSystemInfo>("Device", x => x.DeviceName),
                        new CsvColumn<OperatingSystemInfo>("Caption", x => x.Caption),
                        new CsvColumn<OperatingSystemInfo>("Version", x => x.Version),
                        new CsvColumn<OperatingSystemInfo>("Build", x => x.BuildNumber),
                        new CsvColumn<OperatingSystemInfo>("Last Boot", x => x.LastBootUpTime),
                        new CsvColumn<OperatingSystemInfo>("Logged On User", x => x.LoggedOnUser)
                    });
                    break;
            }
        }
    }
}
