using System.Collections.Generic;
using GxMcp.Worker.Services;
using Xunit;

namespace GxMcp.Worker.Tests
{
    public class AiCompleteServiceTests
    {
        private static System.Func<string, string> Envless(Dictionary<string, string> map = null)
        {
            return key => map != null && map.TryGetValue(key, out var v) ? v : null;
        }

        [Fact]
        public void Complete_EnvVarsUnset_ReturnsAiEndpointNotConfigured()
        {
            var svc = new AiCompleteService(http: null, envLookup: Envless());

            var j = svc.Complete("MyObj", "Events", "explain this code", 100);

            Assert.Equal("error", (string)j["status"]);
            Assert.Equal("AiEndpointNotConfigured", (string)j["error"]?["code"]);
            Assert.NotNull(j["error"]?["hint"]);
        }

        [Fact]
        public void Complete_EnvVarsSetButContextEmpty_ReturnsInvalidRequest()
        {
            var env = new Dictionary<string, string>
            {
                ["GXMCP_AI_COMPLETE_URL"] = "https://example.invalid/v1/chat/completions",
                ["GXMCP_AI_COMPLETE_KEY"] = "test-key"
            };
            var svc = new AiCompleteService(http: null, envLookup: Envless(env));

            var j = svc.Complete("MyObj", "Events", "", 100);
            Assert.Equal("error", (string)j["status"]);
            Assert.Equal("InvalidRequest", (string)j["error"]?["code"]);
        }
    }
}
