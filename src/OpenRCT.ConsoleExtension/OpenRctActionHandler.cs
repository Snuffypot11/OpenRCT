using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using Microsoft.ConfigurationManagement.AdminConsole;
using Microsoft.ConfigurationManagement.AdminConsole.Schema;
using Microsoft.ConfigurationManagement.ManagementProvider;
using OpenRCT.Core.Models;
using OpenRCT.UI;

namespace OpenRCT.ConsoleExtension
{
    /// <summary>
    /// MECM Console AssemblyType action handler.
    /// Captures single OR multiple selected devices (or a Device Collection) plus the console's
    /// active SMS Provider connection in a single invocation, and dispatches into OpenRCT.UI.
    /// </summary>
    public static class OpenRctActionHandler
    {
        public static void Execute(
            object sender,
            ScopeNode scopeNode,
            ActionDescription action,
            IResultObject selectedResultObject,
            PropertyDataUpdated dataUpdatedDelegate,
            Status status)
        {
            try
            {
                var actionKey = action?.Parameters?.Trim();
                if (string.IsNullOrWhiteSpace(actionKey))
                {
                    MessageBox.Show("OpenRCT action XML is missing <Parameters> action key.", "OpenRCT", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var (smsServer, siteCode) = ExtractSmsConnection(scopeNode, selectedResultObject);
                var context = BuildInvocationContext(actionKey!, smsServer, siteCode, selectedResultObject);

                if (Application.Current?.Dispatcher != null)
                {
                    Application.Current.Dispatcher.Invoke(() => ToolDispatcher.Dispatch(context));
                }
                else
                {
                    ToolDispatcher.Dispatch(context);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"OpenRCT failed to launch action: {ex.Message}", "OpenRCT", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static ToolInvocationContext BuildInvocationContext(
            string actionKey,
            string? smsServer,
            string? siteCode,
            IResultObject? selected)
        {
            var devices = new List<TargetDevice>();
            string? collectionId = null;
            string? collectionName = null;

            foreach (var item in EnumerateSelectedObjects(selected))
            {
                var colId = TryGetProp(item, "CollectionID");
                var resourceId = TryGetProp(item, "ResourceID");
                var name = TryGetProp(item, "Name");

                // Device Collection node has CollectionID without ResourceID
                if (!string.IsNullOrWhiteSpace(colId) && string.IsNullOrWhiteSpace(resourceId))
                {
                    collectionId = colId;
                    collectionName = name;
                    break;
                }

                if (!string.IsNullOrWhiteSpace(name))
                {
                    var domain = TryGetProp(item, "Domain") ?? TryGetProp(item, "ResourceDomainORWorkgroup");
                    devices.Add(new TargetDevice
                    {
                        Name = name!,
                        Fqdn = TryGetProp(item, "FQDN") ?? TryGetProp(item, "FullDomainName"),
                        ResourceId = resourceId,
                        Domain = domain,
                        SiteCode = siteCode,
                        SmsProviderServer = smsServer
                    });
                }
            }

            return new ToolInvocationContext
            {
                ActionKey = actionKey,
                SmsProviderServer = smsServer,
                SiteCode = siteCode,
                CollectionId = collectionId,
                CollectionName = collectionName,
                Devices = devices.ToArray()
            };
        }

        private static IEnumerable<IResultObject> EnumerateSelectedObjects(IResultObject? selected)
        {
            if (selected == null) yield break;

            // Multi-select in MECM Console populates ResultObjectList or enumerates child IResultObjects
            var listProp = selected.GetType().GetProperty("ResultObjectList", BindingFlags.Public | BindingFlags.Instance);
            if (listProp?.GetValue(selected) is IEnumerable listObj)
            {
                bool yieldedAny = false;
                foreach (var entry in listObj)
                {
                    if (entry is IResultObject ro)
                    {
                        yieldedAny = true;
                        yield return ro;
                    }
                }
                if (yieldedAny) yield break;
            }

            // Fallback: check if selected itself has Name/CollectionID properties (single selection)
            if (!string.IsNullOrWhiteSpace(TryGetProp(selected, "Name")) ||
                !string.IsNullOrWhiteSpace(TryGetProp(selected, "CollectionID")))
            {
                yield return selected;
                yield break;
            }

            foreach (var child in selected)
            {
                if (child is IResultObject ro) yield return ro;
            }
        }

        internal static (string? Server, string? SiteCode) ExtractSmsConnection(ScopeNode? scopeNode, IResultObject? selected)
        {
            string? server = null;
            string? siteCode = null;

            try
            {
                if (selected != null)
                {
                    server = TryGetProp(selected, "__Server");
                    var ns = TryGetProp(selected, "__Namespace");
                    siteCode = ParseSiteCodeFromNamespace(ns);
                }

                var connMgr = (scopeNode as ConsoleParentNode)?.RootConnectionNode?.GetConnectionManagerInstance("WQL")
                              ?? selected?.ConnectionManager;

                if (connMgr != null && connMgr.NamedValueDictionary != null)
                {
                    if (string.IsNullOrWhiteSpace(server) && connMgr.NamedValueDictionary.TryGetValue("ServerName", out var srvObj))
                    {
                        server = srvObj?.ToString();
                    }
                    if (string.IsNullOrWhiteSpace(siteCode) && connMgr.NamedValueDictionary.TryGetValue("SiteCode", out var siteObj))
                    {
                        siteCode = siteObj?.ToString();
                    }
                }
            }
            catch { }

            return (server, siteCode);
        }

        private static string? TryGetProp(IResultObject obj, string propName)
        {
            try
            {
                if (obj.Properties != null && obj.Properties.TryGetValue(propName, out var item))
                {
                    return item?.StringValue;
                }
            }
            catch { }
            return null;
        }

        private static string? ParseSiteCodeFromNamespace(string? ns)
        {
            if (string.IsNullOrWhiteSpace(ns)) return null;
            const string marker = "site_";
            var idx = ns!.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            return idx >= 0 && idx + marker.Length < ns.Length
                ? ns.Substring(idx + marker.Length).Trim()
                : null;
        }
    }
}
