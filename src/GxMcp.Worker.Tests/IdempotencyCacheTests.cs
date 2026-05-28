using GxMcp.Worker.Helpers;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GxMcp.Worker.Tests
{
    // v2.8.0 — idempotency cache used by CommandDispatcher to serve the same
    // response when an LLM client retries with the same clientRequestId
    // after a socket / gateway timeout.
    public class IdempotencyCacheTests
    {
        public IdempotencyCacheTests() => IdempotencyCache.Clear();

        [Fact]
        public void TryServe_MissingId_ReturnsNull()
        {
            Assert.Null(IdempotencyCache.TryServe(null));
            Assert.Null(IdempotencyCache.TryServe(""));
            Assert.Null(IdempotencyCache.TryServe("   "));
        }

        [Fact]
        public void TryServe_UnknownId_ReturnsNull()
        {
            Assert.Null(IdempotencyCache.TryServe("never-stored"));
        }

        [Fact]
        public void Store_ThenServe_RoundTripsWithReplayTag()
        {
            const string id = "client-1";
            string payload = "{\"status\":\"ok\",\"code\":\"WriteApplied\",\"result\":{\"part\":\"Source\"}}";
            IdempotencyCache.Store(id, payload);

            string replayed = IdempotencyCache.TryServe(id);
            Assert.NotNull(replayed);
            var obj = JObject.Parse(replayed);
            Assert.Equal("ok", (string)obj["status"]);
            Assert.Equal("WriteApplied", (string)obj["code"]);
            Assert.NotNull(obj["_meta"]);
            Assert.True((bool)obj["_meta"]["replayed"]);
            Assert.NotNull(obj["_meta"]["replayedFromUtc"]);
        }

        [Fact]
        public void Store_PreservesNonEnvelopePayload()
        {
            const string id = "client-raw";
            string payload = "not-json-at-all";
            IdempotencyCache.Store(id, payload);
            string replayed = IdempotencyCache.TryServe(id);
            Assert.Equal(payload, replayed);
        }

        [Fact]
        public void Store_NullOrEmpty_NoOp()
        {
            IdempotencyCache.Store(null, "{\"x\":1}");
            IdempotencyCache.Store("", "{\"x\":1}");
            IdempotencyCache.Store("id-but-empty-body", "");
            Assert.Equal(0, IdempotencyCache.Count);
        }

        [Fact]
        public void TwoStoresSameId_SecondOverwrites()
        {
            const string id = "client-2";
            IdempotencyCache.Store(id, "{\"status\":\"ok\",\"code\":\"First\"}");
            IdempotencyCache.Store(id, "{\"status\":\"ok\",\"code\":\"Second\"}");
            var obj = JObject.Parse(IdempotencyCache.TryServe(id));
            Assert.Equal("Second", (string)obj["code"]);
        }

        [Fact]
        public void ReplayTag_PreservesExistingMeta()
        {
            const string id = "client-meta";
            string payload = "{\"status\":\"error\",\"error\":{\"code\":\"X\",\"message\":\"boom\"},\"_meta\":{\"sourceMessage\":\"PT-BR original\"}}";
            IdempotencyCache.Store(id, payload);
            var obj = JObject.Parse(IdempotencyCache.TryServe(id));
            // Original _meta survives + replayed flag added on top.
            Assert.Equal("PT-BR original", (string)obj["_meta"]["sourceMessage"]);
            Assert.True((bool)obj["_meta"]["replayed"]);
        }
    }
}
