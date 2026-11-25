using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using OpenRCT.Core.Interfaces;
using OpenRCT.Core.Models;
using OpenRCT.Core.Settings;
using OpenRCT.UI.Themes;

namespace OpenRCT.UI.Views.Tools
{
    public partial class LapsPasswordWindow : Window
    {
        private sealed class LapsRowView
        {
            public string DeviceName { get; set; } = string.Empty;
            public string LapsType { get; set; } = string.Empty;
            public string AccountName { get; set; } = string.Empty;
            public string RawPassword { get; set; } = string.Empty;
            public string DisplayPassword { get; set; } = string.Empty;
            public DateTime? ExpirationTime { get; set; }
            public string ErrorMessage { get; set; } = string.Empty;
        }

        private readonly IReadOnlyList<TargetDevice> _devices;
        private readonly IActiveDirectoryClient _adClient;
        private readonly OpenRctSettings _settings;
        private readonly ObservableCollection<DeviceErrorEntry> _errors = new ObservableCollection<DeviceErrorEntry>();
        private List<LapsRowView> _rows = new List<LapsRowView>();

        public LapsPasswordWindow(IReadOnlyList<TargetDevice> devices, IActiveDirectoryClient adClient, OpenRctSettings settings)
        {
            InitializeComponent();
            _devices = devices;
            _adClient = adClient;
            _settings = settings;
            ThemeManager.ApplyTheme(this, settings.Theme);
            ErrorDrawer.Errors = _errors;
            Loaded += async (_, __) => await LoadAsync();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

        private async Task LoadAsync()
        {
            _rows.Clear();
            foreach (var dev in _devices)
            {
                try
                {
                    var res = await _adClient.GetLapsPasswordAsync(dev.DisplayName);
                    _rows.Add(new LapsRowView
                    {
                        DeviceName = res.DeviceName,
                        LapsType = res.LapsType,
                        AccountName = res.AccountName,
                        RawPassword = res.Password,
                        ExpirationTime = res.ExpirationTime,
                        ErrorMessage = res.ErrorMessage ?? string.Empty
                    });
                }
                catch (Exception ex)
                {
                    _errors.Add(new DeviceErrorEntry { Source = dev.DisplayName, Message = ex.Message, Details = ex.ToString() });
                }
            }
            RefreshMasking();
        }

        private void Reveal_Changed(object sender, RoutedEventArgs e) => RefreshMasking();

        private void RefreshMasking()
        {
            bool reveal = RevealCheck.IsChecked == true;
            foreach (var r in _rows)
            {
                r.DisplayPassword = string.IsNullOrEmpty(r.RawPassword)
                    ? string.Empty
                    : (reveal ? r.RawPassword : "••••••••••••••••");
            }
            LapsGrid.ItemsSource = null;
            LapsGrid.ItemsSource = _rows;
        }

        private async void CopyPassword_Click(object sender, RoutedEventArgs e)
        {
            if (!(LapsGrid.SelectedItem is LapsRowView row) || string.IsNullOrEmpty(row.RawPassword)) return;
            var copied = row.RawPassword;
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
