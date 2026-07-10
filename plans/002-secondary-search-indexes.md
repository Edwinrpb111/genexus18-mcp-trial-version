# Plan 002: Add secondary type/domain indexes so search & list aren't full O(N) scans

> **Executor instructions**: Follow step by step; run every verification command. Touch
> only in-scope files. Honor STOP conditions. Update the status row in `plans/README.md`.
>
> **Drift check (run first)**: `git diff --stat b326cd4..HEAD -- src/GxMcp.Worker/Services/IndexCacheService.cs src/GxMcp.Worker/Services/SearchService.cs src/GxMcp.Worker/Services/ListService.cs`

## Status

- **Priority**: P1
- **Effort**: M
- **Risk**: LOW-MED
- **Depends on**: none (do before Plan 006)
- **Category**: perf
- **Planned at**: commit `b326cd4`, 2026-07-10

## Why this matters

Every `genexus_query` and `genexus_list_objects` call scans the full in-memory index:
`SearchService.cs:90-292` chains up to 8 sequential `.Where()` predicates over
`index.Objects.Values`, and `ListService.cs:193-470` repeats the pattern. On a large
KB (tens of thousands of objects — the maintainer's flagged worst case) each query is
O(N) regardless of selectivity, and this competes with the single STA thread during
warm-up. The index already maintains secondary structures (`ChildrenByParent`,
`GuidToKey`, and `BuildParentIndex` at `IndexCacheService.cs:469`); this plan extends
that same copy-on-write pattern with Type and BusinessDomain indexes and has the two
read services intersect candidate sets before falling back to a scan for free-text.

## Current state

- `src/GxMcp.Worker/Services/IndexCacheService.cs`
  - `BuildParentIndex` (`:469`) builds `ChildrenByParent`; `AddOrUpdateEntryInParentIndex`
    incrementally maintains it under the existing copy-on-write mutation discipline.
  - `GuidToKey` is another existing secondary dictionary — mirror its lifecycle.
- `src/GxMcp.Worker/Services/SearchService.cs:90-292` — filter chain; the type filter
  goes through an `IsTypeMatch` helper (alias/synonym aware).
- `src/GxMcp.Worker/Services/ListService.cs:193-470` — parallel filter chain; type
  filter uses inline `filterTypes.Contains` (NOTE: confirm whether this differs
  semantically from `IsTypeMatch` before relying on either — see Plan 006).

## Commands you will need

| Purpose | Command | Expected |
|---|---|---|
| Set SDK ref | `$env:GX_PATH = 'C:\Program Files (x86)\GeneXus\GeneXus18'` | — |
| Build | `dotnet build src\GxMcp.Worker\GxMcp.Worker.csproj -v:minimal` | 0 errors |
| Test | `dotnet test src\GxMcp.Worker.Tests\GxMcp.Worker.Tests.csproj` | all pass (4 known skips) |

## Scope

**In scope:** `IndexCacheService.cs` (add Type/Domain indexes + maintenance),
`SearchService.cs` and `ListService.cs` (use them as candidate-set prefilters),
new tests in `src/GxMcp.Worker.Tests`.

**Out of scope:** the on-disk serialized format (these indexes are derived, rebuilt on
load — do NOT persist them). Ranking/scoring logic in SearchService. Plan 006's shared
filter builder (do this first; commonize after).

## Steps

### Step 1: Add derived Type and Domain indexes

Alongside `ChildrenByParent`/`GuidToKey`, add `Dictionary<string, HashSet<string>>`
(case-insensitive) mapping normalized type → object keys and normalized
businessDomain → object keys. Populate them in the same pass as `BuildParentIndex`,
and maintain them in the same add/update/remove hooks that maintain the parent index.
They are **derived state**: rebuild on index load, never serialize.

**Verify**: build 0 errors; add a unit test that after inserting known entries the
type index returns exactly the expected keys.

### Step 2: Use the indexes as prefilters

In `SearchService` and `ListService`, when a type and/or domain filter is present,
start from the intersection of the relevant index buckets instead of
`index.Objects.Values`, then apply the remaining predicates (description/date/parent/
free-text) to that reduced candidate set. When no indexed filter is present, keep the
current full-scan path unchanged.

**Verify**: existing SearchService/ListService tests still pass unchanged (behavior
must be identical — this is a perf change, not a semantics change).

### Step 3: Equivalence test

Add a test that runs the same filters through the indexed path and a brute-force
`Where` over all objects and asserts identical result sets, for: type-only,
domain-only, type+domain, and type+description-substring.

**Verify**: `dotnet test ...` all pass.

## Done criteria

- [ ] Build 0 errors
- [ ] New index-equivalence tests pass; existing Search/List tests unchanged and green
- [ ] Full Worker suite green (4 known skips allowed)
- [ ] Type/Domain indexes are NOT written to disk (grep the serialize path)

## STOP conditions

- The copy-on-write mutation pattern for `ChildrenByParent` isn't clear enough to
  mirror safely — report instead of introducing a differently-synchronized structure.
- Any existing Search/List test changes result → you changed semantics; STOP.

## Maintenance notes

- Reviewer: verify the new indexes are maintained in EVERY mutation path the parent
  index is (add/update/remove/clear), or they'll drift and silently drop results.
