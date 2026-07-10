# Plan 008: Decompose Gateway Program.cs (5657 lines) into per-concern modules

> **Executor instructions**: Pure extract refactor of the process entry point. Verify
> the full Gateway suite green after every extraction. Honor STOP conditions. Update
> `plans/README.md`.
>
> **Drift check**: `git diff --stat b326cd4..HEAD -- src/GxMcp.Gateway/Program.cs`

## Status

- **Priority**: P3
- **Effort**: L
- **Risk**: MED (startup/handshake sequencing lives here)
- **Depends on**: none
- **Category**: tech-debt
- **Planned at**: commit `b326cd4`, 2026-07-10

## Why this matters

`src/GxMcp.Gateway/Program.cs` (5657 lines) interleaves MCP stdio transport, multiple
`while(true)`/`Task.Delay` polling loops (`:1630,1738,2130`), build-poll result
assembly / `partial_success` handling (`:3484,3936`), the HTTP server, and static hint
strings. Every new command surface touches this file and reasoning about any one
polling loop means holding the whole file in context. Extracting concerns behind narrow
seams mirrors the Worker's service-per-concern design.

## Current state

- `Program.cs` — MCP loop + `whoami` builder + worker lifecycle + HTTP server
  (`StartHttpServer`, now with the audit branch's token auth) + build-poll orchestration.
- Tests: `src/GxMcp.Gateway.Tests` (~562 passing), including `McpRouterTests`,
  contract golden tests, `OperationTrackerTests`.

## Seams to extract

1. **Build-poll / partial_success orchestration** (`:3484,3936` region) → its own class.
2. **Notification / progress relay loops** → a dedicated relay type.
3. **HTTP server** (`StartHttpServer` + the auth helpers added on the audit branch) →
   a `GatewayHttpServer` class.
4. Leave the core MCP stdio dispatch loop + startup handshake in a slimmer `Program`.

## Steps

Per seam, one commit: move to a new class injected/called from `Program`; no behavior
change; build; run the full Gateway suite (green after each). Start with the HTTP
server (most self-contained) to build confidence, then build-poll, then relays.

## Commands

| Purpose | Command | Expected |
|---|---|---|
| Build | `dotnet build src\GxMcp.Gateway\GxMcp.Gateway.csproj -v:minimal` | 0 errors |
| Test | `dotnet test src\GxMcp.Gateway.Tests\GxMcp.Gateway.Tests.csproj` | all pass (7 known skips) |

## Scope

**In scope:** `Program.cs` → new per-concern files under `src/GxMcp.Gateway/`.
**Out of scope:** startup/handshake ordering (must be byte-identical in effect);
the MCP protocol semantics; the tool routing in `Routers/*`.

## Done criteria

- [ ] Build 0 errors; full Gateway suite green after EVERY seam commit
- [ ] `Program.cs` materially smaller; HTTP/build-poll/relay concerns in own files
- [ ] `git diff --color-moved` shows moves, not logic rewrites
- [ ] Manual smoke: `npx . doctor --mcp-smoke` (or the CI `mcp_llm_contract_smoke.ps1`)
      still passes — startup path unbroken

## STOP conditions

- An extraction changes startup ordering or the handshake (initialize → tools/list)
  in any observable way → STOP.
- A polling loop shares state with the transport loop in a way that can't be cleanly
  separated → report before forcing it.

## Maintenance notes

- Reviewer: focus on startup sequencing and the HTTP auth middleware (added on the
  audit branch) surviving the move intact.
