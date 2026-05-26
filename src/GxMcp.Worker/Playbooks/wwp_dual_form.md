# WWP host WebForm — dual-form schema + edit boundary

A WorkWithPlus-hosted WebPanel has TWO `<Form>` elements inside
`WebForm`:

```xml
<webForm>
  <Form type="webForm">
    <!-- Legacy WebForm; mostly empty wrapper. Some KBs use this. -->
  </Form>
  <Form type="layout">
    <detail>
      <layout id="<GUID>">
        <table controlName="..." tableType="Responsive" class="<theme-guid>-N">
          <!-- the actual rendered tree: <textblock>, <data>, <action>, <errorviewer> -->
        </table>
      </layout>
    </detail>
  </Form>
</webForm>
```

The SDK's HTML generator reads **only** `<Form type="layout">` for WWP
hosts. Raw edits to `<Form type="webForm">` are silently dropped.

## The hard rule: edit PatternInstance, not WebForm

Every WWP host's WebForm is **regenerated from PatternInstance** on
each apply-on-save. Direct WebForm edits are wiped on the next reapply.

```
✓ genexus_edit name=WorkWithPlus<Name> part=PatternInstance ...
✗ genexus_edit name=WorkWithPlus<Name> part=WebForm ...   # blown away on save
```

If the IDE / agent must change the rendered layout, change
`PatternInstance` and rebuild — WWP propagates to WebForm.

## Allowed control-element attributes (SDK-validated)

The SDK strictly validates layout-form attributes; unknown attributes
trigger src0265 / src0216 on the next build. Common elements:

| Element        | Allowed attrs                                                            |
| -------------- | ------------------------------------------------------------------------ |
| `<textblock>`  | `controlName`, `caption`, `class`                                        |
| `<data>`       | `attribute` (use `&Var` form), `class`, `controlName` only when bound    |
| `<action>`     | `controlName`, `onClickEvent`, `caption`, `class` (NO `id`)              |
| `<errorviewer>`| `controlName="ErrorViewer"` (NO `id`)                                    |
| `<table>`      | `controlName`, `tableType` (`Responsive`/`Form`), `class` (theme GUID-N) |

**Avoid**: `id` on action/errorviewer, `defaultCaption` on textblock,
`controlName` on bound `<data>`, `ColSpan` on cells, `HAlign` on action
cells. These are rejected by the WWP HTML generator.

## Theme class GUIDs

WWP themes expose classes via `<guid>-<index>` strings, e.g.
`d4876646-98dd-419b-8c1c-896f83c48368-1`. To find the GUID for an
existing class:

```
genexus_layout action=get_tree name=<WorkingHost>
```

…then copy the `class=` value from a matching element. Or use symbolic
names like `Attribute`, `AttributeBold`, `TextBlockTitle` — the MCP's
write verifier tolerates symbolic ↔ GUID equivalence.

## Variable references

In `<data>` elements the attribute name uses an ampersand prefix:

```xml
<data attribute="&amp;MyVar" class="Attribute" />
```

The MCP's verifier tolerates the `&amp;Var` ↔ `var:N` SDK-normalized
form, so either round-trips cleanly.

## Recovering from a wedged WebForm

If a manual WebForm edit broke the host:

```
genexus_versioning action=history_restore discard=true target=<WorkWithPlusHost>
```

This restores the pre-edit snapshot from `.gx/snapshots/` — IDE
Discard-changes parity.
