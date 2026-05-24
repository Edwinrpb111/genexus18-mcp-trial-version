using GxMcp.Worker.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GxMcp.Worker.Tests
{
    public class SdPanelServiceTests
    {
        // We pass null dependencies; the service's error paths trigger before
        // any SDK call, so we can validate dispatch + error envelopes without
        // a live KB. The happy paths are exercised via the IdealWorkflowSmokeTest
        // when a KB is available (LiveKbFact).
        private static SdPanelService NewService() => new SdPanelService(null, null);

        [Fact]
        public void Dispatch_UnknownAction_ReturnsError()
        {
            var svc = NewService();
            var json = JObject.Parse(svc.Dispatch("teleport", "Foo", new JObject()));
            Assert.Equal("Error", (string)json["status"]);
            Assert.Equal("UnknownAction", (string)json["code"]);
            Assert.Equal("SDPanel", (string)json["kind"]);
        }

        [Fact]
        public void Inspect_MissingTarget_ReturnsError()
        {
            var svc = NewService();
            var json = JObject.Parse(svc.Inspect(null));
            Assert.Equal("Error", (string)json["status"]);
            Assert.Equal("MissingTarget", (string)json["code"]);
            Assert.Equal("SDPanel", (string)json["kind"]);
        }

        [Fact]
        public void Edit_MissingTarget_ReturnsError()
        {
            var svc = NewService();
            var json = JObject.Parse(svc.Edit(null, new JObject()));
            Assert.Equal("Error", (string)json["status"]);
            Assert.Equal("MissingTarget", (string)json["code"]);
        }

        [Fact]
        public void Create_MissingTarget_ReturnsError()
        {
            var svc = NewService();
            var json = JObject.Parse(svc.Create(null, new JObject()));
            Assert.Equal("Error", (string)json["status"]);
            Assert.Equal("MissingTarget", (string)json["code"]);
        }
    }
}
