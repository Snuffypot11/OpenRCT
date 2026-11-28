using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using OpenCMTools.Core.Export;
using OpenCMTools.Core.Interfaces;
using OpenCMTools.Core.Models;
using OpenCMTools.Core.Settings;
using OpenCMTools.UI.Themes;

namespace OpenCMTools.UI.Views.Tools
{
    public partial class PingSystemsWindow : Window
    {
        private readonly IReadOnlyList<TargetDevice> _devices;
        private readonly IEndpointActionService _service;
        private readonly ObservableCollection<PingResultEntry> _rows = new ObservableCollection<PingResultEntry>();
        private CancellationTokenSource? _cts;

        public PingSystemsWindow(IReadOnlyList<TargetDevice> devices, IEndpointActionService service, OpenCMToolsSettings settings)
        {
            InitializeComponent();
            _devices = devices;
            _service = service;
            ThemeManager.ApplyTheme(this, settings.Theme);
            PingGrid.ItemsSource = _rows;

            foreach (var d in _devices)
            {
                _rows.Add(new PingResultEntry { DeviceName = d.DisplayName, StatusText = "Pending..." });
            }

            Loaded += async (_, __) => await RunPingLoopAsync();
            Closing += (_, __) => _cts?.Cancel();
        }

        private async void Ping_Click(object sender, RoutedEventArgs e) => await RunPingLoopAsync();
        private void Stop_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

        private async Task RunPingLoopAsync()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            PingButton.IsEnabled = false;
            StopButton.IsEnabled = true;

            try
            {
                do
                {
                    var tasks = _devices.Select(async dev => (dev, res: await _service.PingDeviceAsync(dev, 2000, ct))).ToArray();
                    var round = await Task.WhenAll(tasks);

                    foreach (var (dev, res) in round)
                    {
                        var existing = _rows.FirstOrDefault(r => string.Equals(r.DeviceName, dev.DisplayName, StringComparison.OrdinalIgnoreCase));
                        if (existing != null)
                        {
                            var idx = _rows.IndexOf(existing);
                            res.SentCount = existing.SentCount + 1;
                            res.SuccessCount = existing.SuccessCount + (res.IsOnline ? 1 : 0);
                            _rows[idx] = res;
                        }
                    }

                    var online = _rows.Count(r => r.IsOnline);
                    SummaryText.Text = $"Online: {online} / {_rows.Count}";

                    if (ContinuousCheck.IsChecked != true) break;
                    await Task.Delay(2000, ct);
                } while (!ct.IsCancellationRequested);
            }
            catch (OperationCanceledException) { }
            finally
            {
                PingButton.IsEnabled = true;
                StopButton.IsEnabled = false;
            }
        }

        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = $"OpenCMTools_Ping_{DateTime.Now:yyyyMMdd_HHmm}.csv" };
            if (dlg.ShowDialog(this) != true) return;
            CsvExporter.WriteToFile(dlg.FileName, _rows, new[]
            {
                new CsvColumn<PingResultEntry>("Device", r => r.DeviceName),
                new CsvColumn<PingResultEntry>("IP", r => r.IpAddress),
                new CsvColumn<PingResultEntry>("Online", r => r.IsOnline),
                new CsvColumn<PingResultEntry>("Status", r => r.StatusText),
                new CsvColumn<PingResultEntry>("Sent", r => r.SentCount),
                new CsvColumn<PingResultEntry>("Succeeded", r => r.SuccessCount)
            });
        }
    }
}
