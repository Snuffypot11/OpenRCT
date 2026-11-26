using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace OpenRCT.Connectors.Wmi
{
    /// <summary>
    /// Thrown when a fast online pre-check determines that a target device is offline or unreachable,
    /// avoiding 21-42s OS-level DCOM/RPC handshake hangs per WMI query.
    /// </summary>
    public sealed class DeviceUnreachableException : IOException
    {
        public DeviceUnreachableException(string host, string message) : base(message)
        {
            Host = host;
        }

        public string Host { get; }
    }

    /// <summary>
    /// Fast, two-stage host reachability pre-check (DNS + ICMP Ping + RPC/135 &amp; SMB/445 TCP probe fallback)
    /// with a short-lived per-host cache so multi-query tools never re-probe an offline device repeatedly.
    /// </summary>
    public static class DeviceReachabilityChecker
    {
        private sealed class CacheEntry
        {
            public bool IsReachable { get; set; }
            public string? FailureReason { get; set; }
            public DateTime ExpiresUtc { get; set; }
        }

        private static readonly ConcurrentDictionary<string, CacheEntry> Cache =
            new ConcurrentDictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

        private static readonly TimeSpan ReachableCacheTtl = TimeSpan.FromSeconds(12);
        private static readonly TimeSpan UnreachableCacheTtl = TimeSpan.FromSeconds(10);

        public static void ClearCache() => Cache.Clear();

        public static void ClearHost(string host)
        {
            if (!string.IsNullOrWhiteSpace(host))
            {
                Cache.TryRemove(host.Trim(), out _);
            }
        }

        public static bool IsLocalHost(string? host)
        {
            if (string.IsNullOrWhiteSpace(host)) return true;
            var h = host!.Trim();
            if (h == "." ||
                string.Equals(h, "localhost", StringComparison.OrdinalIgnoreCase) ||
                h == "127.0.0.1" ||
                h == "::1")
            {
                return true;
            }

            try
            {
                var localName = Environment.MachineName;
                if (string.Equals(h, localName, StringComparison.OrdinalIgnoreCase) ||
                    h.StartsWith(localName + ".", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch { }

            return false;
        }

        public static async Task EnsureReachableAsync(string host, int timeoutMs = 1500, CancellationToken ct = default)
        {
            if (IsLocalHost(host)) return;

            var normalized = host.Trim();
            var now = DateTime.UtcNow;
            if (Cache.TryGetValue(normalized, out var cached) && cached.ExpiresUtc > now)
            {
                if (!cached.IsReachable)
                {
                    throw new DeviceUnreachableException(normalized, cached.FailureReason ?? $"Device '{normalized}' appears offline or unreachable.");
                }
                return;
            }

            ct.ThrowIfCancellationRequested();
            var clampedTimeoutMs = Math.Max(300, Math.Min(10000, timeoutMs));

            // Stage 0: Fast DNS resolution (or IP parse)
            IPAddress? targetIp = null;
            if (!IPAddress.TryParse(normalized, out targetIp))
            {
                try
                {
                    var dnsTask = Dns.GetHostAddressesAsync(normalized);
                    var delayTask = Task.Delay(Math.Min(clampedTimeoutMs, 2000), ct);
                    var finished = await Task.WhenAny(dnsTask, delayTask).ConfigureAwait(false);
                    if (finished != dnsTask)
                    {
                        ct.ThrowIfCancellationRequested();
                        var msg = $"Device '{normalized}' is unreachable (DNS lookup timed out).";
                        CacheFailure(normalized, msg);
                        throw new DeviceUnreachableException(normalized, msg);
                    }

                    var addrs = await dnsTask.ConfigureAwait(false);
                    targetIp = addrs.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                               ?? addrs.FirstOrDefault();
                    if (targetIp == null)
                    {
                        var msg = $"Device '{normalized}' is unreachable (DNS returned no IP addresses).";
                        CacheFailure(normalized, msg);
                        throw new DeviceUnreachableException(normalized, msg);
                    }
                }
                catch (DeviceUnreachableException)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (SocketException sex)
                {
                    var msg = $"Device '{normalized}' is offline or not found in DNS ({sex.SocketErrorCode}).";
                    CacheFailure(normalized, msg);
                    throw new DeviceUnreachableException(normalized, msg);
                }
                catch (Exception ex)
                {
                    var msg = $"Device '{normalized}' DNS resolution failed: {ex.Message}";
                    CacheFailure(normalized, msg);
                    throw new DeviceUnreachableException(normalized, msg);
                }
            }

            // Stage 1: Fast ICMP Ping (~1-15ms on LAN when online)
            var pingTimeoutMs = Math.Max(300, Math.Min(clampedTimeoutMs, 1000));
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(targetIp!, pingTimeoutMs).ConfigureAwait(false);
                if (reply != null && reply.Status == IPStatus.Success)
                {
                    CacheSuccess(normalized);
                    return;
                }
            }
            catch
            {
                // ICMP may be disabled or blocked by policy; fall through to TCP probe
            }

            ct.ThrowIfCancellationRequested();

            // Stage 2: Fast parallel TCP probe on RPC (135) and SMB (445)
            // Handles corporate subnets where ICMP Echo is dropped by firewall.
            var tcpTimeoutMs = Math.Max(400, Math.Min(clampedTimeoutMs, 1000));
            var probe135 = ProbeTcpPortAliveAsync(targetIp!, 135, tcpTimeoutMs, ct);
            var probe445 = ProbeTcpPortAliveAsync(targetIp!, 445, tcpTimeoutMs, ct);
            var tcpResults = await Task.WhenAll(probe135, probe445).ConfigureAwait(false);

            if (tcpResults.Any(alive => alive))
            {
                CacheSuccess(normalized);
                return;
            }

            var offlineMsg = $"Device '{normalized}' appears offline or unreachable (no ICMP, RPC/135, or SMB/445 response within {clampedTimeoutMs}ms).";
            CacheFailure(normalized, offlineMsg);
            throw new DeviceUnreachableException(normalized, offlineMsg);
        }

        private static async Task<bool> ProbeTcpPortAliveAsync(IPAddress ip, int port, int timeoutMs, CancellationToken ct)
        {
            try
            {
                using var client = new TcpClient(ip.AddressFamily);
                var connectTask = client.ConnectAsync(ip, port);
                var delayTask = Task.Delay(timeoutMs, ct);
                var completed = await Task.WhenAny(connectTask, delayTask).ConfigureAwait(false);
                if (completed != connectTask)
                {
                    return false;
                }

                await connectTask.ConfigureAwait(false);
                return true;
            }
            catch (SocketException sex) when (sex.SocketErrorCode == SocketError.ConnectionRefused)
            {
                // An immediate TCP RST proves the host OS is powered on and reachable on the network.
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void CacheSuccess(string host)
        {
            Cache[host] = new CacheEntry
            {
                IsReachable = true,
                ExpiresUtc = DateTime.UtcNow.Add(ReachableCacheTtl)
            };
        }

        private static void CacheFailure(string host, string reason)
        {
            Cache[host] = new CacheEntry
            {
                IsReachable = false,
                FailureReason = reason,
                ExpiresUtc = DateTime.UtcNow.Add(UnreachableCacheTtl)
            };
        }
    }
}
