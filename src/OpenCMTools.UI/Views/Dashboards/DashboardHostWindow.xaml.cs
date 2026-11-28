using System.Windows;
using OpenCMTools.Core.Settings;
using OpenCMTools.UI.Themes;

namespace OpenCMTools.UI.Views.Dashboards
{
    public partial class DashboardHostWindow : Window
    {
        public DashboardHostWindow(string title, UIElement content, OpenCMToolsSettings settings)
        {
            ThemeManager.ApplyTheme(this, settings.Theme);
            InitializeComponent();
            Title = $"OpenCMTools - {title}";
            HostContent.Content = content;
        }
    }
}
