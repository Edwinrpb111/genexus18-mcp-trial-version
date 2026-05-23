using GxMcp.Worker.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GxMcp.Worker.Tests
{
    // Wave-3: NavigationViewService — "View Navigation / View Last Navigation" parity.
    // We exercise the input-validation and missing-KB paths without bringing up
    // the GeneXus SDK. End-to-end navigation + cache round-trip is left to the
    // live worker integration suite.
    public class NavigationViewServiceTests
    {
        [Fact]
        public void View_MissingName_ReturnsError()
        {
            var svc = new NavigationViewService(navigation: null, kbService: null);
            var j = JObject.Parse(svc.View("", latest: false));
            Assert.NotNull(j["error"]);
        }

        [Fact]
        public void View_NoNavigationService_NoCache_ReturnsNoNavigation()
        {
            var svc = new NavigationViewService(navigation: null, kbService: null);
            var j = JObject.Parse(svc.View("Foo", latest: true));
            Assert.Equal("NoNavigation", (string)j["code"]);
        }

        [Fact]
        public void View_WhitespaceName_TreatedAsMissing()
        {
            var svc = new NavigationViewService(navigation: null, kbService: null);
            var j = JObject.Parse(svc.View("   ", latest: false));
            Assert.NotNull(j["error"]);
        }
    }
}
