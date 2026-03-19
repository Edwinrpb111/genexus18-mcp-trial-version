# Nexus IDE SOTA Plan

## Goal

Deliver a native editor experience in Nexus IDE without losing the MCP-first architecture:

- MCP remains the single domain/backend layer
- Nexus IDE becomes a native editor client on top of MCP
- Explorer, Ctrl+P, search, breadcrumbs, watchers, and save behave like a real workspace

## Current Status

- [x] MCP-first backend consolidated
- [x] Gateway build passing
- [x] Gateway test suite passing
- [x] Extension compile passing
- [x] Progress checkpoint committed on branch `codex/remove-api-command-central-role`
- [ ] Native VS Code/Antigravity workspace experience complete
- [ ] Ready for `main`
- [ ] SOTA

## Target Architecture

### Domain Layer

- MCP is the only source of truth for:
  - read
  - write
  - refactor
  - history
  - structure
  - build
  - analysis

### Editor Layer

- Nexus IDE uses a physical mirrored workspace as the main editor surface
- `.gx_mirror` becomes the primary mounted workspace
- `gxkb18:/` stops being the primary runtime path

### Mirror Role

- local editable/indexable representation of the KB
- not a second source of truth
- hydrated and synchronized through MCP

## Execution Plan

### Phase 1: Mirror-First Runtime

- [x] Redefine the main workspace mount
  - Replace `gxkb18:/` as the primary mounted workspace in [extension.ts](C:/Projetos/GenexusMCP/src/nexus-ide/src/extension.ts)
  - Mount `.gx_mirror` as a real folder in the editor
  - Keep `gxkb18:/` only as temporary fallback if still needed during migration

- [ ] Promote shadow to full KB materialization
  - Expand [gxShadowService.ts](C:/Projetos/GenexusMCP/src/nexus-ide/src/gxShadowService.ts)
  - Create a full hydrate routine for the KB tree
  - Materialize real directories/files with stable paths

- [x] Separate read sync from write sync
  - Keep [ShadowManager.ts](C:/Projetos/GenexusMCP/src/nexus-ide/src/managers/ShadowManager.ts) for disk-to-KB sync
  - Add explicit KB-to-disk hydrate flow
  - Prevent sync loops

- [x] Define canonical physical path model
  - Refactor [GxUriParser.ts](C:/Projetos/GenexusMCP/src/nexus-ide/src/utils/GxUriParser.ts)
  - Refactor [GxPartMapper.ts](C:/Projetos/GenexusMCP/src/nexus-ide/src/utils/GxPartMapper.ts)
  - Standardize object and part file naming

### Phase 2: Native Editor Integration

- [x] Make Explorer and KB Explorer read the same real workspace
  - Reduce divergence between custom tree and native Explorer
  - Ensure both reflect the same files and hierarchy

- [ ] Adapt providers to physical mirror files
- [~] Adapt providers to physical mirror files
  - [definitionProvider.ts](C:/Projetos/GenexusMCP/src/nexus-ide/src/definitionProvider.ts)
  - [hoverProvider.ts](C:/Projetos/GenexusMCP/src/nexus-ide/src/hoverProvider.ts)
  - [referenceProvider.ts](C:/Projetos/GenexusMCP/src/nexus-ide/src/referenceProvider.ts)
  - [renameProvider.ts](C:/Projetos/GenexusMCP/src/nexus-ide/src/renameProvider.ts)
  - [workspaceSymbolProvider.ts](C:/Projetos/GenexusMCP/src/nexus-ide/src/workspaceSymbolProvider.ts)
  - Definition/reference/symbol/completion/codelens and key commands already accept mirror files
  - Remaining validation is runtime behavior in Antigravity and any providers still depending on fallback virtual URIs

- [ ] Restore native Ctrl+P and search behavior
  - Remove dependency on proposed `registerFileSearchProvider` for critical behavior
  - Let the editor index `.gx_mirror` natively
  - Keep custom search providers only as optional enhancement

- [ ] Guarantee save roundtrip
  - Saving in the editor persists to GeneXus through MCP
  - Validate patch/full-write behavior
  - Validate Source, Rules, Events, Variables, Structure, Layout, and Indexes where applicable

### Phase 3: Performance and Hardening

- [ ] Define incremental hydration strategy
  - Fast initial tree
  - Lazy heavy content loading
  - Background metadata sync
  - No random warmup behavior

- [ ] Update integrated test flow
- [~] Update integrated test flow
  - Fix [test_all.ps1](C:/Projetos/GenexusMCP/scripts/test_all.ps1) for valid MCP handshake and real runtime expectations
  - Add mirror-first extension tests
  - `test_all.ps1` now passes with MCP `2025-06-18`
  - extension tests now cover mirror index parsing and mirrored part resolution

- [ ] Remove legacy runtime assumptions
  - De-emphasize or remove `gxkb18:/` from the critical path
  - Remove temporary fallback logic that no longer belongs

- [ ] Align documentation with final architecture
  - Update README/docs/skills after the runtime pivot is complete

## Acceptance Criteria

The work is only complete when all of the following are true:

- [ ] MCP remains fully functional for external clients
- [ ] Antigravity/VS Code opens the KB as a real workspace folder
- [ ] Native Explorer shows the complete KB correctly
- [ ] KB Explorer does not diverge from Native Explorer
- [ ] Ctrl+P finds KB objects natively
- [ ] Saving files persists correctly to GeneXus
- [ ] Core providers still work on the mirrored files
- [ ] Gateway tests pass
- [ ] Extension compile passes
- [ ] Integrated test cycle passes
- [x] Integrated test cycle passes
- [ ] No critical feature depends on proposed editor APIs

## Notes

- The current blocker is architectural, not just a bug in search or tree rendering.
- Continuing to optimize `gxkb18:/` as the primary runtime path is not the correct route to the desired native experience.
- The correct path is MCP as backend plus mirrored physical workspace as editor surface.
- Current implementation status:
  - mirror mount is the main workspace path
  - a persisted `.gx_index.json` now stabilizes `file -> {type,name,part}` and `object -> file`
  - KB Explorer now reads the same physical mirror as the native Explorer
  - part switching for mirrored text parts now creates/resolves physical files lazily through the mirror index
  - integrated test cycle is green again
  - remaining gaps are native search validation and runtime validation in Antigravity with the real KB loaded
