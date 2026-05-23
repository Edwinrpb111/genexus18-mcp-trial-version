using GxMcp.Worker.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GxMcp.Worker.Tests
{
    /// <summary>
    /// Item 43 (friction 2026-05-22) — DDL diff/preview pre-reorg. Stub
    /// implementation (no SDK plan API surfaced on net48). Test asserts the
    /// envelope shape so the schema contract is stable for the LLM consumer.
    /// </summary>
    public class ReorgPreviewTests
    {
        [Fact]
        public void ReorgPreview_StubReturnsExpectedShape()
        {
            var svc = new BuildService();
            string json = svc.ReorgPreview("MyTrn");
            var jo = JObject.Parse(json);
            Assert.Equal("MyTrn", jo["target"]?.ToString());
            Assert.NotNull(jo["ddl"] as JArray);
            var summary = jo["summary"] as JObject;
            Assert.NotNull(summary);
            Assert.Equal(0, summary!["tables_added"]?.ToObject<int>());
            Assert.Equal(0, summary["tables_changed"]?.ToObject<int>());
            Assert.Equal(0, summary["columns_added"]?.ToObject<int>());
            Assert.Equal(0, summary["columns_dropped"]?.ToObject<int>());
            Assert.False(string.IsNullOrEmpty(jo["note"]?.ToString()));
        }
    }
}
