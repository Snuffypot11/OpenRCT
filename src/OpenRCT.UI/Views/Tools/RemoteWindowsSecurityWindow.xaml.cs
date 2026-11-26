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
    public partial class RemoteWindowsSecurityWindow : Window
    {
        private readonly IReadOnlyList<TargetDevice> _devices;
        private readonly Dictionary<string, TargetDevice> _deviceMap;
        private readonly IRemoteWindowsSecurityService _securityService;
        private readonly IEndpointActionService _endpointService;
        private readonly IActiveDirectoryClient _adClient;
        private readonly ISmsProviderClient _smsClient;
        private readonly string _smsServer;
        private readonly string _siteCode;
        private readonly OpenRctSettings _settings;
        private readonly ObservableCollection<DeviceErrorEntry> _errors = new ObservableCollection<DeviceErrorEntry>();
        private readonly HashSet<string> _loadedTabs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private string _activeTab = "BitLocker";
        private List<BitLockerVolumeInfo> _bitLockerItems = new List<BitLockerVolumeInfo>();
        private List<TpmStatusInfo> _tpmItems = new List<TpmStatusInfo>();
        private List<FirmwareSecurityInfo> _firmwareItems = new List<FirmwareSecurityInfo>();
        private List<VbsStatusInfo> _vbsItems = new List<VbsStatusInfo>();
        private List<DefenderStatusInfo> _defenderItems = new List<DefenderStatusInfo>();
        private List<DefenderExclusionInfo> _exclusionItems = new List<DefenderExclusionInfo>();
        private List<FirewallProfileInfo> _firewallProfiles = new List<FirewallProfileInfo>();
        private List<FirewallRuleInfo> _firewallRules = new List<FirewallRuleInfo>();

        public RemoteWindowsSecurityWindow(
            IReadOnlyList<TargetDevice> devices,
            IRemoteWindowsSecurityService securityService,
            IEndpointActionService endpointService,
            IActiveDirectoryClient adClient,
            ISmsProviderClient smsClient,
            string smsServer,
            string siteCode,
            OpenRctSettings settings)
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

            _securityService = securityService;
            _endpointService = endpointService;
            _adClient = adClient;
            _smsClient = smsClient;
            _smsServer = smsServer;
            _siteCode = siteCode;
            _settings = settings;

            ThemeManager.ApplyTheme(this, settings.Theme);
            InitializeComponent();
            ErrorDrawer.Errors = _errors;

            Title = _devices.Count == 1
                ? $"Remote Windows Security - {_devices[0].DisplayName}"
                : $"Remote Windows Security - {_devices.Count} computers";
            ScopeSubtitleText.Text = _devices.Count == 1
                ? _devices[0].DisplayName
                : $"{_devices.Count} targeted computers";

            var filterOptions = DeviceFilterOption.BuildOptions(_devices);
            DeviceFilterCombo.ItemsSource = filterOptions;
            if (filterOptions.Count > 0) DeviceFilterCombo.SelectedIndex = 0;

            UpdateSectionChrome();
            Loaded += async (_, __) => await LoadActiveTabAsync(forceRefresh: true);
        }

        private DeviceFilterOption? ActiveDeviceFilter => DeviceFilterCombo.SelectedItem as DeviceFilterOption;

        private async void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            if (NavList.SelectedItem is ListBoxItem item && item.Tag is string tag)
            {
                _activeTab = tag;
                UpdateSectionChrome();
                await LoadActiveTabAsync(forceRefresh: false);
            }
        }

        private void DeviceFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded) ApplyFilterAndBind();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (IsLoaded) ApplyFilterAndBind();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadActiveTabAsync(forceRefresh: true);

        private void UpdateSectionChrome()
        {
            var isBl = _activeTab == "BitLocker";
            var isTpm = _activeTab == "TPM";
            var isFw = _activeTab == "Firmware";
            var isDef = _activeTab == "Defender";
            var isRules = _activeTab == "FirewallRules";

            SuspendBitLockerButton.Visibility = isBl ? Visibility.Visible : Visibility.Collapsed;
            ResumeBitLockerButton.Visibility = isBl ? Visibility.Visible : Visibility.Collapsed;
            ViewRecoveryKeysButton.Visibility = isBl ? Visibility.Visible : Visibility.Collapsed;
            ClearTpmButton.Visibility = isTpm ? Visibility.Visible : Visibility.Collapsed;
            RunSecureBootTaskButton.Visibility = isFw ? Visibility.Visible : Visibility.Collapsed;
            DefenderQuickScanButton.Visibility = isDef ? Visibility.Visible : Visibility.Collapsed;
            DefenderFullScanButton.Visibility = isDef ? Visibility.Visible : Visibility.Collapsed;
            DefenderUpdateDefsButton.Visibility = isDef ? Visibility.Visible : Visibility.Collapsed;
            EnableRuleButton.Visibility = isRules ? Visibility.Visible : Visibility.Collapsed;
            DisableRuleButton.Visibility = isRules ? Visibility.Visible : Visibility.Collapsed;

            switch (_activeTab)
            {
                case "BitLocker":
                    GroupedGrid.ConfigureColumns(
                        nameHeader: "Drive",
                        secondaryHeader: "Protection Status",
                        versionHeader: "Encryption Method",
                        statusHeader: "Conversion Status",
                        childStatusHeader: "Volume Type",
                        childSecondaryHeader: "Lock Status",
                        childExtraHeader: "Encrypted",
                        childTimestampHeader: "Key Protectors",
                        showSecondary: true,
                        showVersion: true,
                        showExtra: false,
                        showStatus: true,
                        showChildSecondary: true,
                        showChildExtra: true,
                        showChildTimestamp: true,
                        showChildError: false);
                    break;

                case "TPM":
                    GroupedGrid.ConfigureColumns(
                        nameHeader: "TCG Version",
                        secondaryHeader: "Activated",
                        versionHeader: "Enabled",
                        extraHeader: "Owned",
                        statusHeader: "Ready",
                        childStatusHeader: "Manufacturer",
                        childSecondaryHeader: "Manufacturer Version",
                        childExtraHeader: "FIPS Ready",
                        childTimestampHeader: "Locked Out",
                        showSecondary: true,
                        showVersion: true,
                        showExtra: true,
                        showStatus: true,
                        showChildSecondary: true,
                        showChildExtra: true,
                        showChildTimestamp: true,
                        showChildError: false);
                    break;

                case "Firmware":
                    GroupedGrid.ConfigureColumns(
                        nameHeader: "UEFI Enabled",
                        secondaryHeader: "SecureBoot Enabled",
                        versionHeader: "",
                        statusHeader: "Certificate Status",
                        childStatusHeader: "BIOS Version",
                        childSecondaryHeader: "Certificate Update Readiness",
                        childExtraHeader: "Certificate Error",
                        childTimestampHeader: "Certificate Error Event",
                        childErrorHeader: "Confidence Level",
                        showSecondary: true,
                        showVersion: false,
                        showExtra: false,
                        showStatus: true,
                        showChildSecondary: true,
                        showChildExtra: true,
                        showChildTimestamp: true,
                        showChildError: true);
                    break;

                case "VBS":
                    GroupedGrid.ConfigureColumns(
                        nameHeader: "VBS Status",
                        secondaryHeader: "Credential Guard",
                        versionHeader: "",
                        statusHeader: "HVCI (Memory Integrity)",
                        childStatusHeader: "VBS Status",
                        childSecondaryHeader: "Running Services",
                        childExtraHeader: "Configured Services",
                        childTimestampHeader: "Required Properties",
                        showSecondary: true,
                        showVersion: false,
                        showExtra: false,
                        showStatus: true,
                        showChildSecondary: true,
                        showChildExtra: true,
                        showChildTimestamp: true,
                        showChildError: false);
                    break;

                case "Defender":
                    GroupedGrid.ConfigureColumns(
                        nameHeader: "Protection",
                        secondaryHeader: "Real-time Protection",
                        versionHeader: "Signature Version",
                        extraHeader: "Client Version",
                        statusHeader: "Engine Version",
                        childStatusHeader: "Last Signature Update",
                        childSecondaryHeader: "Last Full Scan",
                        childExtraHeader: "Last Quick Scan",
                        showSecondary: true,
                        showVersion: true,
                        showExtra: true,
                        showStatus: true,
                        showChildSecondary: true,
                        showChildExtra: true,
                        showChildTimestamp: false,
                        showChildError: false);
                    break;

                case "DefenderExclusions":
                    GroupedGrid.ConfigureColumns(
                        nameHeader: "Path / Exclusion Value",
                        secondaryHeader: "",
                        versionHeader: "",
                        statusHeader: "Type",
                        childStatusHeader: "Type",
                        childSecondaryHeader: "Value",
                        showSecondary: false,
                        showVersion: false,
                        showExtra: false,
                        showStatus: true,
                        showChildSecondary: true,
                        showChildExtra: false,
                        showChildTimestamp: false,
                        showChildError: false);
                    break;

                case "WindowsFirewall":
                    GroupedGrid.ConfigureColumns(
                        nameHeader: "Profile Name",
                        secondaryHeader: "",
                        versionHeader: "",
                        statusHeader: "Enabled",
                        childStatusHeader: "Enabled",
                        childSecondaryHeader: "Default Inbound",
                        childExtraHeader: "Default Outbound",
                        showSecondary: false,
                        showVersion: false,
                        showExtra: false,
                        showStatus: true,
                        showChildSecondary: true,
                        showChildExtra: true,
                        showChildTimestamp: false,
                        showChildError: false);
                    break;

                case "FirewallRules":
                    GroupedGrid.ConfigureColumns(
                        nameHeader: "Display Name",
                        secondaryHeader: "Action",
                        versionHeader: "Direction",
                        extraHeader: "Profiles",
                        statusHeader: "Enabled",
                        childStatusHeader: "Enabled",
                        childSecondaryHeader: "Group",
                        childExtraHeader: "Direction / Action",
                        showSecondary: true,
                        showVersion: true,
                        showExtra: true,
                        showStatus: true,
                        showChildSecondary: true,
                        showChildExtra: true,
                        showChildTimestamp: false,
                        showChildError: false);
                    break;
            }
        }

        private async Task LoadActiveTabAsync(bool forceRefresh)
        {
            if (_devices.Count == 0) return;
            if (!forceRefresh && _loadedTabs.Contains(_activeTab))
            {
                ApplyFilterAndBind();
                return;
            }

            LoadingOverlay.Visibility = Visibility.Visible;
            LoadingText.Text = _devices.Count == 1
                ? $"Querying {_activeTab} on {_devices[0].DisplayName}..."
                : $"Querying {_activeTab} across 0 / {_devices.Count} computers...";

            var maxConcurrency = Math.Max(1, _settings.MaxConcurrentDevices);
            var completed = 0;
            var errs = new ConcurrentBag<DeviceErrorEntry>();

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
                            var rows = await fetch(dev);
                            foreach (var r in rows) bag.Add(r);
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

            try
            {
                switch (_activeTab)
                {
                    case "BitLocker":
                        _bitLockerItems = await QueryAllAsync<BitLockerVolumeInfo>(async d => await _securityService.GetBitLockerVolumesAsync(d));
                        break;
                    case "TPM":
                        _tpmItems = await QueryAllAsync<TpmStatusInfo>(async d => new[] { await _securityService.GetTpmStatusAsync(d) });
                        break;
                    case "Firmware":
                        _firmwareItems = await QueryAllAsync<FirmwareSecurityInfo>(async d => new[] { await _securityService.GetFirmwareSecurityAsync(d) });
                        break;
                    case "VBS":
                        _vbsItems = await QueryAllAsync<VbsStatusInfo>(async d => new[] { await _securityService.GetVbsStatusAsync(d) });
                        break;
                    case "Defender":
                        _defenderItems = await QueryAllAsync<DefenderStatusInfo>(async d => new[] { await _securityService.GetDefenderStatusAsync(d) });
                        break;
                    case "DefenderExclusions":
                        _exclusionItems = await QueryAllAsync<DefenderExclusionInfo>(async d => await _securityService.GetDefenderExclusionsAsync(d));
                        break;
                    case "WindowsFirewall":
                        _firewallProfiles = await QueryAllAsync<FirewallProfileInfo>(async d => await _securityService.GetFirewallProfilesAsync(d));
                        break;
                    case "FirewallRules":
                        _firewallRules = await QueryAllAsync<FirewallRuleInfo>(async d => await _securityService.GetFirewallRulesAsync(d));
                        break;
                }

                _loadedTabs.Add(_activeTab);
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
            var term = SearchBox.Text?.Trim() ?? string.Empty;
            var filterDev = ActiveDeviceFilter?.Device;

            bool MatchesDevice(string? dev) =>
                filterDev == null ||
                string.Equals(dev, filterDev.DisplayName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(dev, filterDev.ConnectAddress, StringComparison.OrdinalIgnoreCase);

            bool MatchesText(params string?[] vals) =>
                string.IsNullOrEmpty(term) ||
                vals.Any(v => v != null && v.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);

            List<ExpandableItemGroup> groups;
            int rawCount;

            switch (_activeTab)
            {
                case "BitLocker":
                {
                    var rows = _bitLockerItems.Where(x => MatchesDevice(x.DeviceName) && MatchesText(x.DeviceName, x.DriveLetter, x.ProtectionStatus, x.EncryptionMethod, x.ConversionStatus, x.KeyProtectors)).ToList();
                    rawCount = rows.Count;
                    groups = ExpandableItemGroup.FromGroupedData(MultiDeviceGrouper.GroupBitLockerVolumes(rows));
                    break;
                }
                case "TPM":
                {
                    var rows = _tpmItems.Where(x => MatchesDevice(x.DeviceName) && MatchesText(x.DeviceName, x.SpecVersion, x.Manufacturer, x.ManufacturerVersion)).ToList();
                    rawCount = rows.Count;
                    groups = ExpandableItemGroup.FromGroupedData(MultiDeviceGrouper.GroupTpmStatuses(rows));
                    break;
                }
                case "Firmware":
                {
                    var rows = _firmwareItems.Where(x => MatchesDevice(x.DeviceName) && MatchesText(x.DeviceName, x.BiosVersion, x.CertificateStatus, x.CertificateUpdateReadiness, x.ConfidenceLevel, x.CertificateError)).ToList();
                    rawCount = rows.Count;
                    groups = ExpandableItemGroup.FromGroupedData(MultiDeviceGrouper.GroupFirmwareSecurity(rows));
                    break;
                }
                case "VBS":
                {
                    var rows = _vbsItems.Where(x => MatchesDevice(x.DeviceName) && MatchesText(x.DeviceName, x.VbsStatus, x.CredentialGuardStatus, x.HvciStatus, x.RunningServices)).ToList();
                    rawCount = rows.Count;
                    groups = ExpandableItemGroup.FromGroupedData(MultiDeviceGrouper.GroupVbsStatuses(rows));
                    break;
                }
                case "Defender":
                {
                    var rows = _defenderItems.Where(x => MatchesDevice(x.DeviceName) && MatchesText(x.DeviceName, x.SignatureVersion, x.ClientVersion, x.EngineVersion)).ToList();
                    rawCount = rows.Count;
                    groups = ExpandableItemGroup.FromGroupedData(MultiDeviceGrouper.GroupDefenderStatuses(rows));
                    break;
                }
                case "DefenderExclusions":
                {
                    var rows = _exclusionItems.Where(x => MatchesDevice(x.DeviceName) && MatchesText(x.DeviceName, x.ExclusionType, x.Value)).ToList();
                    rawCount = rows.Count;
                    groups = ExpandableItemGroup.FromGroupedData(MultiDeviceGrouper.GroupDefenderExclusions(rows));
                    break;
                }
                case "WindowsFirewall":
                {
                    var rows = _firewallProfiles.Where(x => MatchesDevice(x.DeviceName) && MatchesText(x.DeviceName, x.ProfileName, x.Enabled ? "Enabled" : "Disabled", x.DefaultInboundAction, x.DefaultOutboundAction)).ToList();
                    rawCount = rows.Count;
                    groups = ExpandableItemGroup.FromGroupedData(MultiDeviceGrouper.GroupFirewallProfiles(rows));
                    break;
                }
                default:
                {
                    var rows = _firewallRules.Where(x => MatchesDevice(x.DeviceName) && MatchesText(x.DeviceName, x.DisplayName, x.Group, x.Action, x.Direction, x.Profiles, x.Enabled ? "Enabled" : "Disabled")).ToList();
                    rawCount = rows.Count;
                    groups = ExpandableItemGroup.FromGroupedData(MultiDeviceGrouper.GroupFirewallRules(rows));
                    break;
                }
            }

            GroupedGrid.SetGroups(groups);
            StatusText.Text = $"{groups.Count} group(s) / {rawCount} device record(s) ({_errors.Count} error(s))";
        }

        private TargetDevice? ResolveDevice(string deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName)) return _devices.FirstOrDefault();
            if (_deviceMap.TryGetValue(deviceName, out var found)) return found;
            return _devices.FirstOrDefault(d =>
                string.Equals(d.DisplayName, deviceName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(d.ConnectAddress, deviceName, StringComparison.OrdinalIgnoreCase));
        }

        private async void SuspendBitLocker_Click(object sender, RoutedEventArgs e) => await SetBitLockerProtectionAsync(suspend: true);
        private async void ResumeBitLocker_Click(object sender, RoutedEventArgs e) => await SetBitLockerProtectionAsync(suspend: false);

        private async Task SetBitLockerProtectionAsync(bool suspend)
        {
            var targets = GroupedGrid.GetCheckedOrSelectedPayloads<BitLockerVolumeInfo>();
            if (targets.Count == 0)
            {
                MessageBox.Show(this, "Check one or more BitLocker volume rows first.", "OpenRCT", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var verb = suspend ? "Suspend BitLocker protection" : "Resume BitLocker protection";
            if (_settings.ConfirmDestructiveActions &&
                MessageBox.Show(this, $"{verb} on {targets.Count} volume(s)?", "Confirm BitLocker Action", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            await ExecuteBulkAsync(verb, targets, (dev, vol) => _securityService.SetBitLockerProtectionAsync(dev, vol.DriveLetter, suspend));
            await LoadActiveTabAsync(forceRefresh: true);
        }

        private void ViewRecoveryKeys_Click(object sender, RoutedEventArgs e)
        {
            var checkedTargets = GroupedGrid.GetCheckedOrSelectedPayloads<BitLockerVolumeInfo>()
                .Select(t => ResolveDevice(t.DeviceName))
                .Where(d => d != null)
                .Select(d => d!)
                .GroupBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            var scope = checkedTargets.Count > 0 ? checkedTargets : _devices.ToList();
            new BitLockerKeyWindow(scope, _adClient, _smsClient, _smsServer, _siteCode, _settings).Show();
        }

        private async void ClearTpm_Click(object sender, RoutedEventArgs e)
        {
            var targets = GroupedGrid.GetCheckedOrSelectedPayloads<TpmStatusInfo>();
            if (targets.Count == 0)
            {
                MessageBox.Show(this, "Check one or more TPM rows first.", "OpenRCT", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (MessageBox.Show(
                this,
                $"WARNING: Clearing the TPM on {targets.Count} device(s) can trigger BitLocker recovery on next reboot! Continue?",
                "Confirm Clear TPM",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            await ExecuteBulkAsync("Clear TPM", targets, (dev, _) => _securityService.ClearTpmAsync(dev));
            await LoadActiveTabAsync(forceRefresh: true);
        }

        private async void RunSecureBootTask_Click(object sender, RoutedEventArgs e)
        {
            var targets = GroupedGrid.GetCheckedOrSelectedPayloads<FirmwareSecurityInfo>();
            if (targets.Count == 0)
            {
                MessageBox.Show(this, "Check one or more Firmware rows (or expand to select specific computers) first.", "OpenRCT", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            await ExecuteBulkAsync("Run Secure Boot Task", targets, (dev, _) => _securityService.RunSecureBootTaskAsync(dev));
            await LoadActiveTabAsync(forceRefresh: true);
        }

        private async void DefenderQuickScan_Click(object sender, RoutedEventArgs e) => await RunDefenderActionAsync("Defender Quick Scan", dev => _endpointService.RunDefenderScanAsync(dev, fullScan: false));
        private async void DefenderFullScan_Click(object sender, RoutedEventArgs e) => await RunDefenderActionAsync("Defender Full Scan", dev => _endpointService.RunDefenderScanAsync(dev, fullScan: true));
        private async void DefenderUpdateDefs_Click(object sender, RoutedEventArgs e) => await RunDefenderActionAsync("Update Defender Definitions", dev => _endpointService.UpdateDefenderDefinitionsAsync(dev));

        private async Task RunDefenderActionAsync(string label, Func<TargetDevice, Task<ActionExecutionResult>> operation)
        {
            var targets = GroupedGrid.GetCheckedOrSelectedPayloads<DefenderStatusInfo>();
            if (targets.Count == 0)
            {
                MessageBox.Show(this, "Check one or more Defender rows first.", "OpenRCT", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            await ExecuteBulkAsync(label, targets, (dev, _) => operation(dev));
            await LoadActiveTabAsync(forceRefresh: true);
        }

        private async void EnableRule_Click(object sender, RoutedEventArgs e) => await SetFirewallRulesAsync(enabled: true);
        private async void DisableRule_Click(object sender, RoutedEventArgs e) => await SetFirewallRulesAsync(enabled: false);

        private async Task SetFirewallRulesAsync(bool enabled)
        {
            var targets = GroupedGrid.GetCheckedOrSelectedPayloads<FirewallRuleInfo>();
            if (targets.Count == 0)
            {
                MessageBox.Show(this, "Check one or more Firewall Rules first.", "OpenRCT", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var verb = enabled ? "Enable Firewall Rule" : "Disable Firewall Rule";
            if (_settings.ConfirmDestructiveActions &&
                MessageBox.Show(this, $"{verb} for {targets.Count} rule assignment(s)?", $"Confirm {verb}", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            await ExecuteBulkAsync(verb, targets, (dev, rule) => _securityService.SetFirewallRuleEnabledAsync(dev, rule.RuleId, enabled));
            await LoadActiveTabAsync(forceRefresh: true);
        }

        private async Task ExecuteBulkAsync<T>(
            string label,
            IReadOnlyList<CheckedPayloadEntry<T>> targets,
            Func<TargetDevice, T, Task<ActionExecutionResult>> operation)
        {
            LoadingOverlay.Visibility = Visibility.Visible;
            LoadingText.Text = $"{label} (0 / {targets.Count})...";
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

                            var res = await operation(dev, entry.Payload);
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
                StatusText.Text = $"{label} — Succeeded: {succeeded}, Failed: {failed}";
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
                FileName = $"OpenRCT_WindowsSecurity_{_activeTab}_{DateTime.Now:yyyyMMdd_HHmm}.csv"
            };
            if (dlg.ShowDialog(this) != true) return;

            switch (_activeTab)
            {
                case "BitLocker":
                    CsvExporter.WriteToFile(dlg.FileName, _bitLockerItems, new[]
                    {
                        new CsvColumn<BitLockerVolumeInfo>("Device", x => x.DeviceName),
                        new CsvColumn<BitLockerVolumeInfo>("Drive", x => x.DriveLetter),
                        new CsvColumn<BitLockerVolumeInfo>("Volume Type", x => x.VolumeType),
                        new CsvColumn<BitLockerVolumeInfo>("Protection Status", x => x.ProtectionStatus),
                        new CsvColumn<BitLockerVolumeInfo>("Encryption Method", x => x.EncryptionMethod),
                        new CsvColumn<BitLockerVolumeInfo>("Conversion Status", x => x.ConversionStatus),
                        new CsvColumn<BitLockerVolumeInfo>("Encrypted %", x => x.EncryptionPercentage),
                        new CsvColumn<BitLockerVolumeInfo>("Lock Status", x => x.LockStatus),
                        new CsvColumn<BitLockerVolumeInfo>("Key Protectors", x => x.KeyProtectors)
                    });
                    break;
                case "TPM":
                    CsvExporter.WriteToFile(dlg.FileName, _tpmItems, new[]
                    {
                        new CsvColumn<TpmStatusInfo>("Device", x => x.DeviceName),
                        new CsvColumn<TpmStatusInfo>("TCG Version", x => x.SpecVersion),
                        new CsvColumn<TpmStatusInfo>("Activated", x => x.IsActivated),
                        new CsvColumn<TpmStatusInfo>("Enabled", x => x.IsEnabled),
                        new CsvColumn<TpmStatusInfo>("Owned", x => x.IsOwned),
                        new CsvColumn<TpmStatusInfo>("Ready", x => x.IsReady),
                        new CsvColumn<TpmStatusInfo>("Manufacturer", x => x.Manufacturer),
                        new CsvColumn<TpmStatusInfo>("Manufacturer Version", x => x.ManufacturerVersion),
                        new CsvColumn<TpmStatusInfo>("FIPS Ready", x => x.FipsReady),
                        new CsvColumn<TpmStatusInfo>("Locked Out", x => x.LockedOut)
                    });
                    break;
                case "Firmware":
                    CsvExporter.WriteToFile(dlg.FileName, _firmwareItems, new[]
                    {
                        new CsvColumn<FirmwareSecurityInfo>("Device", x => x.DeviceName),
                        new CsvColumn<FirmwareSecurityInfo>("UEFI Enabled", x => x.UefiEnabled),
                        new CsvColumn<FirmwareSecurityInfo>("SecureBoot Enabled", x => x.SecureBootEnabled),
                        new CsvColumn<FirmwareSecurityInfo>("BIOS Version", x => x.BiosVersion),
                        new CsvColumn<FirmwareSecurityInfo>("Certificate Status", x => x.CertificateStatus),
                        new CsvColumn<FirmwareSecurityInfo>("Certificate Update Readiness", x => x.CertificateUpdateReadiness),
                        new CsvColumn<FirmwareSecurityInfo>("Certificate Error", x => x.CertificateError),
                        new CsvColumn<FirmwareSecurityInfo>("Certificate Error Event", x => x.CertificateErrorEvent),
                        new CsvColumn<FirmwareSecurityInfo>("Confidence Level", x => x.ConfidenceLevel)
                    });
                    break;
                case "VBS":
                    CsvExporter.WriteToFile(dlg.FileName, _vbsItems, new[]
                    {
                        new CsvColumn<VbsStatusInfo>("Device", x => x.DeviceName),
                        new CsvColumn<VbsStatusInfo>("VBS Status", x => x.VbsStatus),
                        new CsvColumn<VbsStatusInfo>("Credential Guard", x => x.CredentialGuardStatus),
                        new CsvColumn<VbsStatusInfo>("HVCI", x => x.HvciStatus),
                        new CsvColumn<VbsStatusInfo>("Running Services", x => x.RunningServices),
                        new CsvColumn<VbsStatusInfo>("Configured Services", x => x.ConfiguredServices),
                        new CsvColumn<VbsStatusInfo>("Required Properties", x => x.RequiredProperties)
                    });
                    break;
                case "Defender":
                    CsvExporter.WriteToFile(dlg.FileName, _defenderItems, new[]
                    {
                        new CsvColumn<DefenderStatusInfo>("Device", x => x.DeviceName),
                        new CsvColumn<DefenderStatusInfo>("Protection", x => x.AntivirusEnabled ? "Enabled" : "Disabled"),
                        new CsvColumn<DefenderStatusInfo>("Real-time Protection", x => x.RealTimeProtectionEnabled ? "Enabled" : "Disabled"),
                        new CsvColumn<DefenderStatusInfo>("Signature Version", x => x.SignatureVersion),
                        new CsvColumn<DefenderStatusInfo>("Client Version", x => x.ClientVersion),
                        new CsvColumn<DefenderStatusInfo>("Engine Version", x => x.EngineVersion),
                        new CsvColumn<DefenderStatusInfo>("Last Signature Update", x => x.LastSignatureUpdate),
                        new CsvColumn<DefenderStatusInfo>("Last Full Scan", x => x.LastFullScan),
                        new CsvColumn<DefenderStatusInfo>("Last Quick Scan", x => x.LastQuickScan)
                    });
                    break;
                case "DefenderExclusions":
                    CsvExporter.WriteToFile(dlg.FileName, _exclusionItems, new[]
                    {
                        new CsvColumn<DefenderExclusionInfo>("Device", x => x.DeviceName),
                        new CsvColumn<DefenderExclusionInfo>("Type", x => x.ExclusionType),
                        new CsvColumn<DefenderExclusionInfo>("Value", x => x.Value)
                    });
                    break;
                case "WindowsFirewall":
                    CsvExporter.WriteToFile(dlg.FileName, _firewallProfiles, new[]
                    {
                        new CsvColumn<FirewallProfileInfo>("Device", x => x.DeviceName),
                        new CsvColumn<FirewallProfileInfo>("Profile", x => x.ProfileName),
                        new CsvColumn<FirewallProfileInfo>("Enabled", x => x.Enabled ? "Enabled" : "Disabled"),
                        new CsvColumn<FirewallProfileInfo>("Default Inbound", x => x.DefaultInboundAction),
                        new CsvColumn<FirewallProfileInfo>("Default Outbound", x => x.DefaultOutboundAction)
                    });
                    break;
                default:
                    CsvExporter.WriteToFile(dlg.FileName, _firewallRules, new[]
                    {
                        new CsvColumn<FirewallRuleInfo>("Device", x => x.DeviceName),
                        new CsvColumn<FirewallRuleInfo>("Display Name", x => x.DisplayName),
                        new CsvColumn<FirewallRuleInfo>("Group", x => x.Group),
                        new CsvColumn<FirewallRuleInfo>("Action", x => x.Action),
                        new CsvColumn<FirewallRuleInfo>("Direction", x => x.Direction),
                        new CsvColumn<FirewallRuleInfo>("Profiles", x => x.Profiles),
                        new CsvColumn<FirewallRuleInfo>("Enabled", x => x.Enabled ? "Enabled" : "Disabled")
                    });
                    break;
            }
        }
    }
}
