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

namespace OpenRCT.UI.Views
{
    public partial class SystemInfoWindow : Window
    {
        private readonly IReadOnlyList<TargetDevice> _devices;
        private readonly ISystemInfoCollector _collector;
        private readonly OpenRctSettings _settings;
        private readonly ObservableCollection<DeviceErrorEntry> _errors = new ObservableCollection<DeviceErrorEntry>();

        private string _activeTab = "OS";
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
            _collector = collector;
            _settings = settings;

            ThemeManager.ApplyTheme(this, settings.Theme);
            ErrorDrawer.Errors = _errors;

            Title = _devices.Count == 1
                ? $"System Information - {_devices[0].DisplayName}"
                : $"System Information - {_devices.Count} Devices";

            DeviceSelector.ItemsSource = _devices;
            if (_devices.Count > 0) DeviceSelector.SelectedIndex = 0;

            LocalGroupCombo.ItemsSource = new[] { "Administrators", "Remote Desktop Users", "Users", "Power Users", "Hyper-V Administrators" };
            LocalGroupCombo.SelectedIndex = 0;

            Loaded += async (_, __) => await LoadActiveTabAsync();
        }

        private TargetDevice? SelectedDevice => DeviceSelector.SelectedItem as TargetDevice;

        private async void DeviceSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded) await LoadActiveTabAsync();
        }

        private async void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            if (NavList.SelectedItem is ListBoxItem item && item.Tag is string tag)
            {
                _activeTab = tag;
                UpdateVisiblePane();
                await LoadActiveTabAsync();
            }
        }

        private void UpdateVisiblePane()
        {
            OsGrid.Visibility = _activeTab == "OS" ? Visibility.Visible : Visibility.Collapsed;
            HardwareGrid.Visibility = _activeTab == "Hardware" ? Visibility.Visible : Visibility.Collapsed;
            DisksGrid.Visibility = _activeTab == "Disks" ? Visibility.Visible : Visibility.Collapsed;
            ApplicationsGrid.Visibility = _activeTab == "Applications" ? Visibility.Visible : Visibility.Collapsed;
            UpdatesGrid.Visibility = _activeTab == "WindowsUpdates" ? Visibility.Visible : Visibility.Collapsed;
            ServicesGrid.Visibility = _activeTab == "Services" ? Visibility.Visible : Visibility.Collapsed;
            DriversGrid.Visibility = _activeTab == "Drivers" ? Visibility.Visible : Visibility.Collapsed;
            ProfilesGrid.Visibility = _activeTab == "UserProfiles" ? Visibility.Visible : Visibility.Collapsed;
            LocalGroupsGrid.Visibility = _activeTab == "LocalGroups" ? Visibility.Visible : Visibility.Collapsed;
            BatteriesGrid.Visibility = _activeTab == "Batteries" ? Visibility.Visible : Visibility.Collapsed;

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
        }

        private async Task LoadActiveTabAsync()
        {
            var dev = SelectedDevice;
            if (dev == null) return;

            LoadingText.Text = $"Querying {_activeTab} on {dev.DisplayName}...";
            LoadingOverlay.Visibility = Visibility.Visible;
            try
            {
                switch (_activeTab)
                {
                    case "OS":
                        _osItems = new List<OperatingSystemInfo> { await _collector.GetOperatingSystemAsync(dev) };
                        break;
                    case "Hardware":
                        _hwItems = new List<HardwareInfo> { await _collector.GetHardwareAsync(dev) };
                        break;
                    case "Disks":
                        _diskItems = (await _collector.GetDisksAsync(dev)).ToList();
                        break;
                    case "Applications":
                        _appItems = (await _collector.GetApplicationsAsync(dev)).ToList();
                        break;
                    case "WindowsUpdates":
                        _updateItems = (await _collector.GetWindowsUpdatesAsync(dev)).ToList();
                        break;
                    case "Services":
                        _serviceItems = (await _collector.GetServicesAsync(dev)).ToList();
                        break;
                    case "Drivers":
                        _driverItems = (await _collector.GetDriversAsync(dev)).ToList();
                        break;
                    case "UserProfiles":
                        _profileItems = (await _collector.GetUserProfilesAsync(dev)).ToList();
                        break;
                    case "LocalGroups":
                        var groupName = LocalGroupCombo.SelectedItem as string ?? "Administrators";
                        _groupItems = (await _collector.GetLocalGroupMembersAsync(dev, groupName)).ToList();
                        break;
                    case "Batteries":
                        _batteryItems = (await _collector.GetBatteriesAsync(dev)).ToList();
                        break;
                }
                ApplySearchFilter();
            }
            catch (Exception ex)
            {
                _errors.Add(new DeviceErrorEntry { Source = dev.DisplayName, Message = $"{_activeTab}: {ex.Message}", Details = ex.ToString() });
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void ApplySearchFilter()
        {
            var term = SearchBox.Text?.Trim() ?? string.Empty;
            bool Matches(string? s) => string.IsNullOrEmpty(term) || (s != null && s.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);

            switch (_activeTab)
            {
                case "OS":
                    OsGrid.ItemsSource = _osItems.Where(x => Matches(x.Caption) || Matches(x.Version) || Matches(x.LoggedOnUser)).ToList();
                    StatusSummaryText.Text = $"{_osItems.Count} OS record(s)";
                    break;
                case "Hardware":
                    HardwareGrid.ItemsSource = _hwItems.Where(x => Matches(x.Manufacturer) || Matches(x.Model) || Matches(x.SerialNumber)).ToList();
                    StatusSummaryText.Text = $"{_hwItems.Count} hardware record(s)";
                    break;
                case "Disks":
                    var disks = _diskItems.Where(x => Matches(x.DriveLetter) || Matches(x.VolumeName) || Matches(x.FileSystem)).ToList();
                    DisksGrid.ItemsSource = disks;
                    StatusSummaryText.Text = $"{disks.Count} disk(s)";
                    break;
                case "Applications":
                    var apps = _appItems.Where(x => Matches(x.Name) || Matches(x.Publisher) || Matches(x.Version)).ToList();
                    ApplicationsGrid.ItemsSource = apps;
                    StatusSummaryText.Text = $"{apps.Count} application(s)";
                    break;
                case "WindowsUpdates":
                    var upd = _updateItems.Where(x => Matches(x.HotFixId) || Matches(x.Description)).ToList();
                    UpdatesGrid.ItemsSource = upd;
                    StatusSummaryText.Text = $"{upd.Count} update(s)";
                    break;
                case "Services":
                    var svcs = _serviceItems.Where(x => Matches(x.Name) || Matches(x.DisplayName) || Matches(x.State)).ToList();
                    ServicesGrid.ItemsSource = svcs;
                    StatusSummaryText.Text = $"{svcs.Count} service(s)";
                    break;
                case "Drivers":
                    var drv = _driverItems.Where(x => Matches(x.FriendlyName) || Matches(x.Manufacturer) || Matches(x.DeviceClass)).ToList();
                    DriversGrid.ItemsSource = drv;
                    StatusSummaryText.Text = $"{drv.Count} driver(s)";
                    break;
                case "UserProfiles":
                    var profs = _profileItems.Where(x => Matches(x.AccountName) || Matches(x.LocalPath) || Matches(x.Sid)).ToList();
                    ProfilesGrid.ItemsSource = profs;
                    StatusSummaryText.Text = $"{profs.Count} profile(s)";
                    break;
                case "LocalGroups":
                    var grps = _groupItems.Where(x => Matches(x.FullName) || Matches(x.MemberType)).ToList();
                    LocalGroupsGrid.ItemsSource = grps;
                    StatusSummaryText.Text = $"{grps.Count} member(s)";
                    break;
                case "Batteries":
                    var bats = _batteryItems.Where(x => Matches(x.Name) || Matches(x.BatteryStatusDescription)).ToList();
                    BatteriesGrid.ItemsSource = bats;
                    StatusSummaryText.Text = $"{bats.Count} battery record(s)";
                    break;
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplySearchFilter();
        private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadActiveTabAsync();
        private async void LocalGroupCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded && _activeTab == "LocalGroups") await LoadActiveTabAsync();
        }

        private async void UninstallApp_Click(object sender, RoutedEventArgs e)
        {
            var dev = SelectedDevice;
            if (dev == null) return;
            var checkedApps = _appItems.Where(a => a.IsSelected).ToList();
            if (checkedApps.Count == 0 && ApplicationsGrid.SelectedItem is InstalledApplicationInfo single)
            {
                checkedApps.Add(single);
            }
            if (checkedApps.Count == 0)
            {
                MessageBox.Show(this, "Select one or more applications to uninstall.", "OpenRCT", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (_settings.ConfirmDestructiveActions &&
                MessageBox.Show(this, $"Uninstall {checkedApps.Count} application(s) on {dev.DisplayName}?", "Confirm Uninstall", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            foreach (var app in checkedApps)
            {
                try
                {
                    var res = await _collector.UninstallApplicationAsync(dev, app);
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
            await LoadActiveTabAsync();
        }

        private async void StartService_Click(object sender, RoutedEventArgs e) => await ControlSelectedServiceAsync("start");
        private async void StopService_Click(object sender, RoutedEventArgs e) => await ControlSelectedServiceAsync("stop");
        private async void RestartService_Click(object sender, RoutedEventArgs e) => await ControlSelectedServiceAsync("restart");

        private async Task ControlSelectedServiceAsync(string operation)
        {
            var dev = SelectedDevice;
            if (dev == null || !(ServicesGrid.SelectedItem is WindowsServiceInfo svc)) return;
            try
            {
                await _collector.ControlServiceAsync(dev, svc.Name, operation);
                await LoadActiveTabAsync();
            }
            catch (Exception ex)
            {
                _errors.Add(new DeviceErrorEntry { Source = dev.DisplayName, Message = ex.Message, Details = ex.ToString() });
            }
        }

        private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            var dev = SelectedDevice;
            if (dev == null || !(ProfilesGrid.SelectedItem is UserProfileInfo prof)) return;
            if (MessageBox.Show(this, $"Permanently delete user profile '{prof.AccountName}' ({prof.LocalPath}) on {dev.DisplayName}?",
                "Confirm Delete Profile", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }
            try
            {
                await _collector.DeleteUserProfileAsync(dev, prof.Sid);
                await LoadActiveTabAsync();
            }
            catch (Exception ex)
            {
                _errors.Add(new DeviceErrorEntry { Source = dev.DisplayName, Message = ex.Message, Details = ex.ToString() });
            }
        }

        private async void AddLocalMember_Click(object sender, RoutedEventArgs e)
        {
            var dev = SelectedDevice;
            var groupName = LocalGroupCombo.SelectedItem as string ?? "Administrators";
            if (dev == null) return;
            var prompt = new InputPromptWindow($"Add Member to {groupName}", "Enter Domain\\Username or LocalAccount:");
            prompt.Owner = this;
            if (prompt.ShowDialog() == true && !string.IsNullOrWhiteSpace(prompt.ResponseText))
            {
                try
                {
                    await _collector.ModifyLocalGroupMemberAsync(dev, groupName, prompt.ResponseText, add: true);
                    await LoadActiveTabAsync();
                }
                catch (Exception ex)
                {
                    _errors.Add(new DeviceErrorEntry { Source = dev.DisplayName, Message = ex.Message, Details = ex.ToString() });
                }
            }
        }

        private async void RemoveLocalMember_Click(object sender, RoutedEventArgs e)
        {
            var dev = SelectedDevice;
            var groupName = LocalGroupCombo.SelectedItem as string ?? "Administrators";
            if (dev == null || !(LocalGroupsGrid.SelectedItem is LocalGroupMemberInfo member)) return;
            if (MessageBox.Show(this, $"Remove '{member.FullName}' from '{groupName}' on {dev.DisplayName}?",
                "Confirm Remove Member", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }
            try
            {
                await _collector.ModifyLocalGroupMemberAsync(dev, groupName, member.FullName, add: false);
                await LoadActiveTabAsync();
            }
            catch (Exception ex)
            {
                _errors.Add(new DeviceErrorEntry { Source = dev.DisplayName, Message = ex.Message, Details = ex.ToString() });
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
                default:
                    CsvExporter.WriteToFile(dlg.FileName, _osItems, new[]
                    {
                        new CsvColumn<OperatingSystemInfo>("Device", x => x.DeviceName),
                        new CsvColumn<OperatingSystemInfo>("Caption", x => x.Caption),
                        new CsvColumn<OperatingSystemInfo>("Version", x => x.Version),
                        new CsvColumn<OperatingSystemInfo>("Build", x => x.BuildNumber),
                        new CsvColumn<OperatingSystemInfo>("Last Boot", x => x.LastBootUpTime)
                    });
                    break;
            }
        }
    }
}
