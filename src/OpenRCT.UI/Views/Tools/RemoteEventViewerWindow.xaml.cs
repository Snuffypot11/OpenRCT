using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using OpenRCT.Core.Export;
using OpenRCT.Core.Interfaces;
using OpenRCT.Core.Models;
using OpenRCT.Core.Settings;
using OpenRCT.UI.Themes;

namespace OpenRCT.UI.Views.Tools
{
    public partial class RemoteEventViewerWindow : Window
    {
        private static readonly string[] CoreWindowsLogs =
        {
            "Application",
            "ForwardedEvents",
            "Security",
            "Setup",
            "System"
        };

        private readonly IRemoteEventViewerService _service;
        private readonly ObservableCollection<DeviceErrorEntry> _errors = new ObservableCollection<DeviceErrorEntry>();
        private readonly DispatcherTimer _filterDebounceTimer;

        private string _activeLogName = "Application";
        private int _currentPageIndex;
        private List<RemoteEventEntry> _currentEntries = new List<RemoteEventEntry>();
        private CancellationTokenSource? _activeQueryCts;
        private bool _suppressFilterEvents;

        public RemoteEventViewerWindow(IReadOnlyList<TargetDevice> devices, IRemoteEventViewerService service, OpenRctSettings settings)
        {
            _service = service;
            ThemeManager.ApplyTheme(this, settings.Theme);
            InitializeComponent();
            ErrorDrawer.Errors = _errors;

            _filterDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(420) };
            _filterDebounceTimer.Tick += async (_, __) =>
            {
                _filterDebounceTimer.Stop();
                _currentPageIndex = 0;
                await LoadEventsAsync();
            };

            DeviceCombo.ItemsSource = devices;
            if (devices.Count > 0) DeviceCombo.SelectedIndex = 0;

            Closed += (_, __) => CancelActiveQuery();
            Loaded += async (_, __) => await LoadDeviceLogsAndEventsAsync();
        }

        private TargetDevice? SelectedDevice => DeviceCombo.SelectedItem as TargetDevice;

