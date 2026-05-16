using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using GxMcp.Worker.Models;

namespace GxMcp.Worker.Services
{
    public class EnrichmentQueue
    {
        private readonly IndexEntryEnricher _enricher;
        private readonly ConcurrentQueue<SearchIndex.IndexEntry> _queue = new ConcurrentQueue<SearchIndex.IndexEntry>();
        private readonly SemaphoreSlim _enrichGate = new SemaphoreSlim(1, 1);
        private int _pendingCount;

        public EnrichmentQueue(IndexEntryEnricher enricher)
        {
            _enricher = enricher;
        }

        public int PendingCount { get { return Volatile.Read(ref _pendingCount); } }

        public void Enqueue(SearchIndex.IndexEntry entry)
        {
            if (entry == null || entry.IsEnriched) return;
            _queue.Enqueue(entry);
            Interlocked.Increment(ref _pendingCount);
        }

        public async Task DrainAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            SearchIndex.IndexEntry entry;
            while (_queue.TryDequeue(out entry))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _enrichGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    _enricher.Enrich(entry);
                    Interlocked.Decrement(ref _pendingCount);
                }
                finally
                {
                    _enrichGate.Release();
                }
            }
        }

        public async Task PromoteAsync(SearchIndex.IndexEntry entry, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (entry == null || entry.IsEnriched) return;
            await _enrichGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _enricher.Enrich(entry);
            }
            finally
            {
                _enrichGate.Release();
            }
        }
    }
}
