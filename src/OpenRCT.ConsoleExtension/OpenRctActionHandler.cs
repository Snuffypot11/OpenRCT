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
using OpenRCT.Core.Models;
using OpenRCT.UI;

namespace OpenRCT.ConsoleExtension
{
    /// <summary>
    /// Ensures OpenRCT.Core.dll, OpenRCT.Connectors.dll, and OpenRCT.UI.dll are resolved
    /// from the OpenRCT installation folder when loaded inside Microsoft.ConfigurationManagement.exe
    /// (including when OpenRCT.ConsoleExtension.dll is loaded from AdminConsole\bin).
    /// Written in pure C# 5.0 so Windows' built-in .NET 4.8 csc.exe compiles it cleanly on any workstation.
    /// </summary>
    internal static class OpenRctAssemblyResolver
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
                var asmDir = Path.GetDirectoryName(typeof(OpenRctAssemblyResolver).Assembly.Location);
                if (!string.IsNullOrEmpty(asmDir))
                {
                    if (File.Exists(Path.Combine(asmDir, "OpenRCT.UI.dll")))
                    {
                        return asmDir;
                    }

                    var markerFile = Path.Combine(asmDir, "OpenRCT.installdir.txt");
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
                    using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\OpenRCT"))
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
                    var pfDir = Path.Combine(pf, "OpenRCT");
                    if (File.Exists(Path.Combine(pfDir, "OpenRCT.UI.dll")))
                    {
                        return pfDir;
                    }
                }

                var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                if (!string.IsNullOrEmpty(pf86))
                {
                    var pf86Dir = Path.Combine(pf86, "OpenRCT");
                    if (File.Exists(Path.Combine(pf86Dir, "OpenRCT.UI.dll")))
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
                if (string.IsNullOrEmpty(requestedName) || !requestedName.StartsWith("OpenRCT.", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
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
    /// active SMS Provider connection in a single invocation, and dispatches into OpenRCT.UI.
    /// </summary>
    public static class OpenRctActionHandler
    {
        static OpenRctActionHandler()
        {
            OpenRctAssemblyResolver.EnsureRegistered();
        }

        public static void Execute(
            object sender,
            ScopeNode scopeNode,
            ActionDescription action,
            IResultObject selectedResultObject,
            PropertyDataUpdated dataUpdatedDelegate,
            Status status)
        {
            OpenRctAssemblyResolver.EnsureRegistered();
            try
            {
                ExecuteCore(scopeNode, action, selectedResultObject);
            }
            catch (Exception ex)
            {
                MessageBox.Show("OpenRCT failed to launch action: " + ex.Message, "OpenRCT", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    "OpenRCT could not map console action '" + (displayName ?? "(null)") + "'.",
                    "OpenRCT",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            string smsServer;
            string siteCode;
            ExtractSmsConnection(scopeNode, selectedResultObject, out smsServer, out siteCode);

            var context = BuildInvocationContext(actionKey, smsServer, siteCode, selectedResultObject);

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

        private static IEnumerable<IResultObject> EnumerateSelectedObjects(IResultObject selected)
        {
            if (selected == null) yield break;

            var listProp = selected.GetType().GetProperty("ResultObjectList", BindingFlags.Public | BindingFlags.Instance);
            if (listProp != null)
            {
                var listObj = listProp.GetValue(selected, null) as IEnumerable;
                if (listObj != null)
                {
                    bool yieldedAny = false;
                    foreach (var entry in listObj)
                    {
                        var ro = entry as IResultObject;
                        if (ro != null)
                        {
                            yieldedAny = true;
                            yield return ro;
                        }
                    }
                    if (yieldedAny) yield break;
                }
            }

            if (!string.IsNullOrEmpty(TryGetProp(selected, "Name")) ||
                !string.IsNullOrEmpty(TryGetProp(selected, "CollectionID")))
            {
                yield return selected;
                yield break;
            }

            foreach (var child in selected)
            {
                var ro = child as IResultObject;
                if (ro != null) yield return ro;
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
                if (obj != null && obj.Properties != null)
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