        private async void DeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            _currentPageIndex = 0;
            await LoadDeviceLogsAndEventsAsync();
        }

        private async Task LoadDeviceLogsAndEventsAsync()
        {
            var dev = SelectedDevice;
            if (dev == null) return;

            Title = $"Remote Event Viewer - {dev.DisplayName}";
            _errors.Clear();
            BuildBaseLogTree();
            await LoadEventsAsync();

            // Populate Applications and Services Logs asynchronously so the core logs render immediately
            try
            {
                var logNames = await _service.GetLogNamesAsync(dev);
                PopulateAppsAndServicesLogs(logNames);
            }
            catch (Exception ex)
            {
                _errors.Add(new DeviceErrorEntry { Source = dev.DisplayName, Message = $"Unable to enumerate all event logs: {ex.Message}", Details = ex.ToString() });
            }
        }

        private void BuildBaseLogTree()
        {
            LogTree.Items.Clear();

            var winNode = new TreeViewItem
            {
                Header = "Windows Logs",
                IsExpanded = true,
                Tag = null
            };

            foreach (var log in CoreWindowsLogs)
            {
                var item = new TreeViewItem
                {
                    Header = log,
                    Tag = log
                };
                if (string.Equals(log, _activeLogName, StringComparison.OrdinalIgnoreCase))
                {
                    item.IsSelected = true;
                }
                winNode.Items.Add(item);
            }

            var appsNode = new TreeViewItem
            {
                Header = "Applications and Services Logs",
                IsExpanded = false,
                Tag = null
            };
            appsNode.Items.Add(new TreeViewItem { Header = "Loading...", Tag = null });

            LogTree.Items.Add(winNode);
            LogTree.Items.Add(appsNode);
        }

        private void PopulateAppsAndServicesLogs(IReadOnlyList<string> allLogNames)
        {
            if (LogTree.Items.Count < 2 || !(LogTree.Items[1] is TreeViewItem appsRoot)) return;
            appsRoot.Items.Clear();

            var coreSet = new HashSet<string>(CoreWindowsLogs, StringComparer.OrdinalIgnoreCase);
            var extraLogs = allLogNames.Where(n => !coreSet.Contains(n)).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

            var folderCache = new Dictionary<string, TreeViewItem>(StringComparer.OrdinalIgnoreCase);

            foreach (var logName in extraLogs)
            {
                // Split Microsoft-Windows-XYZ/Operational into clean nested folders
                var slashIdx = logName.IndexOf('/');
                if (slashIdx > 0)
                {
                    var prefix = logName.Substring(0, slashIdx);
                    var leaf = logName.Substring(slashIdx + 1);

                    // Group Microsoft-Windows-* under Microsoft -> Windows -> <Feature>
                    TreeViewItem parent;
                    if (prefix.StartsWith("Microsoft-Windows-", StringComparison.OrdinalIgnoreCase))
                    {
                        var msFolder = GetOrCreateFolder(appsRoot, folderCache, "Microsoft", "Microsoft");
                        var winFolder = GetOrCreateFolder(msFolder, folderCache, "Microsoft/Windows", "Windows");
                        var feature = prefix.Substring("Microsoft-Windows-".Length);
                        parent = GetOrCreateFolder(winFolder, folderCache, $"Microsoft/Windows/{feature}", feature);
                    }
                    else
                    {
                        parent = GetOrCreateFolder(appsRoot, folderCache, prefix, prefix);
                    }

                    parent.Items.Add(new TreeViewItem { Header = leaf, Tag = logName, ToolTip = logName });
                }
                else
                {
                    appsRoot.Items.Add(new TreeViewItem { Header = logName, Tag = logName, ToolTip = logName });
                }
            }
        }

        private static TreeViewItem GetOrCreateFolder(TreeViewItem parent, Dictionary<string, TreeViewItem> cache, string key, string header)
        {
            if (cache.TryGetValue(key, out var existing)) return existing;
            var node = new TreeViewItem { Header = header, Tag = null };
            parent.Items.Add(node);
            cache[key] = node;
            return node;
        }

        private async void LogTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (!IsLoaded) return;
            if (LogTree.SelectedItem is TreeViewItem item && item.Tag is string logName && !string.IsNullOrWhiteSpace(logName))
            {
                if (!string.Equals(_activeLogName, logName, StringComparison.OrdinalIgnoreCase))
                {
                    _activeLogName = logName;
                    _currentPageIndex = 0;
                    await LoadEventsAsync();
                }
            }
        }

        private void FilterText_Changed(object sender, TextChangedEventArgs e)
        {
            if (!IsLoaded || _suppressFilterEvents) return;
            _filterDebounceTimer.Stop();
            _filterDebounceTimer.Start();
        }

        private async void FilterControl_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || _suppressFilterEvents) return;
            _currentPageIndex = 0;
            await LoadEventsAsync();
        }

        private async void DatePicker_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || _suppressFilterEvents) return;
            _currentPageIndex = 0;
            await LoadEventsAsync();
        }

        private async void ClearFilters_Click(object sender, RoutedEventArgs e)
        {
            _suppressFilterEvents = true;
            try
            {
                SeverityCombo.SelectedIndex = 0;
                SourceBox.Text = string.Empty;
                EventIdBox.Text = string.Empty;
                MaxEntriesBox.Text = "200";
                StartDatePicker.SelectedDate = null;
                EndDatePicker.SelectedDate = null;
                KeywordBox.Text = string.Empty;
                _currentPageIndex = 0;
            }
            finally
            {
                _suppressFilterEvents = false;
            }
            await LoadEventsAsync();
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadEventsAsync();

        private async void PrevPage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPageIndex > 0)
            {
                _currentPageIndex--;
                await LoadEventsAsync();
            }
        }

        private async void NextPage_Click(object sender, RoutedEventArgs e)
        {
            _currentPageIndex++;
            await LoadEventsAsync();
        }

        private void CancelActiveQuery()
        {
            var old = _activeQueryCts;
            _activeQueryCts = null;
            if (old != null)
            {
                try { old.Cancel(); } catch { }
                try { old.Dispose(); } catch { }
            }
        }

        private RemoteEventLogQuery BuildQuery()
        {
            var maxEntries = 200;
            if (int.TryParse(MaxEntriesBox.Text?.Trim(), out var parsed) && parsed >= 10 && parsed <= 2000)
            {
                maxEntries = parsed;
            }

            var severity = (SeverityCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All";

            return new RemoteEventLogQuery
            {
                LogName = _activeLogName,
                Severity = severity,
                Source = SourceBox.Text?.Trim() ?? string.Empty,
                EventIdsText = EventIdBox.Text?.Trim() ?? string.Empty,
                Keyword = KeywordBox.Text?.Trim() ?? string.Empty,
                StartDate = StartDatePicker.SelectedDate,
                EndDate = EndDatePicker.SelectedDate,
                MaxEntries = maxEntries,
                PageIndex = _currentPageIndex
            };
        }

        private async Task LoadEventsAsync()
        {
            var dev = SelectedDevice;
            if (dev == null) return;

            CancelActiveQuery();
            var cts = new CancellationTokenSource();
            _activeQueryCts = cts;

            var query = BuildQuery();
            LoadingText.Text = $"Querying '{query.LogName}' on {dev.DisplayName}...";
            LoadingOverlay.Visibility = Visibility.Visible;
            StatusText.Text = $"Loading {query.LogName}...";

            try
            {
                var page = await _service.QueryEventsAsync(dev, query, cts.Token);
                if (cts.IsCancellationRequested) return;

                _currentEntries = page.Entries;
                EventsGrid.ItemsSource = _currentEntries;

                var startNum = page.Entries.Count == 0 ? 0 : (page.PageIndex * page.PageSize) + 1;
                var endNum = (page.PageIndex * page.PageSize) + page.Entries.Count;
                PageNumberText.Text = $"Page {page.PageIndex + 1}";
                PrevPageButton.IsEnabled = page.PageIndex > 0;
                NextPageButton.IsEnabled = page.HasMorePages;

                var totalStr = page.TotalEntriesInLog.HasValue
                    ? $" — ~{page.TotalEntriesInLog.Value:N0} total entries in log (filters may affect this count)"
                    : string.Empty;
                PageSummaryText.Text = page.Entries.Count == 0
                    ? $"No matching entries{totalStr}"
                    : $"Showing {startNum:N0}-{endNum:N0} loaded{totalStr}";

                StatusText.Text = $"{dev.DisplayName} | {page.LogName} | {page.Entries.Count} event(s) on page {page.PageIndex + 1}";

                if (_currentEntries.Count > 0)
                {
                    EventsGrid.SelectedIndex = 0;
                }
                else
                {
                    EventDetailTextBox.Text = string.Empty;
                    EventXmlTextBox.Text = string.Empty;
                }
            }
            catch (OperationCanceledException)
            {
                // Superseded by newer filter query
            }
            catch (Exception ex)
            {
                _errors.Add(new DeviceErrorEntry { Source = $"{dev.DisplayName} ({query.LogName})", Message = ex.Message, Details = ex.ToString() });
                StatusText.Text = "Failed to query event log (see Errors drawer).";
            }
            finally
            {
                if (ReferenceEquals(_activeQueryCts, cts))
                {
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void EventsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(EventsGrid.SelectedItem is RemoteEventEntry entry))
            {
                EventDetailTextBox.Text = string.Empty;
                EventXmlTextBox.Text = string.Empty;
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Log Name:     {_activeLogName}");
            sb.AppendLine($"Source:       {entry.Source}");
            sb.AppendLine($"Logged:       {entry.TimeCreatedDisplay}");
            sb.AppendLine($"Event ID:     {entry.EventId}");
            sb.AppendLine($"Level:        {entry.Level}");
            if (!string.IsNullOrWhiteSpace(entry.TaskCategory))
                sb.AppendLine($"Task:         {entry.TaskCategory}");
            if (!string.IsNullOrWhiteSpace(entry.UserSid))
                sb.AppendLine($"User SID:     {entry.UserSid}");
            sb.AppendLine($"Computer:     {entry.MachineName}");
            sb.AppendLine(new string('-', 60));
            sb.AppendLine(entry.Message);

            EventDetailTextBox.Text = sb.ToString();
            EventXmlTextBox.Text = entry.RawXml;
        }

        private void CopyDetails_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(EventDetailTextBox.Text))
            {
                Clipboard.SetText(EventDetailTextBox.Text);
                StatusText.Text = "Event details copied to clipboard.";
            }
        }

        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            if (_currentEntries.Count == 0) return;
            var safeLog = _activeLogName.Replace('/', '_').Replace('\\', '_');
            var dlg = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv",
                FileName = $"OpenRCT_EventLog_{safeLog}_{DateTime.Now:yyyyMMdd_HHmm}.csv"
            };
            if (dlg.ShowDialog(this) != true) return;

            CsvExporter.WriteToFile(dlg.FileName, _currentEntries, new[]
            {
                new CsvColumn<RemoteEventEntry>("Computer", x => x.MachineName),
                new CsvColumn<RemoteEventEntry>("Level", x => x.Level),
                new CsvColumn<RemoteEventEntry>("Time Created", x => x.TimeCreatedDisplay),
                new CsvColumn<RemoteEventEntry>("Source", x => x.Source),
                new CsvColumn<RemoteEventEntry>("Event ID", x => x.EventId),
                new CsvColumn<RemoteEventEntry>("Task Category", x => x.TaskCategory),
                new CsvColumn<RemoteEventEntry>("Message", x => x.Message)
            });
        }
    }
}
