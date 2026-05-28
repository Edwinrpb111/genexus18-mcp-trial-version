using GxMcp.Worker.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GxMcp.Worker.Tests
{
    public class OrientServiceTests
    {
        [Fact]
        public void Welcome_WithoutKb_ReturnsStructuredResponse()
        {
            var svc = new OrientService(kbService: null);
            var raw = svc.Welcome();
            var json = JObject.Parse(raw);

            Assert.Equal("ok", (string)json["status"]!);
            Assert.NotNull(json["result"]!["kb"]);
            Assert.NotNull(json["result"]!["recentEdits"]);
            Assert.NotNull(json["result"]!["gotchas"]);
            Assert.True(((JArray)json["result"]!["gotchas"]!).Count == 3);
            Assert.True(((JArray)json["result"]!["topTools"]!).Count == 5);
        }

        [Fact]
        public void Welcome_NoKbPath_RecentEdits_IsEmpty()
        {
            var svc = new OrientService(kbService: null);
            var json = JObject.Parse(svc.Welcome());
            Assert.Empty((JArray)json["result"]!["recentEdits"]!);
        }
    }
}
