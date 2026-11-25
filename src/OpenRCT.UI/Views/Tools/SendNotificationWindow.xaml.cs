using System.Windows;
using OpenRCT.Core.Settings;
using OpenRCT.UI.Themes;

namespace OpenRCT.UI.Views.Tools
{
    public partial class SendNotificationWindow : Window
    {
        public string NotificationMessage => MessageBoxInput.Text;
        public int DisplaySeconds => int.TryParse(DurationBox.Text, out var s) && s > 0 ? s : 300;

        public SendNotificationWindow(int targetCount, OpenRctSettings settings)
        {
            InitializeComponent();
            ThemeManager.ApplyTheme(this, settings.Theme);
            TargetSummaryText.Text = $"Send interactive notification to {targetCount} target device(s):";
        }

        private void Send_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(MessageBoxInput.Text))
            {
                MessageBox.Show(this, "Enter a message before sending.", "OpenRCT", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
            Close();
        }
    }
}
