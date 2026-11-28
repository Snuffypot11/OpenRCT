using System;
using System.Collections;
using System.Collections.Generic;
using System.Xml;

namespace Microsoft.ConfigurationManagement.ManagementProvider
{
    public interface IPropertyDataItem
    {
        string StringValue { get; set; }
        int IntegerValue { get; set; }
        object ObjectValue { get; set; }
    }

    public interface IResultObject : IEnumerable
    {
        IDictionary<string, IPropertyDataItem> Properties { get; }
        ConnectionManagerBase? ConnectionManager { get; }
    }

    public class ResultObjectBase : IResultObject
    {
        public IDictionary<string, IPropertyDataItem> Properties { get; } = new Dictionary<string, IPropertyDataItem>(StringComparer.OrdinalIgnoreCase);
        public ConnectionManagerBase? ConnectionManager { get; set; }
        public ArrayList? ResultObjectList { get; set; }
        public IEnumerator GetEnumerator() => (ResultObjectList ?? new ArrayList()).GetEnumerator();
    }

    public class ConnectionManagerBase
    {
        public IDictionary<string, object> NamedValueDictionary { get; } = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
    }
}

namespace Microsoft.ConfigurationManagement.AdminConsole.Schema
{
    public class ActionDescription
    {
        public string? DisplayName { get; set; }
        public string? Description { get; set; }
        public string? MnemonicDisplayName { get; set; }
    }

    public class AssemblyDescription
    {
        public string? Assembly { get; set; }
        public string? Type { get; set; }
    }

    public class ViewAssemblyDescription
    {
        public string? Assembly { get; set; }
        public string? Type { get; set; }
    }
}

namespace Microsoft.ConfigurationManagement.AdminConsole
{
    using Microsoft.ConfigurationManagement.AdminConsole.Schema;
    using Microsoft.ConfigurationManagement.ManagementProvider;

    public delegate void PropertyDataUpdated(object sender, EventArgs e);

    public class Status { }

    public class RootConnectionNode
    {
        public ConnectionManagerBase? GetConnectionManagerInstance(string name) => null;
    }

    public class ScopeNode
    {
        public RootConnectionNode? RootConnectionNode { get; set; }
    }

    public class ConsoleParentNode : ScopeNode { }

    public class Overview { }

    public class OverviewControllerBase
    {
        public object? Content { get; set; }
        public ScopeNode? ScopeNode { get; set; }
        public virtual void EndInit() { }
    }

    public interface IConsoleView
    {
        Type TypeOfView { get; }
        bool TryConfigure(ref XmlElement persistedConfigurationData);
        bool TryInitialize(ScopeNode scopeNode, AssemblyDescription resourceAssembly, ViewAssemblyDescription viewAssemblyDescription);
    }

    public interface IConsoleView2 : IConsoleView
    {
        Type TypeOfViewController { get; }
    }
}
