using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GxMcp.Gateway
{
    public sealed record KbPoolStatus(KbHandle Handle, int? Pid, long? WorkingSetBytes, DateTime LastActivityUtc);

    public sealed class WorkerPoolFullException : Exception
    {
        public IReadOnlyList<KbHandle> OpenKbs { get; }
        public WorkerPoolFullException(IReadOnlyList<KbHandle> openKbs)
            : base($"WorkerPool full ({openKbs.Count} KBs open). Close one with genexus_kb action=close before opening another.")
        {
            OpenKbs = openKbs;
        }
    }

    public sealed class WorkerPool
    {
        private readonly Configuration _config;
        private readonly ConcurrentDictionary<string, Entry> _entries =
            new ConcurrentDictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        // Item 53: configured warm-spare count. Pre-spawn up to N workers
        // (bound to declared KBs in config.Environment.KBs[]) so the first
        // client tool call after process start doesn't pay the cold-start
        // cost. Capped at 5 to avoid runaway memory.
        public const int MaxWarmSpareCount = 5;
        private int _warmSpareCount;
        public int WarmSpareCount => _warmSpareCount;
        // PERFORMANCE (G-A1): previously a single `_spawnLock` serialised every Acquire across
        // all KBs. Now each KB has its own SpawnGate (per-Entry SemaphoreSlim), so two clients
        // opening different KBs proceed in parallel. The narrow `_capacityLock` still
        // protects the capacity/eviction window, which is cheap and infrequent.
        private readonly object _capacityLock = new object();

        public event Action<string>? OnRpcResponse;
        public event Action<KbHandle>? OnWorkerExited;

        public WorkerPool(Configuration config) { _config = config; }

        private sealed class Entry
        {
            public KbHandle Handle = null!;
            public WorkerProcess? Worker;
            public DateTime LastActivityUtc = DateTime.UtcNow;
            public readonly SemaphoreSlim SpawnGate = new SemaphoreSlim(1, 1);
        }

        public IReadOnlyList<KbHandle> ListOpen() =>
            _entries.Values
                .Where(e => e.Worker != null)
                .Select(e => e.Handle)
                .ToArray();

        public bool IsAtCapacity()
        {
            int max = _config.Server?.MaxOpenKbs ?? 3;
            return _entries.Count >= max;
        }

        public IReadOnlyList<string> GetKnownAliases()
        {
            return _entries.Keys.ToList();
        }

        public WorkerProcess? TryGetWorker(string alias)
        {
            if (_entries.TryGetValue(alias, out var entry)) return entry.Worker;
            return null;
        }

        public WorkerProcess? TryGet(string alias)
        {
            if (_entries.TryGetValue(alias.ToLowerInvariant(), out var entry))
            {
                return entry.Worker;
            }

            return null;
        }

        public IReadOnlyList<KbPoolStatus> Snapshot()
        {
            return _entries.Values
                .Where(e => e.Worker != null)
                .Select(e => new KbPoolStatus(
                    e.Handle,
                    e.Worker!.Pid,
                    e.Worker!.WorkingSetBytes,
                    e.LastActivityUtc))
                .ToArray();
        }

        public async Task<WorkerProcess> AcquireAsync(KbHandle handle, CancellationToken ct)
        {
            var entry = _entries.GetOrAdd(handle.NormalizedAlias, _ => new Entry { Handle = handle });
            if (entry.Worker != null)
            {
                entry.LastActivityUtc = DateTime.UtcNow;
                return entry.Worker;
            }

            // PERFORMANCE (G-A1): per-KB gate. Two concurrent acquires for the SAME KB
            // serialise here, but concurrent acquires for DIFFERENT KBs are now parallel.
            await entry.SpawnGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (entry.Worker != null) return entry.Worker;

                // Narrow lock around the capacity-window: cheap, prevents two different
                // KBs from both deciding "not full" at the same instant.
                lock (_capacityLock)
                {
                    int max = _config.Server?.MaxOpenKbs ?? 3;
                    if (_entries.Count > max)
                    {
                        var victim = SelectVictim();
                        if (victim != null)
                        {
                            EvictEntry(victim);
                        }

                        if (_entries.Count > max)
                        {
                            _entries.TryRemove(handle.NormalizedAlias, out _);
                            throw new WorkerPoolFullException(ListOpen());
                        }
                    }
                }

                var worker = new WorkerProcess(_config, handle);
                worker.OnRpcResponse += json => OnRpcResponse?.Invoke(json);
                var capturedHandle = handle;
                worker.OnWorkerExited += () =>
                {
                    OnWorkerExited?.Invoke(capturedHandle);
                    _entries.TryRemove(capturedHandle.NormalizedAlias, out _);
                };
                worker.Start();
                if (worker.SpawnMs.HasValue)
                {
                    Program.OperationTracker.RegisterSpawnSample(handle.NormalizedAlias, worker.SpawnMs.Value);
                }
                entry.Worker = worker;
                entry.LastActivityUtc = DateTime.UtcNow;
                return worker;
            }
            finally
            {
                entry.SpawnGate.Release();
            }
        }

        public bool Close(string alias)
        {
            if (_entries.TryRemove(alias.ToLowerInvariant(), out var entry))
            {
                try { entry.Worker?.Stop(); } catch { }
                return true;
            }

            return false;
        }

        public void StopAll()
        {
            foreach (var e in _entries.Values)
            {
                try { e.Worker?.Stop(); } catch { }
            }
            _entries.Clear();
        }

        private Entry? SelectVictim()
        {
            // PERFORMANCE (G-B1): linear scan for the min LastActivityUtc instead of OrderBy
            // (which materialises the whole sequence into an internal buffer just to take the
            // first element). At MaxOpenKbs=3 the absolute time is irrelevant, but the
            // allocation-free path is friendlier to the eviction hot-spot under future growth.
            Entry? victim = null;
            DateTime oldest = DateTime.MaxValue;
            foreach (var e in _entries.Values)
            {
                if (e.Worker == null) continue;
                if (e.LastActivityUtc < oldest)
                {
                    oldest = e.LastActivityUtc;
                    victim = e;
                }
            }
            return victim;
        }

        private void EvictEntry(Entry entry)
        {
            try { entry.Worker?.Stop(); } catch { }
            _entries.TryRemove(entry.Handle.NormalizedAlias, out _);
        }

        // Item 53: configure warm-spare count + optionally pre-spawn against the
        // supplied declared KBs. Caps at MaxWarmSpareCount. Returns:
        //   configured  – the count actually persisted (clamped + capped),
        //   requested   – the original requested value (so the agent sees the clamp),
        //   capped      – true if the request exceeded MaxWarmSpareCount,
        //   prespawned  – aliases of KBs the gateway successfully pre-spawned a worker for,
        //   skipped     – aliases skipped because they were already open or the spawn failed.
        public WarmSpareResult ConfigureWarmSpares(int requested, System.Collections.Generic.IReadOnlyList<KbHandle> declaredKbs)
        {
            int requestedOrig = requested;
            bool capped = false;
            if (requested < 0) requested = 0;
            if (requested > MaxWarmSpareCount) { requested = MaxWarmSpareCount; capped = true; }
            Interlocked.Exchange(ref _warmSpareCount, requested);

            var prespawned = new System.Collections.Generic.List<string>();
            var skipped = new System.Collections.Generic.List<string>();
            if (requested == 0 || declaredKbs == null)
            {
                return new WarmSpareResult(requestedOrig, requested, capped, prespawned, skipped);
            }

            int budget = requested;
            foreach (var kb in declaredKbs)
            {
                if (budget <= 0) break;
                if (_entries.TryGetValue(kb.NormalizedAlias, out var existing) && existing.Worker != null)
                {
                    skipped.Add(kb.Alias);
                    continue;
                }
                try
                {
                    // Block on AcquireAsync — pre-spawn is a one-shot setup call.
                    var task = AcquireAsync(kb, CancellationToken.None);
                    task.Wait();
                    prespawned.Add(kb.Alias);
                    budget--;
                }
                catch
                {
                    skipped.Add(kb.Alias);
                }
            }
            return new WarmSpareResult(requestedOrig, requested, capped, prespawned, skipped);
        }

        public sealed record WarmSpareResult(
            int Requested,
            int Configured,
            bool Capped,
            System.Collections.Generic.IReadOnlyList<string> Prespawned,
            System.Collections.Generic.IReadOnlyList<string> Skipped);

        internal void RegisterForTest(KbHandle h, DateTime? lastActivity = null)
        {
            _entries[h.NormalizedAlias] = new Entry
            {
                Handle = h,
                LastActivityUtc = lastActivity ?? DateTime.UtcNow
            };
        }

        internal KbHandle? SelectVictimForTest()
        {
            return _entries.Values.OrderBy(e => e.LastActivityUtc).FirstOrDefault()?.Handle;
        }
    }
}
