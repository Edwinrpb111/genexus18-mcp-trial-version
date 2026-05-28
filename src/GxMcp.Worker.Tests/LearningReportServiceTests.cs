using System;
using System.IO;
using GxMcp.Worker.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GxMcp.Worker.Tests
{
    public class LearningReportServiceTests
    {
        [Fact]
        public void ReportCore_NoFile_ReturnsEmptyAggregate()
        {
            string tmpKb = Path.Combine(Path.GetTempPath(), "gxmcp_learn_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tmpKb);
                var json = JObject.Parse(LearningReportService.ReportCore(tmpKb, null, null));
                Assert.Equal("ok", (string)json["status"]);
                Assert.Equal("LearningReportGenerated", (string)json["code"]);
                Assert.Equal(0, (int)json["result"]["totalEntries"]);
                Assert.Empty((JArray)json["result"]["byTool"]);
            }
            finally { try { Directory.Delete(tmpKb, recursive: true); } catch { } }
        }

        [Fact]
        public void ReportCore_AggregatesTools_AndExtractsParenCodes()
        {
            string tmpKb = Path.Combine(Path.GetTempPath(), "gxmcp_learn_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tmpKb);
                // Write friction.jsonl via the public Append API.
                FrictionLogService.AppendCore(tmpKb, "genexus_edit", "Edit failed (PatchNoMatch): unable to apply", "warn");
                FrictionLogService.AppendCore(tmpKb, "genexus_edit", "Another (PatchNoMatch) issue", "warn");
                FrictionLogService.AppendCore(tmpKb, "genexus_read", "Object missing (NotFound)", "error");

                var json = JObject.Parse(LearningReportService.ReportCore(tmpKb, null, null));
                Assert.Equal("ok", (string)json["status"]);
                Assert.Equal("LearningReportGenerated", (string)json["code"]);
                Assert.Equal(3, (int)json["result"]["totalEntries"]);

                var byTool = (JArray)json["result"]["byTool"];
                Assert.Equal("genexus_edit", (string)byTool[0]["tool"]);
                Assert.Equal(2, (int)byTool[0]["count"]);

                var byCode = (JArray)json["result"]["byCode"];
                // PatchNoMatch (2) ranked above NotFound (1).
                Assert.Equal("PatchNoMatch", (string)byCode[0]["code"]);
                Assert.Equal(2, (int)byCode[0]["count"]);

                var sev = (JObject)json["result"]["severityHistogram"];
                Assert.Equal(2, (int)sev["warn"]);
                Assert.Equal(1, (int)sev["error"]);
            }
            finally { try { Directory.Delete(tmpKb, recursive: true); } catch { } }
        }

        [Fact]
        public void ReportCore_SinceFilter_ExcludesOlderEntries()
        {
            string tmpKb = Path.Combine(Path.GetTempPath(), "gxmcp_learn_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tmpKb);
                string filePath = Path.Combine(tmpKb, ".gx", "friction.jsonl");
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                // Hand-roll entries with controlled timestamps.
                var old = new JObject { ["atUtc"] = "2020-01-01T00:00:00Z", ["tool"] = "old", ["message"] = "x", ["severity"] = "info" };
                var fresh = new JObject { ["atUtc"] = DateTime.UtcNow.ToString("o"), ["tool"] = "new", ["message"] = "y", ["severity"] = "info" };
                File.WriteAllText(filePath, old.ToString(Newtonsoft.Json.Formatting.None) + "\n" + fresh.ToString(Newtonsoft.Json.Formatting.None) + "\n");

                var json = JObject.Parse(LearningReportService.ReportCore(tmpKb, "2024-01-01T00:00:00Z", null));
                Assert.Equal(1, (int)json["result"]["totalEntries"]);
                var byTool = (JArray)json["result"]["byTool"];
                Assert.Equal("new", (string)byTool[0]["tool"]);
            }
            finally { try { Directory.Delete(tmpKb, recursive: true); } catch { } }
        }
    }
}
