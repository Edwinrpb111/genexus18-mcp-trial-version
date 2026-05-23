# Usefulness ranking — `docs/mcp-improvements-2026-05-22.md`

Ranking each of the 100 wishlist items by how much value they deliver to the
**end user of the MCP** (the human asking an LLM agent to drive their KB), not
by implementation cost. "Usefulness" here = how often it fires × how big the
unblock is when it does.

Status legend:
- ✅ **shipped in v2.6.9**
- 🟡 **partial / foundation only** (service exists, not yet a user-facing tool)
- ⬜ **not started**

Tier legend:
- **S — game-changer.** Removes a class of failure or unblocks a workflow that
  was previously dead-ended. Worth shipping even alone.
- **A — high.** Saves the user repeated time (or repeated frustration) on a
  workflow they hit weekly.
- **B — moderate.** Nice when it fires. Workaround exists today but is
  annoying.
- **C — low.** Marginal QoL or niche audience.
- **D — skip / wait for demand.** Doc itself flagged these as low-ROI or
  speculative.

---

## Tier S — game-changers

| # | Item | Status | Why it matters |
|---|---|---|---|
| 1 | Runtime IDs in `genexus_inspect` | ✅ | Before this, every browser-automation flow paid a `grep '_Internalname'` round-trip. Wrong ID = silent `getElementById` null. Single biggest unblock for verify-in-browser. |
| 3 | `.Popup()` async + AUTO_REFRESH playbook | ✅ | Costs ~3 iterations to learn. Once learned, costs none. Permanent net win. |
| 5 | Auto-screenshot after WebForm edit | ⬜ | The HTML sanitizer escaping `<script>` in `Format="HTML"` is **invisible to DOM eval**. Without screenshots the agent will lie to itself about render state. High blast-radius bug class today. |
| 6 | `<script>` / `<img onerror>` warning in `Format="HTML"` | ✅ | Pairs with #5: catches the same bug class before the build. Cheaper feedback loop. |
| 10 | Build envelope success/error correctness | ✅ | The MCP was reporting clean builds as errors. Shallow readers (other LLMs, scripts) acted on the wrong signal. |
| 15 | Multi-object edit transactions | ⬜ | "Add variable + reference it from Events" is a routine 2-call sequence. When call 2 fails today, call 1 is orphaned. Atomicity matters for any non-trivial edit. |
| 28 | Real incremental build | ⬜ | Single biggest latency win available. Today every build copies the full module set. Cutting build to <30s on a touched single-object change would compound every other workflow. |
| 41 | Transaction ↔ DB drift detection | ⬜ | The "edited Aluno2 but Oracle T0001 doesn't have the column" failure mode is cryptic at build time. Catching it at edit time saves the cycle. |
| 51 | Worker hot-reload without warm-cache loss | ⬜ | Every reload today resets the index (40k objects → ~30s warmup). Long sessions accumulate friction. |
| 57 | `genexus_recipe popup_blocking_with_reload` | ✅ | Substitutes ~3 h of manual popup wiring with one call. Pure compounded savings for a common pattern. |
| 65 | `genexus_orient` welcome card | ✅ | First-turn context for a new session. Cheapest possible onboarding signal. |
| 100 | Full feature scaffold from user story | ⬜ | The Santo Graal. Doc-acknowledged XL, but valuation if shipped is enormous. |

## Tier A — high

