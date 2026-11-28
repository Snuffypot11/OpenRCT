using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using OpenCMTools.Core.Interfaces;
using OpenCMTools.Core.Models;
using OpenCMTools.Core.Settings;
using OpenCMTools.UI.Themes;

namespace OpenCMTools.UI.Views.Tools
{
    public partial class BitLockerKeyWindow : Window
    {
        private readonly IReadOnlyList<TargetDevice> _devices;
        private readonly IActiveDirectoryClient _adClient;
        private readonly ISmsProviderClient _smsClient;
        private readonly string _smsServer;
        private readonly string _siteCode;
        private readonly OpenCMToolsSettings _settings;
        private readonly ObservableCollection<DeviceErrorEntry> _errors = new ObservableCollection<DeviceErrorEntry>();

        public BitLockerKeyWindow(
            IReadOnlyList<TargetDevice> devices,
            IActiveDirectoryClient adClient,
            ISmsProviderClient smsClient,
            string smsServer,
            string siteCode,
            OpenCMToolsSettings settings)
        {
            InitializeComponent();
            _devices = devices;
            _adClient = adClient;
            _smsClient = smsClient;
            _smsServer = smsServer;
            _siteCode = siteCode;
            _settings = settings;
            ThemeManager.ApplyTheme(this, settings.Theme);
            ErrorDrawer.Errors = _errors;
            Loaded += async (_, __) => await LoadAsync();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

        private async Task LoadAsync()
        {
            var keys = new List<BitLockerRecoveryKeyInfo>();
            foreach (var dev in _devices)
            {
                try
                {
                    keys.AddRange(await _adClient.GetBitLockerRecoveryKeysAsync(dev.DisplayName));
                }
                catch (Exception ex)
                {
                    _errors.Add(new DeviceErrorEntry { Source = $"{dev.DisplayName} (AD)", Message = ex.Message, Details = ex.ToString() });
                }

                if (!string.IsNullOrWhiteSpace(_smsServer) && !string.IsNullOrWhiteSpace(_siteCode))
                {
                    try
                    {
                        keys.AddRange(await _smsClient.GetConfigMgrBitLockerKeysAsync(_smsServer, _siteCode, dev));
                    }
                    catch { }
                }
            }
            KeysGrid.ItemsSource = keys;
        }

        private async void CopyKey_Click(object sender, RoutedEventArgs e)
        {
            if (!(KeysGrid.SelectedItem is BitLockerRecoveryKeyInfo key) || string.IsNullOrEmpty(key.RecoveryPassword)) return;
            var copied = key.RecoveryPassword;
            Clipboard.SetText(copied);

            if (_settings.AutoClearClipboardSecrets)
            {
                await Task.Delay(TimeSpan.FromSeconds(_settings.ClipboardClearSeconds));
                try
                {
                    if (Clipboard.ContainsText() && Clipboard.GetText() == copied)
                    {
                        Clipboard.Clear();
                    }
                }
                catch { }
            }
        }
    }
}
