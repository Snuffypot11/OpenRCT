using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using Microsoft.ConfigurationManagement.AdminConsole;
using Microsoft.ConfigurationManagement.AdminConsole.Schema;
using Microsoft.ConfigurationManagement.ManagementProvider;
using Microsoft.Win32;
using OpenCMTools.Core.Models;
using OpenCMTools.UI;

namespace OpenCMTools.ConsoleExtension
{
    /// <summary>
    /// Ensures OpenCMTools.Core.dll, OpenCMTools.Connectors.dll, and OpenCMTools.UI.dll are resolved
    /// from the OpenCMTools installation folder when loaded inside Microsoft.ConfigurationManagement.exe
    /// (including when OpenCMTools.ConsoleExtension.dll is loaded from AdminConsole\bin).
    /// Written in pure C# 5.0 so Windows' built-in .NET 4.8 csc.exe compiles it cleanly on any workstation.
    /// </summary>
    internal static class OpenCMToolsAssemblyResolver
    {
        private static int _registered;

        public static void EnsureRegistered()
        {
            if (System.Threading.Interlocked.Exchange(ref _registered, 1) != 0)
            {
                return;
            }

            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
        }

        internal static string ResolveInstallDirectory()
        {
            try
            {
                var asmDir = Path.GetDirectoryName(typeof(OpenCMToolsAssemblyResolver).Assembly.Location);
                if (!string.IsNullOrEmpty(asmDir))
                {
                    if (File.Exists(Path.Combine(asmDir, "OpenCMTools.UI.dll")))
                    {
                        return asmDir;
                    }

                    var markerFile = Path.Combine(asmDir, "OpenCMTools.installdir.txt");
                    if (File.Exists(markerFile))
                    {
                        var markerDir = File.ReadAllText(markerFile).Trim().Trim('"');
                        if (!string.IsNullOrEmpty(markerDir) && Directory.Exists(markerDir))
                        {
                            return markerDir;
                        }
                    }
                }

                try
                {
                    using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\OpenCMTools"))
                    {
                        if (key != null)
                        {
                            var regDir = key.GetValue("InstallDir") as string;
                            if (!string.IsNullOrEmpty(regDir) && Directory.Exists(regDir))
                            {
                                return regDir;
                            }
                        }
                    }
                }
                catch
                {
                }

                var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                if (!string.IsNullOrEmpty(pf))
                {
                    var pfDir = Path.Combine(pf, "OpenCMTools");
                    if (File.Exists(Path.Combine(pfDir, "OpenCMTools.UI.dll")))
                    {
                        return pfDir;
                    }
                }

                var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                if (!string.IsNullOrEmpty(pf86))
                {
                    var pf86Dir = Path.Combine(pf86, "OpenCMTools");
                    if (File.Exists(Path.Combine(pf86Dir, "OpenCMTools.UI.dll")))
                    {
                        return pf86Dir;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            try
            {
                var requestedName = new AssemblyName(args.Name).Name;
                if (string.IsNullOrEmpty(requestedName) || !requestedName.StartsWith("OpenCMTools.", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        if (string.Equals(loaded.GetName().Name, requestedName, StringComparison.OrdinalIgnoreCase))
                        {
                            return loaded;
                        }
                    }
                    catch
                    {
                    }
                }

                var dir = ResolveInstallDirectory();
                if (string.IsNullOrEmpty(dir))
                {
                    return null;
                }

                var candidate = Path.Combine(dir, requestedName + ".dll");
                if (File.Exists(candidate))
                {
                    return Assembly.LoadFrom(candidate);
                }
            }
            catch
            {
            }

            return null;
        }
    }

    /// <summary>
    /// MECM Console AssemblyType action handler (pure C# 5.0).
    /// Captures single OR multiple selected devices (or a Device Collection) plus the console's
    /// active SMS Provider connection in a single invocation, and dispatches into OpenCMTools.UI.
    /// </summary>
    public static class OpenCMToolsActionHandler
    {
        static OpenCMToolsActionHandler()
        {
            OpenCMToolsAssemblyResolver.EnsureRegistered();
        }

        public static void Execute(
            object sender,
            ScopeNode scopeNode,
            ActionDescription action,
            IResultObject selectedResultObject,
            PropertyDataUpdated dataUpdatedDelegate,
            Status status)
        {
            OpenCMToolsAssemblyResolver.EnsureRegistered();
            try
            {
                ExecuteCore(scopeNode, action, selectedResultObject);
            }
            catch (Exception ex)
            {
                MessageBox.Show("OpenCMTools failed to launch action: " + ex.Message, "OpenCMTools", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ExecuteCore(
            ScopeNode scopeNode,
            ActionDescription action,
            IResultObject selectedResultObject)
        {
            var displayName = action != null ? action.DisplayName : null;
            var actionKey = ToolActionKeys.ResolveByDisplayName(displayName);
            if (string.IsNullOrEmpty(actionKey))
            {
                MessageBox.Show(
                    "OpenCMTools could not map console action '" + (displayName ?? "(null)") + "'.",
                    "OpenCMTools",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            string smsServer;
            string siteCode;
            ExtractSmsConnection(scopeNode, selectedResultObject, out smsServer, out siteCode);

            var context = BuildInvocationContext(actionKey, smsServer, siteCode, scopeNode, selectedResultObject);

            if (Application.Current != null && Application.Current.Dispatcher != null)
            {
                Application.Current.Dispatcher.Invoke(new Action(delegate
                {
                    ToolDispatcher.Dispatch(context);
                }));
            }
            else
            {
                ToolDispatcher.Dispatch(context);
            }
        }

        private static ToolInvocationContext BuildInvocationContext(
            string actionKey,
            string smsServer,
            string siteCode,
            ScopeNode scopeNode,
            IResultObject selected)
        {
            var devices = new List<TargetDevice>();
            string collectionId = null;
            string collectionName = null;

            foreach (var item in EnumerateSelectedObjects(selected))
            {
                var colId = TryGetProp(item, "CollectionID");
                var resourceId = TryGetProp(item, "ResourceID");
                var name = TryGetProp(item, "Name");

                if (!string.IsNullOrEmpty(colId) && string.IsNullOrEmpty(resourceId))
                {
                    collectionId = colId;
                    collectionName = name;
                    break;
                }

                if (!string.IsNullOrEmpty(name))
                {
                    var domain = TryGetProp(item, "Domain");
                    if (string.IsNullOrEmpty(domain)) domain = TryGetProp(item, "ResourceDomainORWorkgroup");

                    var fqdn = TryGetProp(item, "FQDN");
                    if (string.IsNullOrEmpty(fqdn)) fqdn = TryGetProp(item, "FullDomainName");

                    devices.Add(new TargetDevice
                    {
                        Name = name,
                        Fqdn = fqdn,
                        ResourceId = resourceId,
                        Domain = domain,
                        SiteCode = siteCode,
                        SmsProviderServer = smsServer
                    });
                }
            }

            // When right-clicking a pinned/opened Device Collection node under Devices in the left tree,
            // MECM may pass the collection via scopeNode rather than selectedResultObject.
            if (string.IsNullOrEmpty(collectionId) && devices.Count == 0 && scopeNode != null)
            {
                ExtractCollectionFromScopeNode(scopeNode, out collectionId, out collectionName);
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

        private static void ExtractCollectionFromScopeNode(ScopeNode scopeNode, out string collectionId, out string collectionName)
        {
            collectionId = null;
            collectionName = null;
            if (scopeNode == null) return;

            try
            {
                var t = scopeNode.GetType();
                var candidateProps = new[] { "Tag", "ResultObject", "ObjectInstance", "Instance", "SelectedObject" };
                for (int i = 0; i < candidateProps.Length; i++)
                {
                    var prop = t.GetProperty(candidateProps[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (prop == null) continue;
                    var val = prop.GetValue(scopeNode, null) as IResultObject;
                    if (val != null)
                    {
                        var colId = TryGetProp(val, "CollectionID");
                        if (!string.IsNullOrEmpty(colId))
                        {
                            collectionId = colId;
                            collectionName = TryGetProp(val, "Name");
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(collectionId))
                {
                    var directColProp = t.GetProperty("CollectionID", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (directColProp != null)
                    {
                        var directVal = directColProp.GetValue(scopeNode, null);
                        if (directVal != null) collectionId = directVal.ToString();
                    }
                }

                if (string.IsNullOrEmpty(collectionName))
                {
                    var dispProp = t.GetProperty("DisplayName", BindingFlags.Public | BindingFlags.Instance);
                    if (dispProp != null)
                    {
                        var dispVal = dispProp.GetValue(scopeNode, null);
                        if (dispVal != null) collectionName = dispVal.ToString();
                    }
                }
            }
            catch
            {
            }
        }

        private static IEnumerable<IResultObject> EnumerateSelectedObjects(IResultObject selected)
        {
            if (selected == null) yield break;

            var collected = new List<IResultObject>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Check ResultObjectList (avoid GenericsArray which throws InvalidCastException on single WqlResultObject)
            try
            {
                var listProp = selected.GetType().GetProperty("ResultObjectList", BindingFlags.Public | BindingFlags.Instance);
                if (listProp != null)
                {
                    var listObj = listProp.GetValue(selected, null) as IEnumerable;
                    if (listObj != null)
                    {
                        foreach (var entry in listObj)
                        {
                            var ro = entry as IResultObject;
                            if (ro != null) AddUnique(collected, seen, ro);
                        }
                    }
                }
            }
            catch
            {
            }

            // 2. Enumerate IResultObject directly (standard multi-select IEnumerable in ConfigMgr)
            if (collected.Count == 0)
            {
                try
                {
                    foreach (var child in selected)
                    {
                        var ro = child as IResultObject;
                        if (ro != null) AddUnique(collected, seen, ro);
                    }
                }
                catch
                {
                }
            }

            // 3. Fallback for single-item result object
            if (collected.Count == 0)
            {
                if (!string.IsNullOrEmpty(TryGetProp(selected, "Name")) ||
                    !string.IsNullOrEmpty(TryGetProp(selected, "CollectionID")))
                {
                    collected.Add(selected);
                }
            }

            foreach (var item in collected)
            {
                yield return item;
            }
        }

        private static void AddUnique(List<IResultObject> target, HashSet<string> seen, IResultObject item)
        {
            var resId = TryGetProp(item, "ResourceID");
            var colId = TryGetProp(item, "CollectionID");
            var name = TryGetProp(item, "Name");
            var key = !string.IsNullOrEmpty(resId)
                ? "R:" + resId
                : (!string.IsNullOrEmpty(colId) ? "C:" + colId : "N:" + (name ?? string.Empty));
            if (key.Length > 2 && seen.Add(key))
            {
                target.Add(item);
            }
        }

        internal static void ExtractSmsConnection(ScopeNode scopeNode, IResultObject selected, out string server, out string siteCode)
        {
            server = null;
            siteCode = null;

            try
            {
                if (selected != null)
                {
                    server = TryGetProp(selected, "__Server");
                    var ns = TryGetProp(selected, "__Namespace");
                    siteCode = ParseSiteCodeFromNamespace(ns);
                }

                ConnectionManagerBase connMgr = null;
                var parentNode = scopeNode as ConsoleParentNode;
                if (parentNode != null && parentNode.RootConnectionNode != null)
                {
                    connMgr = parentNode.RootConnectionNode.GetConnectionManagerInstance("WQL");
                }
                if (connMgr == null && selected != null)
                {
                    connMgr = selected.ConnectionManager;
                }

                ExtractFromConnectionManager(connMgr, ref server, ref siteCode);
            }
            catch
            {
            }
        }

        internal static void ExtractFromConnectionManager(ConnectionManagerBase connMgr, ref string server, ref string siteCode)
        {
            try
            {
                if (connMgr != null && connMgr.NamedValueDictionary != null)
                {
                    object srvObj;
                    if (string.IsNullOrEmpty(server) && connMgr.NamedValueDictionary.TryGetValue("ServerName", out srvObj) && srvObj != null)
                    {
                        server = srvObj.ToString();
                    }
                    object siteObj;
                    if (string.IsNullOrEmpty(siteCode) && connMgr.NamedValueDictionary.TryGetValue("SiteCode", out siteObj) && siteObj != null)
                    {
                        siteCode = siteObj.ToString();
                    }
                }
            }
            catch
            {
            }
        }

        private static string TryGetProp(IResultObject obj, string propName)
        {
            try
            {
                if (obj == null) return null;

                // Check PropertyList dictionary first (present on real ConfigMgr WqlResultObjectBase)
                // so missing optional properties do not log ManagementException noise to SmsAdminUI.log.
                var plProp = obj.GetType().GetProperty("PropertyList", BindingFlags.Public | BindingFlags.Instance);
                if (plProp != null)
                {
                    var dict = plProp.GetValue(obj, null) as IDictionary;
                    if (dict != null)
                    {
                        if (dict.Contains(propName))
                        {
                            var val = dict[propName];
                            if (val != null) return val.ToString();
                        }
                        return null;
                    }
                }

                if (obj.Properties != null)
                {
                    var item = obj.Properties[propName];
                    if (item != null) return item.StringValue;
                }
            }
            catch
            {
            }
            return null;
        }

        private static string ParseSiteCodeFromNamespace(string ns)
        {
            if (string.IsNullOrEmpty(ns)) return null;
            const string marker = "site_";
            var idx = ns.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0 && idx + marker.Length < ns.Length)
            {
                return ns.Substring(idx + marker.Length).Trim();
            }
            return null;
        }
    }
}
