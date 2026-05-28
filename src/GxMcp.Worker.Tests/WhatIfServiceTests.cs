using GxMcp.Worker.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GxMcp.Worker.Tests
{
    public class WhatIfServiceTests
    {
        // Service operates against AnalyzeService/ObjectService. We pass null
        // dependencies; the service guards with ?. and surfaces an empty
        // impact envelope so the categorisation logic can be exercised
        // without a live KB.
        private static WhatIfService NewService() => new WhatIfService(null, null);

        [Fact]
        public void Simulate_MissingChange_ReturnsError()
        {
            var svc = NewService();
            var json = JObject.Parse(svc.Simulate(null));
            Assert.Equal("error", (string)json["status"]);
            Assert.Equal("MissingChange", (string)json["error"]["code"]);
        }

        [Fact]
        public void Simulate_MissingTarget_ReturnsError()
        {
            var svc = NewService();
            var change = new JObject { ["kind"] = "type_change" };
            var json = JObject.Parse(svc.Simulate(change));
            Assert.Equal("error", (string)json["status"]);
            Assert.Equal("MissingTarget", (string)json["error"]["code"]);
        }

        [Fact]
        public void Simulate_NoCallers_ReturnsEmptyCategories()
        {
            var svc = NewService();
            // With null services, ImpactAnalysis short-circuits to empty {}
            // and `callers` defaults to an empty array.
            var change = new JObject
            {
                ["kind"] = "type_change",
                ["target"] = "CustomerName",
                ["attribute"] = "CustomerName",
                ["oldType"] = "Character(20)",
                ["newType"] = "Numeric(10.2)"
            };
            var json = JObject.Parse(svc.Simulate(change));
            Assert.Equal("ok", (string)json["status"]);
            Assert.Equal(0, (int)json["result"]["impactedCount"]);
            Assert.Empty((JArray)json["result"]["breaks"]);
            Assert.Empty((JArray)json["result"]["probably_safe"]);
            Assert.Empty((JArray)json["result"]["unknown"]);
        }

        [Fact]
        public void Simulate_PassesChangeThroughInResponse()
        {
            var svc = NewService();
            var change = new JObject
            {
                ["kind"] = "rename_attribute",
                ["target"] = "OrderId",
                ["oldType"] = "Numeric(10)",
                ["newType"] = "Numeric(12)"
            };
            var json = JObject.Parse(svc.Simulate(change));
            Assert.Equal("ok", (string)json["status"]);
            Assert.Equal("rename_attribute", (string)json["result"]["kind"]);
            Assert.Equal("OrderId", (string)json["result"]["change"]["target"]);
            Assert.NotNull(json["result"]["note"]);
        }
    }
}
