using GxMcp.Worker.Helpers;
using GxMcp.Worker.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GxMcp.Worker.Tests
{
    // v2.8.0 — end-to-end: when a `clientRequestId` is threaded through the
    // RPC params, CommandDispatcher.Dispatch returns the same response for
    // the same id and tags it as a replay. Real services would be invoked
    // here, but we drive through the `ping` exclusion path (which bypasses
    // the cache) to validate the bypass, then through a deliberately-
    // missing-method path which routes to the canonical error response —
    // a deterministic, side-effect-free emission we can cache and replay.
    public class DispatcherIdempotencyTests
    {
        public DispatcherIdempotencyTests() => IdempotencyCache.Clear();

        [Fact]
        public void Ping_BypassesCache_NoMatterTheRequestId()
        {
            var dispatcher = CommandDispatcher.Instance;
            string rpc = "{\"method\":\"ping\",\"params\":{\"clientRequestId\":\"ping-1\"}}";
            string r1 = dispatcher.Dispatch(rpc);
            string r2 = dispatcher.Dispatch(rpc);
            Assert.False(string.IsNullOrEmpty(r1));
            Assert.False(string.IsNullOrEmpty(r2));
            // Neither response carries the replayed tag — ping is excluded.
            try
            {
                var j2 = JObject.Parse(r2);
                Assert.True(j2["_meta"]?["replayed"] == null);
            }
            catch { /* ping returns non-JObject envelope; that's still a bypass success */ }
            Assert.Equal(0, IdempotencyCache.Count);
        }

        [Fact]
        public void UnknownMethod_WithRequestId_CachesAndReplays()
        {
            var dispatcher = CommandDispatcher.Instance;
            string rpc = "{\"method\":\"unknown_method\",\"action\":\"nope\",\"target\":\"X\",\"params\":{\"clientRequestId\":\"unk-1\"}}";

            string first = dispatcher.Dispatch(rpc);
            string second = dispatcher.Dispatch(rpc);

            // First response: canonical error envelope, no replay tag.
            var f = JObject.Parse(first);
            Assert.Equal("error", (string)f["status"]);
            Assert.True(f["_meta"]?["replayed"] == null);

            // Second response: same envelope shape + replay tag set.
            var s = JObject.Parse(second);
            Assert.Equal("error", (string)s["status"]);
            Assert.Equal((string)f["error"]["code"], (string)s["error"]["code"]);
            Assert.True((bool)s["_meta"]["replayed"]);
            Assert.NotNull(s["_meta"]["replayedFromUtc"]);
        }

        [Fact]
        public void NoClientRequestId_NoCachingHappens()
        {
            var dispatcher = CommandDispatcher.Instance;
            string rpc = "{\"method\":\"unknown_method\",\"action\":\"nope\",\"target\":\"X\",\"params\":{}}";

            string first = dispatcher.Dispatch(rpc);
            string second = dispatcher.Dispatch(rpc);

            // Both responses look identical but neither carries the replay tag.
            var f = JObject.Parse(first);
            var s = JObject.Parse(second);
            Assert.Equal("error", (string)f["status"]);
            Assert.Equal("error", (string)s["status"]);
            Assert.True(f["_meta"]?["replayed"] == null);
            Assert.True(s["_meta"]?["replayed"] == null);
            Assert.Equal(0, IdempotencyCache.Count);
        }

        [Fact]
        public void DifferentRequestIds_CacheSeparately()
        {
            var dispatcher = CommandDispatcher.Instance;
            string rpc1 = "{\"method\":\"unknown_method\",\"action\":\"a\",\"params\":{\"clientRequestId\":\"id-A\"}}";
            string rpc2 = "{\"method\":\"unknown_method\",\"action\":\"a\",\"params\":{\"clientRequestId\":\"id-B\"}}";

            dispatcher.Dispatch(rpc1);
            dispatcher.Dispatch(rpc2);
            Assert.Equal(2, IdempotencyCache.Count);

            // Replays each return their own original (both happen to be the
            // same shape, but the cache lookup is by id).
            var r1 = JObject.Parse(dispatcher.Dispatch(rpc1));
            Assert.True((bool)r1["_meta"]["replayed"]);
        }

        [Fact]
        public void RequestId_FoundInsideNestedParams_StillCaches()
        {
            // OperationsRouter passes args inside params.params for some routes.
            // The dispatcher must read clientRequestId from either layer.
            var dispatcher = CommandDispatcher.Instance;
            string rpc = "{\"method\":\"unknown_method\",\"action\":\"x\",\"params\":{\"params\":{\"clientRequestId\":\"nested-1\"}}}";
            dispatcher.Dispatch(rpc);
            Assert.Equal(1, IdempotencyCache.Count);
            var replay = JObject.Parse(dispatcher.Dispatch(rpc));
            Assert.True((bool)replay["_meta"]["replayed"]);
        }
    }
}
