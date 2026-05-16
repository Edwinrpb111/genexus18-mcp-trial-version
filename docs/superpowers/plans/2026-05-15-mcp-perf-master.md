# MCP Performance & Token Optimization — Master Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement each sub-plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reduce per-session token cost and wall-clock latency of the GeneXus MCP across 6 independent fronts, attacked in dependency order with checkpoints.

**Architecture:** Six small/medium refactors layered from cheapest+safest to most invasive: (1) flip `axiCompact` default, (2) trim tool descriptions + serve detail via MCP resources, (3) instrument worker boot/SDK init, (4) add composite `edit_and_build` tool collapsing 3-5 turns into one, (5) wire `notifications/progress` to per-operation `progressToken` so long ops stream live status, (6) split `BulkIndex` into lite pass + lazy enrichment so `list_objects` is usable in ~30s.

**Tech Stack:** .NET 8 (Gateway), .NET Framework 4.8 (Worker), MCP JSON-RPC over stdio + HTTP/SSE, xUnit, Newtonsoft.Json.

---

## Sub-plan inventory

| # | Sub-plan | File | Effort | Risk |
|---|----------|------|--------|------|
| 1 | axiCompact default flip | [`2026-05-15-mcp-perf-1-axicompact-default.md`](./2026-05-15-mcp-perf-1-axicompact-default.md) | ~1d | Low |
| 2 | Tool description trim + resources | [`2026-05-15-mcp-perf-2-tool-descriptions.md`](./2026-05-15-mcp-perf-2-tool-descriptions.md) | ~1d | Low |
| 3 | Worker latency instrumentation | [`2026-05-15-mcp-perf-3-worker-latency.md`](./2026-05-15-mcp-perf-3-worker-latency.md) | ~1d | Low |
| 4 | Composite `edit_and_build` tool | [`2026-05-15-mcp-perf-4-composite-edit-build.md`](./2026-05-15-mcp-perf-4-composite-edit-build.md) | ~3-5d | Medium |
| 5 | Progress streaming via operationId | [`2026-05-15-mcp-perf-5-progress-streaming.md`](./2026-05-15-mcp-perf-5-progress-streaming.md) | ~3-5d | Medium |
| 6 | Fast index (lite + lazy) | [`2026-05-15-mcp-perf-6-fast-index.md`](./2026-05-15-mcp-perf-6-fast-index.md) | ~1-2w | High |

**Total estimated effort:** 3-4 weeks of focused work.

---

## Execution order & dependencies

```
1 axiCompact ──┐
2 tool descs ──┼──> 3 worker latency ──> 4 composite ──┐
               │                                       ├──> 5 streaming ──> 6 fast index
               └────────────────────────────────────── ┘
```

**Rationale for the order:**

- **1 → 2** first: both are isolated envelope/registry changes. Land them together to establish the "smaller-defaults" baseline before bigger refactors that depend on the envelope shape.
- **3** next: instrumentation only. Adds Stopwatch/metrics. Doesn't change behavior, but produces the numbers we need to validate 4/5/6 actually help.
- **4 → 5**: composite tool emits an `operationId`; streaming attaches `progressToken=operationId` to that same id. Doing 5 first would mean retrofitting a token that doesn't exist yet for the most useful long-running call.
- **6 last**: largest refactor; depends on streaming (5) to surface enrichment progress to the LLM, and on instrumentation (3) to prove the lite-pass time budget.

---

## Checkpoints

After each sub-plan completes, **stop and review** before starting the next. Per checkpoint:

- [ ] All tests in the sub-plan pass (`dotnet test src/GxMcp.Gateway.Tests`, `dotnet test src/GxMcp.Worker.Tests`)
- [ ] Smoke test still green: `pwsh scripts/mcp_smoke.ps1 -BaseUrl http://127.0.0.1:5000/mcp`
- [ ] `genexus-mcp doctor --full --mcp-smoke --format json` returns `summary.fail = 0`
- [ ] CHANGELOG.md entry added under the next-version heading
- [ ] User signs off on the diff before moving to the next sub-plan

If a checkpoint fails, **do not advance**. Open a follow-up task and fix in place.

---

## Done criteria (whole master)

- [ ] All 6 sub-plans completed and merged to `main`
- [ ] CHANGELOG.md groups the 6 entries under a single perf release heading (e.g., `## v2.4.0 — Performance & Token Optimization`)
- [ ] `genexus-mcp tools list` shows `genexus_edit_and_build` available
- [ ] Cold-session token usage on a 1000-call benchmark drops by ≥30% vs. pre-change baseline (recorded in `docs/perf_audit_2026-05-15.md`)
- [ ] `BulkIndex` lite pass completes in ≤45s on a 38k-object KB (`[BULK-INDEX-LITE] elapsedMs=...` log line proves it)
- [ ] `notifications/progress` observed in MCP traffic during build/index (proven via stdio capture in `tests/manual/progress-capture.log`)

---

## Rollback strategy

Each sub-plan ends with a commit on `main`. Rollback = `git revert` the merge commit; no schema/data migrations are introduced by any sub-plan. The fast-index sub-plan (6) keeps the old monolithic `BulkIndex` path behind a config flag (`Indexing.UseLitePass`) for one release before removal — see sub-plan 6 for details.

---

## Self-review notes

- Sub-plan 5 depends on the `notifications/progress` routing already partially wired in `src/GxMcp.Gateway/Program.cs:919-932` and `:189-207`. Confirmed during recon — no transport refactor needed.
- Sub-plan 4 reuses `OperationTracker` (`src/GxMcp.Gateway/OperationTracker.cs:21-40`) for the long-tail build; no new lifecycle subsystem.
- Sub-plan 6 retains the on-disk cache format (gzip JSON at `%LOCALAPPDATA%/GxMcp/Cache/index_{KB_SHA256_first_16chars}.json.gz`); only the in-memory build phases are split.
- All cross-plan references use **absolute repo paths** so plans can be reordered safely.
