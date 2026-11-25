using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Win32;
using OpenRCT.Core.Settings;

namespace OpenRCT.UI.Themes
{
    public static class ThemeManager
    {
        public static void ApplyTheme(FrameworkElement element, AppThemePreference preference)
        {
            if (element == null) return;
            var dict = LoadThemeDictionary(preference);
            element.Resources.MergedDictionaries.Clear();
            element.Resources.MergedDictionaries.Add(dict);

            if (dict["PrimaryTextBrush"] is Brush fg)
            {
                TextElement.SetForeground(element, fg);
            }
        }

        public static void ApplyApplicationTheme(AppThemePreference preference)
        {
            if (Application.Current == null) return;
            var dict = LoadThemeDictionary(preference);
            Application.Current.Resources.MergedDictionaries.Clear();
            Application.Current.Resources.MergedDictionaries.Add(dict);
        }

        private static ResourceDictionary LoadThemeDictionary(AppThemePreference preference)
        {
            var useDark = preference == AppThemePreference.Dark ||
                          (preference == AppThemePreference.System && IsWindowsDarkModeEnabled());

            var uri = new Uri(
                useDark
                    ? "pack://application:,,,/OpenRCT.UI;component/Themes/DarkTheme.xaml"
                    : "pack://application:,,,/OpenRCT.UI;component/Themes/LightTheme.xaml",
                UriKind.Absolute);

            return new ResourceDictionary { Source = uri };
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
