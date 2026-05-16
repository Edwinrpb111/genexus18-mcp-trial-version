using GxMcp.Worker.Models;
using GxMcp.Worker.Services;
using Xunit;

namespace GxMcp.Worker.Tests
{
    public class IndexEntryEnricherTests
    {
        [Fact]
        public void Enrich_SetsIsEnrichedToTrue_AfterDelegateRuns()
        {
            var entry = new SearchIndex.IndexEntry { Name = "InvoiceProc", IsEnriched = false };
            bool delegateRan = false;

            var enricher = new IndexEntryEnricher(e =>
            {
                delegateRan = true;
                e.SourceSnippet = "/* source */";
                e.Calls = new System.Collections.Generic.List<string> { "Sub1" };
                e.CalledBy = new System.Collections.Generic.List<string> { "Web1" };
            });

            enricher.Enrich(entry);

            Assert.True(delegateRan);
            Assert.True(entry.IsEnriched);
            Assert.Equal("/* source */", entry.SourceSnippet);
            Assert.Single(entry.Calls);
        }

        [Fact]
        public void Enrich_IsNoOp_WhenEntryAlreadyEnriched()
        {
            var entry = new SearchIndex.IndexEntry { Name = "X", IsEnriched = true, SourceSnippet = "existing" };
            int calls = 0;

            var enricher = new IndexEntryEnricher(_ => { calls++; });
            enricher.Enrich(entry);

            Assert.Equal(0, calls);
            Assert.Equal("existing", entry.SourceSnippet);
        }
    }
}
