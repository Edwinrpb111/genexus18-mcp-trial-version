using GxMcp.Worker.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GxMcp.Worker.Tests
{
    // TECHDEBT-02: ObjectService.CreateObject's "No KB open" / unsupported-type
    // error paths used to hand-roll {"status":"Error",...} strings. These pin
    // the canonical McpResponse.Err envelope now in use (AlreadyExists /
    // CreateObjectFailed require a real SDK KB and are covered by the live
    // worker integration suite instead).
    public class ObjectServiceCreateEnvelopeTests
    {
        private static ObjectService BuildIsolated()
        {
            var indexCache = new IndexCacheService();
            var build = new BuildService();
            var kb = new KbService(indexCache);
            kb.SetBuildService(build);
            build.SetKbService(kb);
            indexCache.SetBuildService(build);
            return new ObjectService(kb, build);
        }

        [Fact]
        public void CreateObject_NoKbOpen_ReturnsCanonicalError()
        {
            var svc = BuildIsolated();
            var j = JObject.Parse(svc.CreateObject("Procedure", "Foo"));
            Assert.Equal("error", (string)j["status"]);
            Assert.Equal("NoKb", (string)j["error"]["code"]);
        }
    }
}
