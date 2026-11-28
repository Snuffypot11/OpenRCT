using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using OpenCMTools.Core.Settings;
using OpenCMTools.UI.Themes;

namespace OpenCMTools.UI.Controls
{
    /// <summary>
    /// Resolution-independent WPF vector icon supporting both Fluent Duotone (default)
    /// and Crisp Monochrome Outline styles, with automatic Light/Dark mode adaptation
    /// and live refresh when the user switches Icon Style in OpenCMTools Settings.
    /// </summary>
    public sealed class VectorIcon : Viewbox
    {
        private sealed class IconSpec
        {
            public IconSpec(Color lightAccent, Color darkAccent, string fillData, string strokeData)
            {
                LightAccent = lightAccent;
                DarkAccent = darkAccent;
                FillData = fillData;
                StrokeData = strokeData;
            }

            public Color LightAccent { get; }
            public Color DarkAccent { get; }
            public string FillData { get; }
            public string StrokeData { get; }
        }

        private static readonly Color BlueLight = Color.FromRgb(0x00, 0x78, 0xD4);
        private static readonly Color BlueDark = Color.FromRgb(0x4C, 0xC2, 0xFF);
        private static readonly Color GreenLight = Color.FromRgb(0x10, 0x7C, 0x10);
        private static readonly Color GreenDark = Color.FromRgb(0x54, 0xD1, 0x68);
        private static readonly Color PurpleLight = Color.FromRgb(0x87, 0x64, 0xB8);
        private static readonly Color PurpleDark = Color.FromRgb(0xC0, 0x84, 0xFC);
        private static readonly Color AmberLight = Color.FromRgb(0xC1, 0x7D, 0x00);
        private static readonly Color AmberDark = Color.FromRgb(0xFB, 0xBF, 0x24);
        private static readonly Color TealLight = Color.FromRgb(0x03, 0x83, 0x87);
        private static readonly Color TealDark = Color.FromRgb(0x2D, 0xD4, 0xBF);
        private static readonly Color RedLight = Color.FromRgb(0xD1, 0x34, 0x38);
        private static readonly Color RedDark = Color.FromRgb(0xF8, 0x71, 0x71);
        private static readonly Color SlateLight = Color.FromRgb(0x47, 0x55, 0x69);
        private static readonly Color SlateDark = Color.FromRgb(0xCB, 0xD5, 0xE1);

