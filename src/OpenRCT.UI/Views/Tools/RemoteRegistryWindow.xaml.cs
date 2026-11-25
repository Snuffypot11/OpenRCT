using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OpenRCT.Connectors.Services;
using OpenRCT.Core.Interfaces;
using OpenRCT.Core.Models;
using OpenRCT.Core.Settings;
using OpenRCT.UI.Themes;

namespace OpenRCT.UI.Views.Tools
{
    public partial class RemoteRegistryWindow : Window
    {
        private sealed class RegNodeTag
        {
            public RegistryHive Hive { get; set; }
            public string SubKeyPath { get; set; } = string.Empty;
            public bool ChildrenLoaded { get; set; }
        }

        private readonly RemoteRegistryService _regService = new RemoteRegistryService();
        private readonly ISystemInfoCollector _sysInfo;
        private readonly ObservableCollection<DeviceErrorEntry> _errors = new ObservableCollection<DeviceErrorEntry>();

        public RemoteRegistryWindow(IReadOnlyList<TargetDevice> devices, ISystemInfoCollector sysInfo, OpenRctSettings settings)
        {
            _sysInfo = sysInfo;
            ThemeManager.ApplyTheme(this, settings.Theme);
            InitializeComponent();
            ErrorDrawer.Errors = _errors;
            DeviceCombo.ItemsSource = devices;
            if (devices.Count > 0) DeviceCombo.SelectedIndex = 0;
            Loaded += (_, __) => PopulateRootHives();
        }

        private TargetDevice? SelectedDevice => DeviceCombo.SelectedItem as TargetDevice;

