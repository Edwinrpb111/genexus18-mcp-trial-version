# Plan 004: Reduce per-object COM round-trips in the lite index walk

> **Executor instructions**: This is a **spike + implement** plan — Step 1 is research
> whose outcome decides whether Steps 2+ are viable. Honor the STOP conditions.
> Update `plans/README.md`.
>
> **Drift check**: `git diff --stat b326cd4..HEAD -- src/GxMcp.Worker/Services/KbService.cs`

## Status

- **Priority**: P2
- **Effort**: L
- **Risk**: MED (COM interop is fragile across GeneXus versions)
- **Depends on**: 001
- **Category**: perf
- **Planned at**: commit `b326cd4`, 2026-07-10

## Why this matters

The lite index walk does 5 discrete COM property reads per object
(`KbService.cs:535-585`: `TypeDescriptor.Name`, `Description`, `LastUpdate`,
`VersionDate`, `UserName`), each incurring COM marshalling (~30ms/object per the
comment at `:588-591`; "~108s for the full lite pass"). On a 30-50k-object KB this
serial round-tripping on the single STA thread is the front-door latency users feel
before anything is queryable. If the SDK exposes a cheaper bulk/DTO accessor, the win
is large; if not, we cache the stable fields to avoid re-paying on every reindex.

## Current state

- `KbService.cs:535-585` — the per-object 5-read loop, each wrapped in try/catch.
- Runs on the STA thread inside `SdkGate` (SDK access invariant, see `SdkGate.cs`).

## Steps

### Step 1 (SPIKE): determine whether the SDK offers a cheaper read

Investigate, using `genexus_sdk_probe` / reflection over the loaded `Artech.*`
assemblies, whether `KBObject` (or the enumerator that yields them) exposes:
- a batched property-set accessor, or a lighter projection/DTO, or
- a way to read `Name`/`Description`/`LastUpdate`/`UserName` without 5 separate
  marshalled property gets.

Write findings into this plan file under a "Spike results" heading.

**STOP if** the spike shows no cheaper accessor exists → skip to Step 3 (caching)
only; do NOT attempt to force a batched API that isn't there.

### Step 2 (if spike positive): coalesce the reads

Replace the 5 discrete reads with the batched accessor. Keep the try/catch resilience
per object (a single bad object must not abort the walk).

### Step 3 (fallback): cache stable fields across reindex

`Name`/`TypeDescriptor` rarely change; `LastUpdate`/`VersionDate` change on edit.
Persist the stable fields keyed by object guid so a reindex re-reads only the volatile
ones (or only objects whose `LastUpdate` advanced). Reuse the delta-on-open machinery.

## Commands

| Purpose | Command | Expected |
|---|---|---|
| Set SDK ref | `$env:GX_PATH = 'C:\Program Files (x86)\GeneXus\GeneXus18'` | — |
| Build | `dotnet build src\GxMcp.Worker\GxMcp.Worker.csproj -v:minimal` | 0 errors |
| Test | `dotnet test src\GxMcp.Worker.Tests\GxMcp.Worker.Tests.csproj` | all pass |
| Benchmark | run the index benchmark in `src/GxMcp.Benchmarks` | lite-walk ms recorded |

## Scope

**In scope:** `KbService.cs` lite-walk; caching support in `IndexCacheService`; tests.
**Out of scope:** the flush model (Plan 003); the in-memory index shape.

## Done criteria

- [ ] Spike results written into this file
- [ ] Build 0 errors; full Worker suite green (4 known skips)
- [ ] A KB-version compatibility test (or a documented manual check across at least the
      installed GeneXus 18 build) confirms the read change doesn't throw
- [ ] Benchmark shows lite-walk time per object reduced (record before/after)

## STOP conditions

- Spike shows no batched accessor AND caching would risk staleness (can't reliably
  detect change) → report; do not ship a cache that can serve stale metadata.
- Any COM read change throws on the installed GeneXus 18 build.

## Maintenance notes

- The ~30ms/object figure is from a code comment, not a fresh measurement — re-measure
  with the benchmark before claiming a win, per "medir antes de teorizar".
