using System.Windows;
using OpenRCT.Core.Settings;
using OpenRCT.UI.Themes;

namespace OpenRCT.UI.Views
{
    public partial class InputPromptWindow : Window
    {
        public string ResponseText => InputBox.Text;

        public InputPromptWindow(string title, string prompt, string defaultValue = "")
        {
            ThemeManager.ApplyTheme(this, new SettingsService().Load().Theme);
            InitializeComponent();
            Title = title;
            PromptLabel.Text = prompt;
            InputBox.Text = defaultValue;
            Loaded += (_, __) => { InputBox.Focus(); InputBox.SelectAll(); };
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