        private void DeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded) PopulateRootHives();
        }

        private void PopulateRootHives()
        {
            var dev = SelectedDevice;
            if (dev == null) return;
            Title = $"Remote Registry Editor - {dev.DisplayName}";
            RegTree.Items.Clear();
            ValuesGrid.ItemsSource = null;

            foreach (var hive in RemoteRegistryService.SupportedHives)
            {
                var node = CreateTreeNode(RemoteRegistryService.HiveDisplayName(hive), hive, string.Empty);
                RegTree.Items.Add(node);
            }
            StatusText.Text = $"Connected to {dev.DisplayName}";
        }

        private TreeViewItem CreateTreeNode(string header, RegistryHive hive, string subKeyPath)
        {
            var item = new TreeViewItem
            {
                Header = header,
                Tag = new RegNodeTag { Hive = hive, SubKeyPath = subKeyPath },
                Foreground = (System.Windows.Media.Brush)(TryFindResource("PrimaryTextBrush") ?? System.Windows.Media.Brushes.Black)
            };
            item.Items.Add(new TreeViewItem { Header = "Loading..." });
            item.Expanded += async (s, e) =>
            {
                e.Handled = true;
                if (s is TreeViewItem tvi) await ExpandNodeAsync(tvi);
            };
            return item;
        }

        private async Task ExpandNodeAsync(TreeViewItem node)
        {
            var dev = SelectedDevice;
            if (dev == null || !(node.Tag is RegNodeTag tag) || tag.ChildrenLoaded) return;

            node.Items.Clear();
            try
            {
                var names = await _regService.GetSubKeyNamesAsync(dev, tag.Hive, tag.SubKeyPath);
                foreach (var name in names)
                {
                    var childPath = string.IsNullOrEmpty(tag.SubKeyPath) ? name : $"{tag.SubKeyPath}\\{name}";
                    node.Items.Add(CreateTreeNode(name, tag.Hive, childPath));
                }
                tag.ChildrenLoaded = true;
            }
            catch (Exception ex)
            {
                _errors.Add(new DeviceErrorEntry { Source = dev.DisplayName, Message = $"Expand '{tag.SubKeyPath}': {ex.Message}", Details = ex.ToString() });
                node.Items.Add(new TreeViewItem { Header = "(Access denied or RemoteRegistry stopped)" });
            }
        }

        private async void RegTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (RegTree.SelectedItem is TreeViewItem tvi && tvi.Tag is RegNodeTag tag)
            {
                PathBox.Text = string.IsNullOrEmpty(tag.SubKeyPath)
                    ? RemoteRegistryService.HiveDisplayName(tag.Hive)
                    : $"{RemoteRegistryService.HiveDisplayName(tag.Hive)}\\{tag.SubKeyPath}";
                await LoadValuesAsync(tag.Hive, tag.SubKeyPath);
            }
        }

        private async Task LoadValuesAsync(RegistryHive hive, string subKeyPath)
        {
            var dev = SelectedDevice;
            if (dev == null) return;
            try
            {
                var vals = await _regService.GetValuesAsync(dev, hive, subKeyPath);
                ValuesGrid.ItemsSource = vals;
                StatusText.Text = $"{vals.Count} value(s) in {RemoteRegistryService.HiveDisplayName(hive)}\\{subKeyPath}";
            }
            catch (Exception ex)
            {
                _errors.Add(new DeviceErrorEntry { Source = dev.DisplayName, Message = ex.Message, Details = ex.ToString() });
                StatusText.Text = "Failed to read values (check RemoteRegistry service & permissions).";
            }
        }

        private async void GoPath_Click(object sender, RoutedEventArgs e)
        {
            if (!TryParsePath(PathBox.Text, out var hive, out var subKey))
            {
                MessageBox.Show(this, "Enter a valid registry path starting with HKEY_LOCAL_MACHINE\\ (or HKLM\\), HKU\\, HKCR\\, or HKCC\\.", "Remote Registry", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            await LoadValuesAsync(hive, subKey);
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            if (TryParsePath(PathBox.Text, out var hive, out var subKey))
            {
                await LoadValuesAsync(hive, subKey);
            }
            else
            {
                PopulateRootHives();
            }
        }

        private async void StartRemoteRegistryService_Click(object sender, RoutedEventArgs e)
        {
            var dev = SelectedDevice;
            if (dev == null) return;
            try
            {
                await _sysInfo.ControlServiceAsync(dev, "RemoteRegistry", "startmode", "Manual");
                var res = await _sysInfo.ControlServiceAsync(dev, "RemoteRegistry", "start");
                StatusText.Text = res.Message;
            }
            catch (Exception ex)
            {
                _errors.Add(new DeviceErrorEntry { Source = dev.DisplayName, Message = ex.Message, Details = ex.ToString() });
            }
        }

        private async void EditValue_Click(object sender, RoutedEventArgs e)
        {
            var dev = SelectedDevice;
            if (dev == null || !TryParsePath(PathBox.Text, out var hive, out var subKey)) return;

            var selected = ValuesGrid.SelectedItem as RemoteRegistryValueItem;
            var namePrompt = new InputPromptWindow("Registry Value Name", "Enter value name:", selected?.Name ?? string.Empty) { Owner = this };
            if (namePrompt.ShowDialog() != true) return;

            var dataPrompt = new InputPromptWindow($"Set Value '{namePrompt.ResponseText}'", "Enter new string/numeric value:", selected?.Data ?? string.Empty) { Owner = this };
            if (dataPrompt.ShowDialog() != true) return;

            try
            {
                var kind = selected?.RawKind ?? RegistryValueKind.String;
                await _regService.SetValueAsync(dev, hive, subKey, namePrompt.ResponseText, kind, dataPrompt.ResponseText);
                await LoadValuesAsync(hive, subKey);
            }
            catch (Exception ex)
            {
                _errors.Add(new DeviceErrorEntry { Source = dev.DisplayName, Message = ex.Message, Details = ex.ToString() });
            }
        }

        private async void DeleteValue_Click(object sender, RoutedEventArgs e)
        {
            var dev = SelectedDevice;
            if (dev == null || !(ValuesGrid.SelectedItem is RemoteRegistryValueItem val) || !TryParsePath(PathBox.Text, out var hive, out var subKey)) return;
            if (MessageBox.Show(this, $"Delete registry value '{val.Name}' from {dev.DisplayName}?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

            try
            {
                await _regService.DeleteValueAsync(dev, hive, subKey, val.Name);
                await LoadValuesAsync(hive, subKey);
            }
            catch (Exception ex)
            {
                _errors.Add(new DeviceErrorEntry { Source = dev.DisplayName, Message = ex.Message, Details = ex.ToString() });
            }
        }

        private static bool TryParsePath(string? raw, out RegistryHive hive, out string subKey)
        {
            hive = RegistryHive.LocalMachine;
            subKey = string.Empty;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            var trimmed = raw!.Trim().Trim('\\');
            var slash = trimmed.IndexOf('\\');
            var rootPart = slash > 0 ? trimmed.Substring(0, slash) : trimmed;
            subKey = slash > 0 ? trimmed.Substring(slash + 1) : string.Empty;

            switch (rootPart.ToUpperInvariant())
            {
                case "HKLM":
                case "HKEY_LOCAL_MACHINE":
                    hive = RegistryHive.LocalMachine; return true;
                case "HKU":
                case "HKEY_USERS":
                    hive = RegistryHive.Users; return true;
                case "HKCR":
                case "HKEY_CLASSES_ROOT":
                    hive = RegistryHive.ClassesRoot; return true;
                case "HKCC":
                case "HKEY_CURRENT_CONFIG":
                    hive = RegistryHive.CurrentConfig; return true;
                default:
                    return false;
            }
        }
    }
}
