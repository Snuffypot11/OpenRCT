using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using OpenCMTools.Core.Interfaces;
using OpenCMTools.Core.Models;
using OpenCMTools.Core.Settings;
using OpenCMTools.UI.Themes;

namespace OpenCMTools.UI.Views.Tools
{
    public partial class AddToCollectionWindow : Window
    {
        private readonly IReadOnlyList<TargetDevice> _devices;
        private readonly ISmsProviderClient _smsClient;
        private readonly string _smsServer;
        private readonly string _siteCode;
        private List<SmsCollectionSummary> _allCollections = new List<SmsCollectionSummary>();

        public AddToCollectionWindow(IReadOnlyList<TargetDevice> devices, ISmsProviderClient smsClient, string smsServer, string siteCode, OpenCMToolsSettings settings)
        {
            InitializeComponent();
            _devices = devices;
            _smsClient = smsClient;
            _smsServer = smsServer;
            _siteCode = siteCode;
            ThemeManager.ApplyTheme(this, settings.Theme);
            HeaderText.Text = $"Select a target Device Collection for {_devices.Count} device(s):";
            Loaded += async (_, __) => await LoadCollectionsAsync();
        }

        private async Task LoadCollectionsAsync()
        {
            try
            {
                _allCollections = (await _smsClient.GetDeviceCollectionsAsync(_smsServer, _siteCode)).ToList();
                ApplyFilter();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to load Device Collections: {ex.Message}", "OpenCMTools", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => ApplyFilter();

        private void ApplyFilter()
        {
            var term = SearchBox.Text?.Trim() ?? string.Empty;
            CollectionsGrid.ItemsSource = string.IsNullOrEmpty(term)
                ? _allCollections
                : _allCollections.Where(c => c.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                             c.CollectionId.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }

        private async void Add_Click(object sender, RoutedEventArgs e)
        {
            if (!(CollectionsGrid.SelectedItem is SmsCollectionSummary selected))
            {
                MessageBox.Show(this, "Select a target collection first.", "OpenCMTools", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            AddButton.IsEnabled = false;
            try
            {
                await _smsClient.AddDevicesToCollectionAsync(_smsServer, _siteCode, selected.CollectionId, _devices);
                MessageBox.Show(this, $"Added {_devices.Count} device(s) to '{selected.Name}' ({selected.CollectionId}).", "OpenCMTools", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to add device(s): {ex.Message}", "OpenCMTools", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                AddButton.IsEnabled = true;
            }
        }
    }
}
