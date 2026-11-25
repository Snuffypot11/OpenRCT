using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using OpenRCT.Core.Interfaces;
using OpenRCT.Core.Models;
using OpenRCT.Core.Settings;
using OpenRCT.UI.Themes;

namespace OpenRCT.UI.Views.Tools
{
    public partial class RunningProcessesWindow : Window
    {
        private readonly IReadOnlyList<TargetDevice> _devices;
        private readonly IEndpointActionService _service;
        private readonly OpenRctSettings _settings;
        private readonly ObservableCollection<DeviceErrorEntry> _errors = new ObservableCollection<DeviceErrorEntry>();
        private List<RemoteProcessInfo> _allProcesses = new List<RemoteProcessInfo>();

        public RunningProcessesWindow(IReadOnlyList<TargetDevice> devices, IEndpointActionService service, OpenRctSettings settings)
        {
            InitializeComponent();
            _devices = devices;
            _service = service;
            _settings = settings;
            ThemeManager.ApplyTheme(this, settings.Theme);
            ErrorDrawer.Errors = _errors;
            Loaded += async (_, __) => await LoadAsync();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

        private async Task LoadAsync()
        {
            _allProcesses.Clear();
            foreach (var dev in _devices)
            {
                try
                {
                    var procs = await _service.GetRunningProcessesAsync(dev);
                    _allProcesses.AddRange(procs);
                }
                catch (Exception ex)
                {
                    _errors.Add(new DeviceErrorEntry { Source = dev.DisplayName, Message = ex.Message, Details = ex.ToString() });
                }
            }
            ApplyFilter();
        }

        private void FilterBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => ApplyFilter();

        private void ApplyFilter()
        {
            var term = FilterBox.Text?.Trim() ?? string.Empty;
            ProcessGrid.ItemsSource = string.IsNullOrEmpty(term)
                ? _allProcesses.ToList()
                : _allProcesses.Where(p => p.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                           p.CommandLine.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }

        private async void Terminate_Click(object sender, RoutedEventArgs e)
        {
            var selected = ProcessGrid.SelectedItems.OfType<RemoteProcessInfo>().ToList();
            if (selected.Count == 0) return;
            if (_settings.ConfirmDestructiveActions &&
                MessageBox.Show(this, $"Terminate {selected.Count} process(es)?", "Confirm Terminate", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }
            foreach (var p in selected)
            {
                var dev = _devices.FirstOrDefault(d => string.Equals(d.DisplayName, p.DeviceName, StringComparison.OrdinalIgnoreCase));
                if (dev == null) continue;
                try
                {
                    await _service.TerminateProcessAsync(dev, p.ProcessId);
                }
                catch (Exception ex)
                {
                    _errors.Add(new DeviceErrorEntry { Source = dev.DisplayName, Message = ex.Message, Details = ex.ToString() });
                }
            }
            await LoadAsync();
        }
    }
}
