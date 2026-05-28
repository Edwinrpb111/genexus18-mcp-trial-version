using System.Linq;
using GxMcp.Worker.Helpers;
using GxMcp.Worker.Models;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GxMcp.Worker.Tests
{
    // v2.8.0 (K) — HealingService.FormatNotFoundError now emits the canonical
    // envelope AND branches on the index probe:
    //   - 2+ exact name matches → AmbiguousName with candidates + nextSteps
    //   - 1 match suggested      → ObjectNotFound with similar-name nextSteps
    //   - cold index             → ObjectNotFoundIndexWarming with retryAfterMs
    public class HealingServiceCanonicalTests
    {
        private static SearchIndex BuildIndex(params (string name, string type)[] entries)
        {
            var idx = new SearchIndex();
            foreach (var (n, t) in entries)
            {
                idx.Objects[$"{t}:{n}"] = new SearchIndex.IndexEntry
                {
                    Guid = System.Guid.NewGuid().ToString(),
                    Name = n,
                    Type = t
                };
            }
            return idx;
        }

        [Fact]
        public void NotFound_IndexCold_EmitsRetryAfterMs()
        {
            string raw = HealingService.FormatNotFoundError("X", null);
            Assert.True(EnvelopeConformance.Validate(raw).Ok);

            var obj = JObject.Parse(raw);
            Assert.Equal("error", (string)obj["status"]);
            Assert.Equal("ObjectNotFoundIndexWarming", (string)obj["error"]["code"]);
            Assert.Equal(2500, (int)obj["error"]["retryAfterMs"]);
            // nextSteps with broad list call.
            var steps = obj["error"]["nextSteps"] as JArray;
            Assert.NotNull(steps);
            Assert.Equal("genexus_list_objects", (string)steps[0]["tool"]);
        }

        [Fact]
        public void AmbiguousName_TwoTypesShareName_EmitsCandidatesAndPerTypeNextSteps()
        {
            var idx = BuildIndex(
                ("Customer", "Transaction"),
                ("Customer", "WebPanel"));
            string raw = HealingService.FormatNotFoundError("Customer", idx);
            Assert.True(EnvelopeConformance.Validate(raw).Ok);

            var obj = JObject.Parse(raw);
            Assert.Equal("error", (string)obj["status"]);
            Assert.Equal("AmbiguousName", (string)obj["error"]["code"]);
            // Candidates list exposed under error.candidates.
            var candidates = obj["error"]["candidates"] as JArray;
            Assert.NotNull(candidates);
            Assert.Equal(2, candidates.Count);
            // One pre-mounted nextStep per candidate, each pinned to a specific type.
            // Order isn't guaranteed (dict iteration), so assert by set membership.
            var steps = obj["error"]["nextSteps"] as JArray;
            Assert.NotNull(steps);
            Assert.Equal(2, steps.Count);
            var stepTypes = steps.Select(s => (string)s["args"]["type"]).ToHashSet();
            Assert.Contains("Transaction", stepTypes);
            Assert.Contains("WebPanel", stepTypes);
            foreach (var s in steps)
            {
                Assert.Equal("genexus_read", (string)s["tool"]);
                Assert.Equal("Customer", (string)s["args"]["name"]);
            }
        }

        [Fact]
        public void NotFound_WithSimilarNames_EmitsSuggestionNextSteps()
        {
            var idx = BuildIndex(
                ("CustomerOrder", "Transaction"),
                ("CustomerProfile", "Transaction"));
            string raw = HealingService.FormatNotFoundError("Customer", idx);
            Assert.True(EnvelopeConformance.Validate(raw).Ok);

            var obj = JObject.Parse(raw);
            Assert.Equal("ObjectNotFound", (string)obj["error"]["code"]);
            Assert.Contains("Did you mean", (string)obj["error"]["hint"]);
            var steps = obj["error"]["nextSteps"] as JArray;
            Assert.True(steps.Count >= 1);
            Assert.Equal("genexus_read", (string)steps[0]["tool"]);
        }

        [Fact]
        public void NotFound_NoSimilar_EmitsBroadListNextStep()
        {
            var idx = BuildIndex(
                ("OtherObject", "Transaction"));
            string raw = HealingService.FormatNotFoundError("Customer", idx);
            Assert.True(EnvelopeConformance.Validate(raw).Ok);

            var obj = JObject.Parse(raw);
            Assert.Equal("ObjectNotFound", (string)obj["error"]["code"]);
            var steps = obj["error"]["nextSteps"] as JArray;
            Assert.True(steps.Count == 1);
            Assert.Equal("genexus_list_objects", (string)steps[0]["tool"]);
        }
    }
}
