# Plan 003: Make index flush incremental / sharded instead of full re-serialize

> **Executor instructions**: Follow step by step; verify each. Honor STOP conditions.
> Update `plans/README.md`. **This plan changes the index persistence contract — the
> crash/partial-write recovery paths are load-bearing. Do not start until Plan 001
> (flush-count regression test) is DONE.**
>
> **Drift check**: `git diff --stat b326cd4..HEAD -- src/GxMcp.Worker/Services/IndexCacheService.cs`

## Status

- **Priority**: P2
- **Effort**: L
- **Risk**: MED
- **Depends on**: 001
- **Category**: perf
- **Planned at**: commit `b326cd4`, 2026-07-10

## Why this matters

`FlushToDisk` (`IndexCacheService.cs:1109-1197`) serializes the **entire** `_index`
snapshot through Newtonsoft + GZip on every flush. During bulk enrichment the index
grows monotonically (12MB→45MB observed, per the comment at `:853-857`), so each of
the `wall_clock / throttle` flushes re-encodes a bigger structure than the last. The
enrichment wall time ends up dominated by repeated full-snapshot encoding rather than
the enrichment work, and it contends with the single STA thread doing SDK reads. This
is the maintainer's #1 pain area (index build degrades on larger KBs).

## Current state

- `FlushToDisk` at `IndexCacheService.cs:1109`: full serialize → GZip → atomic tmp+move.
- Dirty/flushed generation counters (`DirtyGeneration`/`FlushedGeneration`/`IsFullyFlushed`,
  `:39-40`) and `FlushNow` (`:922`) certify "everything dirty is on disk".
- Warm start reads the snapshot and applies a delta (see the delta-on-open path;
  grep `delta` in this file).
- Plan 001 added `FlushWriteCountForTest` and a bound test.

## Approach (choose ONE; (b) preferred)

- **(a) Append-only delta log**: each flush appends only entries dirtied since the last
  flush to a delta file; periodic compaction merges deltas into the base snapshot.
- **(b) Sharded snapshot** (preferred): partition the on-disk cache into shards (e.g.
  by object type, or fixed N-object buckets). A flush re-serializes only shards
  containing dirty entries. Warm start loads all shards.

Whichever you pick, the `DirtyGeneration`/`FlushedGeneration` certification and the
atomic tmp+move crash-safety must be preserved per shard/segment.

## Commands

| Purpose | Command | Expected |
|---|---|---|
| Set SDK ref | `$env:GX_PATH = 'C:\Program Files (x86)\GeneXus\GeneXus18'` | — |
| Build | `dotnet build src\GxMcp.Worker\GxMcp.Worker.csproj -v:minimal` | 0 errors |
| Test | `dotnet test src\GxMcp.Worker.Tests\GxMcp.Worker.Tests.csproj` | all pass |

## Scope

**In scope:** `IndexCacheService.cs` persistence internals; new tests.
**Out of scope:** the in-memory index shape used by Search/List; the enrichment logic
itself; the tool surface.

## Steps

1. **Add a shard/segment layout** behind the existing flush API so callers
   (`ScheduleThrottledFlush`, `FlushNow`, warm-start load) are unchanged externally.
2. **Track dirty shards** — a set of shard ids dirtied since last flush; a flush writes
   only those (atomic tmp+move per shard), then clears the set and advances the
   flushed generation.
3. **Warm-start load** reads every shard and reconstructs `_index`; keep the existing
   delta-on-open behavior working (or fold it into the shard model).
4. **Backward compat**: on load, if a legacy single-file snapshot is present and no
   shards exist, read it and re-emit as shards on the next flush.

**Verify after each step**: build 0 errors; Plan 001's `IndexFlushBoundTests` green;
add a test that dirtying entries in one shard rewrites only that shard's file
(assert file mtimes / a per-shard write counter).

## Test plan

- Shard-isolation: dirty one shard → only that shard file rewritten.
- Crash-safety: simulate an interrupted flush (write tmp, don't move) → load recovers
  the last good shard, `FlushNow` re-certifies.
- Legacy-load: a pre-existing single-file snapshot loads and migrates to shards.
- Warm-start round-trip: flush, reload from disk, assert index content identical.

## Done criteria

- [ ] Build 0 errors; full Worker suite green (4 known skips)
- [ ] Plan 001 bound test still green; new shard-isolation test proves partial rewrite
- [ ] A bulk-enrichment benchmark (see `src/GxMcp.Benchmarks`) shows flush bytes written
      scale with *dirty* entries, not total index size — record before/after in the PR
- [ ] Warm start from both legacy and sharded on-disk formats works

## STOP conditions

- Plan 001 is not DONE (no safety net) — stop.
- The delta-on-open path can't be reconciled with sharding without a redesign larger
  than this plan — report with what you found; the maintainer may re-scope.
- Any crash-recovery test regresses.

## Maintenance notes

- Reviewer scrutiny: the certification invariant (`FlushNow` must never return true
  unless every dirty entry is durably on disk) is the thing that prevents warm-start
  from silently skipping changes forever. Do not weaken it.
