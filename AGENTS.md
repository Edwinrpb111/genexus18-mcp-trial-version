# AGENTS.md

Project-level instructions for AI assistants working on Genexus18MCP.

## Project orient

Two-process MCP server that exposes a GeneXus 18 Knowledge Base to AI agents (Claude Desktop, Claude Code, Cursor, etc.) via the native GeneXus SDK — no parsing of KB files, no scraped IDE state, edits go through the same code paths the IDE uses. Codebase is C# / .NET for everything that touches the SDK; the npm package (`genexus-mcp`) is a thin Node wrapper shipping pre-built Windows binaries and writing MCP-client config.

```
MCP client (Claude/Cursor/…)
   │   stdio JSON-RPC
   ▼
GxMcp.Gateway  (long-running, one per client)
   │   pipes JSON-RPC over stdio
   ▼
GxMcp.Worker   (one per opened KB; STA thread; hosts Artech.* SDK in-process)
   │   COM-flavoured SDK calls
   ▼
GeneXus 18 SDK  (C:\Program Files (x86)\GeneXus\GeneXus18\Artech.*.dll)
   ▼
Knowledge Base on disk
```

- **Gateway** (`src/GxMcp.Gateway/`, **net8.0-windows**) — speaks MCP stdio with the client, owns a `WorkerPool` indexed by KB alias, routes tool calls through `Routers/*.cs` to a per-KB worker. `Program.cs` is the MCP loop + `whoami` builder + worker lifecycle.
- **Worker** (`src/GxMcp.Worker/`, **net48 STA**) — owns the GeneXus SDK in-process. STA thread is mandatory because the SDK is COM-flavoured. `Services/CommandDispatcher.cs` is the RPC switchboard; `KbService` opens KBs; `IndexCacheService` maintains an on-disk `SearchIndex` cache; `Services/{ListService,SearchService,AnalyzeService,WriteService,…}` implement the tools.
- **CLI** (`cli/run.js`) — what `npx genexus-mcp` invokes. Reads MCP client configs (Claude Desktop, Codex, Cursor, VS Code), writes the server entry pointing at `publish/start_mcp.bat`, then forwards stdio to the gateway. Tests are pure Node (`cli/run.test.js`).
- **publish/** — the deployable artifact. Both `install.ps1` (build-from-source) and `npm publish` (via `publish.zip`) ship from this directory. `GxMcp.Gateway.exe` at the root, `worker/GxMcp.Worker.exe` one level down. This layout is asserted by the npm-publish workflow.

### Tool surface lives in two synchronized places

- `src/GxMcp.Gateway/tool_definitions.json` — single source of truth for MCP tool schemas. `ToolSchemaSizeTests` enforces a token budget; bumping requires updating both the budget constant and the comment trail in that test.
- `src/GxMcp.Gateway.Tests/Fixtures/Contract/Discovery/tools-list.response.json` — golden fixture for the discovery `tools/list` envelope. **Must stay alphabetically sorted by tool name.** When you add/change a schema field in `tool_definitions.json`, regenerate the corresponding section in the golden fixture or the contract test fails.

### Adding or modifying a tool

The dispatch path goes: gateway router (`src/GxMcp.Gateway/Routers/*Router.cs`) ↔ worker dispatcher (`src/GxMcp.Worker/Services/CommandDispatcher.cs`). To add a tool: schema in `tool_definitions.json` → router case → dispatcher action → service method → golden fixture update.

**AxiCompact projection:** `genexus_query` and `genexus_list_objects` default to a compact field allowlist defined in `Program.GetDefaultCompactFields`. Adding a field to a tool's output also requires whitelisting it there, or it gets stripped before reaching the client.

## Build / test commands

Set this once per shell when working with Worker code (build-time reference path):

```powershell
$env:GX_PATH = 'C:\Program Files (x86)\GeneXus\GeneXus18'
```

### Build

```powershell
.\build.ps1                                  # full Gateway+Worker build + deploy to publish/
dotnet build Genexus18MCP.sln -v:minimal     # quick solution build (no publish/ refresh)
dotnet build src\GxMcp.Worker\GxMcp.Worker.csproj
dotnet build src\GxMcp.Gateway\GxMcp.Gateway.csproj
```

If the build fails with `MSB3027` / `MSB3021` citing `GxMcp.Gateway.exe` or `GxMcp.Worker.exe` locked, the running dev gateway/worker is holding the binary — see the "Kill the Gateway/Worker" permission below.

### Test

```powershell
dotnet test src\GxMcp.Worker.Tests\GxMcp.Worker.Tests.csproj      # net48; ~570 tests
dotnet test src\GxMcp.Gateway.Tests\GxMcp.Gateway.Tests.csproj    # net8.0; ~310 tests
dotnet test Genexus18MCP.sln                                     # both
npm test                                                          # cli tests only (node --test)

# Single test or filter
dotnet test src\GxMcp.Worker.Tests\GxMcp.Worker.Tests.csproj --filter "FullyQualifiedName~TemporalListTests"
dotnet test ...csproj --filter "FullyQualifiedName=GxMcp.Worker.Tests.TemporalListTests.SortByLastUpdate_OrdersDescending"
```

Known flaky in parallel runs: `EdgeCaseRegressionTests.Dispatcher_PatchApply_ValidateOnly_MapsToDryRun_ViaConvention`, sometimes `PatternApplyServiceTests.*` — all pass in isolation. Treat a single failure as a flake until you reproduce it isolated.

### Reload a running worker without restarting the MCP client

After editing Worker code, you can hot-swap the running worker:

- From inside any MCP session: `genexus_worker_reload mode=hard sourceDir=C:\Projetos\Genexus18MCP\src\GxMcp.Worker\bin\Debug`
- Force-kill path (use when worker is wedged and not responding): `genexus_worker_reload mode=soft force=true`

After a worker-reload the gateway's pipe handle can go stale — if the next call returns `Worker for KB '…' crashed/exited`, reconnect MCP via `/mcp` (Claude Code) once.

## Permissions granted to the assistant

Each entry must include: **trigger** (the precise condition that activates the
permission), **action** (what the assistant may do), and **rationale** (why this
is preferable to asking). Permissions should be reviewed quarterly to catch
broad rules that accumulate over time.

### Kill the Gateway/Worker when they lock build outputs

- **Trigger:** `dotnet build` / `dotnet test` fails with `MSB3027` or `MSB3021`
  citing `GxMcp.Gateway.exe` or `GxMcp.Worker.exe` as the locking process.
- **Action:** `Stop-Process -Name GxMcp.Gateway,GxMcp.Worker -Force` (PowerShell)
  or `taskkill /IM GxMcp.Gateway.exe /F`.
- **Rationale:** these are the user's own dev processes; pausing to ask each
  time adds friction without protecting anything (the user can restart by
  reconnecting the MCP client or rerunning the harness). Permission does NOT
  extend to other processes, system services, or remote machines.
- **Out of scope:** killing arbitrary processes by name match, killing
  GeneXus IDE / Visual Studio, force-killing build daemons under a different
  user, force-killing anything when no MSB lock error is present.
- **Granted:** 2026-05-15 by user. Last reviewed: 2026-05-15.

## Self-update protocol (LLM-facing)

When an AI agent connects to this MCP, it can — and should — proactively check whether the server it's running against is up to date.

### How to check

Call `genexus_whoami`. The response includes an `update` block:

```json
"update": {
  "currentVersion": "2.5.0",
  "latestVersion": "2.5.3",
  "updateAvailable": true,
  "checkedAt": "2026-05-19T19:22:00Z",
  "releaseUrl": "https://github.com/lennix1337/Genexus18MCP/releases/tag/v2.5.3",
  "command": "npx genexus-mcp@latest init",
  "restartRequired": true
}
```

The check is performed by the gateway in the background on `initialize`, cached for 24h in `%LOCALAPPDATA%\GenexusMCP\update-check.json`. `whoami` just reads the cache — instant, no network round-trip on the user's tool call.

### What the LLM should do

- **On the first `whoami` of a session**, look at `update.updateAvailable`. If `true`, surface it to the user in plain language: *"Heads up — GeneXus MCP v{latestVersion} is out (you're on v{currentVersion}). Release notes: {releaseUrl}. Want me to install it?"*
- **If the user agrees**, run the upgrade via the Bash / shell tool the client provides. The exact command lives in `update.command` (default `npx genexus-mcp@latest init`); pass the user's KB and GeneXus paths from `whoami.kb.path` and `whoami.geneXus.installationPath` if running the non-interactive form: `npx genexus-mcp@latest init --kb "<kb>" --gx "<gx>"`.
- **Then tell the user to fully restart the AI client.** The gateway can't hot-reload itself (it's the process the client spawned); the new binaries are picked up on the next launch. `update.restartRequired` is the explicit signal.
- **Do not auto-update without asking.** Installs touch the user's MCP client config and the user expects to see the upgrade prompt before paths change.
- **Don't nag.** Mention the available update once per session, not on every tool call. The cached `checkedAt` is your hint — if it's the same value as a few turns ago, the user has been told.

### When the update check is disabled

Set environment variable `GENEXUS_MCP_NO_UPDATE_CHECK=1` to disable the background check entirely. Some corporate networks block GitHub API; in those cases `update` returns `{currentVersion, updateAvailable: false, note: "no update-check yet ..."}` and the LLM should respect the absence and not pester.

## Tool playbook — v2.6.6 additions

Discoverable via `tools/list`; full schema in `src/GxMcp.Gateway/tool_definitions.json`. Each entry below is a 2-3 line orientation for the LLM agent.

- **`genexus_lifecycle action=status wait=<sec> since=<baseline>`** — event-driven progress. Worker blocks on the task's `ManualResetEventSlim` and returns the moment the state transitions out of `baseline` (or `wait` seconds elapse). Replaces 1-2s polling loops.
- **`genexus_history action=restore discard=true target=<obj>`** — IDE-parity Discard-changes. Restores the part bytes from the most recent `EditSnapshotStore` entry under `.gx/snapshots/`; no commit / rollback / VCS round-trip. Envelope surfaces `restoredFrom` (timestamp + snapshot path).
- **`genexus_preview action=run`** — F5 launcher. Resolves the KB's startup object via `KbService.GetLauncherObjectName` (`StartupObject` env property → `DefaultObject` fallback) and opens it in the headless bridge. No `target` argument required.
- **`genexus_analyze mode=parent_context target=<webpanel>`** — popup-vs-standalone classification. Returns `{ openedAs: "popup"|"standalone", hint }` so the agent knows whether the panel was generated for `genexus_create_popup` or as a top-level screen. The same `popupHint` is inlined into the create_popup response so both sides agree on the first call.

## Release discipline

- Before any release (`./release.ps1`, tag, or GitHub Release), update
  `CHANGELOG.md` with an entry for the exact version being released.

### One-shot release command

Cutting a release is a single command — `./release.ps1` handles version
bumps, build, zip, commit, tag, push, and `gh release create` (with
`publish.zip` attached **in the same API call** as create):

```powershell
.\release.ps1 -Version 2.6.9         # full bump → build → ship
.\release.ps1                        # no version bump; use current package.json
.\release.ps1 -Version 2.6.9 -DryRun # rehearse without touching origin
```

**Don't** run `gh release create` by hand. The workflow at
`.github/workflows/release.yml` expects a `publish.zip` asset on the
release; creating without the asset publishes a release that the
workflow fails on with `publish.zip missing` (the script attaches it in
one call so the workflow's first `release.published` event succeeds).

The Worker can't build on GitHub-hosted runners (it references Artech.\*
DLLs from a local GeneXus 18 install which isn't on `ubuntu-latest`),
so the zip has to be produced on a Windows machine with GeneXus
installed. `release.ps1` does this.

### npmjs.com webpage lag after publish

After `release.ps1` finishes and the workflow turns green, the package
is live on the npm **registry** immediately:

```powershell
npm view genexus-mcp version            # → 2.6.8 right away
npm view genexus-mcp dist-tags --json   # { "latest": "2.6.8" }
npm install -g genexus-mcp@latest       # gets 2.6.8
```

The npmjs.com **website** (`npmjs.com/package/genexus-mcp`) is served
from a separate CDN that caches the rendered page and **can lag the
registry by 10–30 minutes**. The right-sidebar "Version" label and the
"Published N hours ago" line can still show the previous version even
when the README badge (`shields.io`, queries the live registry) already
shows the new one. This is a known npmjs.com UI quirk, not a publish
failure. Don't re-cut the release; just wait or verify via
`npm view` / `registry.npmjs.org/genexus-mcp/latest`.

When a user reports "still on old version after install", the actual
fixes (in order) are:

1. `where.exe genexus-mcp` — multiple matches mean an older install
   (e.g. from `install.ps1` build-from-source) is masking the npm one.
   Remove the non-npm copy from `PATH`.
2. `npm cache clean --force && npm uninstall -g genexus-mcp && npm install -g genexus-mcp@<version>` — pins past any cached metadata.
3. Confirm `genexus-mcp doctor` reports the expected version.

### CHANGELOG voice — release-facing, not roadmap-internal

Entries in `CHANGELOG.md` are read by users on GitHub Releases / npm /
package pages — they should describe **what the user gets**, not how the
sausage was made. Follow these rules:

- **Lead with user-facing capability**, not internal nomenclature. "**`genexus_preview`** — render a WebPanel via headless Chrome..." not "**W4 — Render preview implementation**".
- **No roadmap / workstream codes** (W1, W2, FR#3, SP4.T5, etc.) in the user-facing portion. Cross-reference docs in a single line at the top of the version (`See docs/mcp-roadmap-ide-parity.md for design context.`) if relevant; never sprinkle codes through the bullets.
- **No internal-only context** in user-facing bullets: friction-report cross-references, session narratives, code-archeology asides, "post-roadmap status" tables, agent IDs, commit hashes. Keep those for `docs/` and PR descriptions.
- **Use the four standard sections** (in this order, omit unused ones): `### Added`, `### Fixed`, `### Changed`, `### Removed`. Plus `### Internal` at the **bottom** for engineer-only notes (test counts, schema-budget bumps, internal helper renames, fixture regen instructions).
- **One bullet per capability**, lead bold-name (tool / class / behavior), then 1–4 sentences of plain English. No CLR type dumps in the user-facing copy — link them under `### Internal` if needed.
- **Concrete example values** when they aid comprehension (`"AttributeBlue"`, `Class="…"`), not opaque GUIDs unless the bug was about GUIDs.
- **Past tense for fixes** ("Raw-XML writes that emitted `OnClickEvent=…` were silently ignored…"); imperative-or-present for new features ("Apply a GeneXus pattern… ").
- **Don't reference KB-specific names** (Maria Daiane, AcademicoHomolog1, dani.aspx) in the changelog. The release goes out to everyone; their KB has different objects.
- **Don't claim test counts in the user-facing section.** Test counts and skipped-test caveats go under `### Internal`.

Compare these two takes on the same fix:

> ❌ Roadmap-internal voice
> #### W1 — SDK-routed layout writes (gxButton OnClickEvent fix)
> **`gxButton` custom `OnClickEvent` now wires correctly in WebForm-html.** Friction-report 2026-05-19 #1 root cause: the SDK maps the descriptor name `OnClickEvent` to a per-element XML attribute (gxButton → `Event`, gxAttribute/gxImage → `eventGX`). Raw-XML writes that emit `OnClickEvent=` literally are silently dropped by the HTML generator. Fix: `WebFormTypedPropertyWriter.ApplyDescriptorPathFixup(part)` — post-write hook that walks every IWebTag and routes any descriptor-name attribute through `Artech.Common.Properties.PropertiesObject.SetPropertyValue` / `SetPropertyValueString` via reflection.

> ✅ Release-facing voice
> ### Fixed
> **`gxButton OnClickEvent` for custom events.** Raw-XML writes that emitted `OnClickEvent="'MyEvent'"` were silently ignored by the HTML generator, which only reads the per-element XML attribute the SDK assigns (`Event` for `gxButton`, `eventGX` for `gxAttribute` / `gxImage`). The MCP now routes descriptor-named properties through the SDK's typed property API so the canonical XML attribute is emitted. Applies on every layout save; idempotent.

When in doubt, re-read the entry as if you were a developer who just installed the package and is wondering what changed — would they care about this sentence? If not, demote to `### Internal` or delete.
