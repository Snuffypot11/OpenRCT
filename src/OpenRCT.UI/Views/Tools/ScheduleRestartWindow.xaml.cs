using System.Windows;
using OpenRCT.Core.Models;
using OpenRCT.Core.Settings;
using OpenRCT.UI.Themes;

namespace OpenRCT.UI.Views.Tools
{
    public partial class ScheduleRestartWindow : Window
    {
        public SchedulePowerOptions Options { get; private set; } = new SchedulePowerOptions();

        public ScheduleRestartWindow(int targetCount, OpenRctSettings settings)
        {
            InitializeComponent();
            ThemeManager.ApplyTheme(this, settings.Theme);
            TargetSummaryText.Text = $"Schedule power action for {targetCount} target device(s):";
        }

        private void Schedule_Click(object sender, RoutedEventArgs e)
        {
            Options = new SchedulePowerOptions
            {
                IsShutdown = ShutdownRadio.IsChecked == true,
                DelaySeconds = int.TryParse(DelayBox.Text, out var s) && s >= 0 ? s : 60,
                ForceCloseApps = ForceCheck.IsChecked == true,
                SkipIfUserLoggedOn = SkipLoggedOnCheck.IsChecked == true,
                Message = MessageBoxInput.Text ?? string.Empty
            };
            DialogResult = true;
            Close();
        }
    }
}
