using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenCMTools.Core.Interfaces;
using OpenCMTools.Core.Models;

namespace OpenCMTools.Connectors.Services
{
    /// <summary>
    /// Native .NET EventLogSession-based remote Event Viewer connector with server-side XPath
    /// filtering and paginated reverse-chronological streaming.
    /// </summary>
    public sealed class RemoteEventViewerService : IRemoteEventViewerService
    {
        public Task<IReadOnlyList<string>> GetLogNamesAsync(TargetDevice device, CancellationToken ct = default)
        {
            return Task.Run<IReadOnlyList<string>>(() =>
            {
                ct.ThrowIfCancellationRequested();
                using var session = CreateSession(device.ConnectAddress);
                return session.GetLogNames()
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }, ct);
        }

        public Task<RemoteEventPageResult> QueryEventsAsync(TargetDevice device, RemoteEventLogQuery query, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var logName = string.IsNullOrWhiteSpace(query.LogName) ? "Application" : query.LogName.Trim();
                var pageSize = Math.Max(10, Math.Min(2000, query.MaxEntries <= 0 ? 200 : query.MaxEntries));
                var pageIndex = Math.Max(0, query.PageIndex);
                var skip = pageIndex * pageSize;

                using var session = CreateSession(device.ConnectAddress);

                long? totalInLog = null;
                try
                {
                    var logInfo = session.GetLogInformation(logName, PathType.LogName);
                    totalInLog = logInfo?.RecordCount;
                }
                catch { }

                var xpath = EventLogXPathBuilder.BuildXPath(query);
                var eventQuery = new EventLogQuery(logName, PathType.LogName, xpath)
                {
                    Session = session,
                    ReverseDirection = true
                };

                var sourceFilter = (query.Source ?? string.Empty).Trim();
                var keywordFilter = (query.Keyword ?? string.Empty).Trim();

                var entries = new List<RemoteEventEntry>(pageSize);
                var matchedCount = 0;
                var hasMore = false;

                using var reader = new EventLogReader(eventQuery);
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    using var rec = reader.ReadEvent();
                    if (rec == null) break;

                    var provider = rec.ProviderName ?? string.Empty;
                    if (!string.IsNullOrEmpty(sourceFilter) &&
                        provider.IndexOf(sourceFilter, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    string? message = null;
                    if (!string.IsNullOrEmpty(keywordFilter))
                    {
                        message = SafeFormatDescription(rec);
                        var task = SafeGetTaskDisplayName(rec);
                        if (message.IndexOf(keywordFilter, StringComparison.OrdinalIgnoreCase) < 0 &&
                            provider.IndexOf(keywordFilter, StringComparison.OrdinalIgnoreCase) < 0 &&
                            task.IndexOf(keywordFilter, StringComparison.OrdinalIgnoreCase) < 0 &&
                            rec.Id.ToString().IndexOf(keywordFilter, StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }
                    }

                    matchedCount++;
                    if (matchedCount <= skip)
                    {
                        continue;
                    }

                    if (entries.Count >= pageSize)
                    {
                        hasMore = true;
                        break;
                    }

                    var levelName = SafeGetLevelDisplayName(rec);
                    var taskName = SafeGetTaskDisplayName(rec);
                    message ??= SafeFormatDescription(rec);
                    var xml = SafeToXml(rec);

                    entries.Add(new RemoteEventEntry
                    {
                        RecordId = rec.RecordId ?? 0,
                        Level = levelName,
                        TimeCreated = rec.TimeCreated,
                        Source = provider,
                        EventId = rec.Id,
                        TaskCategory = taskName,
                        Message = message,
                        MachineName = rec.MachineName ?? device.DisplayName,
                        UserSid = rec.UserId?.Value ?? string.Empty,
                        RawXml = xml
                    });
                }

                return new RemoteEventPageResult
                {
                    LogName = logName,
                    Entries = entries,
                    PageIndex = pageIndex,
                    PageSize = pageSize,
                    TotalEntriesInLog = totalInLog,
                    HasMorePages = hasMore
                };
            }, ct);
        }

        private static EventLogSession CreateSession(string host)
        {
            if (string.IsNullOrWhiteSpace(host) ||
                string.Equals(host, ".", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(host, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            {
                return new EventLogSession();
            }

            return new EventLogSession(host);
        }

        private static string SafeFormatDescription(EventRecord rec)
        {
            try
            {
                var desc = rec.FormatDescription();
                if (!string.IsNullOrWhiteSpace(desc)) return desc;
            }
            catch { }

            try
            {
                if (rec.Properties != null && rec.Properties.Count > 0)
                {
                    return string.Join(" | ", rec.Properties.Select(p => p?.Value?.ToString() ?? string.Empty).Where(s => !string.IsNullOrWhiteSpace(s)));
                }
            }
            catch { }

            return "(No formatted description available)";
        }

        private static string SafeGetLevelDisplayName(EventRecord rec)
        {
            try
            {
                var name = rec.LevelDisplayName;
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
            catch { }

            return rec.Level switch
            {
                1 => "Critical",
                2 => "Error",
                3 => "Warning",
                4 => "Information",
                5 => "Verbose",
                _ => "Information"
            };
        }

        private static string SafeGetTaskDisplayName(EventRecord rec)
        {
            try { return rec.TaskDisplayName ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static string SafeToXml(EventRecord rec)
        {
            try { return rec.ToXml() ?? string.Empty; }
            catch { return string.Empty; }
        }
    }
}
