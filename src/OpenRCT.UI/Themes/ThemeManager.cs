using System;
using System.Windows;
using Microsoft.Win32;
using OpenRCT.Core.Settings;

namespace OpenRCT.UI.Themes
{
    public static class ThemeManager
    {
        public static void ApplyTheme(FrameworkElement element, AppThemePreference preference)
        {
            if (element == null) return;
            var useDark = preference == AppThemePreference.Dark ||
                          (preference == AppThemePreference.System && IsWindowsDarkModeEnabled());

            var uri = new Uri(
                useDark
                    ? "pack://application:,,,/OpenRCT.UI;component/Themes/DarkTheme.xaml"
                    : "pack://application:,,,/OpenRCT.UI;component/Themes/LightTheme.xaml",
                UriKind.Absolute);

            var dict = new ResourceDictionary { Source = uri };
            element.Resources.MergedDictionaries.Clear();
            element.Resources.MergedDictionaries.Add(dict);
        }

        private static bool IsWindowsDarkModeEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var val = key?.GetValue("AppsUseLightTheme");
                return val != null && Convert.ToInt32(val) == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
