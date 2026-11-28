using OpenCMTools.Core.Export;
using OpenCMTools.Core.Ipc;
using OpenCMTools.Core.Models;
using Xunit;

namespace OpenCMTools.Tests
{
    public sealed class CsvAndIpcTests
    {
        [Fact]
        public void CsvExporter_ProperlyEscapesCommasQuotesAndNewlines()
        {
            var rows = new[]
            {
                new { Name = "PC-01", Notes = "Normal" },
                new { Name = "PC,02", Notes = "Quoted \"text\"\nLine2" }
            };

            var csv = CsvExporter.Serialize(rows, new[]
            {
                new CsvColumn<dynamic>("Name", r => r.Name),
                new CsvColumn<dynamic>("Notes", r => r.Notes)
            });

            Assert.Contains("Name,Notes", csv);
            Assert.Contains("PC-01,Normal", csv);
            Assert.Contains("\"PC,02\",\"Quoted \"\"text\"\"\nLine2\"", csv);
        }

        [Fact]
        public void BatchInvocationProtocol_RoundTripsToolInvocationContext()
        {
            var ctx = new ToolInvocationContext
            {
                ActionKey = ToolActionKeys.SystemInformation,
                SmsProviderServer = "cm01.contoso.local",
                SiteCode = "PRD",
                Devices = new[]
                {
                    new TargetDevice { Name = "WKSTN-01", ResourceId = "16777220", Domain = "CONTOSO" },
                    new TargetDevice { Name = "WKSTN-02", ResourceId = "16777221", Domain = "CONTOSO" }
                }
            };

            var json = BatchInvocationProtocol.Serialize(ctx);
            var restored = BatchInvocationProtocol.Deserialize(json);

            Assert.Equal(ctx.ActionKey, restored.ActionKey);
            Assert.Equal("cm01.contoso.local", restored.SmsProviderServer);
            Assert.Equal("PRD", restored.SiteCode);
            Assert.Equal(2, restored.Devices.Length);
            Assert.Equal("WKSTN-02", restored.Devices[1].Name);

            var pipe = BatchInvocationProtocol.GetPipeName(ctx.ActionKey);
            Assert.StartsWith("OpenCMTools_Batch_ConsoleTool_SystemInformation_", pipe);
        }
    }
}
