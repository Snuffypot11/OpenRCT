using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace OpenCMTools.Core.Settings
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

    public enum IconStylePreference
    {
        FluentDuotone = 0,
        CrispOutline = 1
    }

    [DataContract]
    public sealed class OpenCMToolsSettings
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

        [DataMember(Order = 14)]
        public string? UpdateRepository { get; set; }

        [DataMember(Order = 15)]
        public bool EnableUpdateChecks { get; set; } = true;

        [DataMember(Order = 16)]
        public IconStylePreference IconStyle { get; set; } = IconStylePreference.FluentDuotone;

        [DataMember(Order = 17)]
        public bool EnableFastOfflineCheck { get; set; } = true;

        [DataMember(Order = 18)]
        public int OfflineCheckTimeoutMs { get; set; } = 1500;

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            // DataContractJsonSerializer uses FormatterServices.GetUninitializedObject (skipping
            // C# constructors and property initializers). Initialize defaults here so newly added
            // settings (like EnableFastOfflineCheck) keep their true defaults when loading older
            // settings.json files.
            PsExecRunAsSystem = true;
            PsExecAcceptEula = true;
            PsExecAdditionalArgs = string.Empty;
            TransportPreference = RemoteTransportPreference.WmiDcomFirst;
            ConnectionTimeoutSeconds = 15;
            MaxConcurrentDevices = 16;
            Theme = AppThemePreference.Light;
            ConfirmDestructiveActions = true;
            AutoClearClipboardSecrets = true;
            ClipboardClearSeconds = 30;
            EnableUpdateChecks = true;
            IconStyle = IconStylePreference.FluentDuotone;
            EnableFastOfflineCheck = true;
            OfflineCheckTimeoutMs = 1500;
        }

        public void Normalize()
        {
            if (ConnectionTimeoutSeconds < 3) ConnectionTimeoutSeconds = 3;
            if (ConnectionTimeoutSeconds > 300) ConnectionTimeoutSeconds = 300;
            if (MaxConcurrentDevices < 1) MaxConcurrentDevices = 1;
            if (MaxConcurrentDevices > 64) MaxConcurrentDevices = 64;
            if (ClipboardClearSeconds < 5) ClipboardClearSeconds = 5;
            if (ClipboardClearSeconds > 300) ClipboardClearSeconds = 300;
            if (OfflineCheckTimeoutMs < 300) OfflineCheckTimeoutMs = 300;
            if (OfflineCheckTimeoutMs > 10000) OfflineCheckTimeoutMs = 10000;
            PsExecPath = string.IsNullOrWhiteSpace(PsExecPath) ? null : PsExecPath!.Trim();
            PsExecAdditionalArgs ??= string.Empty;
            DefaultSmsProviderServer = string.IsNullOrWhiteSpace(DefaultSmsProviderServer) ? null : DefaultSmsProviderServer!.Trim();
            DefaultSiteCode = string.IsNullOrWhiteSpace(DefaultSiteCode) ? null : DefaultSiteCode!.Trim().ToUpperInvariant();
            UpdateRepository = string.IsNullOrWhiteSpace(UpdateRepository) ? null : UpdateRepository!.Trim();
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
        private readonly string? _legacySettingsFilePath;

        public SettingsService(string? settingsFilePath = null, string? legacySettingsFilePath = null)
        {
            if (!string.IsNullOrWhiteSpace(settingsFilePath))
            {
                _settingsFilePath = settingsFilePath!;
                _legacySettingsFilePath = string.IsNullOrWhiteSpace(legacySettingsFilePath) ? null : legacySettingsFilePath;
            }
            else
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                _settingsFilePath = Path.Combine(appData, "OpenCMTools", "settings.json");
                _legacySettingsFilePath = string.IsNullOrWhiteSpace(legacySettingsFilePath)
                    ? Path.Combine(appData, "OpenRCT", "settings.json")
                    : legacySettingsFilePath;
            }
        }

        public string SettingsFilePath => _settingsFilePath;

        public OpenCMToolsSettings Load()
        {
            try
            {
                if (!File.Exists(_settingsFilePath))
                {
                    if (!string.IsNullOrWhiteSpace(_legacySettingsFilePath) && File.Exists(_legacySettingsFilePath))
                    {
                        try
                        {
                            using var legacyStream = File.OpenRead(_legacySettingsFilePath!);
                            var legacySerializer = new DataContractJsonSerializer(typeof(OpenCMToolsSettings));
                            var migrated = legacySerializer.ReadObject(legacyStream) as OpenCMToolsSettings ?? new OpenCMToolsSettings();
                            migrated.Normalize();
                            Save(migrated);
                            return migrated;
                        }
                        catch
                        {
                        }
                    }

                    var defaults = new OpenCMToolsSettings();
                    defaults.Normalize();
                    return defaults;
                }

                using var stream = File.OpenRead(_settingsFilePath);
                var serializer = new DataContractJsonSerializer(typeof(OpenCMToolsSettings));
                var loaded = serializer.ReadObject(stream) as OpenCMToolsSettings ?? new OpenCMToolsSettings();
                loaded.Normalize();
                return loaded;
            }
            catch
            {
                var defaults = new OpenCMToolsSettings();
                defaults.Normalize();
                return defaults;
            }
        }

        public void Save(OpenCMToolsSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            settings.Normalize();

            var directory = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory!);
            }

            var serializer = new DataContractJsonSerializer(typeof(OpenCMToolsSettings));
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
                    Message = "PsExec path is not configured. Configure the path to PsExec.exe or PsExec64.exe in OpenCMTools Settings to enable Interactive Command Prompt."
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

        public static string BuildPsExecArguments(OpenCMToolsSettings settings, string targetHost, string remoteCommand = "cmd.exe")
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
