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

namespace OpenRCT.UI.Views.Tools
{
    public partial class RemoteSoftwareCenterWindow : Window
    {
        private readonly IReadOnlyList<TargetDevice> _devices;
        private readonly Dictionary<string, TargetDevice> _deviceMap;
        private readonly IEndpointActionService _service;
        private readonly OpenRctSettings _settings;
        private readonly ObservableCollection<DeviceErrorEntry> _errors = new ObservableCollection<DeviceErrorEntry>();

        private List<RemoteSoftwareCenterItem> _allItems = new List<RemoteSoftwareCenterItem>();
        private string _activeSection = "Applications";
        private bool _isLoading;

        public RemoteSoftwareCenterWindow(IReadOnlyList<TargetDevice> devices, IEndpointActionService service, OpenRctSettings settings)
        {
            _devices = devices ?? Array.Empty<TargetDevice>();
            _deviceMap = new Dictionary<string, TargetDevice>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in _devices)
            {
                if (!string.IsNullOrWhiteSpace(d.DisplayName) && !_deviceMap.ContainsKey(d.DisplayName))
                {
                    _deviceMap[d.DisplayName] = d;
                }
                if (!string.IsNullOrWhiteSpace(d.ConnectAddress) && !_deviceMap.ContainsKey(d.ConnectAddress))
                {
                    _deviceMap[d.ConnectAddress] = d;
                }
            }

            _service = service;
            _settings = settings;

            ThemeManager.ApplyTheme(this, settings.Theme);
            InitializeComponent();
            ErrorDrawer.Errors = _errors;

            Title = _devices.Count == 1
                ? $"Remote Software Center - {_devices[0].DisplayName}"
                : $"Remote Software Center - {_devices.Count} computers";
            ScopeSubtitleText.Text = _devices.Count == 1
                ? _devices[0].DisplayName
                : $"{_devices.Count} targeted computers";

            var filterOptions = DeviceFilterOption.BuildOptions(_devices);
            DeviceFilterCombo.ItemsSource = filterOptions;
            if (filterOptions.Count > 0) DeviceFilterCombo.SelectedIndex = 0;

            UpdateSectionChrome();
            Loaded += async (_, __) => await LoadAsync();
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

        private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            if (NavList.SelectedItem is ListBoxItem item && item.Tag is string tag)
            {
                _activeSection = tag;
                UpdateSectionChrome();
                ApplyFilterAndBind();
            }
        }