        private static readonly Dictionary<string, IconSpec> Specs = new Dictionary<string, IconSpec>(StringComparer.OrdinalIgnoreCase)
        {
            // 6 Dashboards
            ["DashAdCleanup"] = new IconSpec(BlueLight, BlueDark,
                "M 12,3 A 2.5,2.5 0 1 0 12,8 A 2.5,2.5 0 1 0 12,3 Z M 4,15.5 H 9 V 20.5 H 4 Z M 15,15.5 H 20 V 20.5 H 15 Z",
                "M 12,3 A 2.5,2.5 0 1 0 12,8 A 2.5,2.5 0 1 0 12,3 Z M 12,8 V 12 M 6.5,15.5 V 12 H 17.5 V 15.5 M 4,15.5 H 9 V 20.5 H 4 Z M 15,15.5 H 20 V 20.5 H 15 Z"),
            ["DashBitLocker"] = new IconSpec(GreenLight, GreenDark,
                "M 5,11 H 19 V 20.5 H 5 Z",
                "M 5,11 H 19 V 20.5 H 5 Z M 8,11 V 8 A 4,4 0 0 1 16,8 V 11 M 12,14.5 V 18"),
            ["DashContentDist"] = new IconSpec(BlueLight, BlueDark,
                "M 4,4 H 20 V 9 H 4 Z M 4,15 H 10.5 V 20 H 4 Z M 13.5,15 H 20 V 20 H 13.5 Z",
                "M 4,4 H 20 V 9 H 4 Z M 12,9 V 12 M 7.2,15 V 12 H 16.8 V 15 M 4,15 H 10.5 V 20 H 4 Z M 13.5,15 H 20 V 20 H 13.5 Z"),
            ["DashHardwareAudit"] = new IconSpec(PurpleLight, PurpleDark,
                "M 7,7 H 17 V 17 H 7 Z",
                "M 7,7 H 17 V 17 H 7 Z M 9.5,4 V 7 M 14.5,4 V 7 M 9.5,17 V 20 M 14.5,17 V 20 M 4,9.5 H 7 M 4,14.5 H 7 M 17,9.5 H 20 M 17,14.5 H 20 M 10,12.2 L 11.5,13.7 L 14.5,10.7"),
            ["DashLaps"] = new IconSpec(AmberLight, AmberDark,
                "M 12,3 L 5,6 V 11.5 C 5,16.1 8,19.9 12,21 C 16,19.9 19,16.1 19,11.5 V 6 Z",
                "M 12,3 L 5,6 V 11.5 C 5,16.1 8,19.9 12,21 C 16,19.9 19,16.1 19,11.5 V 6 Z M 11,9 A 2,2 0 1 0 11,13 A 2,2 0 1 0 11,9 Z M 12.6,12.4 L 15.5,15.3"),
            ["DashSoftwareUpdates"] = new IconSpec(TealLight, TealDark,
                "M 12,6 A 6,6 0 1 0 12,18 A 6,6 0 1 0 12,6 Z",
                "M 20,12 A 8,8 0 0 0 6.3,6.4 L 4,8.5 M 4,4 V 8.5 H 8.5 M 4,12 A 8,8 0 0 0 17.7,17.6 L 20,15.5 M 20,20 V 15.5 H 15.5 M 9.5,12.3 L 11.2,14 L 14.7,10.5"),

            // Core Tools & Nav Icons
            ["ToolSystemInfo"] = new IconSpec(BlueLight, BlueDark,
                "M 3.5,4.5 H 20.5 V 16.5 H 3.5 Z",
                "M 3.5,4.5 H 20.5 V 16.5 H 3.5 Z M 8.5,19.5 H 15.5 M 12,16.5 V 19.5 M 12,10 V 14 M 12,7.5 V 7.8"),
            ["ToolSoftwareCenter"] = new IconSpec(BlueLight, BlueDark,
                "M 4,4 H 10.5 V 10.5 H 4 Z M 13.5,4 H 20 V 10.5 H 13.5 Z M 4,13.5 H 10.5 V 20 H 4 Z",
                "M 4,4 H 10.5 V 10.5 H 4 Z M 13.5,4 H 20 V 10.5 H 13.5 Z M 4,13.5 H 10.5 V 20 H 4 Z M 16.8,13.5 V 20 M 13.5,16.8 H 20"),
            ["ToolWindowsSecurity"] = new IconSpec(GreenLight, GreenDark,
                "M 12,3 L 4.5,6.2 V 11.7 C 4.5,16.5 7.7,20.4 12,21.5 C 16.3,20.4 19.5,16.5 19.5,11.7 V 6.2 Z",
                "M 12,3 L 4.5,6.2 V 11.7 C 4.5,16.5 7.7,20.4 12,21.5 C 16.3,20.4 19.5,16.5 19.5,11.7 V 6.2 Z M 12,3 V 21.5 M 4.8,11.5 H 19.2"),
            ["ToolRemoteRegistry"] = new IconSpec(PurpleLight, PurpleDark,
                "M 4,4 H 10 V 10 H 4 Z M 14,14 H 20 V 20 H 14 Z",
                "M 4,4 H 10 V 10 H 4 Z M 14,4 H 20 V 10 H 14 Z M 4,14 H 10 V 20 H 4 Z M 14,14 H 20 V 20 H 14 Z"),
            ["ToolRemoteEventViewer"] = new IconSpec(AmberLight, AmberDark,
                "M 4.5,3.5 H 19.5 V 20.5 H 4.5 Z",
                "M 4.5,3.5 H 19.5 V 20.5 H 4.5 Z M 8,8 H 16 M 8,12 H 16 M 8,16 H 13"),
            ["ToolRunningProcesses"] = new IconSpec(TealLight, TealDark,
                "M 3.5,4.5 H 20.5 V 19.5 H 3.5 Z",
                "M 3.5,4.5 H 20.5 V 19.5 H 3.5 Z M 6.5,13 H 9 L 10.8,9 L 13.2,15 L 15,13 H 17.5"),
            ["ToolFolderShare"] = new IconSpec(AmberLight, AmberDark,
                "M 3.5,5 H 9.7 L 11.7,7 H 20.5 V 19 H 3.5 Z",
                "M 3.5,5 H 9.7 L 11.7,7 H 20.5 V 19 H 3.5 Z"),
            ["ToolPowerShell"] = new IconSpec(BlueLight, BlueDark,
                "M 3.5,4.5 H 20.5 V 19.5 H 3.5 Z",
                "M 3.5,4.5 H 20.5 V 19.5 H 3.5 Z M 7.5,9.5 L 10.5,12 L 7.5,14.5 M 12.5,14.5 H 16.5"),
            ["ToolCommandPrompt"] = new IconSpec(SlateLight, SlateDark,
                "M 3.5,4.5 H 20.5 V 19.5 H 3.5 Z",
                "M 3.5,4.5 H 20.5 V 19.5 H 3.5 Z M 7,9 L 10,12 L 7,15 M 12,15 H 17"),
            ["ToolManageComputer"] = new IconSpec(BlueLight, BlueDark,
                "M 12,9 A 3,3 0 1 0 12,15 A 3,3 0 1 0 12,9 Z",
                "M 12,9 A 3,3 0 1 0 12,15 A 3,3 0 1 0 12,9 Z M 12,4 V 6.2 M 12,17.8 V 20 M 4,12 H 6.2 M 17.8,12 H 20 M 6.3,6.3 L 7.9,7.9 M 16.1,16.1 L 17.7,17.7 M 17.7,6.3 L 16.1,7.9 M 7.9,16.1 L 6.3,17.7"),
            ["ToolClientAction"] = new IconSpec(AmberLight, AmberDark,
                "M 13,3 L 5.5,13.5 H 12 L 11,21 L 18.5,10.5 H 12 Z",
                "M 13,3 L 5.5,13.5 H 12 L 11,21 L 18.5,10.5 H 12 Z"),
            ["ToolClientInfo"] = new IconSpec(TealLight, TealDark,
                "M 12,4 A 8,8 0 1 0 12,20 A 8,8 0 1 0 12,4 Z",
                "M 12,4 A 8,8 0 1 0 12,20 A 8,8 0 1 0 12,4 Z M 12,11 V 16 M 12,8 V 8.3"),
            ["ToolCollections"] = new IconSpec(BlueLight, BlueDark,
                "M 4,5 H 20 V 9.5 H 4 Z M 4,14.5 H 20 V 19 H 4 Z",
                "M 4,5 H 20 V 9.5 H 4 Z M 4,14.5 H 20 V 19 H 4 Z M 8,12 H 16"),
            ["ToolStatusMessages"] = new IconSpec(BlueLight, BlueDark,
                "M 4,4 H 20 V 16 H 9 L 4,20 Z",
                "M 4,4 H 20 V 16 H 9 L 4,20 Z M 8,8.5 H 16 M 8,12 H 13"),
            ["ToolRepair"] = new IconSpec(TealLight, TealDark,
                "M 12,5 A 7,7 0 1 0 12,19 A 7,7 0 1 0 12,5 Z",
                "M 14.5,6.5 L 17.5,9.5 L 9,18 L 6,18 L 6,15 Z"),
            ["ToolRestartService"] = new IconSpec(BlueLight, BlueDark,
                "M 12,6 A 6,6 0 1 0 12,18 A 6,6 0 1 0 12,6 Z",
                "M 20,12 A 8,8 0 1 0 17.7,17.6 M 20,6 V 12 H 14"),
            ["ToolUninstall"] = new IconSpec(RedLight, RedDark,
                "M 6,7 H 18 L 17,20 H 7 Z",
                "M 4,7 H 20 M 9.5,4 H 14.5 M 6,7 L 7,20 H 17 L 18,7 M 10,11 V 17 M 14,11 V 17"),
            ["ToolPing"] = new IconSpec(GreenLight, GreenDark,
                "M 12,10 A 2,2 0 1 0 12,14 A 2,2 0 1 0 12,10 Z",
                "M 12,10 A 2,2 0 1 0 12,14 A 2,2 0 1 0 12,10 Z M 8.5,8.5 A 5,5 0 0 0 8.5,15.5 M 15.5,8.5 A 5,5 0 0 1 15.5,15.5 M 5.5,5.5 A 9.2,9.2 0 0 0 5.5,18.5 M 18.5,5.5 A 9.2,9.2 0 0 1 18.5,18.5"),
            ["ToolWakeOnLan"] = new IconSpec(GreenLight, GreenDark,
                "M 12,5 A 7,7 0 1 0 12,19 A 7,7 0 1 0 12,5 Z",
                "M 12,5 V 11 M 8,7.5 A 6.5,6.5 0 1 0 16,7.5"),
            ["ToolGroupPolicy"] = new IconSpec(BlueLight, BlueDark,
                "M 5,3.5 H 19 V 20.5 H 5 Z",
                "M 5,3.5 H 19 V 20.5 H 5 Z M 8.5,8.5 H 15.5 M 8.5,12 H 15.5 M 8.5,15.5 H 13"),
            ["ToolNotification"] = new IconSpec(AmberLight, AmberDark,
                "M 6,16 H 18 L 16.5,13.5 V 10 A 4.5,4.5 0 0 0 7.5,10 V 13.5 Z",
                "M 6,16 H 18 L 16.5,13.5 V 10 A 4.5,4.5 0 0 0 7.5,10 V 13.5 Z M 10,19 A 2,2 0 0 0 14,19"),
            ["ToolPowerSchedule"] = new IconSpec(RedLight, RedDark,
                "M 12,4 A 8,8 0 1 0 12,20 A 8,8 0 1 0 12,4 Z",
                "M 12,4 A 8,8 0 1 0 12,20 A 8,8 0 1 0 12,4 Z M 12,7.5 V 12 L 15,14"),
            ["ToolKey"] = new IconSpec(AmberLight, AmberDark,
                "M 8.5,8.5 A 3.5,3.5 0 1 0 8.5,15.5 A 3.5,3.5 0 1 0 8.5,8.5 Z",
                "M 8.5,8.5 A 3.5,3.5 0 1 0 8.5,15.5 A 3.5,3.5 0 1 0 8.5,8.5 Z M 12,12 H 20 M 17,12 V 15 M 20,12 V 14.5"),
            ["ToolDefender"] = new IconSpec(GreenLight, GreenDark,
                "M 12,3 L 5,6 V 11.5 C 5,16.1 8,19.9 12,21 C 16,19.9 19,16.1 19,11.5 V 6 Z",
                "M 12,3 L 5,6 V 11.5 C 5,16.1 8,19.9 12,21 C 16,19.9 19,16.1 19,11.5 V 6 Z M 9.2,12.2 L 11.2,14.2 L 15,10.2"),
            ["ToolAdGroups"] = new IconSpec(PurpleLight, PurpleDark,
                "M 9,5.7 A 2.8,2.8 0 1 0 9,11.3 A 2.8,2.8 0 1 0 9,5.7 Z M 16,7.3 A 2.2,2.2 0 1 0 16,11.7 A 2.2,2.2 0 1 0 16,7.3 Z",
                "M 9,5.7 A 2.8,2.8 0 1 0 9,11.3 A 2.8,2.8 0 1 0 9,5.7 Z M 4,18.5 C 4,15.9 6.2,14 9,14 C 11.8,14 14,15.9 14,18.5 M 16,7.3 A 2.2,2.2 0 1 0 16,11.7 A 2.2,2.2 0 1 0 16,7.3 Z M 15,14.3 C 17.2,14.6 19,16.1 19,18.5"),
            ["ToolSettings"] = new IconSpec(SlateLight, SlateDark,
                "M 12,8.8 A 3.2,3.2 0 1 0 12,15.2 A 3.2,3.2 0 1 0 12,8.8 Z",
                "M 12,8.8 A 3.2,3.2 0 1 0 12,15.2 A 3.2,3.2 0 1 0 12,8.8 Z M 12,3.5 V 5.8 M 12,18.2 V 20.5 M 3.5,12 H 5.8 M 18.2,12 H 20.5 M 6,6 L 7.7,7.7 M 16.3,16.3 L 18,18 M 18,6 L 16.3,7.7 M 7.7,16.3 L 6,18"),

            // Remote Windows Security & System Info extras
            ["SecFirewall"] = new IconSpec(RedLight, RedDark,
                "M 4,5 H 20 V 19 H 4 Z",
                "M 4,5 H 20 V 19 H 4 Z M 4,9.6 H 20 M 4,14.3 H 20 M 10,5 V 9.6 M 15,9.6 V 14.3 M 9,14.3 V 19"),
            ["SecFirewallRules"] = new IconSpec(BlueLight, BlueDark,
                "M 5,3.5 H 19 V 20.5 H 5 Z",
                "M 5,3.5 H 19 V 20.5 H 5 Z M 8,8.5 H 16 M 8,12 H 16 M 8,15.5 H 12.5 M 14.5,15.5 L 16,17 L 18.5,14"),
            ["SecExclusions"] = new IconSpec(AmberLight, AmberDark,
                "M 4,4 H 14 V 14 H 4 Z",
                "M 4,4 H 14 V 14 H 4 Z M 14,10 H 20 V 20 H 10 V 14 M 15,15 L 18,18"),
            ["SecVbs"] = new IconSpec(PurpleLight, PurpleDark,
                "M 6,6 H 18 V 18 H 6 Z",
                "M 6,6 H 18 V 18 H 6 Z M 9.5,9.5 H 14.5 V 14.5 H 9.5 Z M 12,3 V 6 M 12,18 V 21 M 3,12 H 6 M 18,12 H 21"),
            ["SysDisks"] = new IconSpec(BlueLight, BlueDark,
                "M 4,6 H 20 V 18 H 4 Z",
                "M 4,6 H 20 V 18 H 4 Z M 4,14 H 20 M 16.5,16 H 17"),
            ["SysBattery"] = new IconSpec(GreenLight, GreenDark,
                "M 4,7.5 H 18 V 16.5 H 4 Z",
                "M 4,7.5 H 18 V 16.5 H 4 Z M 18,10.5 H 20.5 V 13.5 H 18 M 7,10 V 14 M 10.5,10 V 14"),
            ["SysDrivers"] = new IconSpec(PurpleLight, PurpleDark,
                "M 6,6 H 18 V 18 H 6 Z",
                "M 6,6 H 18 V 18 H 6 Z M 9,3 V 6 M 15,3 V 6 M 9,18 V 21 M 15,18 V 21 M 3,9 H 6 M 3,15 H 6 M 18,9 H 21 M 18,15 H 21")
        };

