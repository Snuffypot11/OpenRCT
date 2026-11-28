using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCMTools.Core.Models
{
    public sealed class ClientActionDefinition
    {
        public string Key { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public string[] ScheduleIds { get; }
        public bool IsStateMessageCacheCleanup { get; }

        public ClientActionDefinition(
            string key,
            string displayName,
            string description,
            string[] scheduleIds,
            bool isStateMessageCacheCleanup = false)
        {
            Key = key;
            DisplayName = displayName;
            Description = description;
            ScheduleIds = scheduleIds;
            IsStateMessageCacheCleanup = isStateMessageCacheCleanup;
        }
    }

    /// <summary>
    /// Official catalog of all 12 MECM Client Actions and their SMS_Client.TriggerSchedule GUIDs.
    /// </summary>
    public static class ClientActionRegistry
    {
        public const string AppDeploymentEval = "ClientAction.AppDeploymentEval";
        public const string DiscoveryDataCollection = "ClientAction.DiscoveryDataCollection";
        public const string FileCollection = "ClientAction.FileCollection";
        public const string HardwareInventory = "ClientAction.HardwareInventory";
        public const string MachinePolicyRetrievalAndEval = "ClientAction.MachinePolicyRetrievalAndEval";
        public const string SendUnsentStateMessages = "ClientAction.SendUnsentStateMessages";
        public const string SoftwareInventory = "ClientAction.SoftwareInventory";
        public const string SoftwareMeteringUsageReport = "ClientAction.SoftwareMeteringUsageReport";
        public const string SoftwareUpdatesDeploymentEval = "ClientAction.SoftwareUpdatesDeploymentEval";
        public const string SoftwareUpdatesScan = "ClientAction.SoftwareUpdatesScan";
        public const string StateMessageCacheCleanup = "ClientAction.StateMessageCacheCleanup";
        public const string WindowsInstallerSourceListUpdate = "ClientAction.WindowsInstallerSourceListUpdate";

        private static readonly IReadOnlyList<ClientActionDefinition> _all = new[]
        {
            new ClientActionDefinition(
                AppDeploymentEval,
                "Application Deployment Evaluation Cycle",
                "Evaluates required and available application deployments on the client.",
                new[] { "{00000000-0000-0000-0000-000000000121}" }),
            new ClientActionDefinition(
                DiscoveryDataCollection,
                "Discovery Data Collection Cycle",
                "Generates a Heartbeat Discovery Data Record (DDR) and sends it to the management point.",
                new[] { "{00000000-0000-0000-0000-000000000003}" }),
            new ClientActionDefinition(
                FileCollection,
                "File Collection Cycle",
                "Collects configured files defined in client settings.",
                new[] { "{00000000-0000-0000-0000-000000000010}" }),
            new ClientActionDefinition(
                HardwareInventory,
                "Hardware Inventory Cycle",
                "Collects hardware inventory WMI classes and reports changes to the management point.",
                new[] { "{00000000-0000-0000-0000-000000000001}" }),
            new ClientActionDefinition(
                MachinePolicyRetrievalAndEval,
                "Machine Policy Retrieval and Evaluation Cycle",
                "Requests machine policy assignments from the management point and evaluates updated policy.",
                new[]
                {
                    "{00000000-0000-0000-0000-000000000021}",
                    "{00000000-0000-0000-0000-000000000022}"
                }),
            new ClientActionDefinition(
                SendUnsentStateMessages,
                "Send Unsent State Messages",
                "Forces the client to transmit queued/unsent state messages to the management point.",
                new[] { "{00000000-0000-0000-0000-000000000111}" }),
            new ClientActionDefinition(
                SoftwareInventory,
                "Software Inventory Cycle",
                "Scans local drives for configured file types and reports software inventory.",
                new[] { "{00000000-0000-0000-0000-000000000002}" }),
            new ClientActionDefinition(
                SoftwareMeteringUsageReport,
                "Software Metering Usage Report Cycle",
                "Collects software metering usage data and sends the report to the management point.",
                new[] { "{00000000-0000-0000-0000-000000000031}" }),
            new ClientActionDefinition(
                SoftwareUpdatesDeploymentEval,
                "Software Updates Deployment Evaluation Cycle",
                "Re-evaluates deployed software updates for compliance and enforcement.",
                new[] { "{00000000-0000-0000-0000-000000000108}" }),
            new ClientActionDefinition(
                SoftwareUpdatesScan,
                "Software Updates Scan Cycle",
                "Triggers a Windows Update Agent scan against the Software Update Point (WSUS).",
                new[] { "{00000000-0000-0000-0000-000000000113}" }),
            new ClientActionDefinition(
                StateMessageCacheCleanup,
                "State Message Cache Cleanup",
                "Refreshes and resynchronizes cached client state messages in root\\ccm\\StateMsg.",
                new[] { "{00000000-0000-0000-0000-000000000111}" },
                isStateMessageCacheCleanup: true),
            new ClientActionDefinition(
                WindowsInstallerSourceListUpdate,
                "Windows Installer Source List Update Cycle",
                "Updates Windows Installer source locations for MSI packages.",
                new[] { "{00000000-0000-0000-0000-000000000032}" })
        };

        private static readonly Dictionary<string, ClientActionDefinition> _byKey =
            _all.ToDictionary(a => a.Key, StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyList<ClientActionDefinition> All => _all;

        public static bool TryGet(string key, out ClientActionDefinition? definition) =>
            _byKey.TryGetValue(key, out definition);

        public static ClientActionDefinition GetRequired(string key)
        {
            if (!_byKey.TryGetValue(key, out var def))
            {
                throw new KeyNotFoundException($"Unknown client action key '{key}'.");
            }
            return def;
        }
    }
}
