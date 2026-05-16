using GxMcp.Worker.Models;
using Xunit;

namespace GxMcp.Worker.Tests
{
    public class IndexEntryEnrichmentTests
    {
        [Fact]
        public void IndexEntry_NewInstance_IsNotEnriched()
        {
            var entry = new SearchIndex.IndexEntry { Name = "Foo", Type = "Procedure" };
            Assert.False(entry.IsEnriched);
        }

        [Fact]
        public void IndexEntry_AfterEnrichment_FlagIsTrue()
        {
            var entry = new SearchIndex.IndexEntry { Name = "Foo", Type = "Procedure" };
            entry.IsEnriched = true;
            Assert.True(entry.IsEnriched);
        }
    }
}
