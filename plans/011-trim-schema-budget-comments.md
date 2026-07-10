# Plan 011: Trim ToolSchemaSizeTests comment history to current rationale + pointer

> **Executor instructions**: Cosmetic/test-scaffolding change. Update `plans/README.md`.
>
> **Drift check**: `git diff --stat b326cd4..HEAD -- src/GxMcp.Gateway.Tests/ToolSchemaSizeTests.cs`

## Status

- **Priority**: P3
- **Effort**: S
- **Risk**: LOW
- **Depends on**: none
- **Category**: dx
- **Planned at**: commit `b326cd4`, 2026-07-10

## Why this matters

`ToolSchemaSizeTests.TotalToolSchemaSizeIsUnderBudget` (`src/GxMcp.Gateway.Tests/
ToolSchemaSizeTests.cs:36-190`) carries ~150 lines of inline changelog documenting
every budget bump (3500 → … → 13300 → 9500 → ~11400). The full history is already in
`CHANGELOG.md`; the in-test wall of comments makes a contributor read ~150 lines to
learn what number to type. Low leverage — this is a tidy-up, not a correctness fix.
Deferred (not rejected) because it's pure noise reduction with no user impact.

## Current state

- `ToolSchemaSizeTests.cs:36-190` — the budget constant plus the accreting comment log.
- `AGENTS.md` already documents that bumping the budget "requires updating both the
  budget constant and the comment trail in that test" — update that instruction too.

## Steps

1. Keep the current budget constant and the test logic unchanged.
2. Replace the ~150-line comment block with: a 2-3 sentence rationale (what the budget
   guards and why), the last 2-3 bump entries, and a pointer to `CHANGELOG.md` for the
   full history.
3. Update `AGENTS.md`'s "Tool surface lives in two synchronized places" note so it no
   longer tells contributors to extend an in-test comment trail — point them at
   `CHANGELOG.md` instead.

## Commands

| Purpose | Command | Expected |
|---|---|---|
| Build | `dotnet build src\GxMcp.Gateway.Tests\GxMcp.Gateway.Tests.csproj -v:minimal` | 0 errors |
| Test | `dotnet test src\GxMcp.Gateway.Tests\GxMcp.Gateway.Tests.csproj --filter "FullyQualifiedName~ToolSchemaSizeTests"` | pass |

## Scope

**In scope:** `ToolSchemaSizeTests.cs` (comments only), `AGENTS.md` (the one note).
**Out of scope:** the budget value; the test's assertion logic; `tool_definitions.json`.

## Done criteria

- [ ] Comment block trimmed; test still passes with the same budget value
- [ ] `AGENTS.md` no longer instructs maintaining an in-test comment trail
- [ ] `git diff` touches only comments + AGENTS.md (no logic change)

## STOP conditions

- None expected. If trimming the comments would drop a rationale not captured in
  `CHANGELOG.md`, port that rationale into CHANGELOG first.

## Maintenance notes

- After this, the convention is: budget history lives in `CHANGELOG.md`, the test keeps
  only current rationale.
