# AGENTS.md

Project-level instructions for AI assistants working on Genexus18MCP.

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

## Release discipline

- Before any release (`scripts/release.ps1`, tag, or GitHub Release), update
  `CHANGELOG.md` with an entry for the exact version being released.

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
