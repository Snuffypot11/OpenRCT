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
using Microsoft.ConfigurationManagement.ManagementProvider;
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
            OpenRctAssemblyResolver.EnsureRegistered();

            try
            {
                base.EndInit();
            }
            catch (Exception baseEx)
            {
                LogConsoleViewError(DashboardKey + ".BaseEndInit", baseEx);
            }

            try
            {
                var scopeNode = TryResolveScopeNode();
                string server;
                string siteCode;
                OpenRctActionHandler.ExtractSmsConnection(scopeNode, null, out server, out siteCode);

                if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(siteCode))
                {
                    var cm = TryResolveConnectionManager();
                    OpenRctActionHandler.ExtractFromConnectionManager(cm, ref server, ref siteCode);
                }

                this.Content = CreateDashboardControl(DashboardKey, server, siteCode);
            }
            catch (Exception ex)
            {
                LogConsoleViewError(DashboardKey, ex);
                this.Content = BuildFallbackErrorPanel(DashboardKey, ex);
            }
        }

        private ConnectionManagerBase TryResolveConnectionManager()
        {
            try
            {
                var prop = this.GetType().GetProperty("ConnectionManager", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop != null)
                {
                    var cm = prop.GetValue(this, null) as ConnectionManagerBase;
                    if (cm != null) return cm;
                }

                var ctxProp = this.GetType().GetProperty("Context", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (ctxProp != null)
                {
                    var ctx = ctxProp.GetValue(this, null);
                    if (ctx != null)
                    {
                        var ctxCmProp = ctx.GetType().GetProperty("ConnectionManager", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (ctxCmProp != null)
                        {
                            var cm2 = ctxCmProp.GetValue(ctx, null) as ConnectionManagerBase;
                            if (cm2 != null) return cm2;
                        }
                    }
                }
            }
            catch
            {
            }
            return null;
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

                var ctxProp = this.GetType().GetProperty("Context", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (ctxProp != null)
                {
                    var ctx = ctxProp.GetValue(this, null);
                    var asNode = ctx as ScopeNode;
                    if (asNode != null) return asNode;
                    if (ctx != null)
                    {
                        var ctxNodeProp = ctx.GetType().GetProperty("ScopeNode", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (ctxNodeProp != null)
                        {
                            var val2 = ctxNodeProp.GetValue(ctx, null) as ScopeNode;
                            if (val2 != null) return val2;
                        }
                    }
                }
            }
            catch
            {
            }

            return OpenRctDashboardViewDescriptionBase.LastScopeNode;
        }

        internal static void LogConsoleViewError(string dashboardKey, Exception ex)
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

    // BEGIN_VIEW_DESCRIPTION_BASE
    public abstract class OpenRctDashboardViewDescriptionBase : IConsoleView2
    {
        internal static ScopeNode LastScopeNode;

        static OpenRctDashboardViewDescriptionBase()
        {
            OpenRctAssemblyResolver.EnsureRegistered();
        }

        protected OpenRctDashboardViewDescriptionBase()
        {
            OpenRctAssemblyResolver.EnsureRegistered();
        }

        protected abstract Type ControllerType { get; }

        public Type TypeOfViewController
        {
            get { return ControllerType; }
        }

        public Type TypeOfView
        {
            get { return typeof(Overview); }
        }

        public bool TryConfigure(ref XmlElement persistedConfigurationData)
        {
            return false;
        }

        public bool TryInitialize(ScopeNode scopeNode, AssemblyDescription resourceAssembly, ViewAssemblyDescription viewAssemblyDescription)
        {
            try
            {
                OpenRctAssemblyResolver.EnsureRegistered();
                LastScopeNode = scopeNode;
            }
            catch
            {
            }
            return true;
        }
    }
    // END_VIEW_DESCRIPTION_BASE

    // ---- Parent / Home Overview ----

    public sealed class OpenRctHomeController : OpenRctDashboardControllerBase
    {
        public OpenRctHomeController() : base() { }
        protected override string DashboardKey { get { return ToolActionKeys.DashboardHome; } }
    }

    public sealed class OpenRctHomeConsoleView : OpenRctDashboardViewDescriptionBase
    {
        public OpenRctHomeConsoleView() : base() { }
        protected override Type ControllerType { get { return typeof(OpenRctHomeController); } }
    }

    // ---- Phase 1 Dashboards ----

    public sealed class AdCleanupController : OpenRctDashboardControllerBase
    {
        public AdCleanupController() : base() { }
        protected override string DashboardKey { get { return ToolActionKeys.DashboardAdCleanup; } }
    }

    public sealed class AdCleanupConsoleView : OpenRctDashboardViewDescriptionBase
    {
        public AdCleanupConsoleView() : base() { }
        protected override Type ControllerType { get { return typeof(AdCleanupController); } }
    }

    public sealed class LapsDashboardController : OpenRctDashboardControllerBase
    {
        public LapsDashboardController() : base() { }
        protected override string DashboardKey { get { return ToolActionKeys.DashboardLaps; } }
    }

    public sealed class LapsDashboardConsoleView : OpenRctDashboardViewDescriptionBase
    {
        public LapsDashboardConsoleView() : base() { }
        protected override Type ControllerType { get { return typeof(LapsDashboardController); } }
    }

    public sealed class SoftwareUpdatesController : OpenRctDashboardControllerBase
    {
        public SoftwareUpdatesController() : base() { }
        protected override string DashboardKey { get { return ToolActionKeys.DashboardSoftwareUpdates; } }
    }

    public sealed class SoftwareUpdatesConsoleView : OpenRctDashboardViewDescriptionBase
    {
        public SoftwareUpdatesConsoleView() : base() { }
        protected override Type ControllerType { get { return typeof(SoftwareUpdatesController); } }
    }

    // ---- Phase 2 Dashboards ----

    public sealed class BitLockerComplianceController : OpenRctDashboardControllerBase
    {
        public BitLockerComplianceController() : base() { }
        protected override string DashboardKey { get { return ToolActionKeys.DashboardBitLockerCompliance; } }
    }

    public sealed class BitLockerComplianceConsoleView : OpenRctDashboardViewDescriptionBase
    {
        public BitLockerComplianceConsoleView() : base() { }
        protected override Type ControllerType { get { return typeof(BitLockerComplianceController); } }
    }

    public sealed class ContentDistributionController : OpenRctDashboardControllerBase
    {
        public ContentDistributionController() : base() { }
        protected override string DashboardKey { get { return ToolActionKeys.DashboardContentDistribution; } }
    }

    public sealed class ContentDistributionConsoleView : OpenRctDashboardViewDescriptionBase
    {
        public ContentDistributionConsoleView() : base() { }
        protected override Type ControllerType { get { return typeof(ContentDistributionController); } }
    }

    public sealed class HardwareFirmwareAuditController : OpenRctDashboardControllerBase
    {
        public HardwareFirmwareAuditController() : base() { }
        protected override string DashboardKey { get { return ToolActionKeys.DashboardHardwareFirmwareAudit; } }
    }

    public sealed class HardwareFirmwareAuditConsoleView : OpenRctDashboardViewDescriptionBase
    {
        public HardwareFirmwareAuditConsoleView() : base() { }
        protected override Type ControllerType { get { return typeof(HardwareFirmwareAuditController); } }
    }
}
