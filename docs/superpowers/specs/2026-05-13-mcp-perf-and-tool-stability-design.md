# MCP perf & tool-stability design

**Date:** 2026-05-13
**Status:** Draft — awaiting user review
**Related:** `docs/issues/tools-disappear-mid-session.md`, [axi.md](https://axi.md/) (Agent eXperience Interface principles)

## Problem

Two friction sources observed in real sessions:

1. **Tools disappear mid-session.** After ~30 turns of normal use — particularly after a large `lifecycle status/result` payload — every `mcp__genexus18__*` tool stops being callable. Worker and gateway report healthy; the caller-side schema registry has dropped the tools. Workaround: user runs `/mcp`. Suspected root cause: response truncation path (>80k tokens) evicting tool registrations as a side effect.
2. **Too many roundtrips for common flows.** Three patterns stand out:
   - Edit cycle: `read → edit → re-read to verify`. The re-read is the wasteful turn.
   - Discovery: `list/query/structure` returns data but no hint about the next useful call.
   - Lifecycle: `build → status → status → status …` polling blocks the LLM from doing other work while a long build runs.

Goal: shrink roundtrips and eliminate the disappear-bug without losing functionality, without changing the MCP protocol, and without amplifying the suspected root cause of the disappear-bug (notifications broadcast path).

## Design philosophy

Adopt the subset of [AXI](https://axi.md/) (Agent eXperience Interface) principles that translate from CLI to MCP, plus targeted fixes for the disappear-bug. The framing: build a *lightweight* MCP — token budget is a first-class constraint, every byte in every response must justify itself.

AXI principles adopted: (3) truncation with size hints, (2) minimal-by-default schemas, (4) pre-computed aggregates, (5) definitive empty states, (6) structured errors + idempotent mutations, (9) contextual disclosure / next-step. Principles not adopted: (1) TOON format (deferred — see Open Questions), (7–8, 10) CLI-specific.

## Scope

Eleven coordinated changes grouped by intent:

### Foundation (disappear-bug fix)
- **(A)** Lifecycle payload pagination + gateway-side per-tool size cap.

### Roundtrip reduction
- **(B)** `genexus_edit` returns diff unified by default (no re-read needed).
- **(C)** Discovery tools embed `_meta.suggested_next` hints.
- **(D)** Background-job piggyback: `lifecycle build` becomes async; every response carries `_meta.background_jobs`.
- **(E)** Short builds synchronous; `lifecycle status` supports long-poll for explicit waits.

### Token efficiency (the system-prompt + payload tax)
- **(F)** Trim tool schema (descriptions + inputSchema).
- **(G)** Field selection (`fields=` / `parts=`) on `read` / `list` / `inspect`.
- **(H)** Terse errors by default; verbose opt-in.
- **(I)** Compact JSON serialization + drop null/default fields.

### AXI principles
- **(J)** Minimal-by-default list schemas (verbose opt-in).
- **(K)** Pre-computed aggregates in list responses.
- **(L)** Definitive empty states with `_meta.empty_reason`.
- **(M)** Combined operations (opt-in, off by default).

## Out of scope

- Real MCP `notifications/progress` for builds — *deferred*. Same broadcast path is leading suspect for the disappear-bug.
- New super-tools (`genexus_context`, etc.) — additional API surface not justified.
- Cold-start / pre-warm — not flagged as pain.
- TOON serialization — see Open Questions.
- Batched reads (`read [objA, objB, objC]`) — bigger API change, own spec later.

## Design

### (A) Lifecycle pagination + per-tool cap

**Attacks:** the disappear-bug root cause (oversized `lifecycle status/result` triggering harness-side truncation).

1. `genexus_lifecycle action=status` and `action=result` accept `page`/`page_size` (default `page_size=50` warnings). Mirrors `genexus_query` / `genexus_read`.
2. **Gateway-side per-tool cap:** new `ResponseSizeGuard` middleware. After tool returns, measure serialized payload. If exceeds threshold (default **55k tokens ≈ 220KB UTF-8** — leaves safety margin over the observed 80k harness limit, since envelope + `_meta` overhead bites), gateway truncates the response itself, replaces oversized field with sentinel, adds `_meta.truncated: { reason, original_size, follow_up: { tool, args } }`. Schema registry is never touched.
3. Truncation happens *inside* the gateway, not the harness. Working hypothesis: harness-side truncation drops the tool registry; gateway-side truncation is purely a payload concern.
4. Log every oversized event for one release — calibrate the 55k number empirically before locking it.

**Files:** `Routers/OperationsRouter.cs`, new `ResponseSizeGuard.cs`, `Services/BatchService.cs`.

**Tests:** unit truncation + sentinel; contract paginated `status`/`result`; regression — 200-warning build doesn't exceed cap, tools remain callable on next turn.

### (B) `genexus_edit` returns diff unified

**Attacks:** re-read after edit.

1. Compute unified diff (`-U3` style — minimal embedded context) of each hunk.
2. Response gains top-level `post_state.diff: "..."`.
3. For structural parts (Variables, Rules, Structure) where line-diff is meaningless, `post_state.changes` is a compact JSON of changed nodes only.
4. Opt-in `verbose=true` adds a ±15-line slice around each hunk for cases where extra context is wanted.
5. Optional `return_post_state: false` to disable entirely.
6. If `post_state` would push response over (A)'s cap, degrade to diff-only and set `_meta.post_state_truncated: true`.

**Rationale for diff-only default:** the unified diff already carries enough context (`@@` headers + ` ` context lines) for LLM to confirm edits without an additional slice. Slice ±15 was overkill — 30+ lines of duplicate info per hunk.

**Files:** `Services/JsonPatchService.cs`, `Services/SemanticOpsService.cs`, `Structure/PartAccessor.cs`.

**Tests:** diff format per edit type (source / variables / structure); size bounded by edit footprint, not file size; `verbose` flag adds slices.

### (C) Discovery `_meta.suggested_next`

**Attacks:** post-list / post-query "which one do I read now?" turn.

1. `list_objects`, `query`, `structure`, `search_source` add `_meta.suggested_next: { tool, args, reason }`.
2. Rules: simple, deterministic, no ranking ML.
   - `list_objects` with results → suggest `read` on top-ranked match.
   - `query` single high-confidence → suggest `read`.
   - `query` many matches → suggest filtered `list_objects`.
   - `structure` → suggest `read` on most recently modified part.
3. Terse: drop `reason` if it's >30 chars and adds no signal. Empty result → suggestion omitted (never suggest no-op).

**Files:** `Services/ListService.cs`, `Services/StructureService.cs`, `Services/Structure/IndexService.cs`.

### (D) Background-job piggyback

**Attacks:** build status polling blocking LLM from other work.

1. `lifecycle action=build` returns immediately: `{ job_id, status: "running", estimated_seconds, hint: "continue with other tools; status will appear in _meta.background_jobs" }`.
2. New `BackgroundJobRegistry` per session. States: `running`, `succeeded`, `failed`, `truncated_result_pending`.
3. **Piggyback middleware:** every gateway response (every tool) gets `_meta.background_jobs: [...]` when there are running jobs or unseen completions. Entries are removed after LLM has seen them once (i.e., one response after completion). Running jobs stay until terminal.
4. Session scoped via `HttpSessionRegistry`.
5. Result retention: `BuildResultRetentionSeconds=600` after terminal, so reconnect can still fetch.

**Failure modes:** worker crash → registry marks `failed` with reason; oversized result → `truncated_result_pending`, piggyback tells LLM to call paginated `result`.

**Files:** new `BackgroundJobRegistry.cs`, `Routers/OperationsRouter.cs`, `Services/BatchService.cs` (progress callbacks).

### (E) Short builds synchronous + long-poll

**Attacks:** the "edit one tiny thing then build to verify" case wasting 2 turns; and the "I'm done, just waiting for the build" case wasting many turns.

1. **Synchronous fast-path:** if `lifecycle build` estimates duration < `BuildSyncThresholdSeconds` (default 20s), gateway blocks until completion (with hard timeout) and returns the result directly — no `job_id`. One turn instead of two.
2. **Long-poll on status:** `lifecycle status job_id=X wait_seconds=N` (max 25s) blocks server-side until job terminates or timeout. For "now I'm waiting" scenarios — one call instead of polling loop.
3. Both compose with (D): piggyback for async background work, sync/long-poll for explicit foreground waits.

**Files:** `Routers/OperationsRouter.cs`, `OperationTracker.cs`.

### (F) Tool schema trim

**Attacks:** the ~6k-token fixed prompt tax on every conversation.

1. Audit all 30+ `mcp__genexus18__*` tool definitions: `description` capped at 60 chars, single-line, action-verb.
2. `inputSchema`: drop redundant `description` per-param where name is self-explanatory; drop `examples`; collapse enums to inline; drop default-value duplication.
3. Consolidate rare tools (`genexus_forge`, `genexus_doc`, `genexus_explain_code`) into sub-actions of a guard tool *only if* they share natural taxonomy. Otherwise keep separate but trim descriptions.

**Target:** 30-50% reduction in tool-schema prompt tokens.

**Files:** all tool registrations in `Routers/`, plus `OperationsRouter.cs` schema declarations.

**Tests:** snapshot test on total schema size; contract that all existing tool calls still validate against trimmed schemas.

### (G) Field selection

**Attacks:** payload bloat in reads.

1. `genexus_read`, `genexus_inspect`, `genexus_properties` accept `fields: [...]` / `parts: [...]` — returns only requested portions.
2. Omitted = current behavior (full object). Migration is purely additive.
3. Standardize the param name across tools (`parts` for object subsections, `fields` for property selection).

**Files:** `Services/ListService.cs`, read handlers in worker.

### (H) Terse errors

**Attacks:** 5-15k token bloat per SDK error.

1. Default error response: `{ code, message_one_line, hint }`. Stack traces / SDK diagnostics dropped.
2. `verbose_errors=true` per-call flag, or fetched separately via `genexus_logs` (already exists).
3. `code` is stable and enumerable — LLM can branch on it.

**Files:** `Helpers/SdkDiagnosticsHelper.cs`, error envelope in `OperationsRouter.cs`.

### (I) Compact JSON + drop nulls/defaults

**Attacks:** ~10% slack across every response.

1. Configure `System.Text.Json` / Newtonsoft to omit null values and default booleans (`false`, `0`, empty arrays where semantically equivalent to absence).
2. No indentation in responses.
3. Document the "absence = default" convention so LLM behavior is predictable.

**Caveat:** *do not* drop empty arrays where they're semantically meaningful (e.g., `items: []` for definitive empty state — see L). Whitelist of fields that must remain.

**Files:** gateway response serializer config.

### (J) Minimal-by-default list schemas (AXI #2)

**Attacks:** verbose list responses where LLM only needs to choose a row.

1. `list_objects`, `query`, `history`, `search_source`: default per-item shape = 3-4 fields (`name`, `type`, `modified`/`score`).
2. `verbose=true` or explicit `fields=[…]` returns full per-item shape.
3. Pairs with (G) field selection — same mental model.

**Files:** `Services/ListService.cs`, query/search handlers.

### (K) Pre-computed aggregates (AXI #4)

**Attacks:** "how many X are there?" follow-up calls.

1. List responses include `_meta.aggregates: { total, by_type: {...}, modified_last_7d: N }` where natural.
2. Computed during the same scan that produces the list — near-zero extra cost.
3. Schema is per-tool: `list_objects` returns type-distribution; `history` returns date-distribution; etc.

**Files:** `Services/ListService.cs`, `Services/HistoryService.cs`.

### (L) Definitive empty states (AXI #5)

**Attacks:** ambiguous `{items: []}` triggering re-confirmation calls.

1. When result set is empty, response always includes `_meta.empty_reason: "no_matches" | "filtered_out" | "kb_not_loaded" | "permission" | "other"`.
2. Trivial cost; eliminates a class of speculative re-tries.

**Files:** all list/query/search handlers.

### (M) Combined operations — opt-in (AXI extra)

**Attacks:** "list then immediately read the top match" being two turns.

1. `list_objects` and `query` accept `inline_read_top: 0-3` (default 0): inlines content of top N matches under `inline_reads: [{ name, content }]`.
2. Off by default to keep common case cheap; LLM passes the flag when intent is clear.
3. Counts against (A)'s per-tool cap — guard automatic.

**Status:** ship the parameter, observe usage, iterate. Not load-bearing.

**Files:** `Services/ListService.cs`, query handler.

## Interaction between the changes

(A) is the foundation — puts size-control inside the gateway so nothing else can recreate the disappear-bug by accident. (B)–(M) all add to `_meta` only or to opt-in parameters — never to the tool schema, never to `tools/list`, never to `notifications/tools/list_changed`. The suspected schema-invalidation path stays untouched.

The token efficiency layer (F–I) multiplies the gain of everything else: trimmer schemas + denser payloads + terser errors mean the cap from (A) bites less often.

## Rollout

Single feature flag at gateway: `MCP_PERF_PROFILE=v1` (default on). If a regression surfaces, env-flip to `legacy` restores pre-change behavior. Remove flag after one release if green.

Order of implementation (each independently shippable):
1. **(A)** — fixes the bug, must land first.
2. **(I)** + **(F)** + **(H)** — pure token wins, no API change.
3. **(B)** + **(C)** + **(J)** + **(K)** + **(L)** — roundtrip reductions, additive.
4. **(D)** + **(E)** — async build path, biggest behavior change.
5. **(G)** + **(M)** — opt-in API additions.

## Open questions

- **55k cap threshold** — guess; calibrate with one release of oversize logging.
- **TOON serialization (AXI #1).** AXI reports ~40% savings vs JSON. Deferred because:
  - MCP is multi-client (Cursor, Continue, etc.) — most parse `content.text` as JSON; TOON breaks that genericity.
  - .NET serializer ecosystem for TOON is immature; we'd write our own.
  - Oversized payloads that cause the disappear-bug need pagination, not compression — TOON would only buy marginal headroom on already-bounded responses.
  - **Revisit after (F-L) ship and we have telemetry on what tokens are actually spent on.** If structured-list payloads still dominate, evaluate TOON as opt-in `format=toon` parameter on the few tabular tools that benefit most.
- **`_meta.suggested_next` adoption.** If telemetry shows LLM follows hints blindly even when wrong, tighten the rules. If it ignores them entirely, retire the field.
- **`inline_read_top` default.** Currently 0; if usage data shows it's commonly set to 1, flip the default.

## Non-goals (explicit)

- No changes to `tools/list` or `notifications/tools/list_changed` semantics. The whole point is *not* to touch them while the disappear-bug root cause is unconfirmed.
- No new tools. Everything is additive on existing tools via `_meta` or new optional parameters.
- No MCP protocol extensions.
