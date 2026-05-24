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
            Assert.Equal("Error", (string)json["status"]);
            Assert.Equal("MissingChange", (string)json["code"]);
        }

        [Fact]
        public void Simulate_MissingTarget_ReturnsError()
        {
            var svc = NewService();
            var change = new JObject { ["kind"] = "type_change" };
            var json = JObject.Parse(svc.Simulate(change));
            Assert.Equal("Error", (string)json["status"]);
            Assert.Equal("MissingTarget", (string)json["code"]);
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
            Assert.Equal("Success", (string)json["status"]);
            Assert.Equal(0, (int)json["impactedCount"]);
            Assert.Empty((JArray)json["breaks"]);
            Assert.Empty((JArray)json["probably_safe"]);
            Assert.Empty((JArray)json["unknown"]);
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
            Assert.Equal("Success", (string)json["status"]);
            Assert.Equal("rename_attribute", (string)json["kind"]);
            Assert.Equal("OrderId", (string)json["change"]["target"]);
            Assert.NotNull(json["note"]);
        }
    }
}
