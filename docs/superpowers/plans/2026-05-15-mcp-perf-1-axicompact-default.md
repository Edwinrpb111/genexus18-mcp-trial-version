# Sub-plan 1 — axiCompact Default Flip

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans. Steps use `- [ ]` syntax.

**Goal:** Flip the implicit default of `axiCompact` from `false` to `true` for `genexus_query` and `genexus_list_objects`, so listing tools return compact projections (`name`, `type`, `path`[, `parentPath`]) unless the LLM explicitly opts out with `axiCompact=false`.

**Architecture:** The flag is read in `ShouldUseCompactDefaults()` at `src/GxMcp.Gateway/Program.cs:2628-2636`. Today it returns `false` when the token is missing. We invert: missing/null → `true`; only explicit `false` (boolean or string) disables compact. We also declare the flag in `inputSchema` for the two tools so the LLM can discover the opt-out, with `default: true` for honesty. Documentation in `docs/mcp_capabilities_inventory.md`, `docs/mcp_debugging_guide.md`, and `docs/llm_cli_mcp_playbook.md` is updated to reflect the inverted default.

**Tech Stack:** .NET 8, xUnit, Newtonsoft.Json.

---

## File Structure

- **Modify:** `src/GxMcp.Gateway/Program.cs` (lines 2628-2636, `ShouldUseCompactDefaults`)
- **Modify:** `src/GxMcp.Gateway/tool_definitions.json` (add `axiCompact` to two tools)
- **Modify:** `src/GxMcp.Gateway.Tests/GatewayBudgetTests.cs` (new tests for inverted default + explicit opt-out)
- **Modify:** `docs/mcp_capabilities_inventory.md` (line ~44)
- **Modify:** `docs/mcp_debugging_guide.md` (line ~98)
- **Modify:** `docs/llm_cli_mcp_playbook.md` (lines ~117, ~144)
- **Modify:** `CHANGELOG.md` (new entry under unreleased / v2.4.0)

---

### Task 1: Flip the default in `ShouldUseCompactDefaults`

**Files:**
- Modify: `src/GxMcp.Gateway/Program.cs:2628-2636`

- [ ] **Step 1: Write the failing test for inverted default**

Add to `src/GxMcp.Gateway.Tests/GatewayBudgetTests.cs` (after the existing `NormalizeToolPayloadForAxi_ShouldProjectFields_WhenAxiCompactTrue` test around line 309):

```csharp
[Fact]
public void NormalizeToolPayloadForAxi_ShouldApplyCompactByDefault_WhenAxiCompactOmitted()
{
    var payload = new JObject
    {
        ["results"] = new JArray(
            new JObject
            {
                ["name"] = "InvoiceProc",
                ["type"] = "Procedure",
                ["path"] = "Main/Procs/InvoiceProc",
                ["parentPath"] = "Main/Procs",
                ["description"] = "verbose"
            })
    };

    // No axiCompact arg at all — expected to default to compact.
    var args = new JObject();

    var method = typeof(Program).GetMethod(
        "NormalizeToolPayloadForAxi",
        BindingFlags.NonPublic | BindingFlags.Static);

    Assert.NotNull(method);

    var normalized = (JToken?)method!.Invoke(null, new object?[] { payload, "genexus_list_objects", args, false });
    var obj = Assert.IsType<JObject>(normalized);
    var first = Assert.IsType<JObject>(Assert.IsType<JArray>(obj["results"])[0]);

    Assert.NotNull(first["name"]);
    Assert.NotNull(first["type"]);
    Assert.NotNull(first["path"]);
    Assert.NotNull(first["parentPath"]);
    Assert.Null(first["description"]); // omitted under compact default
}

[Fact]
public void NormalizeToolPayloadForAxi_ShouldReturnFullFields_WhenAxiCompactFalse()
{
    var payload = new JObject
    {
        ["results"] = new JArray(
            new JObject
            {
                ["name"] = "InvoiceProc",
                ["type"] = "Procedure",
                ["path"] = "Main/Procs/InvoiceProc",
                ["parentPath"] = "Main/Procs",
                ["description"] = "verbose"
            })
    };

    var args = new JObject { ["axiCompact"] = false };

    var method = typeof(Program).GetMethod(
        "NormalizeToolPayloadForAxi",
        BindingFlags.NonPublic | BindingFlags.Static);

    var normalized = (JToken?)method!.Invoke(null, new object?[] { payload, "genexus_list_objects", args, false });
    var obj = Assert.IsType<JObject>(normalized);
    var first = Assert.IsType<JObject>(Assert.IsType<JArray>(obj["results"])[0]);

    Assert.NotNull(first["description"]); // full payload preserved
}

[Fact]
public void NormalizeToolPayloadForAxi_ShouldRespectExplicitFields_OverrideCompactDefault()
{
    var payload = new JObject
    {
        ["results"] = new JArray(
            new JObject
            {
                ["name"] = "InvoiceProc",
                ["type"] = "Procedure",
                ["description"] = "verbose"
            })
    };

    // Explicit fields="name,description" must win over the compact default.
    var args = new JObject { ["fields"] = "name,description" };

    var method = typeof(Program).GetMethod(
        "NormalizeToolPayloadForAxi",
        BindingFlags.NonPublic | BindingFlags.Static);

    var normalized = (JToken?)method!.Invoke(null, new object?[] { payload, "genexus_list_objects", args, false });
    var obj = Assert.IsType<JObject>(normalized);
    var first = Assert.IsType<JObject>(Assert.IsType<JArray>(obj["results"])[0]);

    Assert.NotNull(first["name"]);
    Assert.NotNull(first["description"]);
    Assert.Null(first["type"]); // not requested
}
```

