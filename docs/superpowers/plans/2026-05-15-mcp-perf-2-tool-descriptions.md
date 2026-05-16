# Sub-plan 2 — Tool Description Trim + On-Demand Help Resources

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans. Steps use `- [ ]` syntax.

**Goal:** Cut ~500 bytes (~125 tokens) from `tool_definitions.json` by trimming the 5 longest descriptions to a single canonical sentence and moving every example, prefix list, and edge-case note into per-tool MCP resources at `genexus://kb/tool-help/{name}`. The LLM can fetch detail on demand without paying for it on every `tools/list`.

**Architecture:** Two pieces. (1) Edit `src/GxMcp.Gateway/tool_definitions.json` to shorten descriptions for `genexus_query`, `genexus_lifecycle`, `genexus_edit`, `genexus_analyze`, `genexus_read`, ending each with the same suffix: `See genexus://kb/tool-help/<name> for examples.`. (2) Register a new resource template in `src/GxMcp.Gateway/McpRouter.cs` (`resources/templates/list`) and route `resources/read` for `genexus://kb/tool-help/{name}` through a new `BuildToolHelp(string toolName)` builder that returns the original long-form text plus any extra examples.

**Tech Stack:** .NET 8, MCP `resources/read` protocol, xUnit.

---

## File Structure

- **Modify:** `src/GxMcp.Gateway/tool_definitions.json` (5 tool descriptions)
- **Modify:** `src/GxMcp.Gateway/McpRouter.cs` (resource template + `BuildStaticResourceResponse` route + `BuildToolHelp` builder)
- **Create:** `src/GxMcp.Gateway/ToolHelpCatalog.cs` (static dictionary of long-form help texts keyed by tool name)
- **Modify:** `src/GxMcp.Gateway.Tests/ToolSchemaSizeTests.cs` (tighten budget after savings)
- **Modify:** `src/GxMcp.Gateway.Tests/McpRouterTests.cs` (test resource discovery + read for `tool-help`)
- **Modify:** `CHANGELOG.md`

---

### Task 1: Create the `ToolHelpCatalog` with long-form text

**Files:**
- Create: `src/GxMcp.Gateway/ToolHelpCatalog.cs`

- [ ] **Step 1: Write the failing test**

Add to `src/GxMcp.Gateway.Tests/McpRouterTests.cs` (append at end of class):

```csharp
[Fact]
public void ToolHelpCatalog_HasEntriesForTrimmedTools()
{
    string[] expected =
    {
        "genexus_query",
        "genexus_lifecycle",
        "genexus_edit",
        "genexus_analyze",
        "genexus_read"
    };

    foreach (var name in expected)
    {
        var help = ToolHelpCatalog.Get(name);
        Assert.False(string.IsNullOrWhiteSpace(help), $"No help text for {name}");
        Assert.True(help!.Length >= 200, $"Help for {name} should be more detailed than the trimmed description");
    }
}

[Fact]
public void ToolHelpCatalog_ReturnsNullForUnknownTool()
{
    Assert.Null(ToolHelpCatalog.Get("genexus_unknown_tool"));
}
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet test src/GxMcp.Gateway.Tests --filter "FullyQualifiedName~ToolHelpCatalog" --nologo --verbosity minimal
```

Expected: FAIL with "type or namespace 'ToolHelpCatalog' could not be found".

- [ ] **Step 3: Create the catalog file**

Create `src/GxMcp.Gateway/ToolHelpCatalog.cs`:

