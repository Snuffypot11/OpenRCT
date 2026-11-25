using System.Windows.Forms;
using System.Windows.Forms.Integration;
using Microsoft.ConfigurationManagement.AdminConsole;
using OpenRCT.Core.Models;
using OpenRCT.UI;

namespace OpenRCT.ConsoleExtension
{
    /// <summary>
    /// Base IConsoleView2 implementation that embeds an OpenRCT WPF Dashboard UserControl
    /// directly inside the MECM Admin Console results pane using WindowsFormsIntegration.ElementHost.
    /// </summary>
    public abstract class WpfDashboardConsoleViewBase : IConsoleView2
    {
        private ElementHost? _host;
        private ScopeNode? _scopeNode;
        protected abstract string DashboardKey { get; }

        public Control? ViewControl => _host;

        public void Initialize(ScopeNode scopeNode)
        {
            _scopeNode = scopeNode;
            BuildView();
        }

        public void Refresh() => BuildView();

        private void BuildView()
        {
            var (server, siteCode) = OpenRctActionHandler.ExtractSmsConnection(_scopeNode, null);
            var wpfView = ToolDispatcher.CreateDashboardView(DashboardKey, server, siteCode);
            if (_host == null)
            {
                _host = new ElementHost { Dock = DockStyle.Fill };
            }
            _host.Child = wpfView;
        }

        public void Dispose()
        {
            _host?.Dispose();
            _host = null;
        }
    }

    public sealed class AdCleanupConsoleView : WpfDashboardConsoleViewBase
    {
        protected override string DashboardKey => ToolActionKeys.DashboardAdCleanup;
    }

    public sealed class LapsDashboardConsoleView : WpfDashboardConsoleViewBase
    {
        protected override string DashboardKey => ToolActionKeys.DashboardLaps;
    }

    public sealed class SoftwareUpdatesConsoleView : WpfDashboardConsoleViewBase
    {
        protected override string DashboardKey => ToolActionKeys.DashboardSoftwareUpdates;
    }

    public sealed class BitLockerComplianceConsoleView : WpfDashboardConsoleViewBase
    {
        protected override string DashboardKey => ToolActionKeys.DashboardBitLockerCompliance;
    }

    public sealed class ContentDistributionConsoleView : WpfDashboardConsoleViewBase
    {
        protected override string DashboardKey => ToolActionKeys.DashboardContentDistribution;
    }

    public sealed class HardwareFirmwareAuditConsoleView : WpfDashboardConsoleViewBase
    {
        protected override string DashboardKey => ToolActionKeys.DashboardHardwareFirmwareAudit;
    }
}
