using System;
using System.Collections.Generic;
using System.Linq;
using OpenCMTools.Core.Models;

namespace OpenCMTools.Core.Analytics
{
    /// <summary>
    /// Aggregates ConfigMgr Distribution Point metadata and content status rows for the
    /// Content Distribution Monitor dashboard.
    /// </summary>
    public static class ContentDistributionAnalyzer
    {
        public static ContentDistributionState MapMessageState(int messageState) => messageState switch
        {
            1 => ContentDistributionState.Success,
            2 => ContentDistributionState.InProgress,
            3 => ContentDistributionState.Error,
            4 => ContentDistributionState.Error,
            _ => messageState > 2 ? ContentDistributionState.Error : ContentDistributionState.Unknown
        };

        public static string MapContentType(uint objectTypeId) => objectTypeId switch
        {
            0 => "Package",
            2 => "Package",
            14 => "OS Install Package",
            18 => "OS Image",
            19 => "Boot Image",
            21 => "Device Setting Package",
            23 => "Driver Package",
            24 => "Software Update Package",
            31 => "Application",
            _ => $"Content ({objectTypeId})"
        };

        public static string DescribeMessageId(uint messageId, string? fallbackDetails = null)
        {
            string baseDesc = messageId switch
            {
                2330 => "Content was successfully distributed to distribution point.",
                2384 => "Content hash has been successfully verified.",
                2386 => "Content validation cycle completed successfully.",
                2385 => "Content validation started on distribution point.",
                2387 => "Content validation failed for package.",
                2388 => "Failed to retrieve the package list on the distribution point.",
                2391 => "Failed to connect to remote distribution point.",
                2303 => "Content was successfully refreshed on distribution point.",
                2323 => "Failed to initialize NAL.",
                2324 => "Failed to access or create the content share.",
                2332 => "Content was removed from distribution point.",
                2370 => "Failed to install distribution point.",
                2399 => "Successfully completed the installation or upgrade of the distribution point.",
                8204 => "Content is being distributed to the distribution point.",
                _ => $"Status Message ID {messageId}"
            };

            return string.IsNullOrWhiteSpace(fallbackDetails)
                ? baseDesc
                : $"{baseDesc} ({fallbackDetails})";
        }

        public static ContentDistributionSnapshot Aggregate(
            IEnumerable<DistributionPointSummary> distributionPoints,
            IEnumerable<ContentDistributionItem> items)
        {
            var dpList = (distributionPoints ?? Enumerable.Empty<DistributionPointSummary>())
                .Select(CloneSummary)
                .ToList();
            var itemList = (items ?? Enumerable.Empty<ContentDistributionItem>()).ToList();

            var dpByServer = new Dictionary<string, DistributionPointSummary>(StringComparer.OrdinalIgnoreCase);
            var dpByNal = new Dictionary<string, DistributionPointSummary>(StringComparer.OrdinalIgnoreCase);

            foreach (var dp in dpList)
            {
                dp.SuccessCount = 0;
                dp.InProgressCount = 0;
                dp.ErrorCount = 0;
                if (!string.IsNullOrWhiteSpace(dp.ServerName) && !dpByServer.ContainsKey(dp.ServerName))
                {
                    dpByServer[dp.ServerName] = dp;
                }
                if (!string.IsNullOrWhiteSpace(dp.NalPath) && !dpByNal.ContainsKey(dp.NalPath))
                {
                    dpByNal[dp.NalPath] = dp;
                }
            }

            foreach (var item in itemList)
            {
                DistributionPointSummary? matched = null;
                if (!string.IsNullOrWhiteSpace(item.ServerNalPath))
                {
                    dpByNal.TryGetValue(item.ServerNalPath, out matched);
                }
                if (matched == null && !string.IsNullOrWhiteSpace(item.DistributionPoint))
                {
                    dpByServer.TryGetValue(item.DistributionPoint, out matched);
                }
                if (matched == null && !string.IsNullOrWhiteSpace(item.DistributionPoint))
                {
                    matched = new DistributionPointSummary
                    {
                        ServerName = item.DistributionPoint,
                        NalPath = item.ServerNalPath
                    };
                    dpList.Add(matched);
                    dpByServer[matched.ServerName] = matched;
                    if (!string.IsNullOrWhiteSpace(matched.NalPath)) dpByNal[matched.NalPath] = matched;
                }

                if (matched != null)
                {
                    switch (item.State)
                    {
                        case ContentDistributionState.Success:
                            matched.SuccessCount++;
                            break;
                        case ContentDistributionState.InProgress:
                            matched.InProgressCount++;
                            break;
                        case ContentDistributionState.Error:
                            matched.ErrorCount++;
                            break;
                    }
                }
            }

            return new ContentDistributionSnapshot
            {
                DistributionPoints = dpList
                    .OrderByDescending(d => d.ErrorCount)
                    .ThenBy(d => d.ServerName, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                Items = itemList
                    .OrderBy(i => i.State == ContentDistributionState.Error ? 0 : i.State == ContentDistributionState.InProgress ? 1 : 2)
                    .ThenBy(i => i.ContentName, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };
        }

        public static List<ContentDistributionItem> FilterItems(
            IEnumerable<ContentDistributionItem> items,
            string? selectedDpServerName,
            string? statusFilter,
            string? searchQuery)
        {
            IEnumerable<ContentDistributionItem> query = items ?? Enumerable.Empty<ContentDistributionItem>();

            if (!string.IsNullOrWhiteSpace(selectedDpServerName))
            {
                query = query.Where(i => string.Equals(i.DistributionPoint, selectedDpServerName, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(statusFilter) && !string.Equals(statusFilter, "All", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(i => string.Equals(i.DistributionStatus, statusFilter, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                var term = searchQuery!.Trim();
                query = query.Where(i =>
                    (i.ContentName?.IndexOf(term, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                    (i.PackageId?.IndexOf(term, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                    (i.ObjectId?.IndexOf(term, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                    (i.DistributionPoint?.IndexOf(term, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0 ||
                    (i.Description?.IndexOf(term, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0);
            }

            return query.ToList();
        }

        public static string ExtractServerNameFromNalPath(string? nalPath)
        {
            if (string.IsNullOrWhiteSpace(nalPath)) return string.Empty;
            var marker = @"\\";
            var idx = nalPath!.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0) return nalPath.Trim();
            var start = idx + marker.Length;
            var end = nalPath.IndexOfAny(new[] { '\\', '"', ']' }, start);
            return end > start ? nalPath.Substring(start, end - start).Trim() : nalPath.Substring(start).Trim();
        }

        private static DistributionPointSummary CloneSummary(DistributionPointSummary s) => new DistributionPointSummary
        {
            ServerName = s.ServerName,
            NalPath = s.NalPath,
            SiteCode = s.SiteCode,
            Description = s.Description,
            HttpsEnabled = s.HttpsEnabled,
            LedbatEnabled = s.LedbatEnabled,
            InternetFacing = s.InternetFacing,
            MulticastEnabled = s.MulticastEnabled,
            PeerDp = s.PeerDp,
            PullDp = s.PullDp,
            MaintenanceModeEnabled = s.MaintenanceModeEnabled,
            PxeEnabled = s.PxeEnabled,
            PercentFreeSpace = s.PercentFreeSpace
        };
    }
}
