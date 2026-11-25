using System.Windows;
using OpenRCT.Core.Settings;
using OpenRCT.UI.Themes;

namespace OpenRCT.UI.Views.Dashboards
{
    public partial class DashboardHostWindow : Window
    {
        public DashboardHostWindow(string title, UIElement content, OpenRctSettings settings)
        {
            InitializeComponent();
            ThemeManager.ApplyTheme(this, settings.Theme);
            Title = $"OpenRCT — {title}";
            HostContent.Content = content;
        }
    }
}
