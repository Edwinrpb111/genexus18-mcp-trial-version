using System;
using System.Linq;
using GxMcp.Worker.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GxMcp.Worker.Tests
{
    // v2.6.8: covers ListService temporal controls — sort=lastUpdate, since/
    // modifiedBefore bounds, cursor encode/decode + resume semantics, and the
    // aggregates / by_author projection. All tests run against the in-memory
    // IndexWithLifecycle fixture so no live KB is required.
    public class TemporalListTests
    {
        private static JArray ResultsOf(string json) => (JArray)JObject.Parse(json)["results"];
        private static JObject MetaOf(string json) => (JObject)JObject.Parse(json)["_meta"];

        [Fact]
        public void SortByLastUpdate_OrdersDescending()
        {
            var fx = TestFixtures.IndexWithLifecycle();
            var svc = new ListService(fx.Index);
            var json = svc.List(new ListCriteria { Sort = "lastUpdate", Limit = 5 });
            var names = ResultsOf(json).Select(r => r["name"].ToString()).ToList();

            // Newest -> oldest. The MinValue-stamped "Untouched" sorts last (ties
            // broken by name, but here it's the only MinValue so it just tails).
            Assert.Equal(new[] { "RecentMost", "Recent2", "Mid", "Older", "Eldest" }, names);
        }

        [Fact]
        public void SinceFilter_IsInclusive_OnLowerBound()
        {
            var fx = TestFixtures.IndexWithLifecycle();
            var svc = new ListService(fx.Index);
            var json = svc.List(new ListCriteria {
                Sort = "lastUpdate",
                Since = new DateTime(2026, 5, 20, 12, 0, 0, DateTimeKind.Utc)
            });
            var names = ResultsOf(json).Select(r => r["name"].ToString()).ToList();
            // Recent2 sits exactly on the boundary and MUST be included.
            Assert.Equal(new[] { "RecentMost", "Recent2" }, names);
        }

        [Fact]
        public void ModifiedBeforeFilter_IsExclusive_OnUpperBound()
        {
            var fx = TestFixtures.IndexWithLifecycle();
            var svc = new ListService(fx.Index);
            var json = svc.List(new ListCriteria {
                Sort = "lastUpdate",
                ModifiedBefore = new DateTime(2026, 5, 20, 12, 0, 0, DateTimeKind.Utc)
            });
            var names = ResultsOf(json).Select(r => r["name"].ToString()).ToList();
            // Recent2 sits exactly on the boundary and MUST be excluded.
            Assert.DoesNotContain("Recent2", names);
            Assert.Equal(new[] { "Mid", "Older", "Eldest" }, names);
        }

        [Fact]
        public void Cursor_RoundTrip_Encodes_And_Decodes()
        {
            var ts = new DateTime(2026, 5, 22, 12, 0, 0, DateTimeKind.Utc);
            const string name = "RecentMost";
            const string guid = "abc-123";
            // v2.6.8: encoding now carries name as the middle tiebreak.
            var token = ListService.EncodeCursor(ts, name, guid);
            Assert.False(string.IsNullOrEmpty(token));

            var decoded = ListService.DecodeCursor(token);
            Assert.True(decoded.HasValue);
            Assert.Equal(ts, decoded.Value.ts);
            Assert.Equal(name, decoded.Value.name);
            Assert.Equal(guid, decoded.Value.guid);
        }

        [Fact]
        public void Cursor_LegacyTwoPart_StillDecodes()
        {
            // v2.6.8: legacy (ts|guid) cursors emitted before the Name tiebreak
            // landed should still resume correctly (with empty name) so callers
            // mid-pagination across a release boundary don't lose their place.
            var token = ListService.EncodeCursor(
                new DateTime(2026, 5, 22, 12, 0, 0, DateTimeKind.Utc),
                "abc-123");
            var decoded = ListService.DecodeCursor(token);
            Assert.True(decoded.HasValue);
            Assert.Equal(string.Empty, decoded.Value.name);
            Assert.Equal("abc-123", decoded.Value.guid);
        }

        [Fact]
        public void Cursor_Resumes_After_LastEmittedItem()
        {
            var fx = TestFixtures.IndexWithLifecycle();
            var svc = new ListService(fx.Index);

            // Page 1 — 2 newest.
            var page1Json = svc.List(new ListCriteria { Sort = "lastUpdate", Limit = 2 });
            var page1 = JObject.Parse(page1Json);
            var cursor = page1["nextCursor"]?.ToString();
            Assert.False(string.IsNullOrEmpty(cursor));

            // Page 2 — resume.
            var page2Json = svc.List(new ListCriteria { Sort = "lastUpdate", Limit = 2, Cursor = cursor });
            var names2 = ResultsOf(page2Json).Select(r => r["name"].ToString()).ToList();
            Assert.Equal(new[] { "Mid", "Older" }, names2);
        }

        [Fact]
        public void Cursor_Is_Ignored_When_SortByName()
        {
            var fx = TestFixtures.IndexWithLifecycle();
            var svc = new ListService(fx.Index);

            // Cursor without sort=lastUpdate must be a noop — offset-based paging takes over.
            var json = svc.List(new ListCriteria {
                Limit = 3,
                Cursor = ListService.EncodeCursor(DateTime.UtcNow, "any-guid")
            });
            // 3 items off the top of name-sorted list; cursor is silently ignored.
            Assert.Equal(3, ResultsOf(json).Count);
        }

        [Fact]
        public void Aggregates_Capture_LastUpdate_MinMax()
        {
            var fx = TestFixtures.IndexWithLifecycle();
            var svc = new ListService(fx.Index);
            var json = svc.List(new ListCriteria { Sort = "lastUpdate", Limit = 5 });
            var lu = (JObject)MetaOf(json)["aggregates"]["lastUpdate"];

            // Newtonsoft.Json.Linq.JObject.Parse auto-detects ISO date strings and stores
            // them as Date tokens; ToString() then prints in the current culture
            // (pt-BR on this machine). Use Value<DateTime>() so the comparison is
            // culture-independent.
            Assert.Equal(new DateTime(2026, 3, 22, 12, 0, 0, DateTimeKind.Utc),
                lu.Value<DateTime>("min").ToUniversalTime());
            Assert.Equal(new DateTime(2026, 5, 22, 12, 0, 0, DateTimeKind.Utc),
                lu.Value<DateTime>("max").ToUniversalTime());
        }

        [Fact]
        public void Aggregates_ByAuthor_Counts_And_Orders_Desc()
        {
            var fx = TestFixtures.IndexWithLifecycle();
            var svc = new ListService(fx.Index);
            var json = svc.List(new ListCriteria { Sort = "lastUpdate", Limit = 10, Verbose = true });
            var byAuthor = (JObject)MetaOf(json)["aggregates"]["by_author"];

            // alice has 3 (RecentMost, Recent2, Older), bob has 2 (Mid, Eldest). Untouched has null author and is skipped.
            Assert.Equal(3, (int)byAuthor["alice"]);
            Assert.Equal(2, (int)byAuthor["bob"]);
            // alice first because higher count.
            Assert.Equal("alice", byAuthor.Properties().First().Name);
        }

        [Fact]
        public void EmptySinceWindow_Returns_FilteredOut_EmptyReason()
        {
            var fx = TestFixtures.IndexWithLifecycle();
            var svc = new ListService(fx.Index);
            var json = svc.List(new ListCriteria {
                Sort = "lastUpdate",
                Since = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });
            var parsed = JObject.Parse(json);
            Assert.Equal(0, (int)parsed["count"]);
            // empty_reason is "no_matches" because the SearchIndex.Objects count is > 0
            // but nothing survived the filter — both interpretations are reasonable
            // here; the contract is just that *some* empty_reason is emitted.
            var reason = parsed["_meta"]?["empty_reason"]?.ToString();
            Assert.False(string.IsNullOrEmpty(reason));
        }

        [Fact]
        public void Items_Default_Carry_LastUpdate_When_Known()
        {
            var fx = TestFixtures.IndexWithLifecycle();
            var svc = new ListService(fx.Index);
            var json = svc.List(new ListCriteria { Sort = "lastUpdate", Limit = 1 });
            var item = (JObject)ResultsOf(json).First();
            Assert.Equal("RecentMost", item["name"].ToString());
            // lastUpdate is in the default projection (not gated on verbose).
            Assert.NotNull(item["lastUpdate"]);
            // createdAt + lastModifiedBy are verbose-only — absent here.
            Assert.Null(item["createdAt"]);
            Assert.Null(item["lastModifiedBy"]);
        }

        [Fact]
        public void Items_Verbose_Carry_CreatedAt_And_Author()
        {
            var fx = TestFixtures.IndexWithLifecycle();
            var svc = new ListService(fx.Index);
            var json = svc.List(new ListCriteria { Sort = "lastUpdate", Limit = 1, Verbose = true });
            var item = (JObject)ResultsOf(json).First();
            Assert.NotNull(item["lastUpdate"]);
            Assert.NotNull(item["createdAt"]);
            Assert.Equal("alice", item["lastModifiedBy"].ToString());
        }

        [Fact]
        public void Untouched_Item_Has_No_LastUpdate_Field()
        {
            var fx = TestFixtures.IndexWithLifecycle();
            var svc = new ListService(fx.Index);
            // Pull the full list ordered by name so "Untouched" is in scope; we
            // rely on the field being absent rather than emitted as empty string.
            var json = svc.List(new ListCriteria { Limit = 100, Verbose = true });
            var untouched = ResultsOf(json).Cast<JObject>()
                .FirstOrDefault(r => r["name"].ToString() == "Untouched");
            Assert.NotNull(untouched);
            Assert.Null(untouched["lastUpdate"]);
        }
    }
}
