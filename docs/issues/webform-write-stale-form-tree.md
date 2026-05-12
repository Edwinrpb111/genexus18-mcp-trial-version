# WebForm write does not persist: `m_Document` clobbered by stale Form tree on save

**Status:** open · **Component:** `GxMcp.Worker` / `WebFormXmlHelper` · **Severity:** blocks programmatic visual edits

## Symptom

`genexus_edit part=WebForm mode=patch|full` returns:

```
Visual write verification failed — The SDK save path completed, but the persisted WebForm XML
does not match the requested content.
Diff: Child count differs at /GxMultiForm/Form/body/.../td (1 vs 0)
```

The write **always** fails verification regardless of the change (even a single caption swap on
an existing TextBlock). Persisted XML reads back as the unchanged original.

## Root cause (verified via SDK IL disassembly)

The `Artech.Genexus.Common.Parts.WebFormPart` (GeneXus 18.0.7) maintains **two parallel
representations** of the form:

| Storage | Field | Populated by | Read by |
|---|---|---|---|
| Raw XML | `XmlDocument m_Document` | `Document` getter/setter, `EditableContent` setter, `LoadXml` | `SerializeData` (via `Convert.ToByteArray(m_Document, this)`) |
| Parsed tree | per-control `IWebTag` collection with typed `Properties` | `DeserializeDataFromDocument`, IDE control editors | `BeforeSaveKBObject` (via `IWebTag.SaveProperties`) |

The save lifecycle, traced from IL:

1. Caller invokes `obj.EnsureSave(true)` (in `WriteService.WriteVisualPart` line 1004).
2. SDK fires `BeforeSaveKBObject` on every part.
3. `WebFormPart.BeforeSaveKBObject` iterates each `IWebTag` and calls `SaveProperties()`.
4. `IWebTag.SaveProperties()` writes the in-memory typed `Properties` collection **back into the
   `XmlAttribute` collection of the underlying node in `m_Document`** — overwriting whatever was
   there.
5. `SerializeData()` then takes `m_Document` (now with overwritten attributes) and converts it
   to bytes for KB storage.

If the in-memory Properties are stale (which they always are when we modify only the
`m_Document` XML), step 4 silently reverts our changes before step 5 serializes.

## What we tried

All combinations of the following — none persist a change:

- `Document.RemoveAll(); Document.LoadXml(newXml)` (direct DOM rewrite)
- `EditableContent = newXml` (canonical IDE string setter; verified via IL that it does
  `set_Document(new XmlDocument().LoadXml(value)); m_EditableToStoredNeeded = true`)
