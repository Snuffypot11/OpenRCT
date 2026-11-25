using System;
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
            string server;
            string siteCode;
            OpenRctActionHandler.ExtractSmsConnection(this.ScopeNode, null, out server, out siteCode);
            this.Content = ToolDispatcher.CreateDashboardView(DashboardKey, server, siteCode);
        }
    }

    public abstract class OpenRctDashboardViewDescriptionBase : IConsoleView2
    {
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
            return true;
        }
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
