using System;
using System.IO;
using Xunit;
using GxMcp.Gateway;

namespace GxMcp.Gateway.Tests
{
    public class WhoamiVersionTests
    {
        [Fact]
        public void DetectGeneXusVersion_ReadsVersionTxt()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "gx-version-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                File.WriteAllText(Path.Combine(tmp, "version.txt"), "18.0.4\nOther line");
                string? detected = Program.DetectGeneXusVersion(tmp);
                Assert.Equal("18.0.4", detected);
            }
            finally
            {
                Directory.Delete(tmp, recursive: true);
            }
        }

        [Fact]
        public void DetectGeneXusVersion_ReturnsNullWhenMissing()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "gx-version-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                Assert.Null(Program.DetectGeneXusVersion(tmp));
            }
            finally
            {
                Directory.Delete(tmp, recursive: true);
            }
        }

        [Fact]
        public void DetectGeneXusVersion_ReturnsNullForNullOrEmptyPath()
        {
            Assert.Null(Program.DetectGeneXusVersion(null));
            Assert.Null(Program.DetectGeneXusVersion(""));
            Assert.Null(Program.DetectGeneXusVersion("   "));
        }

        [Fact]
        public void DetectGeneXusVersion_AcceptsVersionWithCapitalV()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "gx-version-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                File.WriteAllText(Path.Combine(tmp, "Version.txt"), "18.1.0");
                Assert.Equal("18.1.0", Program.DetectGeneXusVersion(tmp));
            }
            finally
            {
                Directory.Delete(tmp, recursive: true);
            }
        }

        [Fact]
        public void BuildWhoamiPayload_ShapeIsStable_WhenNoConfig()
        {
            var payload = Program.BuildWhoamiPayload();
            Assert.NotNull(payload["connected"]);
            Assert.NotNull(payload["kb"]);
            Assert.NotNull(payload["geneXus"]);
            Assert.NotNull(payload["config"]);
            Assert.NotNull(payload["mcp"]);
            Assert.NotNull(payload["mcp"]?["serverVersion"]);
            Assert.NotNull(payload["mcp"]?["protocolVersion"]);
            Assert.NotNull(payload["geneXus"]?["supportedMajor"]);
            Assert.Equal("18", payload["geneXus"]?["supportedMajor"]?.ToString());
        }

        // v2.3.8 Task 1.2: whoami surfaces index readiness so the agent can know
        // whether it should call `lifecycle action=index` before relying on
        // `search_source` / `analyze` results.
        [Fact]
        public void Whoami_IncludesIndexBlock()
        {
            var payload = Program.BuildWhoamiPayload();
            var index = payload["index"] as Newtonsoft.Json.Linq.JObject;
            Assert.NotNull(index);

            var status = index!["status"]?.ToString();
            Assert.Contains(status, new[] { "Cold", "Reindexing", "Ready" });

            var totalObjects = index["totalObjects"];
            Assert.NotNull(totalObjects);
            Assert.Equal(Newtonsoft.Json.Linq.JTokenType.Integer, totalObjects!.Type);

            // Optional fields must at least be present as JSON keys (may be null).
            Assert.True(index.ContainsKey("lastIndexedAt"));
            Assert.True(index.ContainsKey("progress"));
            Assert.True(index.ContainsKey("etaMs"));
        }
    }
}
