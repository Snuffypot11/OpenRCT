using System;
using System.IO.Packaging;
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

        private static void EnsurePackUriSchemeRegistered()
        {
            try
            {
                if (!UriParser.IsKnownScheme("pack"))
                {
                    _ = PackUriHelper.UriSchemePack;
                }
                // Touch Application.ResourceAssembly so WPF registers the application:/// pack authority
                // even when hosted inside a WinForms process (like Microsoft.ConfigurationManagement.exe)
                // where System.Windows.Application.Current is null.
                _ = Application.ResourceAssembly;
            }
            catch
            {
            }
        }

        private static ResourceDictionary LoadThemeDictionary(AppThemePreference preference)
        {
            var useDark = preference == AppThemePreference.Dark ||
                          (preference == AppThemePreference.System && IsWindowsDarkModeEnabled());

            try
            {
                EnsurePackUriSchemeRegistered();
                var uri = new Uri(
                    useDark
                        ? "pack://application:,,,/OpenRCT.UI;component/Themes/DarkTheme.xaml"
                        : "pack://application:,,,/OpenRCT.UI;component/Themes/LightTheme.xaml",
                    UriKind.Absolute);

                return new ResourceDictionary { Source = uri };
            }
            catch
            {
                return CreateFallbackDictionary(useDark);
            }
        }

        private static ResourceDictionary CreateFallbackDictionary(bool dark)
        {
            var d = new ResourceDictionary();
            d["AppBackgroundBrush"] = new SolidColorBrush(dark ? Color.FromRgb(0x1E, 0x1E, 0x1E) : Color.FromRgb(0xF8, 0xF9, 0xFA));
            d["SurfaceBrush"] = new SolidColorBrush(dark ? Color.FromRgb(0x25, 0x25, 0x26) : Colors.White);
            d["InputBackgroundBrush"] = new SolidColorBrush(dark ? Color.FromRgb(0x2D, 0x2D, 0x30) : Colors.White);
            d["SidebarBrush"] = new SolidColorBrush(dark ? Color.FromRgb(0x2D, 0x2D, 0x30) : Color.FromRgb(0xF3, 0xF5, 0xF8));
            d["BorderBrushSubtle"] = new SolidColorBrush(dark ? Color.FromRgb(0x3F, 0x3F, 0x46) : Color.FromRgb(0xDC, 0xDF, 0xE4));
            d["PrimaryTextBrush"] = new SolidColorBrush(dark ? Color.FromRgb(0xF3, 0xF4, 0xF6) : Color.FromRgb(0x1F, 0x29, 0x37));
            d["SecondaryTextBrush"] = new SolidColorBrush(dark ? Color.FromRgb(0xB8, 0xBC, 0xC4) : Color.FromRgb(0x5F, 0x6B, 0x7A));
            d["AccentBrush"] = new SolidColorBrush(dark ? Color.FromRgb(0x3B, 0x9E, 0xFF) : Color.FromRgb(0x00, 0x78, 0xD4));
            d["AccentHoverBrush"] = new SolidColorBrush(dark ? Color.FromRgb(0x60, 0xB0, 0xFF) : Color.FromRgb(0x10, 0x6E, 0xBE));
            d["SelectionFillBrush"] = new SolidColorBrush(dark ? Color.FromRgb(0x26, 0x4F, 0x78) : Color.FromRgb(0xE1, 0xF0, 0xFB));
            d["SelectionBorderBrush"] = new SolidColorBrush(dark ? Color.FromRgb(0x3B, 0x9E, 0xFF) : Color.FromRgb(0x00, 0x78, 0xD4));
            d["DangerBrush"] = new SolidColorBrush(Color.FromRgb(0xD1, 0x34, 0x38));
            d["SuccessBrush"] = new SolidColorBrush(dark ? Color.FromRgb(0x6C, 0xCB, 0x5F) : Color.FromRgb(0x10, 0x7C, 0x10));
            d["WarningBrush"] = new SolidColorBrush(dark ? Color.FromRgb(0xFC, 0xE1, 0x00) : Color.FromRgb(0xC1, 0x7D, 0x00));
            d["DataGridHeaderBrush"] = new SolidColorBrush(dark ? Color.FromRgb(0x2D, 0x2D, 0x30) : Color.FromRgb(0xF3, 0xF4, 0xF6));
            d["DataGridAltRowBrush"] = new SolidColorBrush(dark ? Color.FromRgb(0x2B, 0x2B, 0x2E) : Color.FromRgb(0xFA, 0xFB, 0xFC));
            return d;
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
