using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace OpenRCT.Core.Settings
{
    public enum RemoteTransportPreference
    {
        WmiDcomFirst = 0,
        WinRmCimFirst = 1,
        WmiDcomOnly = 2,
        WinRmCimOnly = 3
    }

    public enum AppThemePreference
    {
        Light = 0,
        Dark = 1,
        System = 2
    }

    [DataContract]
    public sealed class OpenRctSettings
    {
        [DataMember(Order = 1)]
        public string? PsExecPath { get; set; }

        [DataMember(Order = 2)]
        public bool PsExecRunAsSystem { get; set; } = true;

        [DataMember(Order = 3)]
        public bool PsExecAcceptEula { get; set; } = true;

        [DataMember(Order = 4)]
        public string PsExecAdditionalArgs { get; set; } = string.Empty;

        [DataMember(Order = 5)]
        public RemoteTransportPreference TransportPreference { get; set; } = RemoteTransportPreference.WmiDcomFirst;

        [DataMember(Order = 6)]
        public int ConnectionTimeoutSeconds { get; set; } = 15;

        [DataMember(Order = 7)]
        public int MaxConcurrentDevices { get; set; } = 16;

        [DataMember(Order = 8)]
        public string? DefaultSmsProviderServer { get; set; }

        [DataMember(Order = 9)]
        public string? DefaultSiteCode { get; set; }

        [DataMember(Order = 10)]
        public AppThemePreference Theme { get; set; } = AppThemePreference.Light;

        [DataMember(Order = 11)]
        public bool ConfirmDestructiveActions { get; set; } = true;

        [DataMember(Order = 12)]
        public bool AutoClearClipboardSecrets { get; set; } = true;

        [DataMember(Order = 13)]
        public int ClipboardClearSeconds { get; set; } = 30;

        public void Normalize()
        {
            if (ConnectionTimeoutSeconds < 3) ConnectionTimeoutSeconds = 3;
            if (ConnectionTimeoutSeconds > 300) ConnectionTimeoutSeconds = 300;
            if (MaxConcurrentDevices < 1) MaxConcurrentDevices = 1;
            if (MaxConcurrentDevices > 64) MaxConcurrentDevices = 64;
            if (ClipboardClearSeconds < 5) ClipboardClearSeconds = 5;
            if (ClipboardClearSeconds > 300) ClipboardClearSeconds = 300;
            PsExecPath = string.IsNullOrWhiteSpace(PsExecPath) ? null : PsExecPath!.Trim();
            PsExecAdditionalArgs ??= string.Empty;
            DefaultSmsProviderServer = string.IsNullOrWhiteSpace(DefaultSmsProviderServer) ? null : DefaultSmsProviderServer!.Trim();
            DefaultSiteCode = string.IsNullOrWhiteSpace(DefaultSiteCode) ? null : DefaultSiteCode!.Trim().ToUpperInvariant();
        }
    }

    public sealed class PsExecValidationResult
    {
        public bool IsConfigured { get; set; }
        public bool IsValid { get; set; }
        public bool IsWarning { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public sealed class SettingsService
    {
        private readonly string _settingsFilePath;

        public SettingsService(string? settingsFilePath = null)
        {
            if (!string.IsNullOrWhiteSpace(settingsFilePath))
            {
                _settingsFilePath = settingsFilePath!;
            }
            else
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                _settingsFilePath = Path.Combine(appData, "OpenRCT", "settings.json");
            }
        }

        public string SettingsFilePath => _settingsFilePath;

        public OpenRctSettings Load()
        {
            try
            {
                if (!File.Exists(_settingsFilePath))
                {
                    var defaults = new OpenRctSettings();
                    defaults.Normalize();
                    return defaults;
                }

                using var stream = File.OpenRead(_settingsFilePath);
                var serializer = new DataContractJsonSerializer(typeof(OpenRctSettings));
                var loaded = serializer.ReadObject(stream) as OpenRctSettings ?? new OpenRctSettings();
                loaded.Normalize();
                return loaded;
            }
            catch
            {
                var defaults = new OpenRctSettings();
                defaults.Normalize();
                return defaults;
            }
        }

        public void Save(OpenRctSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            settings.Normalize();

            var directory = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory!);
            }

            var serializer = new DataContractJsonSerializer(typeof(OpenRctSettings));
            using var ms = new MemoryStream();
            using (var writer = JsonReaderWriterFactory.CreateJsonWriter(ms, Encoding.UTF8, ownsStream: false, indent: true, indentChars: "  "))
            {
                serializer.WriteObject(writer, settings);
                writer.Flush();
            }

            File.WriteAllBytes(_settingsFilePath, ms.ToArray());
        }

        public static PsExecValidationResult ValidatePsExecPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return new PsExecValidationResult
                {
                    IsConfigured = false,
                    IsValid = false,
                    Message = "PsExec path is not configured. Configure the path to PsExec.exe or PsExec64.exe in OpenRCT Settings to enable Interactive Command Prompt."
                };
            }

            var trimmed = path!.Trim().Trim('"');
            if (!File.Exists(trimmed))
            {
                return new PsExecValidationResult
                {
                    IsConfigured = true,
                    IsValid = false,
                    Message = $"The configured PsExec executable was not found at '{trimmed}'."
                };
            }

            var fileName = Path.GetFileName(trimmed);
            if (!string.Equals(fileName, "PsExec.exe", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(fileName, "PsExec64.exe", StringComparison.OrdinalIgnoreCase))
            {
                return new PsExecValidationResult
                {
                    IsConfigured = true,
                    IsValid = true,
                    IsWarning = true,
                    Message = $"File exists ('{fileName}'), but is not named PsExec.exe or PsExec64.exe. Verify this is Sysinternals PsExec."
                };
            }

            return new PsExecValidationResult
            {
                IsConfigured = true,
                IsValid = true,
                Message = $"PsExec found at '{trimmed}'."
            };
        }

        public static string BuildPsExecArguments(OpenRctSettings settings, string targetHost, string remoteCommand = "cmd.exe")
        {
            var sb = new StringBuilder();
            if (settings.PsExecAcceptEula)
            {
                sb.Append("-accepteula ");
            }
            sb.Append("\\\\").Append(targetHost.Trim()).Append(' ');
            if (settings.PsExecRunAsSystem)
            {
                sb.Append("-s ");
            }
            if (!string.IsNullOrWhiteSpace(settings.PsExecAdditionalArgs))
            {
                sb.Append(settings.PsExecAdditionalArgs.Trim()).Append(' ');
            }
            sb.Append(remoteCommand);
            return sb.ToString();
        }
    }
}