| # | Item | Status | Why |
|---|---|---|---|
| 2 | HTML-form sanitization catalog in playbooks | ✅ | Saves the same lesson being relearned every new KB / agent. |
| 4 | EOL-normalized patch matching + diff on no-match | ✅ | "Context block not found" with no hint of CRLF/LF mismatch was a daily failure. |
| 7 | spc0150 detectable at edit time | ✅ | 60 s build → 0 s, on a frequent mistake. |
| 8 | Patch write-fallback false negatives | ✅ | Stop double-applying writes that already landed. |
| 9 | `replaceAll` on `genexus_edit` | ✅ | Hidden non-feature; now real. |
| 11 | `genexus_run_object` (replace dani.aspx) | ⬜ | Every dev keeps a `dani.aspx` test harness. Eliminating that ritual is real time. |
| 14 | `genexus_sdk_probe` for SDK discovery | ✅ | "Does `Form.GoTo` exist?" — now answerable without guessing. |
| 17 | Fuzzy patch matching with `did_you_mean` | ✅ | Pairs with #4. Patch failures become self-correcting. |
| 19 | Semantic WebForm editor (`add_textblock`, etc.) | ⬜ | Removes a whole class of "Invalid visual XML" errors. |
| 21 | Universal `dryRun=true` | ✅ | Predict before persist. Used constantly when edits are non-trivial. |
| 22 | Search captions/descriptions/parm names | ✅ | The grep-only-source default missed a common case. |
| 23 | Event-flow ASCII viz | ✅ | First-pass understanding of new WebPanels. |
| 32 | `genexus_log_tail` filtered by object | ✅ | Today the agent doesn't even know where runtime logs are. |
| 37 | Pixel-diff between builds | ⬜ | Catches the kind of regression `chrome-devtools-axi eval` lies about. |
| 45 | "Why pattern didn't apply" diagnose | ✅ | WWP silent failures stop being a guessing game. |
| 47 | Recipe catalog with examples | ✅ | Discoverability of macros. |
| 50 | GAM settings audit | ✅ | Security smell-test, zero effort. |
| 61 | Token-budget on responses | ✅ | Lets the agent decide when to paginate vs round-trip. |
| 62 | Structured `code:` + `docUrl:` on gotchas | ✅ | Machine-parseable warnings → consistent triage. |
| 63 | `suggested_next_step` on errors | ✅ | Every error now has a productive next call. |
| 64 | `projection=minimal/standard/verbose` | ✅ | Cap response size for common read shapes. |
| 69 / 13 | `chrome-devtools-axi` playbook | ✅ | Catalogs the verify-in-browser CLI. (Same item listed twice in doc.) |
| 70 | Playwright fallback for chrome-devtools-axi | ✅ | Removes a hard dependency. |
| 73 | Per-tool latency stats in whoami | ✅ | Lets the agent plan waits realistically. |
| 74 | Most-failed tool calls in whoami | ✅ | "You hit this 5×; try X" — turns repeat mistakes into self-correction. |
| 77 | Auto-fix `WebFormTypedPropertyWriter` quirks | ✅ | Quiet SDK property renames stop being quiet. |

## Tier B — moderate

| # | Item | Status | Why |
|---|---|---|---|
| 12 | `genexus_diff` on generated code | ⬜ | Useful for debugging codegen, not for everyday editing. |
| 16 | `genexus_undo last=N` | ✅ | Cheaper than `git checkout` for a quick revert. |
| 18 | Unified-diff edit input | ⬜ | Alternative input shape; nice but `genexus_edit` already covers the cases. |
| 20 | Auto-format Events on write | ✅ | Saves bikeshedding over indentation. |
| 24 | `genexus_find_callers` with line context | ✅ | Already partially covered by `analyze mode=impact`; the line context is the upgrade. |
| 25 | Hierarchy tree visualization | ✅ | One-shot mental model when joining a KB. |
| 26 | GeneXus type glossary | ✅ | Disambiguates `gxButton` vs `gxAttribute ControlType=Button` etc. |
| 27 | Cancel in-progress build | ✅ | One specific stuck-state escape. |
| 29 | Smoke test post-build | ⬜ | Catches "DLL built but the page 500s" without manual probing. |
| 30 | Build graph viz with ETAs | ⬜ | Helps choose when to wait vs. fan out. |
| 33 | Browser console/network capture | ⬜ | Pairs with #5 / #37 for the verify-in-browser pipeline. |
| 34 | EXPLAIN PLAN on `For each` navigation | ⬜ | Useful when chasing slow queries; niche audience. |
| 38 | A11y audit wrapper | ⬜ | Wires lighthouse into the build-edit-verify loop. |
| 39 | Mobile emulation screenshots | ⬜ | Useful for responsive checks. |
| 40 | OCR on screenshots for escaped text | ⬜ | Backup for #6; lower priority once #5/#6 are live. |
| 42 | Sample data generator | ⬜ | "I need a non-empty grid to test layout" → real value. |
| 43 | DDL diff before reorg | ✅ | Pre-flight on a destructive op. |
| 44 | Index advisor | ⬜ | One-off perf tool. |
| 46 | Pattern visual editor (JSON in/out) | ⬜ | Easier than XML for WWP edits. |
| 48 | Hardcoded credential detector | ✅ | Standard scan. |
| 49 | SQL injection lint | ✅ | Standard scan. |
| 52 | Worker memory dashboard | ✅ | Now visible in whoami; warns before the OOM. |
| 55 | KB-to-KB diff | ⬜ | "What changed between prod and homolog" — useful for env reconciliation. |
| 58 | `radio_group_show_hide` recipe | ✅ | The most-common popup pattern. |
| 59 | `extract_to_procedure` recipe | ✅ | Pairs with #7. |
| 60 | Versioned recipes | ⬜ | Insurance for forward-compat. |
| 66 | Interactive tutorial | ⬜ | Onboarding aid, one-time per dev. |
| 67 | Glossary playbook | ✅ | Discoverability. |
| 68 | `genexus_explain object=<X>` in NL | ⬜ | Stakeholder-facing summaries. |
| 71 | GitHub PR integration | ⬜ | Convenient; offloadable to the user's existing tooling. |
| 72 | Slack/Discord webhook on build fail | ✅ | Pipelines and overnight reorgs. |
| 75 | Token-usage breakdown by tool | ⬜ | Helps the agent self-tune. |
| 79 | Theme-aware preview | ⬜ | Visual QA niche. |
| 80 | Master-page compat lint | ✅ | Catches a misuse before the build. |
| 85 | Auto PR descriptions | ⬜ | Quality-of-life for the human reviewer. |
| 88 | `genexus_blame` | 🟡 | Service shipped; tool wiring pending. Useful when triaging "who broke this". |
| 90 | KB README generator | ⬜ | One-shot doc deliverable. |
| 92 | Bulk translation import | ⬜ | Localization workflow. |
| 93 | Friction bot | ⬜ | Self-improvement loop on the MCP itself. |
| 94 | Heatmap of time spent | ⬜ | End-of-session retrospective. |
| 99 | WCAG fixes for `CaptionExpression` | ⬜ | Accessibility audit assist. |

