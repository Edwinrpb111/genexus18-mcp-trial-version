# Plan 007: Decompose WriteService (6982 lines) along part/property seams

> **Executor instructions**: Pure move/extract refactor. **Do NOT start without a
> characterization-test net around WriteService's public entry points** (see Step 0).
> Each step keeps the codebase building and green. Honor STOP conditions. Update
> `plans/README.md`.
>
> **Drift check**: `git diff --stat b326cd4..HEAD -- src/GxMcp.Worker/Services/WriteService.cs`

## Status

- **Priority**: P3
- **Effort**: L (multi-day)
- **Risk**: MED (highest-risk mutation path in the tool)
- **Depends on**: characterization tests (Step 0) landed first
- **Category**: tech-debt
- **Planned at**: commit `b326cd4`, 2026-07-10

## Why this matters

`WriteService.cs` is a single 6982-line `partial class` with ~34 public members and no
internal file boundaries — property writing, part resolution, pattern application, and
debug/trace helpers all share one file. It's the repo's top churn file, so merge
conflicts and review difficulty concentrate here, and it's near-impossible to unit-test
a single concern in isolation. Splitting it along existing seams shrinks blast radius
without changing behavior.

## Current state

- `src/GxMcp.Worker/Services/WriteService.cs` — `partial class WriteService` (`:15`),
  6982 lines. Only companion today: `Helpers/WritePatternDiagnostics.cs` (diagnostics).
- Related existing seam: `Structure/PartAccessor.cs` (used elsewhere for part access).
- The write path is serialized per target via `AcquirePerTargetLock` (`:718`, already
  fixed in the audit branch — keep that behavior).

## Seams to extract (into `WriteService.<Concern>.cs` partial files or collaborators)

1. **Property writing** — typed/raw property application helpers.
2. **Part resolution / access** — resolving object parts to write into; extend the
   existing `PartAccessor` seam rather than duplicating it.
3. **Pattern application** — the pattern-driven write helpers.
4. **Debug / trace helpers** — the snippet/trace code around `:6727,6897,6960`.

## Step 0 (MANDATORY): characterization tests

Before moving any code, ensure each of WriteService's ~34 public entry points has at
least one test pinning current behavior. Reuse the many existing `WriteService*Tests.cs`
in `src/GxMcp.Worker.Tests` as the model; fill gaps. Run the full suite; it is your
"before" baseline.

## Steps

For each seam (1→4), one commit:
1. Move the members into a new `partial class WriteService` file (or a collaborator the
   service delegates to), no logic changes.
2. Fix up access modifiers only as needed for the move.
3. Build; run the full Worker suite. Must stay green after every seam.

Order so shared mutable state (COM handles, part accessors) stays with its natural
owner; if a member straddles two seams, leave it where its state lives and note why.

## Commands

| Purpose | Command | Expected |
|---|---|---|
| Set SDK ref | `$env:GX_PATH = 'C:\Program Files (x86)\GeneXus\GeneXus18'` | — |
| Build | `dotnet build src\GxMcp.Worker\GxMcp.Worker.csproj -v:minimal` | 0 errors |
| Test | `dotnet test src\GxMcp.Worker.Tests\GxMcp.Worker.Tests.csproj` | all pass (4 known skips) |

## Scope

**In scope:** `WriteService.cs` → split files; new characterization tests.
**Out of scope:** any behavior change; renaming public members; touching callers in
`CommandDispatcher`. The public surface of WriteService must be byte-for-byte the same.

## Done criteria

- [ ] Step 0 characterization coverage exists for all public entry points
- [ ] `WriteService.cs` (any single file) is materially smaller; concerns live in
      separate files/collaborators
- [ ] Build 0 errors; full Worker suite green after EVERY seam commit
- [ ] `git diff` shows moves only — no logic edits (reviewer can verify with `--color-moved`)

## STOP conditions

- Full suite not green before Step 0 is complete → stop; you have no baseline.
- A seam can't be extracted without threading shared mutable state awkwardly across the
  boundary → stop, report the entanglement; the maintainer may re-scope the seam.

## Maintenance notes

- This is the plan most likely to drift; re-run the drift check and re-baseline tests
  if WriteService changed since `b326cd4`.
