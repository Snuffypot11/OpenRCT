using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using OpenRCT.Core.Models;

namespace OpenRCT.Core.Ipc
{
    /// <summary>
    /// Serialization helpers for passing ToolInvocationContext between MECM Executable actions
    /// and a single-instance OpenRCT.App named-pipe multi-select aggregator.
    /// </summary>
    public static class BatchInvocationProtocol
    {
        public static string GetPipeName(string actionKey)
        {
            var safe = new StringBuilder("OpenRCT_Batch_");
            foreach (var ch in actionKey ?? "default")
            {
                safe.Append(char.IsLetterOrDigit(ch) ? ch : '_');
            }
            safe.Append('_');
            safe.Append(Environment.UserName);
            return safe.ToString();
        }

        public static string Serialize(ToolInvocationContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var serializer = new DataContractJsonSerializer(typeof(ToolInvocationContext));
            using var ms = new MemoryStream();
            serializer.WriteObject(ms, context);
            return Encoding.UTF8.GetString(ms.ToArray());
        }

        public static ToolInvocationContext Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException("JSON payload cannot be empty.", nameof(json));
            }

            var serializer = new DataContractJsonSerializer(typeof(ToolInvocationContext));
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
            return serializer.ReadObject(ms) as ToolInvocationContext
                ?? throw new InvalidDataException("Failed to deserialize ToolInvocationContext.");
        }
    }
}
