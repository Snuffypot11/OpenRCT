using System;
using System.IO;
using OpenCMTools.Core.Settings;
using Xunit;

namespace OpenCMTools.Tests
{
    public sealed class SettingsServiceTests
    {
        [Fact]
        public void SaveAndLoad_RoundTripsAllSettingsAndNormalizesBounds()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "OpenCMTools_Tests_" + Guid.NewGuid().ToString("N"));
            var filePath = Path.Combine(tempDir, "settings.json");
            try
            {
                var svc = new SettingsService(filePath);
                var settings = new OpenCMToolsSettings
                {
                    PsExecPath = @"C:\Tools\PsExec64.exe",
                    PsExecRunAsSystem = false,
                    PsExecAcceptEula = true,
                    ConnectionTimeoutSeconds = 999, // clamped to 300
                    MaxConcurrentDevices = 0,       // clamped to 1
                    DefaultSmsProviderServer = "cm01.contoso.local",
                    DefaultSiteCode = "lab",
                    Theme = AppThemePreference.Dark
                };

                svc.Save(settings);
                var loaded = svc.Load();

                Assert.Equal(@"C:\Tools\PsExec64.exe", loaded.PsExecPath);
                Assert.False(loaded.PsExecRunAsSystem);
                Assert.True(loaded.PsExecAcceptEula);
                Assert.Equal(300, loaded.ConnectionTimeoutSeconds);
                Assert.Equal(1, loaded.MaxConcurrentDevices);
                Assert.Equal("cm01.contoso.local", loaded.DefaultSmsProviderServer);
                Assert.Equal("LAB", loaded.DefaultSiteCode);
                Assert.Equal(AppThemePreference.Dark, loaded.Theme);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void ValidatePsExecPath_HandlesUnconfiguredMissingValidAndWarning()
        {
            var empty = SettingsService.ValidatePsExecPath(null);
            Assert.False(empty.IsConfigured);
            Assert.False(empty.IsValid);

            var missing = SettingsService.ValidatePsExecPath(Path.Combine(Path.GetTempPath(), "does-not-exist-psexec.exe"));
            Assert.True(missing.IsConfigured);
            Assert.False(missing.IsValid);

            var tempDir = Path.Combine(Path.GetTempPath(), "OpenCMTools_PsExec_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var validExe = Path.Combine(tempDir, "PsExec64.exe");
                File.WriteAllText(validExe, "stub");
                var validRes = SettingsService.ValidatePsExecPath(validExe);
                Assert.True(validRes.IsConfigured);
                Assert.True(validRes.IsValid);
                Assert.False(validRes.IsWarning);

                var customExe = Path.Combine(tempDir, "othertool.exe");
                File.WriteAllText(customExe, "stub");
                var warnRes = SettingsService.ValidatePsExecPath(customExe);
                Assert.True(warnRes.IsConfigured);
                Assert.True(warnRes.IsValid);
                Assert.True(warnRes.IsWarning);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void BuildPsExecArguments_IncludesExpectedFlags()
        {
            var settings = new OpenCMToolsSettings
            {
                PsExecAcceptEula = true,
                PsExecRunAsSystem = true,
                PsExecAdditionalArgs = "-nobanner"
            };

            var args = SettingsService.BuildPsExecArguments(settings, "WKSTN01.contoso.com");
            Assert.Equal(@"-accepteula \\WKSTN01.contoso.com -s -nobanner cmd.exe", args);
        }

        [Fact]
        public void Load_MigratesLegacySettingsWhenTargetDoesNotExist()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "OpenCMTools_Migrate_" + Guid.NewGuid().ToString("N"));
            var legacyPath = Path.Combine(tempDir, "OpenRCT", "settings.json");
            var newPath = Path.Combine(tempDir, "OpenCMTools", "settings.json");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
                File.WriteAllText(legacyPath, "{\n  \"PsExecPath\": \"C:\\\\Tools\\\\PsExec64.exe\",\n  \"Theme\": 1,\n  \"IconStyle\": 1\n}");

                var svc = new SettingsService(newPath, legacyPath);
                var loaded = svc.Load();

                Assert.Equal(@"C:\Tools\PsExec64.exe", loaded.PsExecPath);
                Assert.Equal(AppThemePreference.Dark, loaded.Theme);
                Assert.Equal(IconStylePreference.CrispOutline, loaded.IconStyle);
                Assert.True(File.Exists(newPath), "Migrated settings should be persisted to the new OpenCMTools settings path.");
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }
    }
}
