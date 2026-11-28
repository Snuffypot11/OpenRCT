using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using OpenCMTools.Core.Interfaces;
using OpenCMTools.Core.Models;
using OpenCMTools.Core.Settings;
using OpenCMTools.UI.Themes;

namespace OpenCMTools.UI.Views.Tools
{
    public partial class AdSecurityGroupsWindow : Window
    {
        private readonly IReadOnlyList<TargetDevice> _devices;
        private readonly IActiveDirectoryClient _adClient;
        private readonly ObservableCollection<DeviceErrorEntry> _errors = new ObservableCollection<DeviceErrorEntry>();

        public AdSecurityGroupsWindow(IReadOnlyList<TargetDevice> devices, IActiveDirectoryClient adClient, OpenCMToolsSettings settings)
        {
            InitializeComponent();
            _devices = devices;
            _adClient = adClient;
            ThemeManager.ApplyTheme(this, settings.Theme);
            ErrorDrawer.Errors = _errors;
            HeaderText.Text = devices.Count == 1 ? $"AD Security Groups — {devices[0].DisplayName}" : $"AD Security Groups — {devices.Count} Devices";
            Loaded += async (_, __) => await LoadAsync();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

        private async Task LoadAsync()
        {
            var rows = new List<AdGroupMembershipInfo>();
            foreach (var dev in _devices)
            {
                try
                {
                    rows.AddRange(await _adClient.GetComputerGroupsAsync(dev.DisplayName));
                }
                catch (Exception ex)
                {
                    _errors.Add(new DeviceErrorEntry { Source = dev.DisplayName, Message = ex.Message, Details = ex.ToString() });
                }
            }
            GroupsGrid.ItemsSource = rows;
        }

        private async void AddGroup_Click(object sender, RoutedEventArgs e)
        {
            var prompt = new InputPromptWindow("Add to AD Security Group", "Enter AD group CN or Distinguished Name:") { Owner = this };
            if (prompt.ShowDialog() != true || string.IsNullOrWhiteSpace(prompt.ResponseText)) return;
            foreach (var dev in _devices)
            {
                try
                {
                    await _adClient.AddComputerToGroupAsync(dev.DisplayName, prompt.ResponseText.Trim());
                }
                catch (Exception ex)
                {
                    _errors.Add(new DeviceErrorEntry { Source = dev.DisplayName, Message = ex.Message, Details = ex.ToString() });
                }
            }
            await LoadAsync();
        }

        private async void RemoveGroup_Click(object sender, RoutedEventArgs e)
        {
            var selected = GroupsGrid.SelectedItems.OfType<AdGroupMembershipInfo>().ToList();
            if (selected.Count == 0) return;
            if (MessageBox.Show(this, $"Remove {selected.Count} group membership(s)?", "Confirm Remove", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            foreach (var g in selected)
            {
                try
                {
                    await _adClient.RemoveComputerFromGroupAsync(g.DeviceName, g.DistinguishedName);
                }
                catch (Exception ex)
                {
                    _errors.Add(new DeviceErrorEntry { Source = g.DeviceName, Message = ex.Message, Details = ex.ToString() });
                }
            }
            await LoadAsync();
        }
    }
}
