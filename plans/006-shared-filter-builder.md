# Plan 006: Factor a shared filter-predicate builder for SearchService & ListService

> **Executor instructions**: Behavior-preserving refactor — but FIRST prove the two
> current filter chains are equivalent (or document where they intentionally differ).
> Honor STOP conditions. Update `plans/README.md`.
>
> **Drift check**: `git diff --stat b326cd4..HEAD -- src/GxMcp.Worker/Services/SearchService.cs src/GxMcp.Worker/Services/ListService.cs`

## Status

- **Priority**: P2
- **Effort**: S-M
- **Risk**: LOW (if equivalence is verified first)
- **Depends on**: 002 (do the indexes first, then commonize)
- **Category**: tech-debt
- **Planned at**: commit `b326cd4`, 2026-07-10

## Why this matters

`SearchService.cs:169-199` and `ListService.cs:193-470` independently re-implement the
same filter predicates (type, domain, description substring, parent/parentPath, date
range). The type filter notably differs: Search uses an `IsTypeMatch` helper
(alias/synonym-aware) while List uses inline `filterTypes.Contains`. That means filter
semantics can silently drift between `search` and `list`, and every new filter must be
added twice. One shared builder removes the drift risk.

## Current state

- `SearchService.cs:169-199` — filter chain, type via `IsTypeMatch`.
- `ListService.cs:193-470` — filter chain, type via inline `Contains`.
- Both operate over `IEnumerable<SearchIndex.IndexEntry>`.

## Steps

### Step 1 (MANDATORY FIRST): characterize the current behavior

Write tests that pin the CURRENT behavior of BOTH services for: type filter with an
alias/synonym, description substring casing, parent vs parentPath, date-range
boundaries, empty-filter. Run them at HEAD — they document reality, including any
Search-vs-List divergence. **Record the divergences in this plan file.**

### Step 2: extract the shared builder

Create an `IndexEntryFilterBuilder` (or equivalent) taking the common criteria and
producing a predicate/`IEnumerable` filter over `IndexEntry`. Decide per divergence
found in Step 1 whether to (a) unify on the richer behavior (`IsTypeMatch`) or
(b) keep a documented option flag. Prefer unifying on `IsTypeMatch` unless a Step-1
test shows a caller depends on the raw-Contains behavior.

### Step 3: route both services through it

Keep Search's ranking/scoring separate — only the filtering is shared. Consult the
Plan 002 type/domain indexes inside the builder where present.

**Verify**: Step 1 characterization tests still pass (or, where you intentionally
unified, update the specific test with a comment explaining the reconciliation).

## Commands

| Purpose | Command | Expected |
|---|---|---|
| Set SDK ref | `$env:GX_PATH = 'C:\Program Files (x86)\GeneXus\GeneXus18'` | — |
| Build | `dotnet build src\GxMcp.Worker\GxMcp.Worker.csproj -v:minimal` | 0 errors |
| Test | `dotnet test src\GxMcp.Worker.Tests\GxMcp.Worker.Tests.csproj` | all pass |

## Scope

**In scope:** `SearchService.cs`, `ListService.cs`, a new filter-builder file, tests.
**Out of scope:** ranking/scoring; the index structures (Plan 002 owns those).

## Done criteria

- [ ] Build 0 errors; full Worker suite green (4 known skips)
- [ ] Step-1 characterization tests exist and pass; divergences documented in this file
- [ ] Both services call the shared builder (no duplicated predicate chains remain)

## STOP conditions

- Step 1 reveals a divergence that a caller clearly depends on and you can't tell
  which behavior is correct → STOP and ask the maintainer which is canonical.

## Maintenance notes

- Reviewer: confirm Search's scoring wasn't accidentally folded into the shared filter.
