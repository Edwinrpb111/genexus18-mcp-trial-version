# Polished WWP popup layout — PatternInstance idiom

A `genexus_create_popup` call wires an empty WWP-hosted WebPanel. For a
polished, organized form (label/value columns aligned, radios stacked
vertically, inline label+input, primary action bar), edit the host's
`PatternInstance` part with the conventions below — **never edit the
generated parent WebForm directly** (next reapply overwrites it).

## Canonical PatternInstance skeleton

```xml
<instance ... type="WebPanel">
  <WPRoot ... Title="<your title>" Template="EmptyWithTitle">
    <table name="TableMain" themeClass="Table100Width" numberOfColumns="1">

      <!-- Outer group with title + border via theme -->
      <table name="TablePrincipal" isGroup="True" numberOfColumns="1"
             title="<group title>" themeClass="GroupFiltro">

        <!-- Section A: read-only summary, label-left + value column -->
        <table name="TableSummary" isGroup="True" numberOfColumns="2"
               themeClass="GroupFiltro">
          <variable name="TotalA"  description="Label A:"  readOnly="True"
                    descriptionPosition="Left" />
          <variable name="TotalB"  description="Label B:"  readOnly="True"
                    descriptionPosition="Left" />
        </table>

        <!-- Section B: stacked radios -->
        <table name="TableChoice" isGroup="True" numberOfColumns="1"
               title="Question? *" themeClass="GroupFiltro">
          <variable name="Answer"
                    controlInfoDef="Custom"         defaultControlInfoDef="Custom"
                    controlType="Radio Button"      defaultControlType="Radio Button"
                    controlValues="Yes:Y,No:N,N/A:X"
                    defaultControlValues="Yes:Y,No:N,N/A:X"
                    controlPropertiesString="Direction=Vertical"
                    isRequired="True" />
        </table>

        <!-- Section C: inline label + input -->
        <table name="TableExtra" isGroup="True" numberOfColumns="2"
               themeClass="GroupFiltro">
          <variable name="Extra" description="Detail:"
                    descriptionPosition="Left" />
        </table>

        <!-- Section D: primary action -->
        <table name="TableActions" themeClass="TableActionsResp"
               numberOfColumns="1">
          <userAction name="Confirm" caption="Confirm"
                      defaultCaption="Confirm" class="PrimaryAction" />
        </table>

      </table>
    </table>
    <steps />
  </WPRoot>
</instance>
```

## Property crib sheet — the non-obvious bits

| Goal                                    | Property                                                        |
| --------------------------------------- | --------------------------------------------------------------- |
| Title with horizontal rule              | `WPRoot Title=… Template="EmptyWithTitle"`                      |
| Box border + padding around a group     | `<table isGroup="True" themeClass="GroupFiltro">`               |
| Label LEFT of input, inline             | `<variable descriptionPosition="Left">` + parent `numberOfColumns="2"` |
| Radio buttons (variable as enum)        | `controlInfoDef="Custom" controlType="Radio Button"` + `controlValues="Label:Value,…"` |
| Radios stacked vertically (not inline)  | `controlPropertiesString="Direction=Vertical"`                  |
| Read-only field                         | `<variable readOnly="True">`                                    |
| Primary blue button                     | `<userAction class="PrimaryAction">`                            |
| Action bar layout (left-aligned, gap)   | `<table name="TableActions" themeClass="TableActionsResp">`     |
| User-defined event from action          | `userAction name="Confirm"` → fires `Event 'DoConfirm'`         |
| Reserved Standard Action — DO NOT use   | `userAction name="Enter"` (reserved; emit `Confirm` / `Save`)   |

## Reserved userAction names

`Enter`, `Cancel`, `Save`, `Delete`, `Insert`, `Update`, `New`, `Refresh` are
**Standard Actions** in WWP. Using them on a userAction binds the WWP
default behavior instead of firing your event. Pick a custom verb
(`Confirm`, `Apply`, `Submit`) and handle `Event 'Do<verb>'` in the
parent's Events part.

## Conditional visibility (toggle a group based on a variable)

```xml
<table name="TableExtra" isGroup="True"
       visibleCondition="&amp;Answer = 'Y'"
       defaultVisibleCondition="&amp;Answer = 'Y'"
       ... />
```

- Renders **hidden on initial load** when the expression is false. ✓
- WWP generates `Sub 'AttributesSecurityCode'` that flips a CSS class
  (`!Invisible` ↔ `GroupFiltro`); call it from `Event Start`.
- **Limitation**: client-side toggle on a radio change does NOT
  re-evaluate the expression without a server roundtrip. If the user
  needs the group to appear immediately on click, either keep it always
  visible and validate in the action event, or wire `AUTO_REFRESH` on
  the controlling variable with a server postback.

## Wiring an action event in the parent

After emitting `<userAction name="Confirm">` in PatternInstance, the
parent host gets an empty `Event 'DoConfirm'`. Edit it via:

```
genexus_edit name=<ParentHost> part=Events mode=patch
  context="Event 'DoConfirm'"
  operation=Insert_After
  content="    // validation + action here"
```

## Apply-on-save

WWP regenerates the parent WebForm from PatternInstance only when
"Apply this pattern on save" is enabled in the IDE (per-instance
checkbox). The MCP cannot toggle this flag reliably across versions —
ask the user to enable it once via the IDE if their PatternInstance
edits aren't reflected after Ctrl+S.

## End-to-end recipe

1. `genexus_create_popup` to scaffold the WWP host.
2. `genexus_edit name=WorkWithPlus<Name> part=PatternInstance mode=full content=<skeleton above>`
3. `genexus_edit name=<ParentHost> part=Events` — add `Event 'Do<Action>'` body.
4. `genexus_lifecycle action=build target=WorkWithPlus<Name> wait_until_done=true`
5. `genexus_preview action=run name=WorkWithPlus<Name>` — visual verify.
