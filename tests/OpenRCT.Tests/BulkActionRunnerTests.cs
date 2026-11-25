using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenRCT.Core.Execution;
using OpenRCT.Core.Models;
using Xunit;

namespace OpenRCT.Tests
{
    public sealed class BulkActionRunnerTests
    {
        [Fact]
        public async Task RunAsync_EnforcesMaxConcurrency_AndDeduplicatesTargets()
        {
            var runner = new BulkActionRunner();
            var targets = Enumerable.Range(1, 20)
                .Select(i => new TargetDevice { Name = $"PC-{i:D2}" })
                .Append(new TargetDevice { Name = "pc-01" }) // duplicate (case-insensitive)
                .ToList();

            int active = 0;
            int maxObserved = 0;

            var summary = await runner.RunAsync(
                "Test Action",
                targets,
                async (dev, ct) =>
                {
                    var current = Interlocked.Increment(ref active);
                    while (true)
                    {
                        var snapshot = maxObserved;
                        if (current <= snapshot || Interlocked.CompareExchange(ref maxObserved, current, snapshot) == snapshot)
                            break;
                    }
                    await Task.Delay(25, ct);
                    Interlocked.Decrement(ref active);
                    return ActionExecutionResult.Success(dev.DisplayName, "Test Action", "OK", TimeSpan.FromMilliseconds(25));
                },
                maxConcurrency: 4);

            Assert.Equal(20, summary.TotalCount);
            Assert.Equal(20, summary.SucceededCount);
            Assert.Empty(summary.Errors);
            Assert.InRange(maxObserved, 1, 4);
        }

        [Fact]
        public async Task RunAsync_CapturesPartialFailuresAndTimeoutsIntoErrorDrawer()
        {
            var runner = new BulkActionRunner();
            var targets = new[]
            {
                new TargetDevice { Name = "GOOD-01" },
                new TargetDevice { Name = "FAIL-02" },
                new TargetDevice { Name = "SLOW-03" }
            };

            var summary = await runner.RunAsync(
                "Mixed Action",
                targets,
                async (dev, ct) =>
                {
                    if (dev.Name == "FAIL-02") throw new InvalidOperationException("RPC server is unavailable.");
                    if (dev.Name == "SLOW-03") await Task.Delay(2000, ct);
                    return ActionExecutionResult.Success(dev.DisplayName, "Mixed Action", "Done", TimeSpan.FromMilliseconds(5));
                },
                maxConcurrency: 3,
                perDeviceTimeout: TimeSpan.FromMilliseconds(100));

            Assert.Equal(3, summary.TotalCount);
            Assert.Equal(1, summary.SucceededCount);
            Assert.Equal(2, summary.FailedCount);
            Assert.Equal(2, summary.Errors.Count);
            Assert.Contains(summary.Errors, e => e.Source == "FAIL-02" && e.Message.Contains("RPC"));
            Assert.Contains(summary.Errors, e => e.Source == "SLOW-03" && e.Message.Contains("timed out"));
        }

        [Fact]
        public async Task RunAsync_RespectsCancellationToken()
        {
            var runner = new BulkActionRunner();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var summary = await runner.RunAsync(
                "Cancelled Action",
                new[] { new TargetDevice { Name = "PC-01" }, new TargetDevice { Name = "PC-02" } },
                (dev, ct) => Task.FromResult(ActionExecutionResult.Success(dev.DisplayName, "Cancelled Action", "OK", TimeSpan.Zero)),
                cancellationToken: cts.Token);

            Assert.Equal(2, summary.CancelledCount);
            Assert.Equal(0, summary.SucceededCount);
        }
    }
}
