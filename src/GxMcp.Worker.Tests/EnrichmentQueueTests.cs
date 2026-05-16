using GxMcp.Worker.Models;
using GxMcp.Worker.Services;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace GxMcp.Worker.Tests
{
    public class EnrichmentQueueTests
    {
        [Fact]
        public async Task Drain_EnrichesAllQueuedEntries_InFifoOrder()
        {
            var enriched = new List<string>();
            var enricher = new IndexEntryEnricher(e =>
            {
                lock (enriched) enriched.Add(e.Name);
            });

            var queue = new EnrichmentQueue(enricher);
            for (int i = 0; i < 10; i++)
            {
                queue.Enqueue(new SearchIndex.IndexEntry { Name = "E" + i });
            }

            await queue.DrainAsync();

            Assert.Equal(10, enriched.Count);
            Assert.Equal("E0", enriched[0]);
            Assert.Equal("E9", enriched[9]);
        }

        [Fact]
        public async Task PromoteAsync_BumpsEntryAheadOfQueue()
        {
            var enriched = new List<string>();
            var enricher = new IndexEntryEnricher(e =>
            {
                lock (enriched) enriched.Add(e.Name);
                System.Threading.Thread.Sleep(2);
            });

            var queue = new EnrichmentQueue(enricher);
            for (int i = 0; i < 50; i++)
            {
                queue.Enqueue(new SearchIndex.IndexEntry { Name = "low" + i });
            }

            var hot = new SearchIndex.IndexEntry { Name = "HOT" };
            await queue.PromoteAsync(hot);

            Assert.True(hot.IsEnriched);
            Assert.Contains("HOT", enriched);
        }
    }
}