        public static readonly DependencyProperty IconKeyProperty =
            DependencyProperty.Register(nameof(IconKey), typeof(string), typeof(VectorIcon),
                new PropertyMetadata(string.Empty, (d, _) => ((VectorIcon)d).Rebuild()));

        public string IconKey
        {
            get => (string)GetValue(IconKeyProperty);
            set => SetValue(IconKeyProperty, value);
        }

        public VectorIcon()
        {
            Stretch = Stretch.Uniform;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            ThemeManager.SyncSettingsFromDisk();
            ThemeManager.AppearanceChanged -= OnAppearanceChanged;
            ThemeManager.AppearanceChanged += OnAppearanceChanged;
            Rebuild();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            ThemeManager.AppearanceChanged -= OnAppearanceChanged;
        }

        private void OnAppearanceChanged(object? sender, EventArgs e)
        {
            Dispatcher.InvokeAsync(Rebuild);
        }

        private void Rebuild()
        {
            var key = IconKey;
            if (string.IsNullOrWhiteSpace(key))
            {
                Child = null;
                return;
            }

            if (string.Equals(key, "OpenCMToolsLogo", StringComparison.OrdinalIgnoreCase))
            {
                Child = BuildPrimaryLogoCanvas();
                return;
            }

            if (!Specs.TryGetValue(key, out var spec))
            {
                spec = Specs["ToolSystemInfo"];
            }

            var isDark = ThemeManager.CurrentIsDark;
            var style = ThemeManager.CurrentIconStyle;

            var canvas = new Canvas { Width = 24, Height = 24 };

            Color strokeColor;
            if (style == IconStylePreference.CrispOutline)
            {
                strokeColor = isDark ? Color.FromRgb(0xE5, 0xE7, 0xEB) : Color.FromRgb(0x1F, 0x29, 0x37);
            }
            else
            {
                strokeColor = isDark ? spec.DarkAccent : spec.LightAccent;
            }

            if (style == IconStylePreference.FluentDuotone && !string.IsNullOrWhiteSpace(spec.FillData))
            {
                var fillAlpha = (byte)(isDark ? 0x38 : 0x2C);
                var fillBrush = new SolidColorBrush(Color.FromArgb(fillAlpha, strokeColor.R, strokeColor.G, strokeColor.B));
                canvas.Children.Add(new Path
                {
                    Data = Geometry.Parse(spec.FillData),
                    Fill = fillBrush,
                    IsHitTestVisible = false
                });
            }

            canvas.Children.Add(new Path
            {
                Data = Geometry.Parse(spec.StrokeData),
                Stroke = new SolidColorBrush(strokeColor),
                StrokeThickness = 1.75,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false
            });

            Child = canvas;
        }

