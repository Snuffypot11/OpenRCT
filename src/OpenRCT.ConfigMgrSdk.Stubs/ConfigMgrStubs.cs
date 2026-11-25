using System;
using System.Collections;
using System.Collections.Generic;
using System.Windows.Forms;

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
        public string? NamedValueDictionaryServer { get; set; }
        public IDictionary<string, object> NamedValueDictionary { get; } = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
    }
}

namespace Microsoft.ConfigurationManagement.AdminConsole.Schema
{
    public class ActionDescription
    {
        public string? DisplayName { get; set; }
        public string? Description { get; set; }
        public string? Parameters { get; set; }
        public string? MnemonicDisplayName { get; set; }
    }
}

namespace Microsoft.ConfigurationManagement.AdminConsole
{
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

    public interface IConsoleView2
    {
        Control? ViewControl { get; }
        void Initialize(ScopeNode scopeNode);
        void Refresh();
        void Dispose();
    }
}
