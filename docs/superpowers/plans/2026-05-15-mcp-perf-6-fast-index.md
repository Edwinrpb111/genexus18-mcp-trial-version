# Sub-plan 6 — Fast Index (Lite Pass + Lazy Enrichment)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans. Steps use `- [ ]` syntax.

**Goal:** Split the monolithic `KbService.BulkIndex` into two phases so `genexus_list_objects`, `genexus_read`, and `genexus_inspect` become usable in ≤45s on a 38k-object KB (today they wait 5-15 minutes). Phase 1 (lite pass) captures only minimal metadata per object (Guid, Name, Type, ParentPath, Path). Phase 2 (lazy enrichment) fills `SourceSnippet`, `Calls`, `CalledBy`, complexity, etc. in the background, with on-demand promotion for entries a tool actively needs.

**Architecture:** Three new components in `src/GxMcp.Worker/Services/`: (a) `LiteIndexBuilder` runs the fast pass; (b) `EnrichmentQueue` is a bounded background worker that drains pending entries with low priority; (c) `IndexEntryEnricher` is the unit that turns a lite entry into a full one. `KbService.BulkIndex` becomes a thin shell that orchestrates these, gated by a new config flag `Indexing.UseLitePass` (default `true`). The old monolithic body is preserved under `BulkIndexLegacy()` and selected when `UseLitePass=false`, so we keep a one-release fallback. `IndexState` gains two new states: `LiteReady` and `Enriching`. `AnalyzeService.ImpactAnalysis` is taught to call `IndexEntryEnricher.EnrichOnDemand(target)` instead of blocking on `Ready` — this gives the LLM impact analysis within seconds of the lite pass completing, even when full enrichment has many minutes left.

**Tech Stack:** .NET Framework 4.8 (Worker), `System.Threading.Channels` (already used by `WorkerProcess`), `System.Diagnostics.Stopwatch`, xUnit. **Depends on sub-plan 5** (the progress emission uses the `ProgressContext`/`ProgressEmitter` introduced there).

---

## File Structure

- **Create:** `src/GxMcp.Worker/Services/LiteIndexBuilder.cs`
- **Create:** `src/GxMcp.Worker/Services/IndexEntryEnricher.cs`
- **Create:** `src/GxMcp.Worker/Services/EnrichmentQueue.cs`
- **Modify:** `src/GxMcp.Worker/Models/SearchIndex.cs` (add `IsEnriched` flag to `IndexEntry`)
- **Modify:** `src/GxMcp.Worker/Services/KbService.cs` (orchestrate lite + lazy; preserve legacy path)
- **Modify:** `src/GxMcp.Worker/Services/IndexCacheService.cs` (new states `LiteReady`/`Enriching`)
- **Modify:** `src/GxMcp.Worker/Services/AnalyzeService.cs` (`ImpactAnalysis` uses on-demand enrichment)
- **Modify:** `src/GxMcp.Worker/App.config` (new `Indexing.UseLitePass` key)
- **Modify:** `src/GxMcp.Worker/Configuration.cs` (parse the flag — locate config-loading code)
- **Modify:** `src/GxMcp.Gateway/Routers/SystemRouter.cs` (no signature change; just confirm `index force=true` still works on the new path)
- **Create:** `src/GxMcp.Worker.Tests/LiteIndexBuilderTests.cs`
- **Create:** `src/GxMcp.Worker.Tests/EnrichmentQueueTests.cs`
- **Create:** `src/GxMcp.Worker.Tests/IndexStateTransitionTests.cs`
- **Modify:** `CHANGELOG.md`

---

### Task 1: Extend `IndexEntry` with `IsEnriched` flag

**Files:**
- Modify: `src/GxMcp.Worker/Models/SearchIndex.cs`

- [ ] **Step 1: Read the current `IndexEntry` definition**

```
grep -n "class IndexEntry\|public string SourceSnippet\|public List<string> Calls" src/GxMcp.Worker/Models/SearchIndex.cs
```

- [ ] **Step 2: Write the failing test**

Create `src/GxMcp.Worker.Tests/IndexEntryEnrichmentTests.cs`:

