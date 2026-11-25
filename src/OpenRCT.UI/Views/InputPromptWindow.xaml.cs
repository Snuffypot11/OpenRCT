using System.Windows;

namespace OpenRCT.UI.Views
{
    public partial class InputPromptWindow : Window
    {
        public string ResponseText => InputBox.Text;

        public InputPromptWindow(string title, string prompt, string defaultValue = "")
        {
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