- [ ] **Step 2: Run tests to verify they fail**

```
dotnet test src/GxMcp.Gateway.Tests --filter "FullyQualifiedName~NormalizeToolPayloadForAxi_ShouldApplyCompactByDefault" --nologo --verbosity minimal
```

Expected: FAIL — first test asserts `first["description"]` is null but today it is not (default is non-compact).

- [ ] **Step 3: Replace `ShouldUseCompactDefaults` with the inverted version**

Replace lines 2628-2636 of `src/GxMcp.Gateway/Program.cs`:

```csharp
        // Returns true when compact-by-default projection should be applied for tools that
        // declare a default compact field set in GetDefaultCompactFields. Default behavior
        // (no axiCompact key) is TRUE — the LLM must pass `axiCompact: false` to opt out.
        private static bool ShouldUseCompactDefaults(JObject? toolArgs)
        {
            if (toolArgs == null) return true;
            var token = toolArgs["axiCompact"];
            if (token == null) return true;
            if (token.Type == JTokenType.Boolean)
            {
                return token.Value<bool>();
            }
            return !bool.TryParse(token.ToString(), out bool parsed) || parsed;
        }
```

- [ ] **Step 4: Run tests to verify they pass**

```
dotnet test src/GxMcp.Gateway.Tests --filter "FullyQualifiedName~NormalizeToolPayloadForAxi" --nologo --verbosity minimal
```

Expected: all 4 `NormalizeToolPayloadForAxi_*` tests PASS (the original `_WhenAxiCompactTrue` test still passes — passing `axiCompact: true` explicitly still works).

- [ ] **Step 5: Commit**

```
git add src/GxMcp.Gateway/Program.cs src/GxMcp.Gateway.Tests/GatewayBudgetTests.cs
git commit -m "feat(envelope): flip axiCompact default to true for list/query tools"
```

---

### Task 2: Declare `axiCompact` in `inputSchema` for the two affected tools

**Files:**
- Modify: `src/GxMcp.Gateway/tool_definitions.json`

- [ ] **Step 1: Read current entries for the two tools**

```
grep -n -A 3 '"name": "genexus_query"' src/GxMcp.Gateway/tool_definitions.json
grep -n -A 3 '"name": "genexus_list_objects"' src/GxMcp.Gateway/tool_definitions.json
```

This locates the two tool entries so the next step can target their `inputSchema.properties` block.

- [ ] **Step 2: Add `axiCompact` property to `genexus_query.inputSchema.properties`**

Inside the `genexus_query` entry, add to `inputSchema.properties` (alongside existing properties like `query`, `limit`, `offset`, etc.):

```json
        "axiCompact": {
          "type": "boolean",
          "description": "Default true. Returns compact projection (name,type,path). Pass false for full payload.",
          "default": true
        }
```

- [ ] **Step 3: Add `axiCompact` property to `genexus_list_objects.inputSchema.properties`**

Inside the `genexus_list_objects` entry, add the same block but with the 4-field projection in the description:

```json
        "axiCompact": {
          "type": "boolean",
          "description": "Default true. Returns compact projection (name,type,path,parentPath). Pass false for full payload.",
          "default": true
        }
```

- [ ] **Step 4: Run the schema budget test**

```
dotnet test src/GxMcp.Gateway.Tests --filter "FullyQualifiedName~ToolSchemaSizeTests" --nologo --verbosity minimal
```

