using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using Microsoft.Win32;
using OpenCMTools.Core.Settings;

namespace OpenCMTools.UI.Themes
{
    public enum ConsoleIconSyncStatus
    {
        Updated,
        AlreadyUpToDate,
        ConsoleNotFound,
        SourceDllNotFound,
        CancelledByUser,
        Failed
    }

    public sealed class ConsoleIconSyncResult
    {
        public ConsoleIconSyncStatus Status { get; set; }
        public string Message { get; set; } = string.Empty;
        public bool RequiresConsoleRestart { get; set; }
    }

    /// <summary>
    /// Synchronizes the selected OpenCMTools Icon Style (Fluent Duotone vs Crisp Monochrome Outline)
    /// with the MECM Admin Console's native resource assembly (&lt;AdminConsole&gt;\bin\OpenCMTools.Icons.dll).
    /// </summary>
    public static class ConsoleIconSyncService
    {
        public static string GetStyleToken(IconStylePreference style) =>
            style == IconStylePreference.CrispOutline ? "Outline" : "Duotone";

        public static string GetSourceDllFileName(IconStylePreference style) =>
            style == IconStylePreference.CrispOutline
                ? "OpenCMTools.Icons.Outline.dll"
                : "OpenCMTools.Icons.Duotone.dll";

        public static bool IsConsoleInstalledAndOutOfSync(IconStylePreference style)
        {
            try
            {
                var consoleRoot = FindMecmConsoleRoot();
                if (string.IsNullOrWhiteSpace(consoleRoot)) return false;

                var installDir = FindOpenCMToolsInstallDir(consoleRoot);
                if (string.IsNullOrWhiteSpace(installDir)) return false;

                var sourceDll = ResolveSourceIconDll(installDir!, style);
                if (string.IsNullOrWhiteSpace(sourceDll) || !File.Exists(sourceDll)) return false;

                var destDll = Path.Combine(consoleRoot!, "bin", "OpenCMTools.Icons.dll");
                if (!File.Exists(destDll)) return true;

                return !FilesHaveSameSha256(sourceDll!, destDll);
            }
            catch
            {
                return false;
            }
        }

