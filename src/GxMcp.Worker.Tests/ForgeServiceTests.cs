using GxMcp.Worker.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GxMcp.Worker.Tests
{
    // TECHDEBT-02: ForgeService.Scaffold used to hand-roll
    // {"status":"Success",...}/{"status":"Error",...} strings. These pin the
    // canonical McpResponse envelope now that Scaffold routes error paths
    // through McpResponse.Err (success-path shape is exercised by the live
    // worker integration suite, which needs a real SDK KB).
    public class ForgeServiceTests
    {
        [Fact]
        public void Scaffold_UnsupportedType_ReturnsCanonicalError()
        {
            var indexCache = new IndexCacheService();
            var kb = new KbService(indexCache);
            var svc = new ForgeService(kb);

            var j = JObject.Parse(svc.Scaffold("NotARealType", "Foo", null));
            Assert.Equal("error", (string)j["status"]);
            Assert.Equal("ScaffoldTypeUnsupported", (string)j["error"]["code"]);
        }

        [Fact]
        public void Scaffold_NoKbOpen_ReturnsCanonicalError()
        {
            var indexCache = new IndexCacheService();
            var kb = new KbService(indexCache);
            var svc = new ForgeService(kb);

            // No KB open: kb.DesignModel access inside the try block throws,
            // which is caught and surfaced as a canonical ScaffoldFailed error.
            var j = JObject.Parse(svc.Scaffold("Procedure", "Foo", null));
            Assert.Equal("error", (string)j["status"]);
            Assert.Equal("ScaffoldFailed", (string)j["error"]["code"]);
        }
    }
}
