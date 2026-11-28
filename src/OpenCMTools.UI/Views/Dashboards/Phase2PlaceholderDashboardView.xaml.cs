using System.Windows.Controls;
using OpenCMTools.Core.Settings;
using OpenCMTools.UI.Themes;

namespace OpenCMTools.UI.Views.Dashboards
{
    public partial class Phase2PlaceholderDashboardView : UserControl
    {
        public Phase2PlaceholderDashboardView(string title, string description, OpenCMToolsSettings settings)
        {
            ThemeManager.ApplyTheme(this, settings.Theme);
            InitializeComponent();
            TitleText.Text = title;
            DescriptionText.Text = description;
        }
    }
}