        public static ConsoleIconSyncResult ApplyConsoleIconStyle(
            IconStylePreference style,
            Window? owner = null,
            bool allowElevationPrompt = true)
        {
            var consoleRoot = FindMecmConsoleRoot();
            if (string.IsNullOrWhiteSpace(consoleRoot))
            {
                return new ConsoleIconSyncResult
                {
                    Status = ConsoleIconSyncStatus.ConsoleNotFound,
                    Message = "MECM Admin Console installation was not detected on this computer. WPF windows have been updated."
                };
            }

            var installDir = FindOpenCMToolsInstallDir(consoleRoot);
            if (string.IsNullOrWhiteSpace(installDir))
            {
                return new ConsoleIconSyncResult
                {
                    Status = ConsoleIconSyncStatus.SourceDllNotFound,
                    Message = "Could not locate the OpenCMTools installation folder containing the icon assemblies."
                };
            }

            var sourceDll = ResolveSourceIconDll(installDir!, style);
            if (string.IsNullOrWhiteSpace(sourceDll) || !File.Exists(sourceDll))
            {
                return new ConsoleIconSyncResult
                {
                    Status = ConsoleIconSyncStatus.SourceDllNotFound,
                    Message = $"Icon assembly '{GetSourceDllFileName(style)}' was not found in '{installDir}'. Re-run the OpenCMTools installer to enable console icon switching."
                };
            }

            var consoleBin = Path.Combine(consoleRoot!, "bin");
            var destDll = Path.Combine(consoleBin, "OpenCMTools.Icons.dll");

            if (File.Exists(destDll) && FilesHaveSameSha256(sourceDll!, destDll))
            {
                return new ConsoleIconSyncResult
                {
                    Status = ConsoleIconSyncStatus.AlreadyUpToDate,
                    Message = $"MECM Admin Console icons are already set to {FormatStyleName(style)}."
                };
            }

            // Attempt 1: Direct safe overwrite (succeeds when elevated or when AdminConsole\bin is writable)
            var directOutcome = TryDirectSafeReplace(sourceDll!, destDll, out var directError);
            if (directOutcome == DirectReplaceOutcome.Succeeded)
            {
                return new ConsoleIconSyncResult
                {
                    Status = ConsoleIconSyncStatus.Updated,
                    RequiresConsoleRestart = true,
                    Message = $"MECM Admin Console icons updated to {FormatStyleName(style)}. Restart the MECM Console to see the new tree and right-click menu icons."
                };
            }

            if (directOutcome == DirectReplaceOutcome.Unauthorized)
            {
                if (!allowElevationPrompt)
                {
                    return new ConsoleIconSyncResult
                    {
                        Status = ConsoleIconSyncStatus.Failed,
                        Message = "Administrator permission is required to update OpenCMTools.Icons.dll in the MECM Admin Console folder."
                    };
                }

                var scriptPath = ResolveInstallScriptPath(installDir!);
                if (string.IsNullOrWhiteSpace(scriptPath) || !File.Exists(scriptPath))
                {
                    return new ConsoleIconSyncResult
                    {
                        Status = ConsoleIconSyncStatus.Failed,
                        Message = $"Administrator permission is required to update '{destDll}', and Install-OpenCMTools.ps1 was not found under '{installDir}'."
                    };
                }

                var promptText =
                    $"Updating the MECM Admin Console icons to {FormatStyleName(style)} requires administrator permission to update OpenCMTools.Icons.dll in:\n\n" +
                    $"{consoleBin}\n\n" +
                    "Continue and approve the Windows UAC prompt now?";

                var choice = owner != null
                    ? MessageBox.Show(owner, promptText, "Apply MECM Console Icon Style", MessageBoxButton.YesNo, MessageBoxImage.Question)
                    : MessageBox.Show(promptText, "Apply MECM Console Icon Style", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (choice != MessageBoxResult.Yes)
                {
                    return new ConsoleIconSyncResult
                    {
                        Status = ConsoleIconSyncStatus.CancelledByUser,
                        Message = "MECM Console icon update was skipped. OpenCMTools windows were updated; you can click 'Apply to MECM Console' in Settings anytime."
                    };
                }

                return RunElevatedIconSync(scriptPath!, installDir!, consoleRoot!, sourceDll!, destDll, style);
            }

            return new ConsoleIconSyncResult
            {
                Status = ConsoleIconSyncStatus.Failed,
                Message = $"Could not update '{destDll}': {directError}"
            };
        }

        private enum DirectReplaceOutcome
        {
            Succeeded,
            Unauthorized,
            Failed
        }

        private static DirectReplaceOutcome TryDirectSafeReplace(string sourceDll, string destDll, out string error)
        {
            error = string.Empty;
            var destDir = Path.GetDirectoryName(destDll);
            var destLeaf = Path.GetFileName(destDll);

            try
            {
                if (!string.IsNullOrWhiteSpace(destDir) && Directory.Exists(destDir))
                {
                    foreach (var stale in Directory.GetFiles(destDir!, destLeaf + ".old.*"))
                    {
                        try { File.Delete(stale); } catch { }
                    }
                }
            }
            catch { }

            try
            {
                File.Copy(sourceDll, destDll, overwrite: true);
                return DirectReplaceOutcome.Succeeded;
            }
            catch (UnauthorizedAccessException ex)
            {
                error = ex.Message;
                return DirectReplaceOutcome.Unauthorized;
            }
            catch (IOException firstIoEx)
            {
                // When MECM Console is currently running, OpenCMTools.Icons.dll is locked in-place.
                // NTFS permits renaming a loaded module so the updated DLL can be copied immediately.
                try
                {
                    if (File.Exists(destDll))
                    {
                        var rotated = $"{destDll}.old.{DateTime.UtcNow.Ticks}";
                        File.Move(destDll, rotated);
                    }

                    File.Copy(sourceDll, destDll, overwrite: true);
                    return DirectReplaceOutcome.Succeeded;
                }
                catch (UnauthorizedAccessException uaEx)
                {
                    error = uaEx.Message;
                    return DirectReplaceOutcome.Unauthorized;
                }
                catch (Exception ex)
                {
                    error = $"{firstIoEx.Message} / {ex.Message}";
                    return DirectReplaceOutcome.Failed;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return DirectReplaceOutcome.Failed;
            }
        }

        private static ConsoleIconSyncResult RunElevatedIconSync(
            string scriptPath,
            string installDir,
            string consoleRoot,
            string sourceDll,
            string destDll,
            IconStylePreference style)
        {
            try
            {
                var styleToken = GetStyleToken(style);
                var safeInstallDir = installDir.TrimEnd('\\') + "\\.";
                var safeConsoleRoot = consoleRoot.TrimEnd('\\') + "\\.";
                var args =
                    $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" " +
                    $"-InstallDir \"{safeInstallDir}\" " +
                    $"-ConsoleRoot \"{safeConsoleRoot}\" " +
                    $"-ApplyIconStyleOnly -IconStyle {styleToken}";

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = args,
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    return new ConsoleIconSyncResult
                    {
                        Status = ConsoleIconSyncStatus.Failed,
                        Message = "Failed to launch elevated PowerShell helper to update MECM Console icons."
                    };
                }

                proc.WaitForExit(30000);
                if (!proc.HasExited)
                {
                    return new ConsoleIconSyncResult
                    {
                        Status = ConsoleIconSyncStatus.Failed,
                        Message = "Timed out waiting for elevated icon update helper. Check C:\\ProgramData\\OpenCMTools\\install.log."
                    };
                }

                if (proc.ExitCode == 0 && File.Exists(destDll) && FilesHaveSameSha256(sourceDll, destDll))
                {
                    return new ConsoleIconSyncResult
                    {
                        Status = ConsoleIconSyncStatus.Updated,
                        RequiresConsoleRestart = true,
                        Message = $"MECM Admin Console icons updated to {FormatStyleName(style)}. Restart the MECM Console to see the new tree and right-click menu icons."
                    };
                }

                return new ConsoleIconSyncResult
                {
                    Status = ConsoleIconSyncStatus.Failed,
                    Message = $"Elevated icon update did not complete (exit code {proc.ExitCode}). Check C:\\ProgramData\\OpenCMTools\\install.log for details."
                };
            }
            catch (Win32Exception w32) when (w32.NativeErrorCode == 1223)
            {
                return new ConsoleIconSyncResult
                {
                    Status = ConsoleIconSyncStatus.CancelledByUser,
                    Message = "UAC prompt was cancelled. OpenCMTools windows were updated; click 'Apply to MECM Console' in Settings anytime to update the console icons."
                };
            }
            catch (Exception ex)
            {
                return new ConsoleIconSyncResult
                {
                    Status = ConsoleIconSyncStatus.Failed,
                    Message = $"Failed to run elevated console icon update: {ex.Message}"
                };
            }
        }

        public static string? FindOpenCMToolsInstallDir(string? consoleRoot = null)
        {
            var candidates = new List<string>();

            try
            {
                var asmLoc = typeof(ConsoleIconSyncService).Assembly.Location;
                if (!string.IsNullOrWhiteSpace(asmLoc))
                {
                    var dir = Path.GetDirectoryName(asmLoc);
                    if (!string.IsNullOrWhiteSpace(dir)) candidates.Add(dir!);
                }
            }
            catch { }

            try
            {
                if (!string.IsNullOrWhiteSpace(AppDomain.CurrentDomain.BaseDirectory))
                {
                    candidates.Add(AppDomain.CurrentDomain.BaseDirectory);
                }
            }
            catch { }

            if (!string.IsNullOrWhiteSpace(consoleRoot))
            {
                try
                {
                    var marker = Path.Combine(consoleRoot!, "bin", "OpenCMTools.installdir.txt");
                    if (File.Exists(marker))
                    {
                        var text = File.ReadAllText(marker).Trim();
                        if (!string.IsNullOrWhiteSpace(text)) candidates.Add(text);
                    }
                }
                catch { }
            }

            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                    using var sub = baseKey.OpenSubKey(@"SOFTWARE\OpenCMTools");
                    var val = sub?.GetValue("InstallDir") as string;
                    if (!string.IsNullOrWhiteSpace(val)) candidates.Add(val!.Trim());
                }
                catch { }
            }

