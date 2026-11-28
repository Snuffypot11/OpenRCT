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
using OpenCMTools.Core.Models;
using OpenCMTools.UI;

namespace OpenCMTools.ConsoleExtension
{
    /// <summary>
    /// Base WPF Console View Controller matching Microsoft Configuration Manager's OverviewControllerBase pattern.
    /// Written in pure C# 5.0 so Windows' built-in .NET 4.8 csc.exe compiles it cleanly on any workstation.
    /// Never assumes compile-time ScopeNode members on OverviewControllerBase and catches all UI exceptions
    /// so Microsoft.ConfigurationManagement.exe cannot crash when clicking OpenCMTools tree nodes.
    /// </summary>
    public abstract class OpenCMToolsDashboardControllerBase : OverviewControllerBase
    {
        protected OpenCMToolsDashboardControllerBase()
            : base()
        {
            OpenCMToolsAssemblyResolver.EnsureRegistered();
        }

        protected abstract string DashboardKey { get; }

        public override void EndInit()
        {
            OpenCMToolsAssemblyResolver.EnsureRegistered();

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
                OpenCMToolsActionHandler.ExtractSmsConnection(scopeNode, null, out server, out siteCode);

                if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(siteCode))
                {
                    var cm = TryResolveConnectionManager();
                    OpenCMToolsActionHandler.ExtractFromConnectionManager(cm, ref server, ref siteCode);
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
        private static FrameworkElement CreateDashboardControl(string dashboardKey, string server, string siteCode)
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

            return OpenCMToolsDashboardViewDescriptionBase.LastScopeNode;
        }

        internal static void LogConsoleViewError(string dashboardKey, Exception ex)
        {
            try
            {
                var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenCMTools");
                Directory.CreateDirectory(logDir);
                var logPath = Path.Combine(logDir, "opencmtools.log");
                File.AppendAllText(
                    logPath,
                    "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] [ConsoleView:" + dashboardKey + "] " + ex.ToString() + Environment.NewLine);
            }
            catch
            {
            }
        }

        private static FrameworkElement BuildFallbackErrorPanel(string dashboardKey, Exception ex)
        {
            var panel = new StackPanel { Margin = new Thickness(24) };
            panel.Children.Add(new TextBlock
            {
                Text = "OpenCMTools Dashboard could not render inline in the MECM Console",
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
                Content = "Open Dashboard in OpenCMTools Window",
                Padding = new Thickness(14, 7, 14, 7),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            launchBtn.Click += delegate
            {
                try
                {
                    var dir = OpenCMToolsAssemblyResolver.ResolveInstallDirectory();
                    if (!string.IsNullOrEmpty(dir))
                    {
                        var exe = Path.Combine(dir, "OpenCMTools.App.exe");
                        if (File.Exists(exe))
                        {
                            Process.Start(exe, "--action \"" + dashboardKey + "\"");
                            return;
                        }
                    }
                    MessageBox.Show("Could not locate OpenCMTools.App.exe.", "OpenCMTools", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                catch (Exception launchEx)
                {
                    MessageBox.Show("Failed to launch OpenCMTools.App.exe: " + launchEx.Message, "OpenCMTools", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
            panel.Children.Add(launchBtn);
            return panel;
        }
    }

    // BEGIN_VIEW_DESCRIPTION_BASE
    public abstract class OpenCMToolsDashboardViewDescriptionBase : IConsoleView2
    {
        internal static ScopeNode LastScopeNode;

        static OpenCMToolsDashboardViewDescriptionBase()
        {
            OpenCMToolsAssemblyResolver.EnsureRegistered();
        }

        protected OpenCMToolsDashboardViewDescriptionBase()
        {
            OpenCMToolsAssemblyResolver.EnsureRegistered();
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
                OpenCMToolsAssemblyResolver.EnsureRegistered();
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

    public sealed class OpenCMToolsHomeController : OpenCMToolsDashboardControllerBase
    {
        public OpenCMToolsHomeController() : base() { }
        protected override string DashboardKey { get { return ToolActionKeys.DashboardHome; } }
    }

    public sealed class OpenCMToolsHomeConsoleView : OpenCMToolsDashboardViewDescriptionBase
    {
        public OpenCMToolsHomeConsoleView() : base() { }
        protected override Type ControllerType { get { return typeof(OpenCMToolsHomeController); } }
    }

    // ---- Phase 1 Dashboards ----

    public sealed class AdCleanupController : OpenCMToolsDashboardControllerBase
    {
        public AdCleanupController() : base() { }
        protected override string DashboardKey { get { return ToolActionKeys.DashboardAdCleanup; } }
    }

    public sealed class AdCleanupConsoleView : OpenCMToolsDashboardViewDescriptionBase
    {
        public AdCleanupConsoleView() : base() { }
        protected override Type ControllerType { get { return typeof(AdCleanupController); } }
    }

    public sealed class LapsDashboardController : OpenCMToolsDashboardControllerBase
    {
        public LapsDashboardController() : base() { }
        protected override string DashboardKey { get { return ToolActionKeys.DashboardLaps; } }
    }

    public sealed class LapsDashboardConsoleView : OpenCMToolsDashboardViewDescriptionBase
    {
        public LapsDashboardConsoleView() : base() { }
        protected override Type ControllerType { get { return typeof(LapsDashboardController); } }
    }

    public sealed class SoftwareUpdatesController : OpenCMToolsDashboardControllerBase
    {
        public SoftwareUpdatesController() : base() { }
        protected override string DashboardKey { get { return ToolActionKeys.DashboardSoftwareUpdates; } }
    }

    public sealed class SoftwareUpdatesConsoleView : OpenCMToolsDashboardViewDescriptionBase
    {
        public SoftwareUpdatesConsoleView() : base() { }
        protected override Type ControllerType { get { return typeof(SoftwareUpdatesController); } }
    }

    // ---- Phase 2 Dashboards ----

    public sealed class BitLockerComplianceController : OpenCMToolsDashboardControllerBase
    {
        public BitLockerComplianceController() : base() { }
        protected override string DashboardKey { get { return ToolActionKeys.DashboardBitLockerCompliance; } }
    }

    public sealed class BitLockerComplianceConsoleView : OpenCMToolsDashboardViewDescriptionBase
    {
        public BitLockerComplianceConsoleView() : base() { }
        protected override Type ControllerType { get { return typeof(BitLockerComplianceController); } }
    }

    public sealed class ContentDistributionController : OpenCMToolsDashboardControllerBase
    {
        public ContentDistributionController() : base() { }
        protected override string DashboardKey { get { return ToolActionKeys.DashboardContentDistribution; } }
    }

    public sealed class ContentDistributionConsoleView : OpenCMToolsDashboardViewDescriptionBase
    {
        public ContentDistributionConsoleView() : base() { }
        protected override Type ControllerType { get { return typeof(ContentDistributionController); } }
    }

    public sealed class HardwareFirmwareAuditController : OpenCMToolsDashboardControllerBase
    {
        public HardwareFirmwareAuditController() : base() { }
        protected override string DashboardKey { get { return ToolActionKeys.DashboardHardwareFirmwareAudit; } }
    }

    public sealed class HardwareFirmwareAuditConsoleView : OpenCMToolsDashboardViewDescriptionBase
    {
        public HardwareFirmwareAuditConsoleView() : base() { }
        protected override Type ControllerType { get { return typeof(HardwareFirmwareAuditController); } }
    }
}
