using GxMcp.Worker.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GxMcp.Worker.Tests
{
    // TECHDEBT-02: VersionControlService used to hand-roll {"status":"Error",...}/
    // {"status":"Success"} strings instead of the canonical McpResponse envelope.
    // These pin the canonical shape (status:"error"/"ok" + error.code) now that
    // GetPendingChanges/Update/Commit route through McpResponse.Err/Ok.
    public class VersionControlServiceTests
    {
        private static VersionControlService BuildWithNoKbOpen()
        {
            var indexCache = new IndexCacheService();
            var kb = new KbService(indexCache);
            return new VersionControlService(kb);
        }

        [Fact]
        public void GetPendingChanges_NoKbOpen_ReturnsCanonicalError()
        {
            var svc = BuildWithNoKbOpen();
            var j = JObject.Parse(svc.GetPendingChanges());
            Assert.Equal("error", (string)j["status"]);
            Assert.Equal("NoKb", (string)j["error"]["code"]);
        }

        [Fact]
        public void Update_NoKbOpen_ReturnsCanonicalError()
        {
            var svc = BuildWithNoKbOpen();
            var j = JObject.Parse(svc.Update());
            Assert.Equal("error", (string)j["status"]);
            Assert.Equal("NoKb", (string)j["error"]["code"]);
        }

        [Fact]
        public void Commit_NoKbOpen_ReturnsCanonicalError()
        {
            var svc = BuildWithNoKbOpen();
            var j = JObject.Parse(svc.Commit("msg"));
            Assert.Equal("error", (string)j["status"]);
            Assert.Equal("NoKb", (string)j["error"]["code"]);
        }
    }
}
