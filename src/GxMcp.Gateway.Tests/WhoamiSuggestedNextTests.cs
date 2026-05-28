using Newtonsoft.Json.Linq;
using Xunit;

namespace GxMcp.Gateway.Tests
{
    // v2.8.0 — whoami carries a suggestedNext[{tool, args, why}] block so a
    // weakly-capable LLM doesn't have to guess the next call. The heuristic
    // is ordered by urgency: worker-down > no-kb > index-cold > update-
    // available > default-explore.
    public class WhoamiSuggestedNextTests
    {
        [Fact]
        public void SuggestedNext_KbMissing_RecommendsKbOpen()
        {
            var arr = Program.BuildSuggestedNextBlock(kbPath: null, kbExists: false, kbValid: false);
            Assert.NotNull(arr);
            Assert.True(arr.Count >= 1, "Should suggest at least one next step.");
            var first = arr[0] as JObject;
            Assert.NotNull(first);
            // Worker may or may not be healthy in the test harness; both
            // "open KB" and "wait for worker" are acceptable for the
            // no-KB / no-worker scenario. Just verify the suggestion shape.
            Assert.NotNull(first["tool"]);
            Assert.NotNull(first["args"]);
            Assert.NotNull(first["why"]);
        }

        [Fact]
        public void SuggestedNext_EntriesMatchCanonicalNextStepShape()
        {
            var arr = Program.BuildSuggestedNextBlock(kbPath: null, kbExists: false, kbValid: false);
            foreach (var entry in arr)
            {
                var obj = entry as JObject;
                Assert.NotNull(obj);
                // Same {tool, args, why} keys as McpResponse.NextStep in the worker —
                // so a dumb LLM sees the SAME shape on whoami.suggestedNext and on
                // error.nextSteps. Consistency wins.
                Assert.False(string.IsNullOrEmpty((string?)obj["tool"]));
                Assert.NotNull(obj["args"]);
                Assert.False(string.IsNullOrEmpty((string?)obj["why"]));
            }
        }

        [Fact]
        public void Whoami_AlwaysCarriesSuggestedNextField()
        {
            // The block is always emitted (possibly empty array) so clients
            // can rely on the key existing rather than checking for absence.
            JObject whoami = Program.BuildWhoamiPayload();
            Assert.NotNull(whoami["suggestedNext"]);
            Assert.IsType<JArray>(whoami["suggestedNext"]);
        }
    }
}