## Tier C — low

| # | Item | Status | Why |
|---|---|---|---|
| 31 | Test stub generator (GXtest) | ⬜ | Niche; agents already write tests. |
| 35 | Watch/breakpoint in Events | ⬜ | Requires generator changes; cost > value. |
| 36 | Execution history "who called X" | ⬜ | Useful in production triage; niche. |
| 53 | Worker pool with warm spares | ⬜ | Latency optimization for a non-bottleneck. |
| 54 | Sandbox KB clone | ⬜ | Devs already have git for this. |
| 56 | Cross-KB object import | ⬜ | Rare workflow. |
| 76 | Cross-session learning | ⬜ | Conceptually nice; hard to get right. |
| 78 | SDPanel support parity | ⬜ | Mobile audience only. |
| 81 | Copilot-style AI completion in Events | ⬜ | Duplicates what the upstream LLM already does. |
| 87 | Object dependency heat-map | ⬜ | One-time visual; manual `analyze impact` substitutes. |
| 89 | Auto-screenshot publish to internal server | ⬜ | Team-collab tool, not solo workflow. |
| 91 | Rename Transaction across KB | ⬜ | Refactor exists in IDE; lower urgency. |
| 97 | Slow-network simulation | ⬜ | QA tooling niche. |
| 98 | Cross-browser screenshot comparison | ⬜ | Compat audit niche. |

## Tier D — skip / wait for demand

These are doc-flagged as XL with uncertain ROI or as speculative ideas. Don't
build until a real user asks for them by name.

| # | Item | Doc note |
|---|---|---|
| 82 | Time-travel debugging | flagged "Skip ou aguardar feedback" |
| 83 | Voice-driven edits | flagged speculative |
| 84 | Multi-agent collaboration with lock granularity | XL, "alta complexidade" |
| 86 | "What if" mode for type changes | flagged "alta complexidade" |
| 95 | Auto-generate tests from production patterns | flagged speculative |
| 96 | Reverse-engineer pattern from existing objects | flagged speculative |

---

## What's left of the 100, by tier

- **Tier S not yet shipped:** 5, 15, 28, 41, 51, 100 → next-quarter focus.
- **Tier A not yet shipped:** 11, 19, 37 → quick to medium follow-up wins.
- **Tier B not yet shipped:** most of the long tail; pull into a release as room allows.
- **Tier C / D:** wait on user signal.

## Shipped in v2.6.9 (full list, by item number)

1, 2, 3, 4, 6, 7, 8, 9, 10, 13, 14, 16, 17, 20, 21, 22, 23, 24, 25, 26, 27, 32,
43, 45, 47, 48, 50, 52, 57, 58, 59, 61, 62, 63, 64, 65, 67, 69, 70, 72, 73, 74,
77, 80 — plus item 88 as service-only (no tool yet).

Net: **44 of 100** addressed in one release.
