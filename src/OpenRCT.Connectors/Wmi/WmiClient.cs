using System;
using System.Collections.Generic;
using System.Management;
using System.Threading;
using System.Threading.Tasks;

namespace OpenRCT.Connectors.Wmi
{
    /// <summary>
    /// Helper for executing remote WMI/DCOM queries and static/instance method invocations
    /// with configurable timeouts and safe disposal.
    /// </summary>
    public sealed class WmiClient
    {
        public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(15);
        public bool EnableFastReachabilityCheck { get; set; }
        public int ReachabilityTimeoutMs { get; set; } = 1500;

        public Task EnsureReachableIfEnabledAsync(string host, CancellationToken ct = default)
        {
            if (!EnableFastReachabilityCheck) return Task.CompletedTask;
            return DeviceReachabilityChecker.EnsureReachableAsync(host, ReachabilityTimeoutMs, ct);
        }

        public ManagementScope CreateScope(string host, string wmiNamespace, TimeSpan? timeout = null)
        {
            var options = new ConnectionOptions
            {
                Impersonation = ImpersonationLevel.Impersonate,
                Authentication = AuthenticationLevel.PacketPrivacy,
                EnablePrivileges = true,
                Timeout = timeout ?? DefaultTimeout
            };

            var normalizedHost = string.IsNullOrWhiteSpace(host) ? "." : host.Trim();
            var ns = wmiNamespace.TrimStart('\\');
            var path = new ManagementPath($"\\\\{normalizedHost}\\{ns}");
            var scope = new ManagementScope(path, options);
            scope.Connect();
            return scope;
        }

        public async Task<List<Dictionary<string, object?>>> QueryAsync(
            string host,
            string wmiNamespace,
            string wql,
            TimeSpan? timeout = null,
            CancellationToken ct = default,
            IDictionary<string, object?>? context = null)
        {
            await EnsureReachableIfEnabledAsync(host, ct).ConfigureAwait(false);
            var effectiveTimeout = timeout ?? DefaultTimeout;

            var workTask = Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var results = new List<Dictionary<string, object?>>();
                var scope = CreateScope(host, wmiNamespace, effectiveTimeout);
                var query = new ObjectQuery(wql);
                var enumOptions = new EnumerationOptions
                {
                    ReturnImmediately = true,
                    Rewindable = false,
                    Timeout = effectiveTimeout
                };

                if (context != null && context.Count > 0)
                {
                    var wmiContext = new ManagementNamedValueCollection();
                    foreach (var kvp in context)
                    {
                        if (!string.IsNullOrWhiteSpace(kvp.Key) && kvp.Value != null)
                        {
                            wmiContext.Add(kvp.Key, kvp.Value);
                        }
                    }
                    enumOptions.Context = wmiContext;
                }

                using var searcher = new ManagementObjectSearcher(scope, query, enumOptions);
                using var collection = searcher.Get();
                foreach (ManagementBaseObject obj in collection)
                {
                    ct.ThrowIfCancellationRequested();
                    using (obj)
                    {
                        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                        foreach (PropertyData prop in obj.Properties)
                        {
                            row[prop.Name] = prop.Value;
                        }
                        results.Add(row);
                    }
                }
                return results;
            }, ct);