```csharp
using System.Collections.Generic;

namespace GxMcp.Gateway
{
    internal static class ToolHelpCatalog
    {
        private static readonly Dictionary<string, string> _helpTexts = new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["genexus_query"] =
                "# genexus_query\n\n" +
                "Search objects in the active Knowledge Base.\n\n" +
                "## Query prefixes\n" +
                "- `usedby:<name>` — objects that reference <name>\n" +
                "- `type:<ObjectType>` — filter by Transaction, Procedure, WebPanel, etc.\n" +
                "- `description:<text>` — search inside object descriptions\n" +
                "- `parent:<folder>` — filter by direct parent folder\n" +
                "- `parentPath:<a/b/c>` — filter by full folder path\n\n" +
                "## Index behaviour\n" +
                "- The first call on a fresh install triggers the KB index build.\n" +
                "- `_meta.partial=true` means more results are still being indexed.\n" +
                "- Literal-name queries (no prefix) skip the index entirely.\n" +
                "- `genexus_read`, `genexus_edit`, `genexus_list_objects`, and `genexus_lifecycle` are index-independent.\n\n" +
                "## Defaults\n" +
                "- `axiCompact: true` — pass `false` to get the full payload.\n" +
                "- `limit: 50`, `offset: 0`.\n\n" +
                "## Examples\n" +
                "- `{ query: 'type:Procedure', limit: 20 }`\n" +
                "- `{ query: 'usedby:InvoiceProc' }`\n" +
                "- `{ query: 'OrderTrn', fields: 'name,type,path,description' }`\n",

            ["genexus_lifecycle"] =
                "# genexus_lifecycle\n\n" +
                "Build, validate, index, or poll the active Knowledge Base.\n\n" +
                "## Actions\n" +
                "- `build` — non-blocking when `estimated_seconds >= 20`; returns `{ job_id, status: 'running' }` and surfaces `_meta.background_jobs` on the next call.\n" +
                "- `validate` — runs the GeneXus specifier; same async pattern as build.\n" +
                "- `index` — rebuilds the search index. Pass `force=true` to ignore the on-disk cache.\n" +
                "- `status` — accepts either a `taskId` or `job_id` via `target`; pass `wait_seconds > 0` to long-poll up to 25s.\n" +
                "- `result` — fetch the completion payload of a finished operation.\n" +
                "- `stop-worker` — gracefully recycle the worker process for the active KB.\n\n" +
                "## target format\n" +
                "- Build/validate: object name(s), comma- or semicolon-separated.\n" +
                "- Status/result on a background op: `op:<operationId>` or just `<job_id>`.\n\n" +
                "## Examples\n" +
                "- `{ action: 'build', target: 'InvoiceProc' }`\n" +
                "- `{ action: 'status', target: 'op:abc123', wait_seconds: 25 }`\n" +
                "- `{ action: 'index', force: true }`\n",

            ["genexus_edit"] =
                "# genexus_edit\n\n" +
                "Edit the source or metadata of a GeneXus object.\n\n" +
                "## Required\n" +
                "- Either `name` (single object) **or** `targets` (array) — never both.\n" +
                "- `mode`: `full` (replace whole part) or `patch` (apply unified diff).\n" +
                "- `dryRun: true` with `mode: 'patch'` first to preview without persisting.\n\n" +
                "## Output\n" +
                "- Returns `post_state.diff` (unified diff) by default.\n" +
                "- `verbose: true` adds slices with ±15 lines of context.\n" +
                "- `return_post_state: false` opts out of the post-state block to save tokens.\n\n" +
                "## Disambiguation\n" +
                "If `name` matches multiple objects, the error includes `suggestion` and `availableTypes`. Pass `type=<ObjectType>` or use `parentPath` to disambiguate.\n\n" +
                "## Examples\n" +
                "- `{ name: 'InvoiceProc', part: 'Source', mode: 'patch', content: '<diff>', dryRun: true }`\n" +
                "- `{ name: 'OrderTrn', part: 'Rules', mode: 'full', content: '<rules text>' }`\n",

            ["genexus_analyze"] =
                "# genexus_analyze\n\n" +
                "Semantic analysis across one or more objects.\n\n" +
                "## Modes\n" +
                "- `impact` — callers, callees, blast radius, risk level, affected entry points.\n" +
                "- `dependencies` — typed dependency graph.\n" +
                "- `complexity` — line/cyclomatic counts.\n" +
                "- `naming` — naming-convention audit.\n" +
                "- `summary` — LLM-oriented summary of the object.\n" +
                "- `explain` — natural-language explanation of a slice of source.\n\n" +
                "## When to use what\n" +
                "- Raw source: `genexus_read`.\n" +
                "- Single-object metadata: `genexus_inspect`.\n" +
                "- Cross-object reasoning: `genexus_analyze`.\n\n" +
                "## Notes\n" +
                "- `impact` waits up to 30s for the index to be ready unless `waitForIndex: false`.\n" +
                "- Returns `callersTruncated: true` and `_meta.partial` when the graph is incomplete.\n\n" +
                "## Examples\n" +
                "- `{ mode: 'impact', target: 'InvoiceProc' }`\n" +
                "- `{ mode: 'summary', target: 'OrderTrn' }`\n",

            ["genexus_read"] =
                "# genexus_read\n\n" +
                "Read source or metadata parts of one or more GeneXus objects.\n\n" +
                "## Required\n" +
                "- Either `name` (single) **or** `targets` (array). Never both.\n" +
                "- `parts`: array of part names. Common: `Source`, `Variables`, `Rules`, `Events`, `Structure`, `Layout`. Omitting `parts` returns the canonical default set for the object type.\n\n" +
                "## Pagination\n" +
                "- `offset` and `limit` apply to the **source** part for large objects.\n" +
                "- `_meta.partial: true` and `_meta.nextOffset` signal more content available.\n\n" +
                "## Examples\n" +
                "- `{ name: 'InvoiceProc', parts: ['Source', 'Variables'] }`\n" +
                "- `{ name: 'OrderTrn', parts: ['Rules'], offset: 0, limit: 200 }`\n" +
                "- `{ targets: [{ name: 'A' }, { name: 'B' }], parts: ['Source'] }`\n"
        };

        internal static string? Get(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName)) return null;
            return _helpTexts.TryGetValue(toolName, out var text) ? text : null;
        }

        internal static System.Collections.Generic.IReadOnlyCollection<string> KnownTools => _helpTexts.Keys;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

```
dotnet test src/GxMcp.Gateway.Tests --filter "FullyQualifiedName~ToolHelpCatalog" --nologo --verbosity minimal
```

Expected: both tests PASS.

- [ ] **Step 5: Commit**

```
git add src/GxMcp.Gateway/ToolHelpCatalog.cs src/GxMcp.Gateway.Tests/McpRouterTests.cs
git commit -m "feat(resources): add ToolHelpCatalog with long-form per-tool help text"
```

---

### Task 2: Register the resource template and read handler

**Files:**
- Modify: `src/GxMcp.Gateway/McpRouter.cs:231-309` (`resources/templates/list`)
- Modify: `src/GxMcp.Gateway/McpRouter.cs:686-722` (`BuildStaticResourceResponse`)

- [ ] **Step 1: Write the failing test for resource read**

Add to `src/GxMcp.Gateway.Tests/McpRouterTests.cs`:

```csharp
[Fact]
public void ResourcesRead_ToolHelp_ReturnsMarkdownForKnownTool()
{
    var request = JObject.Parse(@"{
        ""method"": ""resources/read"",
        ""params"": { ""uri"": ""genexus://kb/tool-help/genexus_query"" }
    }");

    var result = McpRouter.HandleMethod(request);
    Assert.NotNull(result);

    var json = JObject.FromObject(result!);
    var contents = (JArray)json["contents"]!;
    var first = (JObject)contents[0];
    Assert.Equal("genexus://kb/tool-help/genexus_query", first["uri"]!.ToString());
    Assert.Equal("text/markdown", first["mimeType"]!.ToString());
    Assert.Contains("Query prefixes", first["text"]!.ToString());
}

[Fact]
public void ResourcesRead_ToolHelp_ReturnsNullForUnknownTool()
{
    var request = JObject.Parse(@"{
        ""method"": ""resources/read"",
        ""params"": { ""uri"": ""genexus://kb/tool-help/genexus_does_not_exist"" }
    }");

    var result = McpRouter.HandleMethod(request);
    Assert.Null(result);
}

[Fact]
public void ResourcesTemplatesList_IncludesToolHelpTemplate()
{
    var request = JObject.Parse(@"{ ""method"": ""resources/templates/list"" }");
    var result = McpRouter.HandleMethod(request);
    Assert.NotNull(result);

    var json = JObject.FromObject(result!);
    var templates = (JArray)json["resourceTemplates"]!;
    Assert.Contains(templates, t =>
        string.Equals(t["uriTemplate"]?.ToString(), "genexus://kb/tool-help/{name}", System.StringComparison.OrdinalIgnoreCase));
}
```

Note: `McpRouter.HandleMethod` is the public-ish dispatch surface used by these tests. If the existing tests use a different entry point, mirror their pattern — search `McpRouterTests.cs` for an existing `resources/read` test and copy its invocation style.

- [ ] **Step 2: Run tests to verify they fail**

```
dotnet test src/GxMcp.Gateway.Tests --filter "FullyQualifiedName~ResourcesRead_ToolHelp|FullyQualifiedName~ResourcesTemplatesList_IncludesToolHelp" --nologo --verbosity minimal
```

Expected: FAIL — neither template nor read handler is registered yet.

- [ ] **Step 3: Add the resource template entry**

In `src/GxMcp.Gateway/McpRouter.cs`, inside the `resourceTemplates` array starting at line 234, append a new entry **before** the closing `}` at line 308:

```csharp
                            new
                            {
                                uriTemplate = "genexus://kb/tool-help/{name}",
                                name = "GeneXus Tool Help",
                                description = "Long-form help for a single MCP tool: prefixes, modes, examples, defaults."
                            }
```

(Add a comma after the previous entry to keep valid C#.)

- [ ] **Step 4: Add the read handler**

In `src/GxMcp.Gateway/McpRouter.cs`, inside `BuildStaticResourceResponse` (currently lines 686-722), add a new branch **before** `return null;`:

```csharp
            const string toolHelpPrefix = "genexus://kb/tool-help/";
            if (uri.StartsWith(toolHelpPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string toolName = uri.Substring(toolHelpPrefix.Length);
                string? text = ToolHelpCatalog.Get(toolName);
                if (text == null) return null;

                return new
                {
                    contents = new[]
                    {
                        new
                        {
                            uri,
                            mimeType = "text/markdown",
                            text
                        }
                    }
                };
            }
```

- [ ] **Step 5: Run tests to verify they pass**

```
dotnet test src/GxMcp.Gateway.Tests --filter "FullyQualifiedName~ResourcesRead_ToolHelp|FullyQualifiedName~ResourcesTemplatesList_IncludesToolHelp" --nologo --verbosity minimal
```

Expected: all three new tests PASS.

- [ ] **Step 6: Commit**

```
git add src/GxMcp.Gateway/McpRouter.cs src/GxMcp.Gateway.Tests/McpRouterTests.cs
git commit -m "feat(resources): expose genexus://kb/tool-help/{name} for per-tool detail"
```

---

### Task 3: Trim the 5 large tool descriptions

**Files:**
- Modify: `src/GxMcp.Gateway/tool_definitions.json`

- [ ] **Step 1: Replace `genexus_query` description**

Find the `genexus_query` entry. Replace its `description` value with:

```
"Search objects in the active KB. Supports prefixes (type:, usedby:, parent:, parentPath:, description:). Compact projection by default. See genexus://kb/tool-help/genexus_query for examples."
```

- [ ] **Step 2: Replace `genexus_lifecycle` description**

```
"Build, validate, index, or poll the KB. Long ops are async with operationId. See genexus://kb/tool-help/genexus_lifecycle for actions and target formats."
```

- [ ] **Step 3: Replace `genexus_edit` description**

```
"Edit an object part. Pass name or targets (mutually exclusive). Mode: full | patch. Use dryRun before persisting. See genexus://kb/tool-help/genexus_edit for examples."
```

- [ ] **Step 4: Replace `genexus_analyze` description**

```
"Cross-object semantic analysis: impact, dependencies, complexity, naming, summary, explain. See genexus://kb/tool-help/genexus_analyze for mode selection."
```

- [ ] **Step 5: Replace `genexus_read` description**

```
"Read source/metadata parts of one or more objects. Pass name or targets, plus parts=[...]. Paginate large source with offset/limit. See genexus://kb/tool-help/genexus_read for examples."
```

- [ ] **Step 6: Tighten the schema size budget**

Edit `src/GxMcp.Gateway.Tests/ToolSchemaSizeTests.cs:44`. The current budget is 5000 tokens; after sub-plan 1 added two `axiCompact` schema entries (~110 tokens) and this sub-plan removed ~125 tokens of description, the new approximate total is ~4985 tokens. Lower the budget to **4900** to lock in the gain:

```csharp
Assert.True(approxTokens < 4900, $"tool_definitions.json is ~{approxTokens} tokens; budget 4900.");
```

- [ ] **Step 7: Run the budget test**

```
dotnet test src/GxMcp.Gateway.Tests --filter "FullyQualifiedName~ToolSchemaSizeTests" --nologo --verbosity minimal
```

Expected: PASS.

- [ ] **Step 8: Commit**

```
git add src/GxMcp.Gateway/tool_definitions.json src/GxMcp.Gateway.Tests/ToolSchemaSizeTests.cs
git commit -m "perf(tools): trim 5 large descriptions to one-liners pointing at tool-help resources"
```

---

### Task 4: Update CHANGELOG and run full regression

**Files:**
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Add changelog entry**

Under the current unreleased / `v2.4.0` heading in `CHANGELOG.md`, add:

```markdown
- **Token reduction**: `tool_definitions.json` shrunk by ~125 tokens. Long-form help for
  `genexus_query`, `genexus_lifecycle`, `genexus_edit`, `genexus_analyze`, and `genexus_read`
  is now served on demand at `genexus://kb/tool-help/{name}` via the MCP resources protocol.
```

- [ ] **Step 2: Run full Gateway test suite**

```
dotnet test src/GxMcp.Gateway.Tests --nologo --verbosity minimal
```

Expected: all tests PASS.

- [ ] **Step 3: Run MCP smoke probe**

```
pwsh -NoProfile -File scripts/mcp_smoke.ps1 -BaseUrl http://127.0.0.1:5000/mcp
```

Expected: exit code 0.

- [ ] **Step 4: Confirm a live resource read**

Issue an MCP request from a connected client (or `curl` against the HTTP endpoint):

```
{ "method": "resources/read", "params": { "uri": "genexus://kb/tool-help/genexus_query" } }
```

Expected: response includes `contents[0].text` with markdown matching the catalog entry.

- [ ] **Step 5: Commit changelog**

```
git add CHANGELOG.md
git commit -m "docs(changelog): note tool description trim and tool-help resources"
```

---

## Done criteria

- [ ] All tasks above completed
- [ ] `dotnet test src/GxMcp.Gateway.Tests` green
- [ ] Smoke probe green
- [ ] Resource read of `genexus://kb/tool-help/genexus_query` returns markdown
- [ ] Token budget test confirms reduction below the new 4900 threshold
- [ ] Sub-plan checkpoint signed off
