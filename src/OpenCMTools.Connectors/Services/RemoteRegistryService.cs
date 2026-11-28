using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using OpenCMTools.Connectors.Wmi;
using OpenCMTools.Core.Models;

namespace OpenCMTools.Connectors.Services
{
    public sealed class RemoteRegistryValueItem
    {
        public string Name { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string Data { get; set; } = string.Empty;
        public RegistryValueKind RawKind { get; set; }
    }

    public sealed class RemoteRegistryService
    {
        public bool EnableFastReachabilityCheck { get; set; } = true;
        public int ReachabilityTimeoutMs { get; set; } = 1500;

        public static readonly RegistryHive[] SupportedHives = new[]
        {
            RegistryHive.LocalMachine,
            RegistryHive.Users,
            RegistryHive.ClassesRoot,
            RegistryHive.CurrentConfig
        };

        public static string HiveDisplayName(RegistryHive hive) => hive switch
        {
            RegistryHive.LocalMachine => "HKEY_LOCAL_MACHINE",
            RegistryHive.Users => "HKEY_USERS",
            RegistryHive.ClassesRoot => "HKEY_CLASSES_ROOT",
            RegistryHive.CurrentConfig => "HKEY_CURRENT_CONFIG",
            _ => hive.ToString()
        };

        private Task EnsureDeviceReachableAsync(TargetDevice device, CancellationToken ct)
        {
            if (!EnableFastReachabilityCheck) return Task.CompletedTask;
            return DeviceReachabilityChecker.EnsureReachableAsync(device.ConnectAddress, ReachabilityTimeoutMs, ct);
        }

        public async Task<IReadOnlyList<string>> GetSubKeyNamesAsync(TargetDevice device, RegistryHive hive, string subKeyPath, CancellationToken ct = default)
        {
            await EnsureDeviceReachableAsync(device, ct).ConfigureAwait(false);
            return await Task.Run<IReadOnlyList<string>>(() =>
            {
                ct.ThrowIfCancellationRequested();
                using var root = RegistryKey.OpenRemoteBaseKey(hive, device.ConnectAddress, RegistryView.Registry64);
                if (string.IsNullOrEmpty(subKeyPath))
                {
                    return root.GetSubKeyNames().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
                }

                using var key = root.OpenSubKey(subKeyPath, writable: false);
                if (key == null) return Array.Empty<string>();
                return key.GetSubKeyNames().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            }, ct).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<RemoteRegistryValueItem>> GetValuesAsync(TargetDevice device, RegistryHive hive, string subKeyPath, CancellationToken ct = default)
        {
            await EnsureDeviceReachableAsync(device, ct).ConfigureAwait(false);
            return await Task.Run<IReadOnlyList<RemoteRegistryValueItem>>(() =>
            {
                ct.ThrowIfCancellationRequested();
                using var root = RegistryKey.OpenRemoteBaseKey(hive, device.ConnectAddress, RegistryView.Registry64);
                using var key = string.IsNullOrEmpty(subKeyPath) ? root : root.OpenSubKey(subKeyPath, writable: false);
                if (key == null) return Array.Empty<RemoteRegistryValueItem>();

                var results = new List<RemoteRegistryValueItem>();
                foreach (var valName in key.GetValueNames().OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                {
                    var kind = key.GetValueKind(valName);
                    var raw = key.GetValue(valName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    results.Add(new RemoteRegistryValueItem
                    {
                        Name = string.IsNullOrEmpty(valName) ? "(Default)" : valName,
                        Kind = kind.ToString(),
                        RawKind = kind,
                        Data = FormatValueData(raw, kind)
                    });
                }
                return results;
            }, ct).ConfigureAwait(false);
        }

        public async Task SetValueAsync(TargetDevice device, RegistryHive hive, string subKeyPath, string valueName, RegistryValueKind kind, string data, CancellationToken ct = default)
        {
            await EnsureDeviceReachableAsync(device, ct).ConfigureAwait(false);
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                using var root = RegistryKey.OpenRemoteBaseKey(hive, device.ConnectAddress, RegistryView.Registry64);
                using var key = root.OpenSubKey(subKeyPath, writable: true)
                    ?? throw new InvalidOperationException($"Cannot open '{subKeyPath}' for writing on {device.DisplayName}.");

                var actualName = valueName == "(Default)" ? string.Empty : valueName;
                object parsed = kind switch
                {
                    RegistryValueKind.DWord => Convert.ToInt32(data.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Convert.ToInt32(data.Substring(2), 16) : int.Parse(data)),
                    RegistryValueKind.QWord => Convert.ToInt64(data.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Convert.ToInt64(data.Substring(2), 16) : long.Parse(data)),
                    RegistryValueKind.MultiString => data.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None),
                    _ => data
                };
                key.SetValue(actualName, parsed, kind);
            }, ct).ConfigureAwait(false);
        }

        public async Task DeleteValueAsync(TargetDevice device, RegistryHive hive, string subKeyPath, string valueName, CancellationToken ct = default)
        {
            await EnsureDeviceReachableAsync(device, ct).ConfigureAwait(false);
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                using var root = RegistryKey.OpenRemoteBaseKey(hive, device.ConnectAddress, RegistryView.Registry64);
                using var key = root.OpenSubKey(subKeyPath, writable: true)
                    ?? throw new InvalidOperationException($"Cannot open '{subKeyPath}' for writing on {device.DisplayName}.");
                var actualName = valueName == "(Default)" ? string.Empty : valueName;
                key.DeleteValue(actualName, throwOnMissingValue: true);
            }, ct).ConfigureAwait(false);
        }

        private static string FormatValueData(object? raw, RegistryValueKind kind)
        {
            if (raw == null) return "(value not set)";
            if (raw is byte[] bytes) return BitConverter.ToString(bytes).Replace("-", " ");
            if (raw is string[] arr) return string.Join(" | ", arr);
            if (kind == RegistryValueKind.DWord && raw is int i) return $"0x{unchecked((uint)i):X8} ({unchecked((uint)i)})";
            if (kind == RegistryValueKind.QWord && raw is long l) return $"0x{unchecked((ulong)l):X16} ({unchecked((ulong)l)})";
            return raw.ToString() ?? string.Empty;
        }
    }
}
