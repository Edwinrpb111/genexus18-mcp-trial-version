# Plan 001: Instrument index flush count and add a regression test that bounds it

> **Executor instructions**: Follow step by step. Run every verification command and
> confirm the expected result before moving on. Touch only in-scope files. If a STOP
> condition occurs, stop and report. Update this plan's status row in `plans/README.md`
> when done (unless a reviewer told you they own the index).
>
> **Drift check (run first)**: `git diff --stat b326cd4..HEAD -- src/GxMcp.Worker/Services/IndexCacheService.cs`
> If it changed since this plan was written, compare the "Current state" excerpts to the
> live code before proceeding; on a mismatch, treat it as a STOP condition.

## Status

- **Priority**: P1
- **Effort**: S
- **Risk**: LOW
- **Depends on**: none
- **Category**: tests
- **Planned at**: commit `b326cd4`, 2026-07-10

## Why this matters

`IndexCacheService` has twice regressed into re-serializing the entire on-disk index
far too often during bulk enrichment (documented in code comments at
`IndexCacheService.cs:853-857` and around `:1613-1618`, e.g. "261 unthrottled full
serializations", "12MB→45MB"). The only guard today is `ScheduleThrottledFlush`'s
runtime time-check plus prose comments — there is no test asserting the flush count
stays bounded. That's the safety net Plans 003 and 004 need before they touch the
persistence path. This plan adds the instrumentation hook and the regression test;
it does not change flush behavior.

## Current state

- `src/GxMcp.Worker/Services/IndexCacheService.cs`
  - `internal void SetFlushThrottleForTest(double seconds)` at `:864` already exists.
  - `internal void ScheduleThrottledFlush()` at `:869`:
    ```csharp
    if (!_savingInProgress && (DateTime.Now - _lastFlushTime).TotalSeconds > _flushThrottleSeconds)
    {
        Task.Run(() => FlushToDisk());
        return;
    }
    ArmTrailingFlush();
    ```
  - `private bool FlushToDisk()` at `:1109` returns `true` when it actually wrote,
    `false` when it no-ops (another flush in flight / nothing dirty).
  - Dirty/flushed generation counters exist: `DirtyGeneration`, `FlushedGeneration`
    (`:39`), `IsFullyFlushed` (`:40`).
- Test project: `src/GxMcp.Worker.Tests` (net48, xUnit). `InternalsVisibleTo` is set
  for `GxMcp.Worker.Tests` (`src/GxMcp.Worker/Properties/AssemblyInfo.cs:3`), so
  internal members are visible to tests.

## Commands you will need

| Purpose | Command | Expected |
|---|---|---|
| Set SDK ref | `$env:GX_PATH = 'C:\Program Files (x86)\GeneXus\GeneXus18'` | — |
| Build | `dotnet build src\GxMcp.Worker\GxMcp.Worker.csproj -v:minimal` | 0 errors |
| Test | `dotnet test src\GxMcp.Worker.Tests\GxMcp.Worker.Tests.csproj --filter "FullyQualifiedName~IndexFlushBoundTests"` | all pass |

## Scope

**In scope:**
- `src/GxMcp.Worker/Services/IndexCacheService.cs` — add a test-only flush counter.
- `src/GxMcp.Worker.Tests/IndexFlushBoundTests.cs` (create).

**Out of scope:**
- Any change to *when* flushing happens (that's Plan 003). This plan only observes.
- The serialization format / sidecar logic.

## Steps

### Step 1: Add a test-only flush counter

In `IndexCacheService`, add an internal counter incremented once inside `FlushToDisk`
each time it performs a real write (the path that returns `true`), plus an internal
reader and reset:

```csharp
private long _flushWriteCount; // test observability only
internal long FlushWriteCountForTest => System.Threading.Interlocked.Read(ref _flushWriteCount);
internal void ResetFlushWriteCountForTest() => System.Threading.Interlocked.Exchange(ref _flushWriteCount, 0);
```

Increment `_flushWriteCount` via `Interlocked.Increment` at the point in `FlushToDisk`
where a write is committed (immediately before the `return true` for the success
path). Do NOT increment on the no-op/early-return paths.

**Verify**: `dotnet build src\GxMcp.Worker\GxMcp.Worker.csproj -v:minimal` → 0 errors.

### Step 2: Write the regression test

Create `IndexFlushBoundTests.cs`. Build a small in-memory index (follow an existing
`IndexCacheService` test for construction — search the test project for how other
tests instantiate it and point it at a temp KB dir). Then:

1. `SetFlushThrottleForTest(large)` — e.g. 3600 seconds, so the immediate-flush branch
   is never taken and everything routes through the trailing timer.
2. `ResetFlushWriteCountForTest()`.
3. Drive N (e.g. 200) `MarkDirty()` + `ScheduleThrottledFlush()` cycles in a tight loop.
4. Assert `FlushWriteCountForTest` is bounded (≤ 2) immediately after the loop —
   the throttle must coalesce, not flush per call.
5. Then `SetFlushThrottleForTest(0)` and call `FlushNow()`; assert `IsFullyFlushed`.

**Verify**: `dotnet test ... --filter "FullyQualifiedName~IndexFlushBoundTests"` →
all pass. If the count assertion fails at HEAD, that means the throttle is already
broken — STOP and report (that's a live bug, not a test-authoring problem).

## Test plan

- `FlushCount_StaysBounded_UnderBurstOfDirtyUpdates` — the core assertion above.
- `FlushNow_Certifies_AllDirtyState` — sanity that forced flush still works.
- Model construction after an existing test in `src/GxMcp.Worker.Tests` that uses
  `IndexCacheService` with a temp directory.

## Done criteria

- [ ] `dotnet build src\GxMcp.Worker\GxMcp.Worker.csproj` exits 0
- [ ] New `IndexFlushBoundTests` pass
- [ ] Full suite still green: `dotnet test src\GxMcp.Worker.Tests\GxMcp.Worker.Tests.csproj` (allow the 4 known skips)
- [ ] Only in-scope files modified (`git status`)

## STOP conditions

- The bound assertion fails at current HEAD (throttle already regressed — report it).
- `FlushToDisk`'s success path isn't a single identifiable `return true` (structure
  drifted) — report rather than guessing where to increment.

## Maintenance notes

- Plans 003 and 004 must keep this test green. If the flush model legitimately changes
  (e.g. sharded flushes), update the bound, don't delete the test.