            try
            {
                var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                if (!string.IsNullOrWhiteSpace(pf)) candidates.Add(Path.Combine(pf, "OpenCMTools"));
            }
            catch { }

            foreach (var cand in candidates)
            {
                var trimmed = cand.Trim().Trim('"').TrimEnd('\\');
                if (string.IsNullOrWhiteSpace(trimmed) || !Directory.Exists(trimmed)) continue;

                if (File.Exists(Path.Combine(trimmed, "OpenCMTools.Icons.Duotone.dll")) ||
                    File.Exists(Path.Combine(trimmed, "OpenCMTools.Icons.Outline.dll")) ||
                    File.Exists(Path.Combine(trimmed, "bin", "OpenCMTools.Icons.Duotone.dll")) ||
                    File.Exists(Path.Combine(trimmed, "bin", "OpenCMTools.Icons.Outline.dll")))
                {
                    return trimmed;
                }
            }

            foreach (var cand in candidates)
            {
                var trimmed = cand.Trim().Trim('"').TrimEnd('\\');
                if (!string.IsNullOrWhiteSpace(trimmed) && Directory.Exists(trimmed))
                {
                    return trimmed;
                }
            }

            return null;
        }

        private static string? ResolveSourceIconDll(string installDir, IconStylePreference style)
        {
            var fileName = GetSourceDllFileName(style);
            var direct = Path.Combine(installDir, fileName);
            if (File.Exists(direct)) return direct;

            var nestedBin = Path.Combine(installDir, "bin", fileName);
            if (File.Exists(nestedBin)) return nestedBin;

            var parentDir = Path.GetDirectoryName(installDir);
            if (!string.IsNullOrWhiteSpace(parentDir))
            {
                var parentBin = Path.Combine(parentDir!, "bin", fileName);
                if (File.Exists(parentBin)) return parentBin;
            }

            return null;
        }

