using System;
using GxMcp.Worker.Models;

namespace GxMcp.Worker.Services
{
    public class IndexEntryEnricher
    {
        private readonly Action<SearchIndex.IndexEntry> _enrichDelegate;

        public IndexEntryEnricher(Action<SearchIndex.IndexEntry> enrichDelegate)
        {
            if (enrichDelegate == null) throw new ArgumentNullException("enrichDelegate");
            _enrichDelegate = enrichDelegate;
        }

        public void Enrich(SearchIndex.IndexEntry entry)
        {
            if (entry == null) return;
            if (entry.IsEnriched) return;

            _enrichDelegate(entry);
            entry.IsEnriched = true;
        }
    }
}
