using System.Collections.Concurrent;
using GxMcp.Worker.Models;
using GxMcp.Worker.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GxMcp.Worker.Tests
{
    // SP4.T1: When WriteObject is called with a name that maps to multiple objects of
    // different types in the index and no type filter is provided, the response should
    // embed an inline alternatives[] array instead of returning a bare "not found" hint.
    public class WriteServiceAmbiguousNameTests
    {
        private static (WriteService writeService, IndexCacheService indexCache) BuildServiceWithIndex()
        {
            var indexCache = new IndexCacheService();
            var build = new BuildService();
            var kb = new KbService(indexCache);
            kb.SetBuildService(build);
            build.SetKbService(kb);
            indexCache.SetBuildService(build);
            var obj = new ObjectService(kb, build);
            return (new WriteService(obj), indexCache);
        }

        private static SearchIndex BuildAmbiguousIndex(string sharedName)
        {
            var idx = new SearchIndex
            {
                Objects = new ConcurrentDictionary<string, SearchIndex.IndexEntry>(System.StringComparer.OrdinalIgnoreCase)
            };
            idx.Objects["Procedure:" + sharedName] = new SearchIndex.IndexEntry
            {
                Guid = System.Guid.NewGuid().ToString(),
                Name = sharedName,
                Type = "Procedure",
                ParentPath = "Root Module"
            };
            idx.Objects["WebPanel:" + sharedName] = new SearchIndex.IndexEntry
            {
                Guid = System.Guid.NewGuid().ToString(),
                Name = sharedName,
                Type = "WebPanel",
                ParentPath = "Root Module"
            };
            return idx;
        }

        [Fact]
        public void WriteObject_AmbiguousName_ReturnsErrorWithAlternativesArray()
        {
            var (ws, indexCache) = BuildServiceWithIndex();
            indexCache.UpdateIndex(BuildAmbiguousIndex("InvoiceProc"));

            string result = ws.WriteObject("InvoiceProc", "Source", "// code");
            var json = JObject.Parse(result);

            Assert.Equal("Error", json["status"]?.ToString());
            var alternatives = json["alternatives"] as JArray;
            Assert.NotNull(alternatives);
            Assert.True(alternatives.Count >= 2, "Expected at least 2 alternatives");
            foreach (var alt in alternatives)
            {
                Assert.False(string.IsNullOrEmpty(alt["name"]?.ToString()), "Each alternative must have a name");
                Assert.False(string.IsNullOrEmpty(alt["type"]?.ToString()), "Each alternative must have a type");
                Assert.NotNull(alt["parentPath"]);
            }
        }

        [Fact]
        public void WriteObject_AmbiguousName_ErrorMessageIsAmbiguous()
        {
            var (ws, indexCache) = BuildServiceWithIndex();
            indexCache.UpdateIndex(BuildAmbiguousIndex("InvoiceProc"));

            string result = ws.WriteObject("InvoiceProc", "Source", "// code");
            var json = JObject.Parse(result);

            Assert.Contains("Ambiguous", json["message"]?.ToString());
        }

        [Fact]
        public void WriteObject_AmbiguousName_AlternativesContainBothTypes()
        {
            var (ws, indexCache) = BuildServiceWithIndex();
            indexCache.UpdateIndex(BuildAmbiguousIndex("InvoiceProc"));

            string result = ws.WriteObject("InvoiceProc", "Source", "// code");
            var json = JObject.Parse(result);

            var alternatives = json["alternatives"] as JArray;
            Assert.NotNull(alternatives);
            var types = new System.Collections.Generic.HashSet<string>();
            foreach (var alt in alternatives) types.Add(alt["type"]?.ToString());
            Assert.Contains("Procedure", types);
            Assert.Contains("WebPanel", types);
        }

        [Fact]
        public void WriteObject_UnambiguousName_DoesNotReturnAlternatives()
        {
            // Only one type in the index — should not trigger disambiguation.
            var (ws, indexCache) = BuildServiceWithIndex();
            var idx = new SearchIndex
            {
                Objects = new ConcurrentDictionary<string, SearchIndex.IndexEntry>(System.StringComparer.OrdinalIgnoreCase)
            };
            idx.Objects["Procedure:OnlyProc"] = new SearchIndex.IndexEntry
            {
                Guid = System.Guid.NewGuid().ToString(),
                Name = "OnlyProc",
                Type = "Procedure",
                ParentPath = "Root Module"
            };
            indexCache.UpdateIndex(idx);

            // No KB is open so this will fall through to "Object not found", but NOT to the
            // ambiguity envelope (there is only one type).
            string result = ws.WriteObject("OnlyProc", "Source", "// code");
            var json = JObject.Parse(result);

            // Must not have an alternatives array
            Assert.Null(json["alternatives"]);
        }

        [Fact]
        public void WriteObject_TypeFilterProvided_SkipsAmbiguityCheck()
        {
            // Even if 2 types exist in the index, supplying typeFilter bypasses disambiguation.
            var (ws, indexCache) = BuildServiceWithIndex();
            indexCache.UpdateIndex(BuildAmbiguousIndex("InvoiceProc"));

            string result = ws.WriteObject("InvoiceProc", "Source", "// code", typeFilter: "Procedure");
            var json = JObject.Parse(result);

            // Should NOT return an ambiguity error — it may return "Object not found" (no real KB)
            // but must not contain alternatives[].
            Assert.Null(json["alternatives"]);
        }
    }
}