```csharp
using GxMcp.Worker.Models;
using Xunit;

namespace GxMcp.Worker.Tests
{
    public class IndexEntryEnrichmentTests
    {
        [Fact]
        public void IndexEntry_NewInstance_IsNotEnriched()
        {
            var entry = new IndexEntry { Name = "Foo", Type = "Procedure" };
            Assert.False(entry.IsEnriched);
        }

        [Fact]
        public void IndexEntry_AfterEnrichment_FlagIsTrue()
        {
            var entry = new IndexEntry { Name = "Foo", Type = "Procedure" };
            entry.IsEnriched = true;
            Assert.True(entry.IsEnriched);
        }
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

```
dotnet test src/GxMcp.Worker.Tests --filter "FullyQualifiedName~IndexEntryEnrichmentTests" --nologo --verbosity minimal
```

Expected: FAIL — `IsEnriched` does not exist.

- [ ] **Step 4: Add the property**

Inside `class IndexEntry` in `src/GxMcp.Worker/Models/SearchIndex.cs`, add:

```csharp
        // Set to true once Calls/CalledBy/SourceSnippet/Complexity have been populated.
        // Lite-pass entries set this to false; the lazy enrichment phase flips it to true.
        public bool IsEnriched { get; set; }
```

- [ ] **Step 5: Run test to verify it passes**

```
dotnet test src/GxMcp.Worker.Tests --filter "FullyQualifiedName~IndexEntryEnrichmentTests" --nologo --verbosity minimal
```

Expected: PASS.

- [ ] **Step 6: Commit**

```
git add src/GxMcp.Worker/Models/SearchIndex.cs src/GxMcp.Worker.Tests/IndexEntryEnrichmentTests.cs
git commit -m "feat(index): add IsEnriched flag to IndexEntry"
```

---

### Task 2: Implement `LiteIndexBuilder`

**Files:**
- Create: `src/GxMcp.Worker/Services/LiteIndexBuilder.cs`

- [ ] **Step 1: Write the failing test**

Create `src/GxMcp.Worker.Tests/LiteIndexBuilderTests.cs`:

```csharp
using GxMcp.Worker.Models;
using GxMcp.Worker.Services;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace GxMcp.Worker.Tests
{
    public class LiteIndexBuilderTests
    {
        [Fact]
        public void Build_ProducesLiteEntries_ForEachKbObject()
        {
            var objects = new List<KbObjectStub>
            {
                new() { Guid = System.Guid.NewGuid(), Name = "InvoiceProc", Type = "Procedure", ParentPath = "Main/Procs" },
                new() { Guid = System.Guid.NewGuid(), Name = "OrderTrn",    Type = "Transaction", ParentPath = "Main/Trns" }
            };

            var builder = new LiteIndexBuilder();
            var entries = builder.Build(objects).ToList();

            Assert.Equal(2, entries.Count);
            Assert.All(entries, e =>
            {
                Assert.False(e.IsEnriched, "Lite entries must not be marked enriched");
                Assert.Null(e.SourceSnippet);
                Assert.True(e.Calls == null || e.Calls.Count == 0);
                Assert.True(e.CalledBy == null || e.CalledBy.Count == 0);
            });
            Assert.Contains(entries, e => e.Name == "InvoiceProc" && e.Type == "Procedure");
        }

        [Fact]
        public void Build_TimingTarget_CompletesUnder1Second_For1000Stubs()
        {
            var objects = Enumerable.Range(0, 1000).Select(i => new KbObjectStub
            {
                Guid = System.Guid.NewGuid(),
                Name = $"Obj{i}",
                Type = "Procedure",
                ParentPath = "Main"
            }).ToList();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var entries = new LiteIndexBuilder().Build(objects).ToList();
            sw.Stop();

            Assert.Equal(1000, entries.Count);
            Assert.True(sw.ElapsedMilliseconds < 1000,
                $"LiteIndexBuilder should process 1000 stubs in <1s; took {sw.ElapsedMilliseconds}ms");
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet test src/GxMcp.Worker.Tests --filter "FullyQualifiedName~LiteIndexBuilderTests" --nologo --verbosity minimal
```

Expected: FAIL — `LiteIndexBuilder` and `KbObjectStub` do not exist.

- [ ] **Step 3: Implement `LiteIndexBuilder` and the `KbObjectStub`**

Create `src/GxMcp.Worker/Services/LiteIndexBuilder.cs`:

```csharp
using System;
using System.Collections.Generic;
using GxMcp.Worker.Models;

namespace GxMcp.Worker.Services
{
    public interface IKbObjectInfo
    {
        Guid Guid { get; }
        string Name { get; }
        string Type { get; }
        string ParentPath { get; }
        string Path { get; }
        string Description { get; }
        string Module { get; }
    }

    // Test/utility shim implementing the same surface as the real SDK KBObject view.
    public class KbObjectStub : IKbObjectInfo
    {
        public Guid Guid { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string ParentPath { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Module { get; set; } = string.Empty;
    }

    public class LiteIndexBuilder
    {
        public IEnumerable<IndexEntry> Build(IEnumerable<IKbObjectInfo> objects)
        {
            foreach (var obj in objects)
            {
                yield return new IndexEntry
                {
                    Guid = obj.Guid,
                    Name = obj.Name,
                    Type = obj.Type,
                    ParentPath = obj.ParentPath,
                    Path = string.IsNullOrEmpty(obj.Path) ? obj.ParentPath + "/" + obj.Name : obj.Path,
                    Description = obj.Description,
                    Module = obj.Module,
                    IsEnriched = false
                };
            }
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```
dotnet test src/GxMcp.Worker.Tests --filter "FullyQualifiedName~LiteIndexBuilderTests" --nologo --verbosity minimal
```

Expected: both PASS.

- [ ] **Step 5: Commit**

```
git add src/GxMcp.Worker/Services/LiteIndexBuilder.cs src/GxMcp.Worker.Tests/LiteIndexBuilderTests.cs
git commit -m "feat(index): add LiteIndexBuilder for fast metadata-only pass"
```

---

### Task 3: Implement `IndexEntryEnricher`

**Files:**
- Create: `src/GxMcp.Worker/Services/IndexEntryEnricher.cs`

This service takes a lite `IndexEntry` plus its underlying KB object handle and fills in `SourceSnippet`, `Calls`, `CalledBy`, `Complexity`. The existing `IndexCacheService.UpdateEntry(obj)` already performs that work — the enricher wraps it so other services can call it on a single entry rather than re-running the whole bulk loop.

- [ ] **Step 1: Write the failing test**

Create `src/GxMcp.Worker.Tests/IndexEntryEnricherTests.cs`:

```csharp
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
            var entry = new IndexEntry { Name = "InvoiceProc", IsEnriched = false };
            bool delegateRan = false;

            var enricher = new IndexEntryEnricher(e =>
            {
                delegateRan = true;
                e.SourceSnippet = "/* source */";
                e.Calls = new System.Collections.Generic.List<string> { "Sub1" };
                e.CalledBy = new System.Collections.Generic.List<string> { "Web1" };
                e.Complexity = 12;
            });

            enricher.Enrich(entry);

            Assert.True(delegateRan);
            Assert.True(entry.IsEnriched);
            Assert.Equal("/* source */", entry.SourceSnippet);
            Assert.Single(entry.Calls);
            Assert.Equal(12, entry.Complexity);
        }

        [Fact]
        public void Enrich_IsNoOp_WhenEntryAlreadyEnriched()
        {
            var entry = new IndexEntry { Name = "X", IsEnriched = true, SourceSnippet = "existing" };
            int calls = 0;

            var enricher = new IndexEntryEnricher(_ => calls++);
            enricher.Enrich(entry);

            Assert.Equal(0, calls);
            Assert.Equal("existing", entry.SourceSnippet);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet test src/GxMcp.Worker.Tests --filter "FullyQualifiedName~IndexEntryEnricherTests" --nologo --verbosity minimal
```

Expected: FAIL.

- [ ] **Step 3: Implement the enricher**

Create `src/GxMcp.Worker/Services/IndexEntryEnricher.cs`:

```csharp
using System;
using GxMcp.Worker.Models;

namespace GxMcp.Worker.Services
{
    public class IndexEntryEnricher
    {
        private readonly Action<IndexEntry> _enrichDelegate;

        public IndexEntryEnricher(Action<IndexEntry> enrichDelegate)
        {
            _enrichDelegate = enrichDelegate ?? throw new ArgumentNullException(nameof(enrichDelegate));
        }

        public void Enrich(IndexEntry entry)
        {
            if (entry == null) return;
            if (entry.IsEnriched) return;

            _enrichDelegate(entry);
            entry.IsEnriched = true;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```
dotnet test src/GxMcp.Worker.Tests --filter "FullyQualifiedName~IndexEntryEnricherTests" --nologo --verbosity minimal
```

Expected: both PASS.

- [ ] **Step 5: Commit**

```
git add src/GxMcp.Worker/Services/IndexEntryEnricher.cs src/GxMcp.Worker.Tests/IndexEntryEnricherTests.cs
git commit -m "feat(index): add IndexEntryEnricher single-entry promotion helper"
```

---

### Task 4: Implement `EnrichmentQueue`

**Files:**
- Create: `src/GxMcp.Worker/Services/EnrichmentQueue.cs`

- [ ] **Step 1: Write the failing test**

Create `src/GxMcp.Worker.Tests/EnrichmentQueueTests.cs`:

```csharp
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
                queue.Enqueue(new IndexEntry { Name = $"E{i}" });
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
                System.Threading.Thread.Sleep(2); // simulate work to keep ordering observable
            });

            var queue = new EnrichmentQueue(enricher);
            for (int i = 0; i < 50; i++)
            {
                queue.Enqueue(new IndexEntry { Name = $"low{i}" });
            }

            var hot = new IndexEntry { Name = "HOT" };
            await queue.PromoteAsync(hot);

            Assert.True(hot.IsEnriched);
            Assert.Contains("HOT", enriched);
            // No guarantee HOT is first overall, but it must enrich before the queue is fully drained.
            int hotIdx = enriched.IndexOf("HOT");
            Assert.InRange(hotIdx, 0, 25);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet test src/GxMcp.Worker.Tests --filter "FullyQualifiedName~EnrichmentQueueTests" --nologo --verbosity minimal
```

Expected: FAIL.

- [ ] **Step 3: Implement the queue**

Create `src/GxMcp.Worker/Services/EnrichmentQueue.cs`:

```csharp
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using GxMcp.Worker.Models;

namespace GxMcp.Worker.Services
{
    public class EnrichmentQueue
    {
        private readonly IndexEntryEnricher _enricher;
        private readonly ConcurrentQueue<IndexEntry> _queue = new ConcurrentQueue<IndexEntry>();
        private readonly SemaphoreSlim _enrichGate = new SemaphoreSlim(1, 1);
        private int _pendingCount;

        public EnrichmentQueue(IndexEntryEnricher enricher)
        {
            _enricher = enricher;
        }

        public int PendingCount => Volatile.Read(ref _pendingCount);

        public void Enqueue(IndexEntry entry)
        {
            if (entry == null || entry.IsEnriched) return;
            _queue.Enqueue(entry);
            Interlocked.Increment(ref _pendingCount);
        }

        public async Task DrainAsync(CancellationToken cancellationToken = default)
        {
            while (_queue.TryDequeue(out var entry))
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

        // Cuts the queue to enrich `entry` immediately. Other in-flight enrichments still finish first
        // because they hold the gate, but the promoted entry runs before the next FIFO dequeue.
        public async Task PromoteAsync(IndexEntry entry, CancellationToken cancellationToken = default)
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
```

- [ ] **Step 4: Run tests to verify they pass**

```
dotnet test src/GxMcp.Worker.Tests --filter "FullyQualifiedName~EnrichmentQueueTests" --nologo --verbosity minimal
```

Expected: both PASS.

- [ ] **Step 5: Commit**

```
git add src/GxMcp.Worker/Services/EnrichmentQueue.cs src/GxMcp.Worker.Tests/EnrichmentQueueTests.cs
git commit -m "feat(index): add EnrichmentQueue with FIFO drain and on-demand promotion"
```

---

### Task 5: Add new `IndexState` values `LiteReady` and `Enriching`

**Files:**
- Modify: `src/GxMcp.Worker/Services/IndexCacheService.cs`

- [ ] **Step 1: Locate the state enum / strings**

```
grep -n "Cold\|Reindexing\|Ready\|IndexState" src/GxMcp.Worker/Services/IndexCacheService.cs | head -20
```

The state today is a string ("Cold" / "Reindexing" / "Ready"). We add two states.

- [ ] **Step 2: Write the failing test**

Create `src/GxMcp.Worker.Tests/IndexStateTransitionTests.cs`:

```csharp
using GxMcp.Worker.Services;
using Xunit;

namespace GxMcp.Worker.Tests
{
    public class IndexStateTransitionTests
    {
        [Fact]
        public void MarkLitePassComplete_TransitionsToLiteReady()
        {
            var cache = IndexCacheServiceTestHarness.NewEmpty();
            cache.MarkReindexStarted(100);
            cache.MarkLitePassComplete(100);

            Assert.Equal("LiteReady", cache.GetState().Status);
        }

        [Fact]
        public void MarkEnrichmentStarted_TransitionsToEnriching()
        {
            var cache = IndexCacheServiceTestHarness.NewEmpty();
            cache.MarkLitePassComplete(100);
            cache.MarkEnrichmentStarted();

            Assert.Equal("Enriching", cache.GetState().Status);
        }

        [Fact]
        public void MarkIndexComplete_FromEnriching_TransitionsToReady()
        {
            var cache = IndexCacheServiceTestHarness.NewEmpty();
            cache.MarkLitePassComplete(100);
            cache.MarkEnrichmentStarted();
            cache.MarkIndexComplete(100);

            Assert.Equal("Ready", cache.GetState().Status);
        }
    }
}
```

`IndexCacheServiceTestHarness.NewEmpty()` should return a fresh `IndexCacheService` configured for in-memory operation. If a test harness for `IndexCacheService` does not already exist, add a static helper in the test project that builds one with the minimum dependencies (refer to existing tests in `src/GxMcp.Worker.Tests/` that already construct an `IndexCacheService`).

- [ ] **Step 3: Run tests to verify they fail**

```
dotnet test src/GxMcp.Worker.Tests --filter "FullyQualifiedName~IndexStateTransitionTests" --nologo --verbosity minimal
```

Expected: FAIL — new methods do not exist.

- [ ] **Step 4: Add the new state methods**

Inside `class IndexCacheService` in `src/GxMcp.Worker/Services/IndexCacheService.cs`, alongside the existing `MarkReindexStarted` / `MarkReindexProgress` / `MarkIndexComplete` methods, add:

```csharp
        public void MarkLitePassComplete(int totalObjects)
        {
            lock (_stateLock)
            {
                _state.Status = "LiteReady";
                _state.TotalObjects = totalObjects;
                _state.LitePassCompletedUtc = System.DateTime.UtcNow;
                _state.Progress = 1.0;
                _state.EtaMs = 0;
                OnStateChanged();
            }
        }

        public void MarkEnrichmentStarted()
        {
            lock (_stateLock)
            {
                _state.Status = "Enriching";
                _state.EnrichmentStartedUtc = System.DateTime.UtcNow;
                OnStateChanged();
            }
        }
```

Add fields `LitePassCompletedUtc` and `EnrichmentStartedUtc` to the `IndexState` POCO (search the file for `class IndexState` or `public string Status`).

- [ ] **Step 5: Make sure `MarkIndexComplete` works from `Enriching`**

Confirm the existing `MarkIndexComplete` does not reject the transition from `Enriching` — if it has a guard, relax it to accept `Reindexing`, `Enriching`, or `LiteReady`.

- [ ] **Step 6: Run tests to verify they pass**

```
dotnet test src/GxMcp.Worker.Tests --filter "FullyQualifiedName~IndexStateTransitionTests" --nologo --verbosity minimal
```

Expected: all 3 PASS.

- [ ] **Step 7: Commit**

```
git add src/GxMcp.Worker/Services/IndexCacheService.cs src/GxMcp.Worker.Tests/IndexStateTransitionTests.cs
git commit -m "feat(index): add LiteReady and Enriching states to IndexCacheService"
```

---

### Task 6: Orchestrate the two phases inside `KbService.BulkIndex`

**Files:**
- Modify: `src/GxMcp.Worker/Services/KbService.cs` (lines 117-273 area)
- Modify: `src/GxMcp.Worker/App.config` (new `Indexing.UseLitePass` key)
- Modify: `src/GxMcp.Worker/Configuration.cs` (parse the flag)

- [ ] **Step 1: Add the config flag**

In `src/GxMcp.Worker/App.config`, inside `<appSettings>`, add:

```xml
    <add key="Indexing.UseLitePass" value="true" />
```

In `src/GxMcp.Worker/Configuration.cs` (or wherever app-settings are exposed; search for `ConfigurationManager.AppSettings` if uncertain), add:

```csharp
        public static bool UseLitePass
        {
            get
            {
                var raw = System.Configuration.ConfigurationManager.AppSettings["Indexing.UseLitePass"];
                return string.IsNullOrWhiteSpace(raw) || string.Equals(raw, "true", System.StringComparison.OrdinalIgnoreCase);
            }
        }
```

- [ ] **Step 2: Rename the existing `BulkIndex` body to `BulkIndexLegacy`**

In `src/GxMcp.Worker/Services/KbService.cs`, copy lines 117-273 verbatim into a new method `private string BulkIndexLegacy(bool force)` placed immediately below the existing `BulkIndex`. **Do not delete the original yet** — the next step makes it call out to either path.

- [ ] **Step 3: Replace the body of `BulkIndex(bool force)` with the orchestrator**

Replace the original `BulkIndex(bool force)` body with:

```csharp
        public string BulkIndex(bool force)
        {
            if (!Configuration.UseLitePass)
            {
                return BulkIndexLegacy(force);
            }

            Logger.Info($"BulkIndex(force={force}) requested — fast index path.");
            if (_isIndexing) return "{\"status\":\"Already in progress\"}";

            // Wait briefly for the KB to open (same warm-up window as legacy path).
            int waitMs = 0;
            while (waitMs < 15000 && !_indexCacheService.IsInitialized)
            {
                Thread.Sleep(200);
                waitMs += 200;
            }

            if (force)
            {
                try
                {
                    _indexCacheService.Clear();
                    _indexCacheService.DeleteOnDiskSnapshot();
                    _indexCacheService.MarkReindexStarted(0);
                }
                catch (Exception ex) { Logger.Warn("BulkIndex(fast) force-clear failed: " + ex.Message); }
            }
            else if (!_indexCacheService.IsIndexMissing)
            {
                var loaded = _indexCacheService.GetIndex();
                if (loaded != null && loaded.Objects.Count > 0)
                {
                    try { _indexCacheService.MarkIndexComplete(loaded.Objects.Count); } catch { }
                    return "{\"status\":\"AlreadyIndexed\",\"objects\":" + loaded.Objects.Count + ",\"path\":\"fast\"}";
                }
            }

            _isIndexing = true;
            _processedCount = 0;
            _totalCount = 0;

            var bulkSw = Stopwatch.StartNew();

            var liteThread = new Thread(() =>
            {
                try
                {
                    var liteSw = Stopwatch.StartNew();
                    dynamic kb = GetKB();
                    if (kb == null)
                    {
                        _isIndexing = false;
                        return;
                    }

                    var objectList = (System.Collections.IEnumerable)kb.DesignModel.Objects;
                    var snapshot = new List<KeyValuePair<Guid, string>>();
                    var liteEntries = new List<IndexEntry>();

                    foreach (global::Artech.Architecture.Common.Objects.KBObject obj in objectList)
                    {
                        snapshot.Add(new KeyValuePair<Guid, string>(obj.Guid, obj.Name));
                        _totalCount++;

                        liteEntries.Add(new IndexEntry
                        {
                            Guid = obj.Guid,
                            Name = obj.Name,
                            Type = obj.GetType().Name,
                            ParentPath = SafeParentPath(obj),
                            Path = SafePath(obj),
                            Description = SafeDescription(obj),
                            Module = SafeModule(obj),
                            IsEnriched = false
                        });

                        if (_totalCount % 500 == 0) Thread.Sleep(1);

                        if (_totalCount % 1000 == 0)
                        {
                            GxMcp.Worker.Helpers.ProgressEmitter.Emit(
                                progress: System.Math.Min(_totalCount, 50000),
                                total: 50000,
                                message: $"Lite-index pass: {_totalCount} objects captured");
                        }
                    }

                    _indexCacheService.ReplaceAll(liteEntries);
                    _indexCacheService.MarkLitePassComplete(_totalCount);
                    liteSw.Stop();
                    Logger.Info($"[BULK-INDEX-LITE] elapsedMs={liteSw.ElapsedMilliseconds} objects={_totalCount}");

                    // Kick off lazy enrichment on a background thread; do not block the lite pass.
                    var enrichThread = new Thread(() =>
                    {
                        try
                        {
                            _indexCacheService.MarkEnrichmentStarted();
                            var enricher = new IndexEntryEnricher(e =>
                            {
                                try
                                {
                                    var obj = kb.DesignModel.Objects.Get(e.Guid);
                                    if (obj == null) return;
                                    _indexCacheService.UpdateEntry(obj); // existing full enrichment
                                }
                                catch (Exception ex) { Logger.Warn($"Enrich {e.Name} failed: {ex.Message}"); }
                            });

                            var queue = new EnrichmentQueue(enricher);
                            foreach (var entry in liteEntries) queue.Enqueue(entry);
                            _indexCacheService.SetEnrichmentQueue(queue);

                            queue.DrainAsync().GetAwaiter().GetResult();
                            _indexCacheService.MarkIndexComplete(_totalCount);
                            bulkSw.Stop();
                            Logger.Info($"[BULK-INDEX-FULL] elapsedMs={bulkSw.ElapsedMilliseconds} processed={_totalCount}");
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"[BULK-INDEX-ENRICH-FAIL] error={ex.Message}");
                            try { _indexCacheService.MarkIndexFailed(); } catch { }
                        }
                        finally
                        {
                            _isIndexing = false;
                        }
                    }) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "GxMcp-Enrich" };
                    enrichThread.Start();
                }
                catch (Exception ex)
                {
                    Logger.Error($"[BULK-INDEX-LITE-FAIL] error={ex.Message}");
                    try { _indexCacheService.MarkIndexFailed(); } catch { }
                    _isIndexing = false;
                }
            }) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "GxMcp-Lite" };
            liteThread.Start();

            return "{\"status\":\"LiteStarted\",\"hint\":\"list_objects is usable after a few seconds; analyze impact uses on-demand enrichment.\"}";
        }

        // SafeParentPath / SafePath / SafeDescription / SafeModule wrap try/catch around the
        // SDK property reads — different KBObject subclasses raise different exceptions on
        // some properties, and the lite pass refuses to die for one bad object.
        private static string SafeParentPath(object obj)
        {
            try { return (string)((dynamic)obj).ParentPath ?? string.Empty; } catch { return string.Empty; }
        }
        private static string SafePath(object obj)
        {
            try { return (string)((dynamic)obj).Path ?? string.Empty; } catch { return string.Empty; }
        }
        private static string SafeDescription(object obj)
        {
            try { return (string)((dynamic)obj).Description ?? string.Empty; } catch { return string.Empty; }
        }
        private static string SafeModule(object obj)
        {
            try { return (string)((dynamic)obj).Module?.Name ?? string.Empty; } catch { return string.Empty; }
        }
```

`IndexCacheService.ReplaceAll(IEnumerable<IndexEntry>)` and `IndexCacheService.SetEnrichmentQueue(EnrichmentQueue)` are new helpers — add them:

```csharp
        // In IndexCacheService.cs
        private EnrichmentQueue _enrichmentQueue;

        public void ReplaceAll(System.Collections.Generic.IEnumerable<IndexEntry> entries)
        {
            lock (_indexLock)
            {
                _index = new System.Collections.Concurrent.ConcurrentDictionary<string, IndexEntry>(System.StringComparer.OrdinalIgnoreCase);
                foreach (var entry in entries)
                {
                    _index[entry.Guid.ToString()] = entry;
                }
                _isInitialized = true;
            }
        }

        public void SetEnrichmentQueue(EnrichmentQueue queue) => _enrichmentQueue = queue;
        public EnrichmentQueue GetEnrichmentQueue() => _enrichmentQueue;
```

Mirror the existing access patterns in `IndexCacheService` — if the in-memory dictionary uses a different keying scheme (Name vs Guid), adapt the snippet to match.

- [ ] **Step 4: Build**

```
dotnet build src/GxMcp.Worker --configuration Debug --nologo --verbosity minimal
```

Expected: build green.

- [ ] **Step 5: Run all worker tests**

```
dotnet test src/GxMcp.Worker.Tests --nologo --verbosity minimal
```

Expected: PASS (existing tests should not regress; the legacy path is still reachable).

- [ ] **Step 6: Commit**

```
git add src/GxMcp.Worker/Services/KbService.cs src/GxMcp.Worker/Services/IndexCacheService.cs src/GxMcp.Worker/App.config src/GxMcp.Worker/Configuration.cs
git commit -m "feat(index): split BulkIndex into lite pass + lazy enrichment behind Indexing.UseLitePass flag"
```

---

### Task 7: On-demand promotion in `AnalyzeService.ImpactAnalysis`

**Files:**
- Modify: `src/GxMcp.Worker/Services/AnalyzeService.cs`

- [ ] **Step 1: Locate the wait-for-index gate**

```
grep -n "waitForIndex\|IndexState\|GetState" src/GxMcp.Worker/Services/AnalyzeService.cs | head -10
```

The existing logic blocks on `IndexState.Ready` for up to 30s. We replace that with on-demand promotion when the index is in `LiteReady` or `Enriching`.

- [ ] **Step 2: Replace the gate with on-demand enrichment**

Inside `ImpactAnalysis`, find the wait loop and replace with the following pattern (preserving the `waitForIndex` opt-out and the timeout semantics):

```csharp
            var state = _indexCacheService.GetState();
            if (state.Status == "Cold" || state.Status == "Reindexing")
            {
                // Still in the lite pass — caller must wait or skip.
                if (!waitForIndex)
                {
                    return JsonConvert.SerializeObject(new
                    {
                        status = state.Status,
                        progress = state.Progress,
                        etaMs = state.EtaMs,
                        hint = "Pass waitForIndex=true to block until lite pass completes."
                    });
                }

                int waited = 0;
                while ((_indexCacheService.GetState().Status == "Cold" || _indexCacheService.GetState().Status == "Reindexing") && waited < waitTimeoutMs)
                {
                    Thread.Sleep(250);
                    waited += 250;
                    ct.ThrowIfCancellationRequested();
                }
            }

            // From this point on the lite pass is complete. We do not wait for full enrichment —
            // we enrich exactly the target's reachable graph on demand.
            var targetEntry = _indexCacheService.FindByName(target);
            if (targetEntry == null)
            {
                return JsonConvert.SerializeObject(new
                {
                    status = "Error",
                    error = "Object not found in index",
                    target
                });
            }

            var queue = _indexCacheService.GetEnrichmentQueue();
            if (queue != null && !targetEntry.IsEnriched)
            {
                queue.PromoteAsync(targetEntry, ct).GetAwaiter().GetResult();
            }
```

The remainder of `ImpactAnalysis` (the BFS walk) continues unchanged — except inside the loop, when a neighbour is visited that is still a lite entry, also `queue?.PromoteAsync(neighbour, ct).GetAwaiter().GetResult();` before reading its `Calls`/`CalledBy`.

- [ ] **Step 3: Build and run tests**

```
dotnet build src/GxMcp.Worker --configuration Debug --nologo --verbosity minimal
dotnet test src/GxMcp.Worker.Tests --nologo --verbosity minimal
```

Expected: PASS.

- [ ] **Step 4: Commit**

```
git add src/GxMcp.Worker/Services/AnalyzeService.cs
git commit -m "feat(analyze): ImpactAnalysis enriches only the target's graph on demand"
```

---

### Task 8: End-to-end timing test, CHANGELOG, rollback note

**Files:**
- Modify: `CHANGELOG.md`
- Modify: `docs/superpowers/plans/2026-05-15-mcp-perf-master.md` (re-check rollback section)
- Create: `tests/manual/fast-index-timing.md`

- [ ] **Step 1: Document the manual timing test**

Create `tests/manual/fast-index-timing.md`:

```markdown
# Manual: fast-index timing verification

## Goal
Confirm that on a 38k-object KB, `genexus_list_objects` is usable in ≤45s after a cold start
with `Indexing.UseLitePass=true`.

## Steps

1. Pick a large KB (≥30k objects). Note the object count via the previous
   `[BULK-INDEX]` log line.
2. Clear the on-disk cache: delete `%LOCALAPPDATA%/GxMcp/Cache/index_*.json.gz` for that KB.
3. Start the worker fresh. Time how long the `[BULK-INDEX-LITE]` log line takes to appear.
4. Issue `genexus_list_objects --limit 5` immediately afterwards — it must return real
   objects, not "Reindexing".
5. Issue `genexus_analyze mode=impact target=<RealProc>` — it must return within a few
   seconds, even while `[BULK-INDEX-FULL]` is still pending.
6. Note the wall-clock time on `[BULK-INDEX-FULL]` for comparison with the legacy `[BULK-INDEX]`
   line from the prior baseline run.

## Acceptance
- `[BULK-INDEX-LITE] elapsedMs` ≤ 45000 (45s)
- `genexus_list_objects` returns within 1s after lite completes
- `genexus_analyze mode=impact` returns within 5s for a target with <100 callers

Save the full stdout/stderr capture as `tests/manual/fast-index-timing.log`.
```

- [ ] **Step 2: Add CHANGELOG entry**

```markdown
- **Fast index**: `BulkIndex` is now split into a lite pass (metadata only, ~30-45s on a 38k-object KB)
  followed by background enrichment. `genexus_list_objects`, `genexus_read`, and `genexus_inspect`
  are usable immediately after the lite pass. `genexus_analyze mode=impact` enriches only the
  target's reachable graph on demand, returning in seconds even before full enrichment finishes.
  The legacy monolithic path is preserved one release behind the `Indexing.UseLitePass=false`
  flag in `App.config` for rollback safety.
```

- [ ] **Step 3: Run the full test matrix**

```
dotnet test src/GxMcp.Worker.Tests --nologo --verbosity minimal
dotnet test src/GxMcp.Gateway.Tests --nologo --verbosity minimal
pwsh -NoProfile -File scripts/mcp_smoke.ps1 -BaseUrl http://127.0.0.1:5000/mcp
```

Expected: all green.

- [ ] **Step 4: Live verification (manual)**

Follow `tests/manual/fast-index-timing.md` against a real ≥30k-object KB. Capture the log.

- [ ] **Step 5: Commit**

```
git add CHANGELOG.md tests/manual/fast-index-timing.md tests/manual/fast-index-timing.log
git commit -m "docs: fast index lite+lazy with timing verification artifact"
```

---

## Rollback

If a regression appears in production, set `Indexing.UseLitePass=false` in `App.config` and recycle the worker. The legacy monolithic `BulkIndexLegacy` runs unchanged. Keep the legacy method for **one release** after merge, then remove in the following cycle.

## Done criteria

- [ ] All tasks above completed
- [ ] Worker tests green; Gateway tests green; smoke probe green
- [ ] `tests/manual/fast-index-timing.log` shows lite pass ≤45s on a 38k-object KB
- [ ] `genexus_list_objects --limit 5` returns results within 1s after lite completes
- [ ] `genexus_analyze mode=impact` returns within 5s on a representative target while `[BULK-INDEX-FULL]` is still pending
- [ ] Sub-plan checkpoint signed off
- [ ] Master plan done criteria revisited and ticked
