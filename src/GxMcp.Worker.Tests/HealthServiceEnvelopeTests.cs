using GxMcp.Worker.Models;
using GxMcp.Worker.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GxMcp.Worker.Tests
{
    // v2.8.0 — HealthService is the canonical-envelope reference impl.
    public class HealthServiceEnvelopeTests
    {
        [Fact]
        public void Ping_EmitsCanonicalOk()
        {
            string raw = new HealthService().Ping();
            var v = EnvelopeConformance.Validate(raw);
            Assert.True(v.Ok, v.ToString());

            var obj = JObject.Parse(raw);
            Assert.Equal("ok", (string)obj["status"]);
            Assert.Equal("Ready", (string)obj["code"]);
            Assert.NotNull(obj["result"]?["timestamp"]);
        }

        [Fact]
        public void GetHealthReport_IndexMissing_EmitsCanonicalError()
        {
            // Worker's cache/search_index.json doesn't exist in the test base dir,
            // so this hits the SearchIndexMissing branch.
            string raw = new HealthService().GetHealthReport();
            var v = EnvelopeConformance.Validate(raw);
            Assert.True(v.Ok, v.ToString());

            var obj = JObject.Parse(raw);
            Assert.Equal("error", (string)obj["status"]);
            Assert.Equal("SearchIndexMissing", (string)obj["error"]["code"]);
            Assert.False(string.IsNullOrEmpty((string)obj["error"]["hint"]));
            var ns = obj["error"]["nextSteps"] as JArray;
            Assert.NotNull(ns);
            Assert.True(ns.Count >= 1);
            Assert.Equal("genexus_lifecycle", (string)ns[0]["tool"]);
        }

        [Fact]
        public void McpResponse_Ok_RoundTripsThroughValidator()
        {
            string raw = McpResponse.Ok(target: "X", code: "Foo", result: new JObject { ["k"] = 1 });
            Assert.True(EnvelopeConformance.Validate(raw).Ok);
        }

        [Fact]
        public void McpResponse_Err_RequiresCodeAndMessage()
        {
            string raw = McpResponse.Err(
                code: "WhateverFailed",
                message: "Boom.",
                hint: "Try again.",
                nextSteps: new JArray(McpResponse.NextStep("genexus_health", null, "Health is the safest fallback.")));
            Assert.True(EnvelopeConformance.Validate(raw).Ok);
            var obj = JObject.Parse(raw);
            Assert.Equal("error", (string)obj["status"]);
            Assert.Equal("WhateverFailed", (string)obj["error"]["code"]);
        }

        [Fact]
        public void McpResponse_Accepted_RequiresOperationId()
        {
            string raw = McpResponse.Accepted(target: "X", operationId: "op-1", pollTarget: "op:op-1");
            Assert.True(EnvelopeConformance.Validate(raw).Ok);
        }

        [Fact]
        public void EnvelopeConformance_RejectsLegacySuccess()
        {
            string legacy = "{\"status\":\"Success\",\"action\":\"Write\",\"target\":\"X\",\"part\":\"Source\"}";
            var v = EnvelopeConformance.Validate(legacy);
            Assert.False(v.Ok);
        }

        [Fact]
        public void EnvelopeConformance_RejectsErrorWithoutCode()
        {
            string bad = "{\"status\":\"error\",\"error\":{\"message\":\"Boom.\"}}";
            var v = EnvelopeConformance.Validate(bad);
            Assert.False(v.Ok);
            Assert.Contains(v.Violations, x => x.Contains("error.code"));
        }
    }
}