- `EditableToStored()` after either of the above — **throws `GxException: "Atributo desconhecido
  'att:13937'"`** from
  `Artech.Genexus.Common.CustomTypes.AttributeVariableConverter.GetAttVarByName`. The lookup
  walks `att:NNNN` references and fails because the worker context's `KBModel` returns null for
  attribute IDs that exist in the KB (probably needs `KBModel.Resolve()` or equivalent priming
  the IDE does that we don't replicate). The throw poisons the part: control list partially
  mutated, save then drops the affected controls.
- `DeserializeDataFromDocument()` (the more tolerant cousin) — runs without throwing, logs
  success, but the next `obj.EnsureSave(true)` still produces the unchanged XML. This implies
  the typed `Properties` reload from `m_Document` isn't actually happening for the controls we
  modify, OR `SaveProperties` re-serializes from a copy taken before our reload.
- Reflection nullification of fields matching `form|layout|parsed|cached` — no effect.
- Invocation of `Invalidate`/`Refresh`/`Reload`/`MarkDirty`/`Touch`/`OnDocumentChanged` — no
  effect.
- `webFormPart.Save()` directly (in addition to `obj.EnsureSave`) — no effect.

## Verified SDK surface

```
namespace Artech.Genexus.Common.Parts;
public class WebFormPart : KBObjectPart {
    // properties
    public string EditableContent { get; set; }          // set: new XmlDoc → set_Document → flag
    public XmlDocument Document { get; set; }            // set: stfld m_Document + OnPropertyValueChanged
    public int LastModification { get; }
    public IndexableContent Content { get; }
    public IContentAnalyzer Analyzer { get; }
    public IEnumerable<...> Variables { get; }
    public string PartDescriptionCache { get; set; }

    // fields
    string m_EditableContent;
    bool m_EditableToStoredNeeded;
    int m_LastModification;
    bool m_FixPending;
    XmlNodeChangedEventHandler InvalidateHandler;
    XmlDocument m_Document;

    // methods (relevant subset)
    void EditableToStored();                             // calls (msg) overload
    void EditableToStored(OutputMessages messages);      // FixClassProperty per tag + WebFormEditable.EditableToStored
    void DeserializeDataFromDocument();                  // TrackDocumentChanges + tag.SaveProperties +
                                                         // FixDuplicatedControl + FixNoNameControl + FixClassProperty +
                                                         // FixWebFormData + WebFormEditable.ConvertAfterDeserialize +
                                                         // InvalidateLastModification
    void BeforeSaveKBObject();                           // ITERATES TAGS, READS PropDefinitionCollection
    void OnBeforeSaveEntity(EntityEventArgs args);       // ITERATES TAGS, CALLS tag.SaveProperties  ← clobber site
    void TrackDocumentChanges();                         // hooks XmlDocument.NodeInserted/Removed → InvalidateLastModification
    void UntrackDocumentChanges(XmlDocument doc);
    Byte[] SerializeData();                              // Convert.ToByteArray(m_Document, this)  ← OK if m_Document fresh
    void DeserializeData(byte[] data);
    void InvalidateData();
    void InitializeData();
    void FixWebFormData();
    void InvalidateLastModification();                   // m_LastModification++
    Variable GetVariable(...);
    IEnumerable<Variable> GetReferencedVariables();
    IEnumerable<...> GetPartReferences();
}

// static helper that does the actual editable→stored conversion:
namespace Artech.Genexus.Common.Parts.WebForm;
public static class WebFormEditable {
    void EditableToStored(WebFormPart webPart, OutputMessages messages);
    // implementation: GetVersions(part.Document); GetForms(part.Document); for each form: Form.Import(handler, kbObject, addError);
    void EditableToStored(KBObject kbObj, XmlElement webFormElement, Action<...> addError);
}
```

## Hypothesis for fix

Two viable paths, both significantly more work than the patch-as-text approach we have today:

### A. Bypass `SaveProperties` clobber by updating typed Properties

For each control we want to mutate:

1. Read the WebForm XML, identify the target node by `id` attribute.
2. Locate the in-memory `IWebTag` whose `Node` matches that id (walk
   `WebFormHelper.EnumerateWebTag(part)`).
3. For each XML attribute the user changed (e.g. `CaptionExpression`,
   `PATTERN_ELEMENT_CUSTOM_PROPERTIES`), look up the matching `IPropertyDefinition` in
   `tag.PropertiesDefinition` and call `tag.Properties[def] = newValue` via the SDK's
   `PropertyValueConverter` for the declared property type.
4. Save normally — `BeforeSaveKBObject.SaveProperties` will now write OUR values back to the
   document.

This requires building a mapping table from `<gxTextBlock>` / `<gxAttribute>` / `<fieldset>` /
etc. XML attribute names → typed Property keys (`Caption`, `Class`, `ControlType`, `Width`, …)
for every supported control type. The mapping changes between control types (a TextBlock
exposes `Caption` as a `Tokens` expression; a SimpleGrid item exposes `ControlType`,
`ControlValues`, `ColumnClass`, …). Effort: ~1–2 weeks of careful work + tests against a
catalog of real forms.

### B. Force a model rebuild by deleting and re-importing the part

Theoretically possible via `KBObject.Parts.Delete(part)` then `Parts.Add(newPart)`, but most
SDK part types are immutable post-creation and the K2BTools/WorkWithPlus patterns attached to
the WebPanel reference the part by GUID — this would break pattern instances. **Not
recommended.**

### C. Pre-prime the KBModel so EditableToStored doesn't throw

Inject the missing `att:NNNN` resolutions into whatever cache `AttributeVariableConverter` uses
before invoking `EditableToStored`. Reflection target unknown — need to follow the call chain
from `GetAttVarByName` into the resolver to find the cache. If we can prime it, then
`EditableToStored` would not throw, would update the typed Properties, and the rest of the
save would persist them correctly. This is the cleanest path if reachable.

## Reproduction

1. Open KB `AcademicoHomolog1` in GeneXus 18.0.7.
2. Via MCP, call `genexus_edit name=ListaAtiCPAlunoUniGra part=WebForm mode=patch
   verifyRollback=true` with any caption change on `TextBlockSaldoHoras`.
3. Observe `Visual write verification failed` with diff at the deeply-nested td. Logs show
   `EditableToStored() threw: GxException: Atributo desconhecido 'att:13937'` if using
   the `EditableContent` + `EditableToStored` path.

## Workaround in place today

`genexus_edit part=WebForm` **read** works (dry-run patches succeed). For **write**, the user
must drag controls onto the form in the IDE; properties on existing controls can be tweaked
via `genexus_layout set_property` (which goes through a different code path that DOES work for
property mutation on already-parsed controls).

## Files touched while investigating

- `src/GxMcp.Worker/Helpers/WebFormXmlHelper.cs` — added `TrySetEditableContent`,
  `PushDocumentToStoredModel`, and unwrapping of `TargetInvocationException` for diagnostics.
- `src/GxMcp.Worker/Services/PatchService.cs` — added WebForm/Layout branch to
  `ReadSourceFast` (read path works).
- `src/GxMcp.Worker/Services/WriteService.cs` — `WriteVisualPart` is the verification site (no
  changes needed here, the diff message is accurate).

## Next steps for a future session

1. Reach for path **A** or **C** above.
2. Add a `genexus_layout set_control_property` action that takes `controlId + propertyName +
   value` and goes through `IWebTag.Properties` directly — even without solving the global
   write problem, this would let an LLM mutate properties on existing controls reliably.
3. Build a small `WebFormControlMap` registry: `controlTagName → (xmlAttrName →
   typedPropertyKey)`. Start with `gxTextBlock`, `gxAttribute`, `fieldset`, `IMG`,
   `simplegrid item`. Each entry is a few lines.
