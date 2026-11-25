using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using OpenRCT.Core.Models;
using OpenRCT.Core.Settings;
using OpenRCT.UI;
using OpenRCT.UI.Themes;

namespace OpenRCT.App
{
    public partial class MainLauncherWindow : Window
    {
        private readonly SettingsService _settingsService = new SettingsService();

        public MainLauncherWindow()
        {
            var settings = _settingsService.Load();
            ThemeManager.ApplyTheme(this, settings.Theme);
            InitializeComponent();
        }

        private TargetDevice[] ParseTargets()
        {
            var raw = TargetsBox.Text ?? string.Empty;
            return raw.Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(name => new TargetDevice { Name = name })
                .ToArray();
        }

        private void RunTool_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn) || !(btn.Tag is string actionKey)) return;
            var targets = ParseTargets();
            if (targets.Length == 0)
            {
                MessageBox.Show(this, "Enter at least one target device hostname or FQDN.", "OpenRCT", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            ToolDispatcher.Dispatch(new ToolInvocationContext { ActionKey = actionKey, Devices = targets });
        }

        private void OpenDashboard_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn) || !(btn.Tag is string dashboardKey)) return;
            ToolDispatcher.Dispatch(new ToolInvocationContext { ActionKey = dashboardKey });
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            ToolDispatcher.Dispatch(new ToolInvocationContext { ActionKey = ToolActionKeys.OpenSettings });
            ThemeManager.ApplyTheme(this, _settingsService.Load().Theme);
        }

        private void ViewInstallLog_Click(object sender, RoutedEventArgs e)
        {
            var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OpenRCT", "install.log");
            if (!File.Exists(logPath))
            {
                MessageBox.Show(this, $"No install log was found at:\n{logPath}", "OpenRCT Diagnostics", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Process.Start(new ProcessStartInfo { FileName = "notepad.exe", Arguments = $"\"{logPath}\"", UseShellExecute = true });
        }

        private void RepairConsoleExtension_Click(object sender, RoutedEventArgs e)
        {
            var installDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            var scriptPath = Path.Combine(installDir, "scripts", "Install-OpenRCT.ps1");
            if (!File.Exists(scriptPath))
            {
                MessageBox.Show(this, $"Installer script not found at:\n{scriptPath}", "OpenRCT", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoExit -NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -InstallDir \"{installDir}\" -SkipBinaryCopy",
                    Verb = "runas",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not launch repair script: {ex.Message}", "OpenRCT", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