        private static string? ResolveInstallScriptPath(string installDir)
        {
            var direct = Path.Combine(installDir, "scripts", "Install-OpenCMTools.ps1");
            if (File.Exists(direct)) return direct;

            var parentDir = Path.GetDirectoryName(installDir);
            if (!string.IsNullOrWhiteSpace(parentDir))
            {
                var parentScript = Path.Combine(parentDir!, "scripts", "Install-OpenCMTools.ps1");
                if (File.Exists(parentScript)) return parentScript;
            }

            return null;
        }

        public static string? FindMecmConsoleRoot()
        {
            foreach (var scope in new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.Machine, EnvironmentVariableTarget.User })
            {
                try
                {
                    var envVal = Environment.GetEnvironmentVariable("SMS_ADMIN_UI_PATH", scope);
                    var resolved = ResolveConsoleRootFromCandidate(envVal);
                    if (resolved != null) return resolved;
                }
                catch { }
            }

            var regPaths = new[]
            {
                @"SOFTWARE\WOW6432Node\Microsoft\ConfigMgr10\Setup",
                @"SOFTWARE\Microsoft\ConfigMgr10\Setup",
                @"SOFTWARE\WOW6432Node\Microsoft\SMS\Setup",
                @"SOFTWARE\Microsoft\SMS\Setup"
            };

            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                foreach (var regPath in regPaths)
                {
                    try
                    {
                        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                        using var sub = baseKey.OpenSubKey(regPath);
                        if (sub == null) continue;

                        foreach (var valueName in new[] { "UI Installation Directory", "Installation Directory" })
                        {
                            var val = sub.GetValue(valueName) as string;
                            var resolved = ResolveConsoleRootFromCandidate(val);
                            if (resolved != null) return resolved;

                            if (!string.IsNullOrWhiteSpace(val))
                            {
                                var adminSub = ResolveConsoleRootFromCandidate(Path.Combine(val!.Trim(), "AdminConsole"));
                                if (adminSub != null) return adminSub;
                            }
                        }
                    }
                    catch { }
                }
            }

            var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var commonPaths = new List<string>();
            if (!string.IsNullOrWhiteSpace(pf86))
            {
                commonPaths.Add(Path.Combine(pf86, "Microsoft Endpoint Manager", "AdminConsole"));
                commonPaths.Add(Path.Combine(pf86, "Microsoft Configuration Manager", "AdminConsole"));
            }
            if (!string.IsNullOrWhiteSpace(pf))
            {
                commonPaths.Add(Path.Combine(pf, "Microsoft Endpoint Manager", "AdminConsole"));
                commonPaths.Add(Path.Combine(pf, "Microsoft Configuration Manager", "AdminConsole"));
            }
            commonPaths.Add(@"D:\Program Files\Microsoft Configuration Manager\AdminConsole");
            commonPaths.Add(@"D:\Program Files (x86)\Microsoft Configuration Manager\AdminConsole");
            commonPaths.Add(@"E:\Program Files\Microsoft Configuration Manager\AdminConsole");

            foreach (var path in commonPaths)
            {
                var resolved = ResolveConsoleRootFromCandidate(path);
                if (resolved != null) return resolved;
            }

            return null;
        }

        private static string? ResolveConsoleRootFromCandidate(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return null;
            try
            {
                var current = candidate!.Trim().Trim('"').TrimEnd('\\');
                for (var i = 0; i < 5; i++)
                {
                    if (string.IsNullOrWhiteSpace(current)) break;
                    if (Directory.Exists(Path.Combine(current, "XmlStorage")) &&
                        Directory.Exists(Path.Combine(current, "bin")))
                    {
                        return current;
                    }

                    var parent = Path.GetDirectoryName(current);
                    if (string.IsNullOrWhiteSpace(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                    current = parent!;
                }
            }
            catch { }

            return null;
        }

        private static bool FilesHaveSameSha256(string pathA, string pathB)
        {
            try
            {
                var infoA = new FileInfo(pathA);
                var infoB = new FileInfo(pathB);
                if (!infoA.Exists || !infoB.Exists) return false;
                if (infoA.Length != infoB.Length) return false;

                using var sha = SHA256.Create();
                byte[] hashA;
                using (var fsA = File.OpenRead(pathA)) hashA = sha.ComputeHash(fsA);
                byte[] hashB;
                using (var fsB = File.OpenRead(pathB)) hashB = sha.ComputeHash(fsB);

                if (hashA.Length != hashB.Length) return false;
                for (var i = 0; i < hashA.Length; i++)
                {
                    if (hashA[i] != hashB[i]) return false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string FormatStyleName(IconStylePreference style) =>
            style == IconStylePreference.CrispOutline ? "Crisp Monochrome Outline" : "Fluent Duotone";
    }
}
