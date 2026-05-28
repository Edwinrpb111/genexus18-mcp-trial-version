using System;
using GxMcp.Worker.Models;
using GxMcp.Worker.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GxMcp.Worker.Tests
{
    // v2.8.0 — runtime conformance: invoke each service's "no KB / no
    // input" error path (or another path that doesn't need a live SDK)
    // and validate the output through EnvelopeConformance. This catches
    // services that build the envelope with a typoed status / missing
    // error.code / payload at top level — defects EnvelopeContractGuard
    // can't see because they live in computed JSON, not source literals.
    //
    // Only services with a constructable no-deps fast path are exercised.
    // Adding a new service to this matrix means: ensure it has at least
    // one method that fails cleanly without a KB and add a row below.
    public class ServiceRuntimeEnvelopeTests
    {
        [Fact]
        public void HealthService_Ping_IsCanonical()
        {
            AssertConforms(new HealthService().Ping());
        }

        [Fact]
        public void HealthService_GetHealthReport_IsCanonical()
        {
            // Hits the SearchIndexMissing branch (no index file at base dir).
            AssertConforms(new HealthService().GetHealthReport());
        }

        [Fact]
        public void McpResponse_Ok_Minimal_IsCanonical()
        {
            AssertConforms(McpResponse.Ok());
            AssertConforms(McpResponse.Ok(target: "X"));
            AssertConforms(McpResponse.Ok(target: "X", code: "Foo"));
            AssertConforms(McpResponse.Ok(target: "X", code: "Foo", result: new JObject { ["k"] = 1 }));
        }

        [Fact]
        public void McpResponse_Err_RequiresCodeAndMessage_RoundTrip()
        {
            // Missing code/message would still need to survive the validator
            // — confirm both the success and failure paths.
            string ok = McpResponse.Err(code: "X", message: "boom");
            AssertConforms(ok);

            string bad = "{\"status\":\"error\",\"error\":{\"message\":\"no code\"}}";
            Assert.False(EnvelopeConformance.Validate(bad).Ok);
        }

        [Fact]
        public void McpResponse_Err_WithNextSteps_PreservesStructure()
        {
            string raw = McpResponse.Err(
                code: "PartNotFound",
                message: "Part 'X' not found.",
                hint: "Pass a real part name.",
                nextSteps: new JArray(McpResponse.NextStep(
                    tool: "genexus_read",
                    args: new JObject { ["name"] = "Y" },
                    why: "Returns availableParts.")));

            AssertConforms(raw);
            var obj = JObject.Parse(raw);
            var step = obj["error"]?["nextSteps"]?[0] as JObject;
            Assert.NotNull(step);
            Assert.Equal("genexus_read", (string)step["tool"]);
            Assert.Equal("Y", (string)step["args"]["name"]);
            Assert.Equal("Returns availableParts.", (string)step["why"]);
        }

        [Fact]
        public void McpResponse_Partial_CarriesWarnings()
        {
            string raw = McpResponse.Partial(
                target: "X",
                code: "BulkPartial",
                result: new JObject { ["completed"] = 3, ["failed"] = 1 },
                warnings: new JArray(new JObject { ["message"] = "one item rolled back" }));
            AssertConforms(raw);
            var obj = JObject.Parse(raw);
            Assert.Equal("partial", (string)obj["status"]);
            Assert.NotNull(obj["warnings"]);
        }

        [Fact]
        public void McpResponse_Accepted_RequiresOperationId()
        {
            string ok = McpResponse.Accepted(target: "X", operationId: "op-1", pollTarget: "op:op-1");
            AssertConforms(ok);

            // The validator must reject an accepted envelope without operationId.
            string bad = "{\"status\":\"accepted\",\"target\":\"X\"}";
            Assert.False(EnvelopeConformance.Validate(bad).Ok);
        }

        [Fact]
        public void EnvelopeConformance_DoesNotAcceptUnknownStatus()
        {
            string bad = "{\"status\":\"weird\"}";
            var r = EnvelopeConformance.Validate(bad);
            Assert.False(r.Ok);
            Assert.Contains(r.Violations, v => v.Contains("ok|error|partial|accepted"));
        }

        [Fact]
        public void EnvelopeConformance_DoesNotAcceptLegacyTopLevelFields()
        {
            string bad = "{\"status\":\"ok\",\"action\":\"Write\",\"target\":\"X\",\"details\":\"...\"}";
            var r = EnvelopeConformance.Validate(bad);
            Assert.False(r.Ok);
            Assert.Contains(r.Violations, v => v.Contains("action"));
        }

        private static void AssertConforms(string envelopeJson)
        {
            var r = EnvelopeConformance.Validate(envelopeJson);
            Assert.True(r.Ok, "Envelope violates canonical contract: " + r);
        }
    }
}
