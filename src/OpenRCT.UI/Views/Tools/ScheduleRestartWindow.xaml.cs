using System;
using System.Globalization;
using System.Windows;
using OpenRCT.Core.Models;
using OpenRCT.Core.Settings;
using OpenRCT.UI.Themes;

namespace OpenRCT.UI.Views.Tools
{
    public partial class ScheduleRestartWindow : Window
    {
        public SchedulePowerOptions Options { get; private set; } = new SchedulePowerOptions();

        public ScheduleRestartWindow(int targetCount, OpenRctSettings settings, bool defaultShutdown = false)
        {
            ThemeManager.ApplyTheme(this, settings.Theme);
            InitializeComponent();
            TargetSummaryText.Text = $"Configure power action for {targetCount} target device(s):";
            ScheduleDatePicker.SelectedDate = DateTime.Today;
            ScheduleTimeBox.Text = DateTime.Now.AddHours(1).ToString("HH:mm");

            if (defaultShutdown)
            {
                ShutdownRadio.IsChecked = true;
            }
            else
            {
                RestartRadio.IsChecked = true;
            }
            UpdateLabels();
        }

        private void ActionType_Changed(object sender, RoutedEventArgs e)
        {
            if (IsLoaded) UpdateLabels();
        }

        private void UpdateLabels()
        {
            var isShutdown = ShutdownRadio.IsChecked == true;
            Title = isShutdown ? "Shutdown System(s)" : "Restart System(s)";
            ExecuteButton.Content = isShutdown ? "Shutdown" : "Restart";

            if (MessageBoxInput != null &&
                (MessageBoxInput.Text.Contains("scheduled a system restart") || MessageBoxInput.Text.Contains("scheduled a system shutdown")))
            {
                MessageBoxInput.Text = isShutdown
                    ? "Your IT administrator has scheduled a system shutdown. Please save your work."
                    : "Your IT administrator has scheduled a system restart. Please save your work.";
            }
        }

        private void TimingMode_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            bool scheduled = ScheduledTimeRadio.IsChecked == true;
            DelayBox.IsEnabled = !scheduled;
            ScheduleDatePicker.IsEnabled = scheduled;
            ScheduleTimeBox.IsEnabled = scheduled;
        }

        private void NoticeCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (MessageBoxInput != null)
            {
                MessageBoxInput.IsEnabled = IncludeNoticeCheck.IsChecked == true;
            }
        }

        private void Preset0_Click(object sender, RoutedEventArgs e) { CountdownRadio.IsChecked = true; DelayBox.Text = "0"; }
        private void Preset60_Click(object sender, RoutedEventArgs e) { CountdownRadio.IsChecked = true; DelayBox.Text = "60"; }
        private void Preset300_Click(object sender, RoutedEventArgs e) { CountdownRadio.IsChecked = true; DelayBox.Text = "300"; }
        private void Preset900_Click(object sender, RoutedEventArgs e) { CountdownRadio.IsChecked = true; DelayBox.Text = "900"; }

        private void Schedule_Click(object sender, RoutedEventArgs e)
        {
            int delaySeconds;
            if (ScheduledTimeRadio.IsChecked == true)
            {
                var date = ScheduleDatePicker.SelectedDate ?? DateTime.Today;
                if (!TimeSpan.TryParseExact(ScheduleTimeBox.Text.Trim(), new[] { @"hh\:mm", @"h\:mm" }, CultureInfo.InvariantCulture, out var tod))
                {
                    MessageBox.Show(this, "Enter a valid 24-hour time (e.g. 22:00).", "OpenRCT", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var targetTime = date.Date + tod;
                if (targetTime <= DateTime.Now)
                {
                    MessageBox.Show(this, "Scheduled date & time must be in the future.", "OpenRCT", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                delaySeconds = (int)Math.Min(315360000, Math.Round((targetTime - DateTime.Now).TotalSeconds));
            }
            else
            {
                if (!int.TryParse(DelayBox.Text.Trim(), out delaySeconds) || delaySeconds < 0)
                {
                    MessageBox.Show(this, "Enter a valid non-negative countdown in seconds.", "OpenRCT", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            Options = new SchedulePowerOptions
            {
                IsShutdown = ShutdownRadio.IsChecked == true,
                DelaySeconds = delaySeconds,
                ForceCloseApps = ForceCheck.IsChecked == true,
                SkipIfUserLoggedOn = SkipLoggedOnCheck.IsChecked == true,
                Message = IncludeNoticeCheck.IsChecked == true ? (MessageBoxInput.Text ?? string.Empty) : string.Empty
            };
            DialogResult = true;
            Close();
        }
    }
}
