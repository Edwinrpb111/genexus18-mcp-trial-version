---
name: GeneXus MCP Mastery
description: Current MCP-first usage guide for the GeneXus gateway, with coordination for specialized skills.
---

# GeneXus MCP Mastery

Use this skill to interact with the GeneXus KB through the MCP server in this repository.

## Coordination with Specialized Skills

When working on complex GeneXus tasks, coordinate this "Transport" skill with "Knowledge" skills (imported from [`genexuslabs/genexus-skills`](https://github.com/genexuslabs/genexus-skills) — see [`.gemini/skills/NOTICE.md`](../NOTICE.md)):

- **[Nexa](../nexa/SKILL.md)**: Authoritative reference set for GeneXus 18 objects, commands, types, properties, and modeling rules (`object-*.md`, `common-*.md`, `properties-*.md`, `global-*.md`). Load on demand — keep context minimal.
- **[GeneXus 18 Guidelines](../genexus18-guidelines/SKILL.md)**: Local conventions and engineering rules layered on top of Nexa.
- **[Chameleon Controls Library](../frontend/chameleon-controls-library/SKILL.md)**: 58 web-component docs for Chameleon UI elements.
- **[Mercury Design System](../frontend/mercury-design-system/SKILL.md)**: Mercury tokens, bundles, icons, theming.
- **[Design System Builder](../frontend/design-system-builder/SKILL.md)**: Authoring custom design systems for KBs.
- **[UI Creator](../frontend/ui-creator/SKILL.md)**: Templates for generating GeneXus panels and screens.

## Preferred Workflow

1. **Search**: Use `genexus_query` to find entry points.
2. **Read**: Use `genexus_read` or `resources/read` to get source/metadata.
3. **Plan**: Apply rules from **GeneXus 18 Guidelines** or **Nexa** to design the solution.
4. **Edit**: Use `genexus_edit` for focused changes (singular `name` or `targets[]` for multi-object atomic edits).
5. **UI/UX**: If the task involves screens, apply **Frontend Skills** for modern aesthetics.
6. **Validate**: Use `genexus_lifecycle` to build and verify.

## Multi-KB (v2.3.0+)

The server can hold multiple KBs open at once (`Server.MaxOpenKbs`, default 3), each in its own Worker process. Cross-KB tool calls run in parallel — intra-KB calls remain serialized by the SDK's STA constraint.

- Every non-meta tool accepts an optional `kb` argument: an alias declared in `config.Environment.KBs[]` or an absolute path. When exactly one KB is open and no `kb` is given, that KB is used; with 2+ open the `kb` arg becomes required (`KB_AMBIGUOUS`).
- `genexus_kb action=list` returns open KBs with PID, working-set memory, and idle seconds — use it to decide when to free a slot before opening a new KB.
- `genexus_kb action=open` (alias and/or path) acquires a Worker on demand; `action=close` releases it; `action=set_default` persists `DefaultKb` to `config.json`.
- Legacy single-KB configs (`Environment.KBPath`) auto-migrate to `KBs[]` + `DefaultKb` at load time; existing flows keep working unchanged.

## Tool Best Practices

| Tool | Tip |
| --- | --- |
| `genexus_query` | Narrow down by `typeFilter` for faster results. |
| `genexus_read` | Always check the `Variables` part if adding logic. |
| `genexus_edit` | Prefer `mode=patch` for surgical changes; use `targets[]` for atomic multi-object edits. |
| `genexus_analyze` | One tool covers `linter`, `navigation`, `hierarchy`, `impact`, `data_context`, `ui_context`, `pattern_metadata`, `summary`, and `explain` (the last two replaced `genexus_summarize` and `genexus_explain_code`). |
| `genexus_sql` | `action=ddl` for Transaction/Table DDL, `action=navigation` for the SQL produced by a For Each (replaces `genexus_get_sql` and `genexus_get_sql_for_navigation`). |
| `genexus_kb` | List/open/close/set_default — multi-KB pool management (replaces `genexus_open_kb`). |
| `genexus_properties` | Essential for enabling "Business Component" or "Expose as Web Service". |

## Anti-patterns

- Do not attempt to guess object names; always query first.
- Do not use `genexus_edit` without reading the latest state of the object.
- Avoid large monolithic edits; prefer smaller, validable changes.
- Do not call removed tools (`genexus_batch_read`, `genexus_batch_edit`, `genexus_open_kb`, `genexus_get_sql`, `genexus_get_sql_for_navigation`, `genexus_summarize`, `genexus_explain_code`). They return JSON-RPC -32601 with `error.data.replacedBy` pointing at the current tool.
