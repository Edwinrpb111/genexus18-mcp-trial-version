using System.Collections.Generic;
using GxMcp.Worker.Models;
using GxMcp.Worker.Services;

namespace GxMcp.Worker.Tests
{
    // v2.3.8 (Task 1.3): shared helpers for graph-related tests. Avoids
    // standing up a real KB by populating IndexCacheService via the
    // internal test seam LoadFromEntries.
    public static class TestFixtures
    {
        public class CallGraphFixture
        {
            public IndexCacheService Index;
        }

        // A simple A -> B -> C call chain.
        // A.Calls = [B]; B.Calls = [C]; C.CalledBy = [B]; B.CalledBy = [A].
        public static CallGraphFixture SmallCallGraph()
        {
            var entries = new List<SearchIndex.IndexEntry>
            {
                new SearchIndex.IndexEntry {
                    Name = "A", Type = "Procedure",
                    Calls = new List<string> { "B" },
                    CalledBy = new List<string>(),
                    SourceSnippet = "B()"
                },
                new SearchIndex.IndexEntry {
                    Name = "B", Type = "Procedure",
                    Calls = new List<string> { "C" },
                    CalledBy = new List<string> { "A" },
                    SourceSnippet = "C()"
                },
                new SearchIndex.IndexEntry {
                    Name = "C", Type = "Procedure",
                    Calls = new List<string>(),
                    CalledBy = new List<string> { "B" },
                    SourceSnippet = ""
                }
            };
            var svc = new IndexCacheService();
            svc.LoadFromEntries(entries);
            return new CallGraphFixture { Index = svc };
        }

        // Linear chain N0 -> N1 -> N2 -> ... -> N{depth-1}.
        // Root = "N0". Each entry's Calls points to the next node only.
        public static CallGraphFixture LargeCallChain(int depth)
        {
            var entries = new List<SearchIndex.IndexEntry>();
            for (int i = 0; i < depth; i++)
            {
                var e = new SearchIndex.IndexEntry {
                    Name = "N" + i,
                    Type = "Procedure",
                    Calls = new List<string>(),
                    CalledBy = new List<string>(),
                    SourceSnippet = ""
                };
                if (i < depth - 1) e.Calls.Add("N" + (i + 1));
                if (i > 0) e.CalledBy.Add("N" + (i - 1));
                entries.Add(e);
            }
            var svc = new IndexCacheService();
            svc.LoadFromEntries(entries);
            return new CallGraphFixture { Index = svc };
        }
    }
}
