# Plan 005: Replace CommandDispatcher's giant switch with a registration table

> **Executor instructions**: Behavior-preserving refactor. Verify with the existing
> command/e2e tests before and after. Honor STOP conditions. Update `plans/README.md`.
>
> **Drift check**: `git diff --stat b326cd4..HEAD -- src/GxMcp.Worker/Services/CommandDispatcher.cs`

## Status

- **Priority**: P2
- **Effort**: M
- **Risk**: LOW (pure dispatch refactor, if done incrementally)
- **Depends on**: none
- **Category**: tech-debt
- **Planned at**: commit `b326cd4`, 2026-07-10

## Why this matters

`CommandDispatcher.cs` (1761 lines) declares 50+ service fields (`:11-90`) and routes
`switch (method?.ToLower())` (`:447`) across ~83 `case` labels. Adding any command means
editing the constructor, field list, and switch in lockstep — a guaranteed merge-conflict
hotspot (it's the #1 churn file in the repo) with no compile-time exhaustiveness. A
lookup table caps that growth: each service registers its own command names.

## Current state

- `CommandDispatcher.cs:11-90` — service fields, constructed `:200-215`ish.
- `:347-400` — `Dispatch` wrapper (idempotency, cancellation, progress context).
- `:447` — `switch (method?.ToLower())` with ~83 cases → service method calls.
- `:1687-1693` — `DispatchInternal` catch-all.
- Tests: many `*Tests.cs` in `src/GxMcp.Worker.Tests` exercise dispatch end to end,
  including `EdgeCaseRegressionTests` (known-flaky in parallel — treat a single failure
  as a flake, re-run isolated).

## Commands

| Purpose | Command | Expected |
|---|---|---|
| Set SDK ref | `$env:GX_PATH = 'C:\Program Files (x86)\GeneXus\GeneXus18'` | — |
| Build | `dotnet build src\GxMcp.Worker\GxMcp.Worker.csproj -v:minimal` | 0 errors |
| Test | `dotnet test src\GxMcp.Worker.Tests\GxMcp.Worker.Tests.csproj` | all pass (4 known skips) |

## Scope

**In scope:** `CommandDispatcher.cs`; optionally small additions to the services to let
them expose their command handlers. **Out of scope:** the wrapper concerns in
`Dispatch` (idempotency/cancellation/progress) — keep them exactly as-is around the
table lookup. Do NOT change any command's behavior or name.

## Steps

1. Introduce `Dictionary<string, Func<JObject, string>>` (case-insensitive) keyed by
   the same lowercased method names the switch uses today.
2. Populate it once at construction — one entry per current `case`, delegating to the
   exact same service call the case made. Keep `DispatchInternal`'s try/catch and the
   `default:` (unknown-method) behavior as the table's miss path.
3. Replace the `switch` body with a table lookup; leave `Dispatch`'s wrapper untouched.
4. Delete the now-dead switch.

Do this in ONE commit only after the table produces identical routing — or split into
"add table alongside switch, assert parity in a test, then remove switch".

**Verify**: full Worker suite green before and after; count of registered commands ==
count of former cases (add a test asserting the table has an entry for every method
name the golden `tool_definitions.json` implies, if practical).

## Done criteria

- [ ] Build 0 errors; full Worker suite green (re-run any single failure isolated)
- [ ] `grep -n "case \"" src/GxMcp.Worker/Services/CommandDispatcher.cs` returns no
      dispatch cases (switch removed)
- [ ] No command renamed or dropped (diff the registered keys against the old case labels)

## STOP conditions

- A case does more than route (inline logic beyond calling a service) — extract it
  faithfully into the table entry; if it entangles wrapper concerns, STOP and report.
- Parity test shows any method routes differently → STOP.

## Maintenance notes

- After this lands, "add a tool" no longer edits a switch — document the registration
  step in `AGENTS.md`'s "Adding or modifying a tool" section.
