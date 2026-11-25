using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenRCT.Core.Models;

namespace OpenRCT.Core.Execution
{
    public sealed class BulkActionSummary
    {
        public string ActionName { get; set; } = string.Empty;
        public List<ActionExecutionResult> Results { get; set; } = new List<ActionExecutionResult>();
        public List<DeviceErrorEntry> Errors { get; set; } = new List<DeviceErrorEntry>();
        public int TotalCount => Results.Count;
        public int SucceededCount => Results.Count(r => r.Status == ActionExecutionStatus.Succeeded);
        public int WarningCount => Results.Count(r => r.Status == ActionExecutionStatus.Warning);
        public int FailedCount => Results.Count(r => r.Status == ActionExecutionStatus.Failed);
        public int CancelledCount => Results.Count(r => r.Status == ActionExecutionStatus.Cancelled);
    }

    /// <summary>
    /// Executes an action across 1..N target devices with bounded concurrency, per-device timeout,
    /// cancellation, live progress callbacks, and aggregated error capture.
    /// </summary>
    public sealed class BulkActionRunner
    {
        public async Task<BulkActionSummary> RunAsync(
            string actionName,
            IEnumerable<TargetDevice> targets,
            Func<TargetDevice, CancellationToken, Task<ActionExecutionResult>> executeForDevice,
            int maxConcurrency = 16,
            TimeSpan? perDeviceTimeout = null,
            IProgress<ActionExecutionResult>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (targets == null) throw new ArgumentNullException(nameof(targets));
            if (executeForDevice == null) throw new ArgumentNullException(nameof(executeForDevice));

            var targetList = targets
                .Where(t => t != null && !string.IsNullOrWhiteSpace(t.ConnectAddress))
                .GroupBy(t => t.ConnectAddress, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            var concurrency = Math.Max(1, Math.Min(64, maxConcurrency));
            var semaphore = new SemaphoreSlim(concurrency, concurrency);
            var results = new ConcurrentBag<ActionExecutionResult>();
            var errors = new ConcurrentBag<DeviceErrorEntry>();

            var tasks = targetList.Select(async target =>
            {
                var sw = Stopwatch.StartNew();
                if (cancellationToken.IsCancellationRequested)
                {
                    var cancelled = ActionExecutionResult.Cancelled(target.DisplayName, actionName, TimeSpan.Zero);
                    results.Add(cancelled);
                    progress?.Report(cancelled);
                    return;
                }

                bool acquired = false;
                try
                {
                    await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                    acquired = true;

                    progress?.Report(new ActionExecutionResult
                    {
                        DeviceName = target.DisplayName,
                        ActionName = actionName,
                        Status = ActionExecutionStatus.Running,
                        Message = "Running..."
                    });

                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    if (perDeviceTimeout.HasValue && perDeviceTimeout.Value > TimeSpan.Zero)
                    {
                        linkedCts.CancelAfter(perDeviceTimeout.Value);
                    }

                    ActionExecutionResult result;
                    try
                    {
                        result = await executeForDevice(target, linkedCts.Token).ConfigureAwait(false);
                        result.Duration = sw.Elapsed;
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && perDeviceTimeout.HasValue)
                    {
                        result = ActionExecutionResult.Failure(
                            target.DisplayName,
                            actionName,
                            $"Operation timed out after {perDeviceTimeout.Value.TotalSeconds:0.#}s.",
                            "Timeout",
                            sw.Elapsed);
                    }
                    catch (OperationCanceledException)
                    {
                        result = ActionExecutionResult.Cancelled(target.DisplayName, actionName, sw.Elapsed);
                    }
                    catch (Exception ex)
                    {
                        result = ActionExecutionResult.Failure(
                            target.DisplayName,
                            actionName,
                            ex.Message,
                            ex.ToString(),
                            sw.Elapsed);
                    }

                    results.Add(result);
                    if (result.Status == ActionExecutionStatus.Failed)
                    {
                        errors.Add(new DeviceErrorEntry
                        {
                            Timestamp = DateTime.Now,
                            Source = target.DisplayName,
                            Message = result.Message,
                            Details = result.ErrorDetails
                        });
                    }
                    progress?.Report(result);
                }
                catch (OperationCanceledException)
                {
                    var cancelled = ActionExecutionResult.Cancelled(target.DisplayName, actionName, sw.Elapsed);
                    results.Add(cancelled);
                    progress?.Report(cancelled);
                }
                finally
                {
                    if (acquired)
                    {
                        semaphore.Release();
                    }
                }
            }).ToArray();

            await Task.WhenAll(tasks).ConfigureAwait(false);

            var orderedResults = results
                .OrderBy(r => r.DeviceName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var orderedErrors = errors
                .OrderBy(e => e.Source, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new BulkActionSummary
            {
                ActionName = actionName,
                Results = orderedResults,
                Errors = orderedErrors
            };
        }
    }
}
