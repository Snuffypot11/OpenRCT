using System;
using System.Windows;
using System.Windows.Controls;
using OpenRCT.Core.Models;
using OpenRCT.Core.Settings;
using OpenRCT.UI.Controls;
using OpenRCT.UI.Themes;

namespace OpenRCT.UI.Views.Dashboards
{
    /// <summary>
    /// Overview landing view rendered when selecting the OpenRCT parent node
    /// under Assets and Compliance -> Overview in the MECM Admin Console.
    /// </summary>
    public sealed class OpenRctHomeDashboardView : UserControl
    {
        private readonly string? _smsServer;
        private readonly string? _siteCode;

        public OpenRctHomeDashboardView(string? smsServer, string? siteCode, OpenRctSettings settings)
        {
            _smsServer = smsServer;
            _siteCode = siteCode;
            ThemeManager.ApplyTheme(this, settings.Theme);
            SetResourceReference(BackgroundProperty, "AppBackgroundBrush");

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var root = new StackPanel { Margin = new Thickness(24) };

            var headerRow = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
            var logo = new VectorIcon
            {
                IconKey = "OpenRctLogo",
                Width = 40,
                Height = 40,
                Margin = new Thickness(0, 0, 14, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(logo, Dock.Left);
            headerRow.Children.Add(logo);

            var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var header = new TextBlock
            {
                Text = "OpenRCT - Open Source Right Click Tools",
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            header.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryTextBrush");
            titleStack.Children.Add(header);

            var connInfo = string.IsNullOrWhiteSpace(siteCode)
                ? "Select a dashboard below or from the OpenRCT tree under Assets and Compliance -> Overview."
                : $"Connected to Site {siteCode} ({smsServer}). Select a dashboard below or from the navigation tree.";
            var subtitle = new TextBlock
            {
                Text = connInfo,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap
            };
            subtitle.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
            titleStack.Children.Add(subtitle);
            headerRow.Children.Add(titleStack);
            root.Children.Add(headerRow);

            var cards = new WrapPanel { Orientation = Orientation.Horizontal };
            cards.Children.Add(CreateCard(
                "DashAdCleanup",
                "Active Directory Cleanup Tool",
                "Compare Active Directory OUs against ConfigMgr Collections, identify stale records, and clean up AD or ConfigMgr.",
                "Open Dashboard",
                () => LaunchAction(ToolActionKeys.DashboardAdCleanup)));
            cards.Children.Add(CreateCard(
                "DashBitLocker",
                "BitLocker Compliance",
                "Audit BitLocker recovery key storage in AD & ConfigMgr alongside encryption and compliance status.",
                "Open Dashboard",
                () => LaunchAction(ToolActionKeys.DashboardBitLockerCompliance)));
            cards.Children.Add(CreateCard(
                "DashContentDist",
                "Content Distribution Monitor",
                "Monitor Distribution Points, inspect Content Status, and redistribute, validate, or remove content.",
                "Open Dashboard",
                () => LaunchAction(ToolActionKeys.DashboardContentDistribution)));
            cards.Children.Add(CreateCard(
                "DashHardwareAudit",
                "Hardware and Firmware Audit",
                "Analyze chassis, manufacturer, model, UEFI, Secure Boot, TPM, and BIOS versions with nested chart filtering.",
                "Open Dashboard",
                () => LaunchAction(ToolActionKeys.DashboardHardwareFirmwareAudit)));
            cards.Children.Add(CreateCard(
                "DashLaps",
                "LAPS Dashboard",
                "Audit Legacy Microsoft LAPS and Windows LAPS password status, expiration, and client health across AD & ConfigMgr.",
                "Open Dashboard",
                () => LaunchAction(ToolActionKeys.DashboardLaps)));
            cards.Children.Add(CreateCard(
                "DashSoftwareUpdates",
                "Software Updates Deployment Status",
                "Inspect collection update compliance, missing updates by category, Software Update Groups, and trigger remediation.",
                "Open Dashboard",
                () => LaunchAction(ToolActionKeys.DashboardSoftwareUpdates)));
            cards.Children.Add(CreateCard(
                "ToolSettings",
                "OpenRCT Settings",
                "Configure optional PsExec path, theme (Light / Dark / System), icon style, concurrency limits, SMS Provider, and updates.",
                "Open Settings",
                () => LaunchAction(ToolActionKeys.OpenSettings)));

            root.Children.Add(cards);
            scroll.Content = root;
            Content = scroll;
        }

        private Border CreateCard(string iconKey, string title, string description, string buttonText, Action onClick)
        {
            var border = new Border
            {
                Width = 315,
                Margin = new Thickness(0, 0, 14, 14),
                Padding = new Thickness(16),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6)
            };
            border.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
            border.SetResourceReference(Border.BorderBrushProperty, "BorderBrushSubtle");

            var panel = new StackPanel();
            var titleRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var icon = new VectorIcon
            {
                IconKey = iconKey,
                Width = 22,
                Height = 22,
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(icon, Dock.Left);
            titleRow.Children.Add(icon);

            var titleBlock = new TextBlock
            {
                Text = title,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryTextBrush");
            titleRow.Children.Add(titleBlock);
            panel.Children.Add(titleRow);

            var descBlock = new TextBlock
            {
                Text = description,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 48,
                Margin = new Thickness(0, 0, 0, 12)
            };
            descBlock.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
            panel.Children.Add(descBlock);

            var btn = new Button
            {
                Content = buttonText,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            btn.SetResourceReference(StyleProperty, "PrimaryButton");
            btn.Click += (_, __) => onClick();
            panel.Children.Add(btn);

            border.Child = panel;
            return border;
        }

        private void LaunchAction(string actionKey)
        {
            ToolDispatcher.Dispatch(new ToolInvocationContext
            {
                ActionKey = actionKey,
                SmsProviderServer = _smsServer,
                SiteCode = _siteCode
            });
        }
    }
}
