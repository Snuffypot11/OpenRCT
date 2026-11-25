using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using OpenRCT.Core.Ipc;
using OpenRCT.Core.Models;
using OpenRCT.Core.Settings;
using OpenRCT.UI;

namespace OpenRCT.App
{
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            if (e.Args == null || e.Args.Length == 0)
            {
                new MainLauncherWindow().Show();
                return;
            }

            var context = ParseCliArguments(e.Args);
            if (string.IsNullOrWhiteSpace(context.ActionKey))
            {
                new MainLauncherWindow().Show();
                return;
            }

            // Named-Pipe Multi-Select Batch Aggregator:
            // When launched via MECM Class="Executable" on N selected devices, MECM spawns N processes nearly simultaneously.
            // The first process acquires the per-action mutex and collects all sibling device payloads over a 350ms window
            // so all selected assets open together in ONE unified window.
            if (context.Devices.Length == 1 && !string.IsNullOrWhiteSpace(context.ActionKey))
            {
                var aggregated = await CoordinateMultiSelectBatchAsync(context);
                if (aggregated == null)
                {
                    // Handed off our device to the primary coordinator instance; exit cleanly.
                    Shutdown(0);
                    return;
                }
                context = aggregated;
            }

            ToolDispatcher.Dispatch(context);
            if (Windows.Count == 0)
            {
                Shutdown(0);
            }
        }

        private static async Task<ToolInvocationContext?> CoordinateMultiSelectBatchAsync(ToolInvocationContext initial)
        {
            var pipeName = BatchInvocationProtocol.GetPipeName(initial.ActionKey);
            var mutexName = $"Local\\{pipeName}_Mutex";

            using var mutex = new Mutex(true, mutexName, out bool isCoordinator);
            if (!isCoordinator)
            {
                try
                {
                    using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
                    client.Connect(600);
                    var payload = Encoding.UTF8.GetBytes(BatchInvocationProtocol.Serialize(initial));
                    await client.WriteAsync(payload, 0, payload.Length);
                    client.Flush();
                    return null;
                }
                catch
                {
                    // Coordinator already closed its batch window; proceed standalone
                    return initial;
                }
            }

            var collected = new List<TargetDevice>(initial.Devices);
            using var windowCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(350));

            while (!windowCts.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(pipeName, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(windowCts.Token);
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var json = await reader.ReadToEndAsync();
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var incoming = BatchInvocationProtocol.Deserialize(json);
                        collected.AddRange(incoming.Devices);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    break;
                }
            }

            initial.Devices = collected
                .GroupBy(d => d.ConnectAddress, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToArray();

            return initial;
        }

        public static ToolInvocationContext ParseCliArguments(string[] args)
        {
            var ctx = new ToolInvocationContext();
            var devices = new List<TargetDevice>();
            string? pendingDeviceName = null;
            string? pendingResourceId = null;
            string? pendingDomain = null;

            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                string NextVal() => i + 1 < args.Length ? args[++i] : string.Empty;

                switch (arg.ToLowerInvariant())
                {
                    case "--settings":
                        ctx.ActionKey = ToolActionKeys.OpenSettings;
                        break;
                    case "--action":
                        ctx.ActionKey = NextVal();
                        break;
                    case "--device":
                        var rawDevs = NextVal().Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                        if (rawDevs.Length == 1) pendingDeviceName = rawDevs[0].Trim();
                        else devices.AddRange(rawDevs.Select(n => new TargetDevice { Name = n.Trim() }));
                        break;
                    case "--resource-id":
                        pendingResourceId = NextVal().Trim();
                        break;
                    case "--domain":
                        pendingDomain = NextVal().Trim();
                        break;
                    case "--collection-id":
                        ctx.CollectionId = NextVal().Trim();
                        break;
                    case "--collection-name":
                        ctx.CollectionName = NextVal().Trim();
                        break;
                    case "--server":
                        ctx.SmsProviderServer = NextVal().Trim();
                        break;
                    case "--site-code":
                        ctx.SiteCode = NextVal().Trim();
                        break;
                    case "--namespace":
                        var ns = NextVal();
                        var idx = ns.IndexOf("site_", StringComparison.OrdinalIgnoreCase);
                        if (idx >= 0 && idx + 5 < ns.Length) ctx.SiteCode = ns.Substring(idx + 5).Trim();
                        break;
                }
            }

            if (!string.IsNullOrWhiteSpace(pendingDeviceName))
            {
                devices.Add(new TargetDevice
                {
                    Name = pendingDeviceName!,
                    ResourceId = pendingResourceId,
                    Domain = pendingDomain,
                    SiteCode = ctx.SiteCode,
                    SmsProviderServer = ctx.SmsProviderServer
                });
            }

            ctx.Devices = devices.ToArray();
            return ctx;
        }
    }
}
