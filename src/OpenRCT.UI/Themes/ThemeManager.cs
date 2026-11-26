using System;
using System.IO.Packaging;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using OpenRCT.Core.Settings;

namespace OpenRCT.UI.Themes
{
    public static class ThemeManager
    {
        private static ImageSource? _cachedWindowIcon;
        private static DateTime _lastSettingsWriteUtc = DateTime.MinValue;

        public static bool CurrentIsDark { get; private set; }
        public static IconStylePreference CurrentIconStyle { get; private set; } = IconStylePreference.FluentDuotone;

        public static event EventHandler? AppearanceChanged;

        public static void SetIconStyle(IconStylePreference style)
        {
            if (CurrentIconStyle != style)
            {
                CurrentIconStyle = style;
                AppearanceChanged?.Invoke(null, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Reloads persisted appearance preferences (such as IconStyle) if settings.json changed
        /// on disk (for example, saved by the standalone app while the MECM Console is open).
        /// </summary>
        public static void SyncSettingsFromDisk()
        {
            try
            {
                var svc = new SettingsService();
                var path = svc.SettingsFilePath;
                if (!System.IO.File.Exists(path)) return;

                var writeUtc = System.IO.File.GetLastWriteTimeUtc(path);
                if (writeUtc == _lastSettingsWriteUtc) return;

                _lastSettingsWriteUtc = writeUtc;
                var saved = svc.Load();
                if (CurrentIconStyle != saved.IconStyle)
                {
                    CurrentIconStyle = saved.IconStyle;
                    AppearanceChanged?.Invoke(null, EventArgs.Empty);
                }
            }
            catch
            {
            }
        }

        private const string OpenRctThemeMarkerKey = "__OpenRctThemeDictionary";
        private const string OpenRctThemeIsDarkKey = "__OpenRctThemeIsDark";

        private static bool IsOpenRctThemeDictionary(ResourceDictionary d)
        {
            if (d == null) return false;
            try
            {
                if (d.Contains(OpenRctThemeMarkerKey)) return true;
                var src = d.Source?.OriginalString;
                if (!string.IsNullOrWhiteSpace(src) &&
                    src!.IndexOf("OpenRCT.UI;component/Themes/", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static bool AlreadyHasMatchingThemeDictionary(ResourceDictionary resources, bool useDark)
        {
            if (resources == null) return false;
            try
            {
                foreach (var d in resources.MergedDictionaries)
                {
                    if (d != null && d.Contains(OpenRctThemeIsDarkKey) && d[OpenRctThemeIsDarkKey] is bool existingDark)
                    {
                        if (existingDark == useDark) return true;
                    }
                    else if (d?.Source?.OriginalString != null)
                    {
                        var src = d.Source.OriginalString;
                        if (useDark && src.IndexOf("DarkTheme.xaml", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                        if (!useDark && src.IndexOf("LightTheme.xaml", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static void ReplaceOpenRctThemeDictionary(ResourceDictionary resources, ResourceDictionary newDict, bool useDark)
        {
            if (resources == null || newDict == null) return;
            if (AlreadyHasMatchingThemeDictionary(resources, useDark)) return;

            newDict[OpenRctThemeMarkerKey] = true;
            newDict[OpenRctThemeIsDarkKey] = useDark;

            var existingToRemove = new System.Collections.Generic.List<ResourceDictionary>();
            foreach (var d in resources.MergedDictionaries)
            {
                if (IsOpenRctThemeDictionary(d))
                {
                    existingToRemove.Add(d);
                }
            }

            // Add the new dictionary BEFORE removing the old one so DynamicResource lookups
            // never observe a transient state with missing brushes/styles.
            resources.MergedDictionaries.Add(newDict);
            foreach (var old in existingToRemove)
            {
                resources.MergedDictionaries.Remove(old);
            }
        }

        public static void ApplyTheme(FrameworkElement element, AppThemePreference preference)
        {
            if (element == null) return;
            var useDark = ResolveIsDark(preference);
            CurrentIsDark = useDark;

            try
            {
                var svc = new SettingsService();
                if (System.IO.File.Exists(svc.SettingsFilePath))
                {
                    _lastSettingsWriteUtc = System.IO.File.GetLastWriteTimeUtc(svc.SettingsFilePath);
                }
                var saved = svc.Load();
                CurrentIconStyle = saved.IconStyle;
            }
            catch { }

            try
            {
                if (!AlreadyHasMatchingThemeDictionary(element.Resources, useDark))
                {
                    var dict = LoadThemeDictionary(preference);
                    ReplaceOpenRctThemeDictionary(element.Resources, dict, useDark);

                    if (dict["PrimaryTextBrush"] is Brush fg)
                    {
                        TextElement.SetForeground(element, fg);
                    }
                }
            }
            catch { }

            if (element is Window window)
            {
                ApplyWindowIcon(window);
            }

            try
            {
                AppearanceChanged?.Invoke(null, EventArgs.Empty);
            }
            catch { }
        }

        public static void ApplyApplicationTheme(AppThemePreference preference, Window? excludeWindow = null)
        {
            var useDark = ResolveIsDark(preference);
            CurrentIsDark = useDark;
            try
            {
                var app = Application.Current;
                if (app != null)
                {
                    // CRITICAL: Never touch Application.Current.Resources when hosted inside
                    // Microsoft.ConfigurationManagement.exe (MECM Admin Console), because clearing
                    // or mutating the host Application's MergedDictionaries crashes the console.
                    var isStandaloneOpenRct = string.Equals(
                        app.GetType().FullName,
                        "OpenRCT.App.App",
                        StringComparison.Ordinal);

                    if (isStandaloneOpenRct && !AlreadyHasMatchingThemeDictionary(app.Resources, useDark))
                    {
                        var dict = LoadThemeDictionary(preference);
                        ReplaceOpenRctThemeDictionary(app.Resources, dict, useDark);
                    }

                    foreach (Window win in app.Windows)
                    {
                        try
                        {
                            if (win == null || ReferenceEquals(win, excludeWindow)) continue;
                            var ns = win.GetType().Namespace;
                            if (!string.IsNullOrEmpty(ns) &&
                                ns!.StartsWith("OpenRCT.", StringComparison.Ordinal) &&
                                !AlreadyHasMatchingThemeDictionary(win.Resources, useDark))
                            {
                                var winDict = LoadThemeDictionary(preference);
                                ReplaceOpenRctThemeDictionary(win.Resources, winDict, useDark);
                                if (winDict["PrimaryTextBrush"] is Brush fg)
                                {
                                    TextElement.SetForeground(win, fg);
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            try
            {
                AppearanceChanged?.Invoke(null, EventArgs.Empty);
            }
            catch { }
        }

        public static void ApplyWindowIcon(Window window)
        {
            if (window == null) return;
            try
            {
                var icon = GetAppWindowIcon();
                if (icon != null)
                {
                    window.Icon = icon;
                }
            }
            catch { }
        }

        /// <summary>
        /// Renders the primary OpenRCT Option B ("Open O Ring") vector logo to a crisp 32x32 BitmapSource
        /// for WPF Window title bars and taskbars (works both in standalone EXE and hosted inside MECM Console).
        /// </summary>
        public static ImageSource? GetAppWindowIcon()
        {
            if (_cachedWindowIcon != null) return _cachedWindowIcon;

            try
            {
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    var grad = new LinearGradientBrush(
                        Color.FromRgb(0x0E, 0xA5, 0xE9),
                        Color.FromRgb(0x00, 0x67, 0xB8),
                        new Point(0, 0),
                        new Point(1, 1));
                    dc.DrawRoundedRectangle(grad, null, new Rect(2, 2, 28, 28), 7, 7);

                    var ringPen = new Pen(Brushes.White, 3.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                    dc.DrawGeometry(null, ringPen, Geometry.Parse("M 22.2,10.6 A 8.5,8.5 0 1 0 22.8,21"));

                    var cursorFill = new SolidColorBrush(Color.FromRgb(0xBA, 0xE6, 0xFD));
                    var cursorPen = new Pen(Brushes.White, 0.9) { LineJoin = PenLineJoin.Round };
                    dc.DrawGeometry(cursorFill, cursorPen, Geometry.Parse("M 17.5,13 L 25.5,16.2 L 21.5,17.8 L 19.9,21.8 Z"));
                }

                var rtb = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                rtb.Freeze();
                _cachedWindowIcon = rtb;
                return _cachedWindowIcon;
            }
            catch
            {
                return null;
            }
        }

        private static bool ResolveIsDark(AppThemePreference preference) =>
            preference == AppThemePreference.Dark ||
            (preference == AppThemePreference.System && IsWindowsDarkModeEnabled());

        private static void EnsurePackUriSchemeRegistered()
        {
            try
            {
                if (!UriParser.IsKnownScheme("pack"))
                {
                    _ = PackUriHelper.UriSchemePack;
                }
                _ = Application.ResourceAssembly;
            }
            catch
            {
            }
        }

        private static ResourceDictionary LoadThemeDictionary(AppThemePreference preference)
        {
            var useDark = ResolveIsDark(preference);

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
