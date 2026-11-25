using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Xml;
using Microsoft.ConfigurationManagement.AdminConsole;
using Microsoft.ConfigurationManagement.AdminConsole.Schema;
using OpenRCT.Core.Models;
using OpenRCT.UI;

namespace OpenRCT.ConsoleExtension
{
    /// <summary>
    /// Base WPF Console View Controller matching Microsoft Configuration Manager's OverviewControllerBase pattern.
    /// Written in pure C# 5.0 so Windows' built-in .NET 4.8 csc.exe compiles it cleanly on any workstation.
    /// Never assumes compile-time ScopeNode members on OverviewControllerBase and catches all UI exceptions
    /// so Microsoft.ConfigurationManagement.exe cannot crash when clicking OpenRCT tree nodes.
    /// </summary>
    public abstract class OpenRctDashboardControllerBase : OverviewControllerBase
    {
        protected OpenRctDashboardControllerBase()
            : base()
        {
            OpenRctAssemblyResolver.EnsureRegistered();
        }

        protected abstract string DashboardKey { get; }

        public override void EndInit()
        {
            base.EndInit();
            OpenRctAssemblyResolver.EnsureRegistered();

            try
            {
                var scopeNode = TryResolveScopeNode();
                string server;
                string siteCode;
                OpenRctActionHandler.ExtractSmsConnection(scopeNode, null, out server, out siteCode);
                this.Content = CreateDashboardControl(DashboardKey, server, siteCode);
            }
            catch (Exception ex)
            {
                LogConsoleViewError(DashboardKey, ex);
                this.Content = BuildFallbackErrorPanel(DashboardKey, ex);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static object CreateDashboardControl(string dashboardKey, string server, string siteCode)
        {
            return ToolDispatcher.CreateDashboardView(dashboardKey, server, siteCode);
        }

        private ScopeNode TryResolveScopeNode()
        {
            try
            {
                var prop = this.GetType().GetProperty("ScopeNode", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop != null)
                {
                    var val = prop.GetValue(this, null) as ScopeNode;
                    if (val != null) return val;
                }
            }
            catch
            {
            }

            return OpenRctDashboardViewDescriptionBase.LastScopeNode;
        }

        private static void LogConsoleViewError(string dashboardKey, Exception ex)
        {
            try
            {
                var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenRCT");
                Directory.CreateDirectory(logDir);
                var logPath = Path.Combine(logDir, "openrct.log");
                File.AppendAllText(
                    logPath,
                    "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] [ConsoleView:" + dashboardKey + "] " + ex.ToString() + Environment.NewLine);
            }
            catch
            {
            }
        }

        private static UIElement BuildFallbackErrorPanel(string dashboardKey, Exception ex)
        {
            var panel = new StackPanel { Margin = new Thickness(24) };
            panel.Children.Add(new TextBlock
            {
                Text = "OpenRCT Dashboard could not render inline in the MECM Console",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            });
            panel.Children.Add(new TextBlock
            {
                Text = "Details: " + ex.Message,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });

            var launchBtn = new Button
            {
                Content = "Open Dashboard in OpenRCT Window",
                Padding = new Thickness(14, 7, 14, 7),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            launchBtn.Click += delegate
            {
                try
                {
                    var dir = OpenRctAssemblyResolver.ResolveInstallDirectory();
                    if (!string.IsNullOrEmpty(dir))
                    {
                        var exe = Path.Combine(dir, "OpenRCT.App.exe");
                        if (File.Exists(exe))
                        {
                            Process.Start(exe, "--action \"" + dashboardKey + "\"");
                            return;
                        }
                    }
                    MessageBox.Show("Could not locate OpenRCT.App.exe.", "OpenRCT", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                catch (Exception launchEx)
                {
                    MessageBox.Show("Failed to launch OpenRCT.App.exe: " + launchEx.Message, "OpenRCT", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
            panel.Children.Add(launchBtn);
            return panel;
        }
    }

    public abstract class OpenRctDashboardViewDescriptionBase : IConsoleView2
    {
        internal static ScopeNode LastScopeNode;

        static OpenRctDashboardViewDescriptionBase()
        {
            OpenRctAssemblyResolver.EnsureRegistered();
        }

        protected override Type TypeOfView
        {
            get { return typeof(Overview); }
        }

        public override bool TryConfigure(ref XmlElement persistedConfigurationData)
        {
            return false;
        }

        public new bool TryInitialize(ScopeNode scopeNode, AssemblyDescription resourceAssembly, ViewAssemblyDescription viewAssemblyDescription)
        {
            OpenRctAssemblyResolver.EnsureRegistered();
            LastScopeNode = scopeNode;
            return true;
        }
    }

    // ---- Parent / Home Overview ----

    public sealed class OpenRctHomeController : OpenRctDashboardControllerBase
    {
        protected override string DashboardKey { get { return ToolActionKeys.DashboardHome; } }
    }

    public sealed class OpenRctHomeConsoleView : OpenRctDashboardViewDescriptionBase
    {
        protected override Type TypeOfViewController { get { return typeof(OpenRctHomeController); } }
    }

    // ---- Phase 1 Dashboards ----

    public sealed class AdCleanupController : OpenRctDashboardControllerBase
    {
        protected override string DashboardKey { get { return ToolActionKeys.DashboardAdCleanup; } }
    }

    public sealed class AdCleanupConsoleView : OpenRctDashboardViewDescriptionBase
    {
        protected override Type TypeOfViewController { get { return typeof(AdCleanupController); } }
    }

    public sealed class LapsDashboardController : OpenRctDashboardControllerBase
    {
        protected override string DashboardKey { get { return ToolActionKeys.DashboardLaps; } }
    }

    public sealed class LapsDashboardConsoleView : OpenRctDashboardViewDescriptionBase
    {
        protected override Type TypeOfViewController { get { return typeof(LapsDashboardController); } }
    }

    public sealed class SoftwareUpdatesController : OpenRctDashboardControllerBase
    {
        protected override string DashboardKey { get { return ToolActionKeys.DashboardSoftwareUpdates; } }
    }

    public sealed class SoftwareUpdatesConsoleView : OpenRctDashboardViewDescriptionBase
    {
        protected override Type TypeOfViewController { get { return typeof(SoftwareUpdatesController); } }
    }

    // ---- Phase 2 Dashboards ----

    public sealed class BitLockerComplianceController : OpenRctDashboardControllerBase
    {
        protected override string DashboardKey { get { return ToolActionKeys.DashboardBitLockerCompliance; } }
    }

    public sealed class BitLockerComplianceConsoleView : OpenRctDashboardViewDescriptionBase
    {
        protected override Type TypeOfViewController { get { return typeof(BitLockerComplianceController); } }
    }

    public sealed class ContentDistributionController : OpenRctDashboardControllerBase
    {
        protected override string DashboardKey { get { return ToolActionKeys.DashboardContentDistribution; } }
    }

    public sealed class ContentDistributionConsoleView : OpenRctDashboardViewDescriptionBase
    {
        protected override Type TypeOfViewController { get { return typeof(ContentDistributionController); } }
    }

    public sealed class HardwareFirmwareAuditController : OpenRctDashboardControllerBase
    {
        protected override string DashboardKey { get { return ToolActionKeys.DashboardHardwareFirmwareAudit; } }
    }

    public sealed class HardwareFirmwareAuditConsoleView : OpenRctDashboardViewDescriptionBase
    {
        protected override Type TypeOfViewController { get { return typeof(HardwareFirmwareAuditController); } }
    }
}
