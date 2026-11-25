using System;
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
            InitializeComponent();
            var settings = _settingsService.Load();
            ThemeManager.ApplyTheme(this, settings.Theme);
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
    }
}