Expected: PASS. The two new property blocks add ~440 bytes (~110 tokens). Current budget is 5000 tokens; we're at ~4940 → ~5050 after. **If this overflows**, bump the budget in `src/GxMcp.Gateway.Tests/ToolSchemaSizeTests.cs:44` to `5200` and add a comment referencing this sub-plan; sub-plan 2 will reclaim space and may lower it back.

- [ ] **Step 5: Commit**

```
git add src/GxMcp.Gateway/tool_definitions.json src/GxMcp.Gateway.Tests/ToolSchemaSizeTests.cs
git commit -m "feat(tools): expose axiCompact in inputSchema for query/list_objects with default true"
```

---

### Task 3: Update user-facing documentation

**Files:**
- Modify: `docs/mcp_capabilities_inventory.md`
- Modify: `docs/mcp_debugging_guide.md`
- Modify: `docs/llm_cli_mcp_playbook.md`

- [ ] **Step 1: Update `docs/mcp_capabilities_inventory.md`**

Find the line that mentions `axiCompact` (around line 44). Replace text that reads "tools accept `axiCompact=true` for compact projection" with:

```markdown
**`axiCompact`** (default **`true`** for `genexus_query` and `genexus_list_objects`): compact projection
returning only `name`, `type`, `path`[, `parentPath`]. Pass `axiCompact: false` to receive the full
payload (e.g., including `description`, `parent`, `metadata`).
```

- [ ] **Step 2: Update `docs/mcp_debugging_guide.md`**

Around line 98 there is a paragraph treating `axiCompact` as a per-call opt-in. Replace it with:

```markdown
### `axiCompact` is on by default

If a previously-visible field (`description`, `parent`, `metadata`, etc.) is missing from a
`genexus_query` or `genexus_list_objects` response, the gateway applied the default compact
projection. To get the full payload, set `axiCompact: false` in the tool arguments, or use the
`fields` parameter for a custom subset.
```

- [ ] **Step 3: Update `docs/llm_cli_mcp_playbook.md`**

Around lines 117 and 144 there are recommendations to pass `axiCompact: true`. Replace with reminders that it is now the default, e.g.:

```markdown
- Listing tools (`genexus_list_objects`, `genexus_query`) return a compact projection by default
  (`name`, `type`, `path`[, `parentPath`]). Pass `axiCompact: false` only when you specifically need
  the verbose payload (description, metadata, etc.).
```

- [ ] **Step 4: Commit**

```
git add docs/mcp_capabilities_inventory.md docs/mcp_debugging_guide.md docs/llm_cli_mcp_playbook.md
git commit -m "docs: reflect axiCompact default flip to true"
```

---

### Task 4: CHANGELOG entry

**Files:**
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Add entry under the next unreleased version heading**

At the top of `CHANGELOG.md`, under the current unreleased / `v2.4.0` section (create the section if missing — pattern matches earlier headings in the file), add:

```markdown
- **BREAKING (envelope)**: `axiCompact` now defaults to `true` for `genexus_query` and
  `genexus_list_objects`. Callers that relied on full payloads must now pass `axiCompact: false`
  explicitly. The flag is declared in `inputSchema` for discoverability.
```

- [ ] **Step 2: Commit**

```
git add CHANGELOG.md
git commit -m "docs(changelog): note axiCompact default flip"
```

---

### Task 5: Full regression + smoke

**Files:** none modified — verification only.

- [ ] **Step 1: Run the full Gateway test suite**

```
dotnet test src/GxMcp.Gateway.Tests --nologo --verbosity minimal
```

Expected: all tests PASS.

- [ ] **Step 2: Run the full Worker test suite**

```
dotnet test src/GxMcp.Worker.Tests --nologo --verbosity minimal
```

Expected: all tests PASS (worker is unaffected, this is a smoke check).

- [ ] **Step 3: Run the MCP smoke probe end-to-end**

```
pwsh -NoProfile -File scripts/mcp_smoke.ps1 -BaseUrl http://127.0.0.1:5000/mcp
```

Expected: exit code 0. If the gateway is not running locally, start it first via the usual launcher (`genexus-mcp` with the configured KB) — this is a manual checkpoint, not an automated test.

- [ ] **Step 4: Confirm the inverted default in a live call**

Call `genexus_list_objects` with `{ limit: 3 }` and verify the response items contain only `name`, `type`, `path`, `parentPath` (no `description`, no `metadata`). Then call again with `{ limit: 3, axiCompact: false }` and verify the verbose fields reappear.

---

## Done criteria

- [ ] All tasks above completed
- [ ] `dotnet test` green for Gateway and Worker test projects
- [ ] Smoke probe green
- [ ] Live LLM call confirmed compact-by-default behavior
- [ ] Sub-plan checkpoint signed off (user review of the diff)