        private void DeviceFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded) ApplyFilterAndBind();
        }

        private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (IsLoaded) ApplyFilterAndBind();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

        private void UpdateSectionChrome()
        {
            var isApps = _activeSection == "Applications";
            var isUpdates = _activeSection == "SoftwareUpdates";
            var isPackages = _activeSection == "TaskSequences";
            var isCompliance = _activeSection == "Compliance";

            InstallAppButton.Visibility = isApps ? Visibility.Visible : Visibility.Collapsed;
            UninstallAppButton.Visibility = isApps ? Visibility.Visible : Visibility.Collapsed;
            RepairAppButton.Visibility = isApps ? Visibility.Visible : Visibility.Collapsed;
            TriggerAppEvalButton.Visibility = isApps ? Visibility.Visible : Visibility.Collapsed;

            InstallUpdateButton.Visibility = isUpdates ? Visibility.Visible : Visibility.Collapsed;
            InstallAllUpdatesButton.Visibility = isUpdates ? Visibility.Visible : Visibility.Collapsed;
            TriggerUpdateScanButton.Visibility = isUpdates ? Visibility.Visible : Visibility.Collapsed;
            TriggerUpdateEvalButton.Visibility = isUpdates ? Visibility.Visible : Visibility.Collapsed;

            RunPackageButton.Visibility = isPackages ? Visibility.Visible : Visibility.Collapsed;
            EvaluateBaselineButton.Visibility = isCompliance ? Visibility.Visible : Visibility.Collapsed;

            if (isApps)
            {
                GroupedGrid.ConfigureColumns(
                    nameHeader: "Name",
                    secondaryHeader: "Publisher",
                    versionHeader: "Version",
                    statusHeader: "Install State",
                    childStatusHeader: "Install State",
                    childSecondaryHeader: "Evaluation State",
                    childTimestampHeader: "Deadline",
                    childErrorHeader: "Error Code",
                    showSecondary: true,
                    showVersion: true,
                    showChildTimestamp: true,
                    showChildError: true);
            }
            else if (isUpdates)
            {
                GroupedGrid.ConfigureColumns(
                    nameHeader: "Update Name",
                    secondaryHeader: "Publisher",
                    versionHeader: "Article / KB",
                    statusHeader: "Compliance",
                    childStatusHeader: "Compliance",
                    childSecondaryHeader: "Evaluation State",
                    childTimestampHeader: "Deadline",
                    childErrorHeader: "Error Code",
                    showSecondary: false,
                    showVersion: true,
                    showChildTimestamp: true,
                    showChildError: true);
            }
            else if (isPackages)
            {
                GroupedGrid.ConfigureColumns(
                    nameHeader: "Program / Task Sequence",
                    secondaryHeader: "Package",
                    versionHeader: "Version",
                    statusHeader: "Last Run Status",
                    childStatusHeader: "Last Run Status",
                    childSecondaryHeader: "Package Name",
                    childTimestampHeader: "Last Run Time",
                    childErrorHeader: "Error Code",
                    showSecondary: true,
                    showVersion: false,
                    showChildTimestamp: true,
                    showChildError: true);
            }
            else
            {
                GroupedGrid.ConfigureColumns(
                    nameHeader: "Baseline Name",
                    secondaryHeader: "Publisher",
                    versionHeader: "Version",
                    statusHeader: "Compliance",
                    childStatusHeader: "Compliance State",
                    childSecondaryHeader: "Evaluation",
                    childTimestampHeader: "Last Evaluation",
                    childErrorHeader: "Error Code",
                    showSecondary: false,
                    showVersion: true,
                    showChildTimestamp: true,
                    showChildError: false);
            }
        }

        private async Task LoadAsync()
        {
            if (_isLoading || _devices.Count == 0) return;
            _isLoading = true;
            _errors.Clear();
            LoadingOverlay.Visibility = Visibility.Visible;
            LoadingText.Text = _devices.Count == 1
                ? $"Querying Software Center on {_devices[0].DisplayName}..."
                : $"Querying Software Center across 0 / {_devices.Count} computers...";

            var collected = new ConcurrentBag<RemoteSoftwareCenterItem>();
            var errs = new ConcurrentBag<DeviceErrorEntry>();
            var completed = 0;
            var maxConcurrency = Math.Max(1, _settings.MaxConcurrentDevices);

            try
            {
                using (var gate = new SemaphoreSlim(maxConcurrency))
                {
                    var tasks = _devices.Select(async dev =>
                    {
                        await gate.WaitAsync();
                        try
                        {
                            var items = await _service.GetRemoteSoftwareCenterItemsAsync(dev);
                            foreach (var item in items)
                            {
                                if (string.IsNullOrWhiteSpace(item.DeviceName))
                                {
                                    item.DeviceName = dev.DisplayName;
                                }
                                collected.Add(item);
                            }
                        }
                        catch (Exception ex)
                        {
                            errs.Add(new DeviceErrorEntry
                            {
                                Source = dev.DisplayName,
                                Message = ex.Message,
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
                                    LoadingText.Text = $"Querying Software Center across {done} / {_devices.Count} computers...");
                            }
                        }
                    }).ToList();

                    await Task.WhenAll(tasks);
                }

                _allItems = collected.ToList();
                foreach (var err in errs.OrderBy(e => e.Source, StringComparer.OrdinalIgnoreCase))
                {
                    _errors.Add(err);
                }

                ApplyFilterAndBind();
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
                _isLoading = false;
            }
        }

        private void ApplyFilterAndBind()
        {
            var term = FilterBox.Text?.Trim() ?? string.Empty;
            var filterDev = ActiveDeviceFilter?.Device;

            bool Matches(RemoteSoftwareCenterItem i)
            {
                if (filterDev != null &&
                    !string.Equals(i.DeviceName, filterDev.DisplayName, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(i.DeviceName, filterDev.ConnectAddress, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (string.IsNullOrEmpty(term)) return true;

                return (i.Name != null && i.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) ||
                       (i.Publisher != null && i.Publisher.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) ||
                       (i.SoftwareVersion != null && i.SoftwareVersion.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) ||
                       (i.PackageName != null && i.PackageName.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) ||
                       (i.DeviceName != null && i.DeviceName.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) ||
                       (i.InstallState != null && i.InstallState.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) ||
                       (i.ComplianceState != null && i.ComplianceState.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            var filtered = _allItems.Where(Matches).ToList();

            var appGroups = MultiDeviceGrouper.GroupSoftwareCenterItems(filtered, SoftwareCenterItemType.Application);
            var updateGroups = MultiDeviceGrouper.GroupSoftwareCenterItems(filtered, SoftwareCenterItemType.SoftwareUpdate);
            var pkgGroups = MultiDeviceGrouper.GroupSoftwareCenterItems(filtered, SoftwareCenterItemType.PackageOrTaskSequence);
            var compGroups = MultiDeviceGrouper.GroupSoftwareCenterItems(filtered, SoftwareCenterItemType.ComplianceBaseline);

            AppsNavItem.Content = $"Applications ({appGroups.Count})";
            UpdatesNavItem.Content = $"Software Updates ({updateGroups.Count})";
            PackagesNavItem.Content = $"Task Sequences ({pkgGroups.Count})";
            ComplianceNavItem.Content = $"Compliance ({compGroups.Count})";

            List<ExpandableItemGroup> activeGroups;
            switch (_activeSection)
            {
                case "SoftwareUpdates":
                    activeGroups = ExpandableItemGroup.FromGroupedData(updateGroups);
                    break;
                case "TaskSequences":
                    activeGroups = ExpandableItemGroup.FromGroupedData(pkgGroups);
                    break;
                case "Compliance":
                    activeGroups = ExpandableItemGroup.FromGroupedData(compGroups);
                    break;
                default:
                    activeGroups = ExpandableItemGroup.FromGroupedData(appGroups);
                    break;
            }

            // When looking at a single device, keep groups collapsed by default unless user expands,
            // and show clean per-item count in status bar.
            GroupedGrid.SetGroups(activeGroups);

            var deviceCount = filtered.Select(i => i.DeviceName).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            StatusText.Text = $"{activeGroups.Count} item(s) across {deviceCount} device(s) ({_errors.Count} error(s))";
        }

        private TargetDevice? ResolveDevice(string deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName)) return _devices.FirstOrDefault();
            if (_deviceMap.TryGetValue(deviceName, out var found)) return found;
            return _devices.FirstOrDefault(d =>
                string.Equals(d.DisplayName, deviceName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(d.ConnectAddress, deviceName, StringComparison.OrdinalIgnoreCase));
        }

        private async void InstallApp_Click(object sender, RoutedEventArgs e) =>
            await InvokeSelectedItemsAsync("Install", uninstall: false, repair: false);

        private async void UninstallApp_Click(object sender, RoutedEventArgs e) =>
            await InvokeSelectedItemsAsync("Uninstall", uninstall: true, repair: false);

        private async void RepairApp_Click(object sender, RoutedEventArgs e) =>
            await InvokeSelectedItemsAsync("Repair", uninstall: false, repair: true);

        private async void InstallUpdate_Click(object sender, RoutedEventArgs e) =>
            await InvokeSelectedItemsAsync("Install Update", uninstall: false, repair: false);

        private async void RunPackage_Click(object sender, RoutedEventArgs e) =>
            await InvokeSelectedItemsAsync("Run", uninstall: false, repair: false);

        private async void EvaluateBaseline_Click(object sender, RoutedEventArgs e) =>
            await InvokeSelectedItemsAsync("Evaluate Baseline", uninstall: false, repair: false);

        private async Task InvokeSelectedItemsAsync(string verb, bool uninstall, bool repair)
        {
            var targets = GroupedGrid.GetCheckedOrSelectedPayloads<RemoteSoftwareCenterItem>();
            if (targets.Count == 0)
            {
                MessageBox.Show(this, "Check one or more items (or expand an item to check specific devices) first.", "OpenRCT", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var distinctDevices = targets.Select(t => t.DeviceName).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            if ((uninstall || targets.Count > 1) && _settings.ConfirmDestructiveActions)
            {
                var confirm = MessageBox.Show(
                    this,
                    $"{verb} {targets.Count} item assignment(s) across {distinctDevices} device(s)?",
                    $"Confirm {verb}",
                    MessageBoxButton.YesNo,
                    uninstall ? MessageBoxImage.Warning : MessageBoxImage.Question);
                if (confirm != MessageBoxResult.Yes) return;
            }

            LoadingOverlay.Visibility = Visibility.Visible;
            LoadingText.Text = $"{verb}: running 0 / {targets.Count}...";
            var succeeded = 0;
            var failed = 0;
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
                                Interlocked.Increment(ref failed);
                                errs.Add(new DeviceErrorEntry { Source = entry.DeviceName, Message = "Unable to resolve target device." });
                                return;
                            }

                            var res = repair
                                ? await _service.RepairRemoteSoftwareCenterApplicationAsync(dev, entry.Payload)
                                : await _service.InvokeRemoteSoftwareCenterItemAsync(dev, entry.Payload, uninstall);

                            if (res.IsSuccess)
                            {
                                Interlocked.Increment(ref succeeded);
                            }
                            else
                            {
                                Interlocked.Increment(ref failed);
                                errs.Add(new DeviceErrorEntry
                                {
                                    Source = $"{dev.DisplayName} ({entry.Payload.Name})",
                                    Message = res.Message,
                                    Details = res.ErrorDetails
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref failed);
                            errs.Add(new DeviceErrorEntry
                            {
                                Source = $"{entry.DeviceName} ({entry.Payload.Name})",
                                Message = ex.Message,
                                Details = ex.ToString()
                            });
                        }
                        finally
                        {
                            gate.Release();
                            var completedCount = Interlocked.Increment(ref done);
                            Dispatcher.InvokeAsync(() =>
                                LoadingText.Text = $"{verb}: running {completedCount} / {targets.Count}...");
                        }
                    }).ToList();

                    await Task.WhenAll(tasks);
                }

                foreach (var err in errs)
                {
                    _errors.Add(err);
                }

                StatusText.Text = $"{verb} complete — Succeeded: {succeeded}, Failed: {failed}";
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }

            await LoadAsync();
            StatusText.Text = $"{verb} complete — Succeeded: {succeeded}, Failed: {failed}";
        }

        private async void InstallAllUpdates_Click(object sender, RoutedEventArgs e)
        {
            var scope = ActiveScopeDevices;
            if (scope.Count == 0) return;

            if (scope.Count > 1 && _settings.ConfirmDestructiveActions)
            {
                if (MessageBox.Show(this, $"Install all pending software updates across {scope.Count} computer(s)?",
                    "Confirm Install All Updates", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            LoadingOverlay.Visibility = Visibility.Visible;
            LoadingText.Text = $"Triggering Install All Pending Updates on {scope.Count} computer(s)...";
            var succeeded = 0;
            var failed = 0;
            var errs = new ConcurrentBag<DeviceErrorEntry>();

            try
            {
                using (var gate = new SemaphoreSlim(Math.Max(1, _settings.MaxConcurrentDevices)))
                {
                    var tasks = scope.Select(async dev =>
                    {
                        await gate.WaitAsync();
                        try
                        {
                            var res = await _service.InstallAllSoftwareUpdatesAsync(dev);
                            if (res.IsSuccess) Interlocked.Increment(ref succeeded);
                            else
                            {
                                Interlocked.Increment(ref failed);
                                errs.Add(new DeviceErrorEntry { Source = dev.DisplayName, Message = res.Message, Details = res.ErrorDetails });
                            }
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref failed);
                            errs.Add(new DeviceErrorEntry { Source = dev.DisplayName, Message = ex.Message, Details = ex.ToString() });
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
            StatusText.Text = $"Install All Updates triggered — Succeeded: {succeeded}, Failed: {failed}";
        }

        private async void TriggerAppEval_Click(object sender, RoutedEventArgs e) =>
            await TriggerClientCycleAsync(ClientActionRegistry.AppDeploymentEval);

        private async void TriggerUpdateScan_Click(object sender, RoutedEventArgs e) =>
            await TriggerClientCycleAsync(ClientActionRegistry.SoftwareUpdatesScan);

        private async void TriggerUpdateEval_Click(object sender, RoutedEventArgs e) =>
            await TriggerClientCycleAsync(ClientActionRegistry.SoftwareUpdatesDeploymentEval);

        private async Task TriggerClientCycleAsync(string actionKey)
        {
            var scope = ActiveScopeDevices;
            if (scope.Count == 0) return;

            var action = ClientActionRegistry.GetRequired(actionKey);
            LoadingOverlay.Visibility = Visibility.Visible;
            LoadingText.Text = $"Running {action.DisplayName} on {scope.Count} computer(s)...";
            var succeeded = 0;
            var failed = 0;
            var errs = new ConcurrentBag<DeviceErrorEntry>();

            try
            {
                using (var gate = new SemaphoreSlim(Math.Max(1, _settings.MaxConcurrentDevices)))
                {
                    var tasks = scope.Select(async dev =>
                    {
                        await gate.WaitAsync();
                        try
                        {
                            var res = await _service.RunClientActionAsync(dev, action);
                            if (res.IsSuccess) Interlocked.Increment(ref succeeded);
                            else
                            {
                                Interlocked.Increment(ref failed);
                                errs.Add(new DeviceErrorEntry { Source = dev.DisplayName, Message = res.Message, Details = res.ErrorDetails });
                            }
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref failed);
                            errs.Add(new DeviceErrorEntry { Source = dev.DisplayName, Message = ex.Message, Details = ex.ToString() });
                        }
                        finally
                        {
                            gate.Release();
                        }
                    }).ToList();

                    await Task.WhenAll(tasks);
                }

                foreach (var err in errs) _errors.Add(err);
                StatusText.Text = $"{action.DisplayName} — Succeeded: {succeeded}, Failed: {failed}";
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
                FileName = $"OpenRCT_SoftwareCenter_{DateTime.Now:yyyyMMdd_HHmm}.csv"
            };
            if (dlg.ShowDialog(this) != true) return;

            CsvExporter.WriteToFile(dlg.FileName, _allItems, new[]
            {
                new CsvColumn<RemoteSoftwareCenterItem>("Device", i => i.DeviceName),
                new CsvColumn<RemoteSoftwareCenterItem>("Type", i => i.ItemType),
                new CsvColumn<RemoteSoftwareCenterItem>("Name", i => i.Name),
                new CsvColumn<RemoteSoftwareCenterItem>("Publisher", i => i.Publisher),
                new CsvColumn<RemoteSoftwareCenterItem>("Version / KB", i => i.SoftwareVersion),
                new CsvColumn<RemoteSoftwareCenterItem>("Install State", i => i.InstallState),
                new CsvColumn<RemoteSoftwareCenterItem>("Compliance State", i => i.ComplianceState),
                new CsvColumn<RemoteSoftwareCenterItem>("Evaluation State", i => i.EvaluationState),
                new CsvColumn<RemoteSoftwareCenterItem>("Error Code", i => i.ErrorCodeHex),
                new CsvColumn<RemoteSoftwareCenterItem>("Last Run / Eval", i => i.LastRunTime),
                new CsvColumn<RemoteSoftwareCenterItem>("Deadline", i => i.Deadline)
            });
        }
    }
}