        private static Canvas BuildPrimaryLogoCanvas()
        {
            var canvas = new Canvas { Width = 32, Height = 32 };
            var bg = new Rectangle
            {
                Width = 28,
                Height = 28,
                RadiusX = 7,
                RadiusY = 7,
                Fill = new LinearGradientBrush(
                    Color.FromRgb(0x0E, 0xA5, 0xE9),
                    Color.FromRgb(0x00, 0x67, 0xB8),
                    new Point(0, 0),
                    new Point(1, 1))
            };
            Canvas.SetLeft(bg, 2);
            Canvas.SetTop(bg, 2);
            canvas.Children.Add(bg);

            canvas.Children.Add(new Path
            {
                Data = Geometry.Parse("M 22.2,10.6 A 8.5,8.5 0 1 0 22.8,21"),
                Stroke = Brushes.White,
                StrokeThickness = 3.2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            });

            canvas.Children.Add(new Path
            {
                Data = Geometry.Parse("M 17.5,13 L 25.5,16.2 L 21.5,17.8 L 19.9,21.8 Z"),
                Fill = new SolidColorBrush(Color.FromRgb(0xBA, 0xE6, 0xFD)),
                Stroke = Brushes.White,
                StrokeThickness = 0.9,
                StrokeLineJoin = PenLineJoin.Round
            });

            return canvas;
        }
    }
}
