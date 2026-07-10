# Plan 009: Characterization test suite for BuildService

> **Executor instructions**: Additive test-only work. Update `plans/README.md`.
>
> **Drift check**: `git diff --stat b326cd4..HEAD -- src/GxMcp.Worker/Services/BuildService.cs`

## Status

- **Priority**: P2
- **Effort**: M
- **Risk**: LOW (test-only)
- **Depends on**: none
- **Category**: tests
- **Planned at**: commit `b326cd4`, 2026-07-10

## Why this matters

`BuildService.cs` (1748 lines) is on the build critical path (one of write/build/index/
KB-open) yet has no dedicated unit-test file — it's only exercised incidentally through
`InProcessBuildRunnerTests`, `EditAndBuildOrchestratorTests`, `DryRunUniversalGuardTests`.
A regression in BuildService that those scenarios don't happen to hit ships silently.

## Current state

- `src/GxMcp.Worker/Services/BuildService.cs` — 1748 lines, no `BuildServiceTests.cs`.
- Existing indirect tests named above are the structural model for how to construct
  the service and its collaborators in a test.

## Steps

1. Identify BuildService's highest-value branches to pin: error-category mapping,
   segmented-target sequencing, notify-on-failure, and the compile-only fast path vs
   full BuildOne selection. Use `git log -p -- src/GxMcp.Worker/Services/BuildService.cs`
   to find the historically bug-prone methods (highest churn) and prioritize those.
2. Create `src/GxMcp.Worker.Tests/BuildServiceTests.cs` testing those directly (mock or
   fake the build runner where a real GeneXus build would be needed — follow how
   `InProcessBuildRunnerTests` isolates the runner).
3. Where a branch genuinely needs a real KB/SDK build, mark it clearly and keep it out
   of the fast unit set (guard with the same SDK-presence pattern the coverage script
   uses) so CI without GeneXus still runs the rest.

## Commands

| Purpose | Command | Expected |
|---|---|---|
| Set SDK ref | `$env:GX_PATH = 'C:\Program Files (x86)\GeneXus\GeneXus18'` | — |
| Build | `dotnet build src\GxMcp.Worker\GxMcp.Worker.csproj -v:minimal` | 0 errors |
| Test | `dotnet test src\GxMcp.Worker.Tests\GxMcp.Worker.Tests.csproj --filter "FullyQualifiedName~BuildServiceTests"` | all pass |

## Scope

**In scope:** `src/GxMcp.Worker.Tests/BuildServiceTests.cs` (create); test doubles only.
**Out of scope:** changing `BuildService.cs` itself (if you find a bug, STOP and report
it — don't fix it inside a test-coverage plan).

## Done criteria

- [ ] `BuildServiceTests.cs` covers error-category mapping, segmented-target sequencing,
      and the fast-path/full-build selection, asserting meaningful behavior (not just
      "does not throw")
- [ ] Full Worker suite green (4 known skips)
- [ ] No change to `BuildService.cs`

## STOP conditions

- A characterization test reveals a real bug in BuildService → STOP, report it as a new
  finding; do not fix it here.

## Maintenance notes

- This suite is the model for Plan 007's Step 0 characterization approach on WriteService.
