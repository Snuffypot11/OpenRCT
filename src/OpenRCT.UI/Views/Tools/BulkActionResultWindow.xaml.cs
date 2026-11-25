using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using OpenRCT.Core.Execution;
using OpenRCT.Core.Export;
using OpenRCT.Core.Models;
using OpenRCT.Core.Settings;
using OpenRCT.UI.Themes;

namespace OpenRCT.UI.Views.Tools
{
    public partial class BulkActionResultWindow : Window
    {
        private readonly string _actionName;
        private readonly IReadOnlyList<TargetDevice> _initialTargets;
        private readonly Func<TargetDevice, CancellationToken, Task<ActionExecutionResult>> _actionFunc;
        private readonly OpenRctSettings _settings;
        private readonly ObservableCollection<ActionExecutionResult> _rows = new ObservableCollection<ActionExecutionResult>();
        private readonly ObservableCollection<DeviceErrorEntry> _errors = new ObservableCollection<DeviceErrorEntry>();
        private CancellationTokenSource? _cts;

        public BulkActionResultWindow(
            string actionName,
            IReadOnlyList<TargetDevice> targets,
            Func<TargetDevice, CancellationToken, Task<ActionExecutionResult>> actionFunc,
            OpenRctSettings settings)
        {
            InitializeComponent();
            _actionName = actionName;
            _initialTargets = targets;
            _actionFunc = actionFunc;
            _settings = settings;

            ThemeManager.ApplyTheme(this, settings.Theme);
            Title = $"OpenRCT — {actionName}";
            HeaderTitleText.Text = actionName;
            ResultsGrid.ItemsSource = _rows;
            ErrorDrawer.Errors = _errors;

            Loaded += async (_, __) => await ExecuteTargetsAsync(_initialTargets);
            Closing += (_, __) => _cts?.Cancel();
        }

        private async Task ExecuteTargetsAsync(IReadOnlyList<TargetDevice> targets)
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            _rows.Clear();
            _errors.Clear();

            foreach (var t in targets)
            {
                _rows.Add(new ActionExecutionResult
                {
                    DeviceName = t.DisplayName,
                    ActionName = _actionName,
                    Status = ActionExecutionStatus.Pending,
                    Message = "Queued..."
                });
            }

            ProgressBar.Maximum = Math.Max(1, targets.Count);
            ProgressBar.Value = 0;
            CancelButton.IsEnabled = true;
            RetryFailedButton.IsEnabled = false;
            UpdateStats();

            var progress = new Progress<ActionExecutionResult>(update =>
            {
                var existing = _rows.FirstOrDefault(r => string.Equals(r.DeviceName, update.DeviceName, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    var idx = _rows.IndexOf(existing);
                    _rows[idx] = update;
                }
                else
                {
                    _rows.Add(update);
                }

                if (update.Status == ActionExecutionStatus.Failed)
                {
                    _errors.Add(new DeviceErrorEntry
                    {
                        Source = update.DeviceName,
                        Message = update.Message,
                        Details = update.ErrorDetails
                    });
                }

                ProgressBar.Value = _rows.Count(r => r.Status != ActionExecutionStatus.Pending && r.Status != ActionExecutionStatus.Running);
                UpdateStats();
            });

            var runner = new BulkActionRunner();
            await runner.RunAsync(
                _actionName,
                targets,
                _actionFunc,
                _settings.MaxConcurrentDevices,
                TimeSpan.FromSeconds(_settings.ConnectionTimeoutSeconds),
                progress,
                _cts.Token);

            CancelButton.IsEnabled = false;
            RetryFailedButton.IsEnabled = _rows.Any(r => r.Status == ActionExecutionStatus.Failed);
            UpdateStats();
        }

        private void UpdateStats()
        {
            int ok = _rows.Count(r => r.Status == ActionExecutionStatus.Succeeded);
            int warn = _rows.Count(r => r.Status == ActionExecutionStatus.Warning);
            int fail = _rows.Count(r => r.Status == ActionExecutionStatus.Failed);
            int done = _rows.Count(r => r.Status != ActionExecutionStatus.Pending && r.Status != ActionExecutionStatus.Running);
            SummaryStatsText.Text = $"Completed {done}/{_rows.Count} — Succeeded: {ok}, Warnings: {warn}, Failed: {fail}";
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

        private async void RetryFailed_Click(object sender, RoutedEventArgs e)
        {
            var failedNames = new HashSet<string>(
                _rows.Where(r => r.Status == ActionExecutionStatus.Failed).Select(r => r.DeviceName),
                StringComparer.OrdinalIgnoreCase);

            var retryTargets = _initialTargets.Where(t => failedNames.Contains(t.DisplayName)).ToList();
            if (retryTargets.Count > 0)
            {
                await ExecuteTargetsAsync(retryTargets);
            }
        }

        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv",
                FileName = $"OpenRCT_{_actionName.Replace(" ", "_")}_{DateTime.Now:yyyyMMdd_HHmm}.csv"
            };
            if (dlg.ShowDialog(this) != true) return;

            CsvExporter.WriteToFile(dlg.FileName, _rows, new[]
            {
                new CsvColumn<ActionExecutionResult>("Device", r => r.DeviceName),
                new CsvColumn<ActionExecutionResult>("Action", r => r.ActionName),
                new CsvColumn<ActionExecutionResult>("Status", r => r.Status),
                new CsvColumn<ActionExecutionResult>("Message", r => r.Message),
                new CsvColumn<ActionExecutionResult>("DurationMs", r => Math.Round(r.Duration.TotalMilliseconds, 0))
            });
        }
    }
}