            return await AwaitWithHardTimeoutAsync(workTask, host, effectiveTimeout, ct).ConfigureAwait(false);
        }

        public async Task<ManagementBaseObject?> InvokeStaticMethodAsync(
            string host,
            string wmiNamespace,
            string className,
            string methodName,
            IDictionary<string, object?>? parameters = null,
            TimeSpan? timeout = null,
            CancellationToken ct = default)
        {
            await EnsureReachableIfEnabledAsync(host, ct).ConfigureAwait(false);
            var effectiveTimeout = timeout ?? DefaultTimeout;

            var workTask = Task.Run<ManagementBaseObject?>(() =>
            {
                ct.ThrowIfCancellationRequested();
                var scope = CreateScope(host, wmiNamespace, effectiveTimeout);
                using var mgmtClass = new ManagementClass(scope, new ManagementPath(className), null);
                using var inParams = mgmtClass.GetMethodParameters(methodName);
                if (parameters != null)
                {
                    foreach (var kvp in parameters)
                    {
                        inParams[kvp.Key] = kvp.Value;
                    }
                }

                return mgmtClass.InvokeMethod(methodName, inParams, new InvokeMethodOptions { Timeout = effectiveTimeout });
            }, ct);

            return await AwaitWithHardTimeoutAsync(workTask, host, effectiveTimeout, ct).ConfigureAwait(false);
        }

        private static async Task<T> AwaitWithHardTimeoutAsync<T>(Task<T> workTask, string host, TimeSpan timeout, CancellationToken ct)
        {
            if (timeout <= TimeSpan.Zero || timeout == Timeout.InfiniteTimeSpan)
            {
                return await workTask.ConfigureAwait(false);
            }

            var delayTask = Task.Delay(timeout, ct);
            var completed = await Task.WhenAny(workTask, delayTask).ConfigureAwait(false);
            if (completed == workTask)
            {
                return await workTask.ConfigureAwait(false);
            }

            // Observe any late exception from the background WMI thread when OS DCOM finally releases.
            _ = workTask.ContinueWith(
                t => { _ = t.Exception; },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            ct.ThrowIfCancellationRequested();
            throw new TimeoutException($"WMI connection to '{host}' timed out after {timeout.TotalSeconds:0.#}s.");
        }

        public async Task<uint> CreateRemoteProcessAsync(
            string host,
            string commandLine,
            TimeSpan? timeout = null,
            CancellationToken ct = default)
        {
            using var outParams = await InvokeStaticMethodAsync(
                host,
                @"root\cimv2",
                "Win32_Process",
                "Create",
                new Dictionary<string, object?> { ["CommandLine"] = commandLine },
                timeout,
                ct).ConfigureAwait(false);

            var retVal = outParams != null && outParams["ReturnValue"] != null
                ? Convert.ToUInt32(outParams["ReturnValue"])
                : 99u;

            if (retVal != 0)
            {
                throw new InvalidOperationException($"Win32_Process.Create failed on {host} with return code {retVal}.");
            }

            return outParams != null && outParams["ProcessId"] != null
                ? Convert.ToUInt32(outParams["ProcessId"])
                : 0u;
        }

        public static string GetString(IDictionary<string, object?> row, string key, string fallback = "")
        {
            if (row.TryGetValue(key, out var val) && val != null)
            {
                return val.ToString() ?? fallback;
            }
            return fallback;
        }

        public static long GetInt64(IDictionary<string, object?> row, string key, long fallback = 0)
        {
            if (row.TryGetValue(key, out var val) && val != null)
            {
                try { return Convert.ToInt64(val); } catch { return fallback; }
            }
            return fallback;
        }

        public static int GetInt32(IDictionary<string, object?> row, string key, int fallback = 0)
        {
            if (row.TryGetValue(key, out var val) && val != null)
            {
                try { return Convert.ToInt32(val); } catch { return fallback; }
            }
            return fallback;
        }

        public static uint GetUInt32(IDictionary<string, object?> row, string key, uint fallback = 0)
        {
            if (row.TryGetValue(key, out var val) && val != null)
            {
                try { return Convert.ToUInt32(val); } catch { return fallback; }
            }
            return fallback;
        }

        public static bool GetBool(IDictionary<string, object?> row, string key, bool fallback = false)
        {
            if (row.TryGetValue(key, out var val) && val != null)
            {
                try { return Convert.ToBoolean(val); } catch { return fallback; }
            }
            return fallback;
        }

        public static DateTime? GetDmtfDateTime(IDictionary<string, object?> row, string key)
        {
            var raw = GetString(row, key);
            if (string.IsNullOrWhiteSpace(raw)) return null;
            try
            {
                return ManagementDateTimeConverter.ToDateTime(raw);
            }
            catch
            {
                return DateTime.TryParse(raw, out var parsed) ? parsed : (DateTime?)null;
            }
        }
    }
}
