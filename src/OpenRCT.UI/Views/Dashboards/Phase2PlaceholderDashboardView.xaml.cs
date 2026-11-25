using System.Windows.Controls;
using OpenRCT.Core.Settings;
using OpenRCT.UI.Themes;

namespace OpenRCT.UI.Views.Dashboards
{
    public partial class Phase2PlaceholderDashboardView : UserControl
    {
        public Phase2PlaceholderDashboardView(string title, string description, OpenRctSettings settings)
        {
            ThemeManager.ApplyTheme(this, settings.Theme);
            InitializeComponent();
            TitleText.Text = title;
            DescriptionText.Text = description;
        }
    }
}
