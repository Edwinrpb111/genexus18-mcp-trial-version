# Sub-plan 4 — Composite `genexus_edit_and_build` Tool

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans. Steps use `- [ ]` syntax.

**Goal:** Add a new MCP tool `genexus_edit_and_build` that collapses the canonical edit workflow (edit → analyze impact → build callers) from 3-5 LLM turns into a single call. The tool returns the edit diff immediately, the impact summary, and a background `operationId` for the build. The LLM polls `genexus_lifecycle action=status target=op:<id>` only once at the end. Also: when `genexus_edit` itself fails because of an ambiguous object name, the error envelope now embeds the alternative matches inline so the LLM can retry without a separate `list_objects` turn.

**Architecture:** New worker service `EditAndBuildOrchestrator` (`src/GxMcp.Worker/Services/EditAndBuildOrchestrator.cs`) coordinates the three existing services (`WriteService.WriteObject`, `AnalyzeService.ImpactAnalysis`, `BuildService.Build`). Composition runs in two phases: a synchronous phase (edit + impact, both fast, ms-to-seconds) and an asynchronous phase (build, which goes through the existing `BuildService` async path and returns `taskId`). The orchestrator returns a single envelope; the build's `taskId` is also registered with the gateway's `OperationTracker` (sub-plan 3 instrumentation) so `genexus_lifecycle action=status target=op:<id>` works seamlessly. Inline disambiguation lives entirely in `WriteService.WriteObject` — when `kb.DesignModel.Objects[name]` returns multiple candidates, the error envelope now includes `alternatives: [{ name, type, parentPath }]`. The composite tool surfaces those alternatives unchanged.

**Tech Stack:** .NET Framework 4.8 (Worker), .NET 8 (Gateway), Newtonsoft.Json, xUnit.

---

## File Structure

- **Create:** `src/GxMcp.Worker/Services/EditAndBuildOrchestrator.cs`
- **Modify:** `src/GxMcp.Worker/Services/WriteService.cs` (inline ambiguous-name alternatives in error envelope)
- **Modify:** `src/GxMcp.Worker/Services/CommandDispatcher.cs` (new `case "editandbuild"`)
- **Modify:** `src/GxMcp.Gateway/Routers/ObjectRouter.cs` (route `genexus_edit_and_build` tool call)
- **Modify:** `src/GxMcp.Gateway/tool_definitions.json` (declare the new tool)
- **Modify:** `src/GxMcp.Gateway/ToolHelpCatalog.cs` (add long-form help — depends on sub-plan 2)
- **Create:** `src/GxMcp.Worker.Tests/EditAndBuildOrchestratorTests.cs`
- **Modify:** `src/GxMcp.Worker.Tests/WriteServiceTests.cs` (test ambiguous-name alternatives)
- **Modify:** `src/GxMcp.Gateway.Tests/McpRouterTests.cs` (route-through test)
- **Modify:** `CHANGELOG.md`

---

### Task 1: Inline disambiguation in `WriteService`

**Files:**
- Modify: `src/GxMcp.Worker/Services/WriteService.cs`

- [ ] **Step 1: Locate the ambiguous-name failure site**

```
grep -n "Disambiguate\|Multiple objects\|ambiguous" src/GxMcp.Worker/Services/WriteService.cs
```

Identify where `WriteObject` (or its helpers) build the error JSON that today returns `"suggestion": "Disambiguate with type=..."`. That site is the insertion point.

- [ ] **Step 2: Write the failing test**

Create `src/GxMcp.Worker.Tests/WriteServiceTests.cs` if it does not exist, or append:

```csharp
using GxMcp.Worker.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GxMcp.Worker.Tests
{
    public class WriteServiceDisambiguationTests
    {
        [Fact]
        public void WriteObject_ReturnsAlternatives_WhenNameIsAmbiguous()
        {
            // ARRANGE: feed a stubbed object lookup that yields two candidates with the same name.
            // (Adapt the harness to whatever DI/fake the existing WriteServiceTests use — search
            //  the test project for an existing fixture before inventing one.)
            var service = WriteServiceTestHarness.WithCandidates(new[]
            {
                new { Name = "InvoiceProc", Type = "Procedure", ParentPath = "Main/Procs" },
                new { Name = "InvoiceProc", Type = "WebPanel",  ParentPath = "Main/Web" }
            });

            string raw = service.WriteObject("InvoiceProc", "WriteObject", new JObject(),
                type: null, true, false, true, dryRun: true);

            var result = JObject.Parse(raw);
            Assert.Equal("Error", result["status"]?.ToString());
            var alternatives = (JArray?)result["alternatives"];
            Assert.NotNull(alternatives);
            Assert.Equal(2, alternatives!.Count);
            Assert.Contains(alternatives, a =>
                a["name"]?.ToString() == "InvoiceProc" && a["type"]?.ToString() == "Procedure");
        }
    }
}
```

If a `WriteServiceTestHarness` does not exist, adapt to the existing test pattern: e.g., construct the real `WriteService` against a stub `IGxKnowledgeBaseProvider` already used in `src/GxMcp.Worker.Tests/`. Search `WriteServiceTests.cs` / `*Harness*.cs` first.

- [ ] **Step 3: Run test to verify it fails**

```
dotnet test src/GxMcp.Worker.Tests --filter "FullyQualifiedName~WriteObject_ReturnsAlternatives" --nologo --verbosity minimal
```

Expected: FAIL.

- [ ] **Step 4: Modify `WriteService` to enumerate and embed alternatives**

At the ambiguous-name site (located in Step 1), replace the existing suggestion-only error block with the following pattern:

```csharp
            // When name matches multiple objects (different types or different parents),
            // return the alternative list inline so the caller can disambiguate without a
            // round-trip to genexus_list_objects.
            if (candidates.Count > 1)
            {
                var alternatives = new JArray();
                foreach (var c in candidates)
                {
                    alternatives.Add(new JObject
                    {
                        ["name"] = c.Name,
                        ["type"] = c.TypeName,
                        ["parentPath"] = c.ParentPath
                    });
                }

                return JsonConvert.SerializeObject(new
                {
                    status = "Error",
                    error = "Ambiguous object name",
                    target = target,
                    suggestion = "Disambiguate by passing 'type' or by using a fully-qualified parentPath.",
                    alternatives,
                    hint = "Retry with one of the alternatives' (name,type) pairs."
                });
            }
```

`candidates` here represents whatever local variable holds the lookup result — match the existing identifier in `WriteService`. `c.TypeName` / `c.ParentPath` should map to the model fields exposed by the project's existing object-lookup helpers.

- [ ] **Step 5: Run test to verify it passes**

```
dotnet test src/GxMcp.Worker.Tests --filter "FullyQualifiedName~WriteObject_ReturnsAlternatives" --nologo --verbosity minimal
```

Expected: PASS.

- [ ] **Step 6: Commit**

```
git add src/GxMcp.Worker/Services/WriteService.cs src/GxMcp.Worker.Tests/WriteServiceTests.cs
git commit -m "feat(edit): embed alternatives in ambiguous-name error envelope"
```

---

### Task 2: Create the orchestrator service

**Files:**
- Create: `src/GxMcp.Worker/Services/EditAndBuildOrchestrator.cs`

- [ ] **Step 1: Write the failing test**

Create `src/GxMcp.Worker.Tests/EditAndBuildOrchestratorTests.cs`:

```csharp
using GxMcp.Worker.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GxMcp.Worker.Tests
{
    public class EditAndBuildOrchestratorTests
    {
        [Fact]
        public void Orchestrate_ReturnsCompositeEnvelope_WhenAllPhasesSucceed()
        {
            var fakeWrite = new FakeWriteService(JObject.Parse(@"{
                ""status"": ""Ok"",
                ""diff"": ""@@ -1 +1 @@\n-old\n+new""
            }"));

            var fakeAnalyze = new FakeAnalyzeService(JObject.Parse(@"{
                ""status"": ""Ready"",
                ""target"": ""InvoiceProc"",
                ""callers"": [""WebInvoice"", ""ReportInvoice""],
                ""callersTruncated"": false,
                ""riskLevel"": ""Low""
            }"));

            var fakeBuild = new FakeBuildService(JObject.Parse(@"{
                ""status"": ""Accepted"",
                ""taskId"": ""b1c2d3e4""
            }"));

            var orchestrator = new EditAndBuildOrchestrator(fakeWrite, fakeAnalyze, fakeBuild);

            string raw = orchestrator.Orchestrate(new JObject
            {
                ["name"] = "InvoiceProc",
                ["part"] = "Source",
                ["mode"] = "patch",
                ["content"] = "@@ -1 +1 @@\n-old\n+new",
                ["buildIncludeCallees"] = "direct"
            });

            var env = JObject.Parse(raw);
            Assert.Equal("Ok", env["status"]?.ToString());
            Assert.NotNull(env["edit"]);
            Assert.NotNull(env["impact"]);
            Assert.NotNull(env["build"]);
            Assert.Equal("b1c2d3e4", env["build"]?["taskId"]?.ToString());
            Assert.Equal(2, ((JArray)env["impact"]!["callers"]!).Count);
        }

        [Fact]
        public void Orchestrate_ShortCircuits_WhenEditFails()
        {
            var fakeWrite = new FakeWriteService(JObject.Parse(@"{
                ""status"": ""Error"",
                ""error"": ""Ambiguous object name"",
                ""alternatives"": [
                    { ""name"": ""InvoiceProc"", ""type"": ""Procedure"" },
                    { ""name"": ""InvoiceProc"", ""type"": ""WebPanel"" }
                ]
            }"));
            var fakeAnalyze = new FakeAnalyzeService(/* should not be called */ null);
            var fakeBuild = new FakeBuildService(/* should not be called */ null);

            var orchestrator = new EditAndBuildOrchestrator(fakeWrite, fakeAnalyze, fakeBuild);

            string raw = orchestrator.Orchestrate(new JObject { ["name"] = "InvoiceProc" });

            var env = JObject.Parse(raw);
            Assert.Equal("Error", env["status"]?.ToString());
            Assert.NotNull(env["alternatives"]);
            Assert.Null(env["impact"]);
            Assert.Null(env["build"]);
            Assert.False(fakeAnalyze.WasCalled);
            Assert.False(fakeBuild.WasCalled);
        }

        [Fact]
        public void Orchestrate_SkipsBuild_WhenImpactReportsNoCallers()
        {
            var fakeWrite = new FakeWriteService(JObject.Parse(@"{ ""status"": ""Ok"" }"));
            var fakeAnalyze = new FakeAnalyzeService(JObject.Parse(@"{
                ""status"": ""Ready"",
                ""callers"": []
            }"));
            var fakeBuild = new FakeBuildService(/* should not be called */ null);

            var orchestrator = new EditAndBuildOrchestrator(fakeWrite, fakeAnalyze, fakeBuild);

            string raw = orchestrator.Orchestrate(new JObject { ["name"] = "OrphanProc" });
            var env = JObject.Parse(raw);

            Assert.Equal("Ok", env["status"]?.ToString());
            Assert.NotNull(env["impact"]);
            Assert.NotNull(env["build"]);
            Assert.Equal("Skipped", env["build"]?["status"]?.ToString());
            Assert.False(fakeBuild.WasCalled);
        }
    }

    // --- test doubles ---
    internal class FakeWriteService : IWriteServiceFacade
    {
        private readonly JObject _result;
        public bool WasCalled { get; private set; }
        public FakeWriteService(JObject result) { _result = result; }
        public string WriteObject(string target, JObject args)
        {
            WasCalled = true;
            return _result.ToString();
        }
    }
    internal class FakeAnalyzeService : IAnalyzeServiceFacade
    {
        private readonly JObject? _result;
        public bool WasCalled { get; private set; }
        public FakeAnalyzeService(JObject? result) { _result = result; }
        public string ImpactAnalysis(string target, bool waitForIndex, int waitTimeoutMs)
        {
            WasCalled = true;
            return _result?.ToString() ?? "{}";
        }
    }
    internal class FakeBuildService : IBuildServiceFacade
    {
        private readonly JObject? _result;
        public bool WasCalled { get; private set; }
        public FakeBuildService(JObject? result) { _result = result; }
        public string Build(string action, string target, string includeCallees, int buildPlanCap)
        {
            WasCalled = true;
            return _result?.ToString() ?? "{}";
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```
dotnet test src/GxMcp.Worker.Tests --filter "FullyQualifiedName~EditAndBuildOrchestratorTests" --nologo --verbosity minimal
```

Expected: FAIL — type `EditAndBuildOrchestrator` does not exist.

- [ ] **Step 3: Create the facade interfaces**

Append to `src/GxMcp.Worker/Services/EditAndBuildOrchestrator.cs` (file does not exist yet — create with full content below):

```csharp
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GxMcp.Worker.Services
{
    public interface IWriteServiceFacade
    {
        string WriteObject(string target, JObject args);
    }

    public interface IAnalyzeServiceFacade
    {
        string ImpactAnalysis(string target, bool waitForIndex, int waitTimeoutMs);
    }

    public interface IBuildServiceFacade
    {
        string Build(string action, string target, string includeCallees, int buildPlanCap);
    }

    public class EditAndBuildOrchestrator
    {
        private readonly IWriteServiceFacade _write;
        private readonly IAnalyzeServiceFacade _analyze;
        private readonly IBuildServiceFacade _build;

        public EditAndBuildOrchestrator(IWriteServiceFacade write, IAnalyzeServiceFacade analyze, IBuildServiceFacade build)
        {
            _write = write;
            _analyze = analyze;
            _build = build;
        }

        public string Orchestrate(JObject args)
        {
            string target = args?["name"]?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(target))
            {
                return JsonConvert.SerializeObject(new
                {
                    status = "Error",
                    error = "name is required"
                });
            }

            string includeCallees = args?["buildIncludeCallees"]?.ToString() ?? "direct";
            int buildPlanCap = args?["buildPlanCap"]?.ToObject<int?>() ?? 200;
            bool waitForIndex = args?["waitForIndex"]?.ToObject<bool?>() ?? true;
            int waitTimeoutMs = args?["waitTimeoutMs"]?.ToObject<int?>() ?? 30000;

            // Phase 1: edit (synchronous)
            string editRaw = _write.WriteObject(target, args);
            var edit = JObject.Parse(editRaw);
            if (!string.Equals(edit["status"]?.ToString(), "Ok", StringComparison.OrdinalIgnoreCase))
            {
                // Propagate edit failure verbatim plus alternatives (already embedded by WriteService).
                edit["phase"] = "edit";
                return edit.ToString();
            }

            // Phase 2: impact (synchronous, fast once index is ready)
            string impactRaw = _analyze.ImpactAnalysis(target, waitForIndex, waitTimeoutMs);
            var impact = JObject.Parse(impactRaw);
            var callers = impact["callers"] as JArray ?? new JArray();

            // Phase 3: build (async) — only when there are callers.
            JObject buildResult;
            if (callers.Count == 0)
            {
                buildResult = new JObject
                {
                    ["status"] = "Skipped",
                    ["reason"] = "No callers to rebuild."
                };
            }
            else
            {
                string targetList = string.Join(",", callers);
                string buildRaw = _build.Build("Build", targetList, includeCallees, buildPlanCap);
                buildResult = JObject.Parse(buildRaw);
            }

            return new JObject
            {
                ["status"] = "Ok",
                ["target"] = target,
                ["edit"] = edit,
                ["impact"] = impact,
                ["build"] = buildResult
            }.ToString();
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```
dotnet test src/GxMcp.Worker.Tests --filter "FullyQualifiedName~EditAndBuildOrchestratorTests" --nologo --verbosity minimal
```

Expected: all 3 tests PASS.

- [ ] **Step 5: Commit**

```
git add src/GxMcp.Worker/Services/EditAndBuildOrchestrator.cs src/GxMcp.Worker.Tests/EditAndBuildOrchestratorTests.cs
git commit -m "feat(worker): add EditAndBuildOrchestrator with edit->impact->build phases"
```

---

### Task 3: Adapt existing services to the new facade interfaces

**Files:**
- Modify: `src/GxMcp.Worker/Services/WriteService.cs`
- Modify: `src/GxMcp.Worker/Services/AnalyzeService.cs`
- Modify: `src/GxMcp.Worker/Services/BuildService.cs`

- [ ] **Step 1: Make `WriteService` implement `IWriteServiceFacade`**

At the class declaration of `WriteService`, add the interface:

```csharp
    public class WriteService : IWriteServiceFacade
```

Add a thin adapter method that forwards to the existing `WriteObject(target, action, payload, type, ..., dryRun)`:

```csharp
        public string WriteObject(string target, JObject args)
        {
            return WriteObject(
                target,
                args?["mode"]?.ToString() == "patch" ? "WritePatch" : "WriteObject",
                args?["content"] as JObject ?? (args?["content"] != null ? new JObject { ["text"] = args["content"]?.ToString() } : new JObject()),
                args?["type"]?.ToString(),
                true,
                false,
                true,
                args?["dryRun"]?.ToObject<bool?>() ?? false);
        }
```

If the existing `WriteObject(target, action, ...)` overload signature differs from what is shown in the recon snippet, mirror its real signature — search `WriteService.cs` for `public string WriteObject(`.

- [ ] **Step 2: Make `AnalyzeService` implement `IAnalyzeServiceFacade`**

```csharp
    public class AnalyzeService : IAnalyzeServiceFacade
```

The existing `ImpactAnalysis(string target, bool waitForIndex, int waitTimeoutMs, CancellationToken ct)` has an extra `CancellationToken` parameter. Add an overload without it:

```csharp
        public string ImpactAnalysis(string target, bool waitForIndex, int waitTimeoutMs)
        {
            return ImpactAnalysis(target, waitForIndex, waitTimeoutMs, System.Threading.CancellationToken.None);
        }
```

- [ ] **Step 3: Make `BuildService` implement `IBuildServiceFacade`**

```csharp
    public class BuildService : IBuildServiceFacade
```

The existing `Build(string action, string target, string includeCallees, int buildPlanCap)` (line 194 of `BuildService.cs`) already matches the facade signature — no new method needed.

- [ ] **Step 4: Build and confirm no regressions**

```
dotnet build src/GxMcp.Worker --configuration Debug --nologo --verbosity minimal
dotnet test src/GxMcp.Worker.Tests --nologo --verbosity minimal
```

Expected: build green, all tests pass.

- [ ] **Step 5: Commit**

```
git add src/GxMcp.Worker/Services/WriteService.cs src/GxMcp.Worker/Services/AnalyzeService.cs src/GxMcp.Worker/Services/BuildService.cs
git commit -m "refactor(worker): WriteService/AnalyzeService/BuildService implement orchestrator facades"
```

---

### Task 4: Wire the dispatcher case

**Files:**
- Modify: `src/GxMcp.Worker/Services/CommandDispatcher.cs`

- [ ] **Step 1: Add field and initialize**

At the top of the class (next to other `_buildService`, `_writeService` fields around line 19-63), add:

```csharp
        private readonly EditAndBuildOrchestrator _editAndBuildOrchestrator;
```

Inside the constructor (around line 63), after the three underlying services are constructed:

```csharp
            _editAndBuildOrchestrator = new EditAndBuildOrchestrator(_writeService, _analyzeService, _buildService);
```

- [ ] **Step 2: Add the dispatch case**

Find the `switch` statement in `Dispatch` (around line 200+). After `case "write":` (line 357) add a new case:

```csharp
                    case "editandbuild":
                        if (action == "Orchestrate")
                        {
                            return _editAndBuildOrchestrator.Orchestrate(args ?? new JObject());
                        }
                        break;
```

- [ ] **Step 3: Build**

```
dotnet build src/GxMcp.Worker --configuration Debug --nologo --verbosity minimal
```

Expected: build green.

- [ ] **Step 4: Commit**

```
git add src/GxMcp.Worker/Services/CommandDispatcher.cs
git commit -m "feat(worker): wire editandbuild dispatch case to orchestrator"
```

---

### Task 5: Register the tool in the gateway

**Files:**
- Modify: `src/GxMcp.Gateway/tool_definitions.json`
- Modify: `src/GxMcp.Gateway/Routers/ObjectRouter.cs`

- [ ] **Step 1: Add the tool definition**

Append a new tool entry to `src/GxMcp.Gateway/tool_definitions.json` (inside the top-level array, after the existing `genexus_edit` entry — keep alphabetical or grouped order):

```json
  {
    "name": "genexus_edit_and_build",
    "description": "Edit an object then rebuild its callers in one call. Returns edit diff + impact + build operationId. See genexus://kb/tool-help/genexus_edit_and_build for examples.",
    "inputSchema": {
      "type": "object",
      "properties": {
        "name":                { "type": "string", "description": "Target object name." },
        "part":                { "type": "string", "description": "Part to edit (Source, Rules, ...)." },
        "content":             { "type": "string", "description": "New content or unified diff." },
        "mode":                { "type": "string", "enum": ["full", "patch"], "default": "patch" },
        "type":                { "type": "string", "description": "Disambiguates when name matches multiple objects." },
        "dryRun":              { "type": "boolean", "default": false },
        "buildIncludeCallees": { "type": "string", "enum": ["none", "direct", "transitive"], "default": "direct" },
        "buildPlanCap":        { "type": "integer", "default": 200 },
        "waitForIndex":        { "type": "boolean", "default": true },
        "waitTimeoutMs":       { "type": "integer", "default": 30000 }
      },
      "required": ["name", "part", "content"]
    }
  }
```

- [ ] **Step 2: Add the router case**

In `src/GxMcp.Gateway/Routers/ObjectRouter.cs`, locate the `ConvertToolCall` method (around line 93-180 per recon). Add a new branch:

```csharp
            if (string.Equals(toolName, "genexus_edit_and_build", StringComparison.OrdinalIgnoreCase))
            {
                return new
                {
                    module = "EditAndBuild",
                    action = "Orchestrate",
                    target = args?["name"]?.ToString(),
                    args = args
                };
            }
```

Place it next to the `genexus_edit` mapping so they live together. The `args` echo is intentional: `CommandDispatcher` reads the full args object to extract `part`, `content`, `mode`, etc.

- [ ] **Step 3: Update `ToolHelpCatalog` with long-form help (depends on sub-plan 2)**

In `src/GxMcp.Gateway/ToolHelpCatalog.cs`, add an entry to the `_helpTexts` dictionary:

```csharp
            ["genexus_edit_and_build"] =
                "# genexus_edit_and_build\n\n" +
                "Edit an object and rebuild its callers in one call.\n\n" +
                "## Required\n" +
                "- `name` — object to edit\n" +
                "- `part` — which part (e.g., `Source`, `Rules`)\n" +
                "- `content` — full text or unified diff\n\n" +
                "## Optional\n" +
                "- `mode` — `patch` (default) or `full`\n" +
                "- `type` — disambiguates when name matches multiple objects\n" +
                "- `dryRun` — preview without persisting (default `false`)\n" +
                "- `buildIncludeCallees` — `none` | `direct` (default) | `transitive`\n" +
                "- `buildPlanCap` — max build-plan size (default 200)\n\n" +
                "## Response\n" +
                "Returns a composite envelope with three blocks:\n" +
                "- `edit` — the diff from genexus_edit\n" +
                "- `impact` — output of genexus_analyze mode=impact (callers, risk, etc.)\n" +
                "- `build` — `{ taskId, status: 'Accepted' }` for async build, or `{ status: 'Skipped' }` when no callers\n\n" +
                "Poll the build via `genexus_lifecycle action=status target=op:<taskId>`.\n\n" +
                "## Errors\n" +
                "If `name` matches multiple objects, the edit phase aborts and the envelope returns `status=Error` with an `alternatives` array — retry with one of the (`name`, `type`) pairs.\n\n" +
                "## Example\n" +
                "`{ name: 'InvoiceProc', part: 'Source', mode: 'patch', content: '<diff>', buildIncludeCallees: 'direct' }`\n"
```

- [ ] **Step 4: Write the router test**

Append to `src/GxMcp.Gateway.Tests/McpRouterTests.cs`:

```csharp
[Fact]
public void GenexusEditAndBuild_RoutesToEditAndBuildModule()
{
    var args = JObject.Parse(@"{
        ""name"": ""InvoiceProc"",
        ""part"": ""Source"",
        ""content"": ""@@ -1 +1 @@\n-old\n+new"",
        ""mode"": ""patch""
    }");

    var router = new GxMcp.Gateway.Routers.ObjectRouter();
    var converted = router.ConvertToolCall("genexus_edit_and_build", args);

    var json = JObject.FromObject(converted!);
    Assert.Equal("EditAndBuild", json["module"]?.ToString());
    Assert.Equal("Orchestrate",  json["action"]?.ToString());
    Assert.Equal("InvoiceProc",  json["target"]?.ToString());
    Assert.NotNull(json["args"]);
}
```

- [ ] **Step 5: Run tests**

```
dotnet test src/GxMcp.Gateway.Tests --filter "FullyQualifiedName~GenexusEditAndBuild_RoutesToEditAndBuildModule" --nologo --verbosity minimal
dotnet test src/GxMcp.Gateway.Tests --filter "FullyQualifiedName~ToolSchemaSizeTests" --nologo --verbosity minimal
```

Expected: both PASS. The schema-size test may need a small budget bump if the new tool pushes us over 4900 tokens — if so, bump to 5050 with a comment referencing this sub-plan.

- [ ] **Step 6: Commit**

```
git add src/GxMcp.Gateway/tool_definitions.json src/GxMcp.Gateway/Routers/ObjectRouter.cs src/GxMcp.Gateway/ToolHelpCatalog.cs src/GxMcp.Gateway.Tests/McpRouterTests.cs src/GxMcp.Gateway.Tests/ToolSchemaSizeTests.cs
git commit -m "feat(tools): expose genexus_edit_and_build composite tool"
```

---

### Task 6: End-to-end smoke and CHANGELOG

**Files:**
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Run all tests**

```
dotnet test src/GxMcp.Gateway.Tests --nologo --verbosity minimal
dotnet test src/GxMcp.Worker.Tests --nologo --verbosity minimal
```

Expected: all PASS.

- [ ] **Step 2: MCP smoke probe**

```
pwsh -NoProfile -File scripts/mcp_smoke.ps1 -BaseUrl http://127.0.0.1:5000/mcp
```

Expected: exit code 0; `tools/list` includes `genexus_edit_and_build`.

- [ ] **Step 3: Live verification on a real KB**

From a connected MCP client, call:

```json
{
  "method": "tools/call",
  "params": {
    "name": "genexus_edit_and_build",
    "arguments": {
      "name": "<a real Procedure name>",
      "part": "Source",
      "mode": "patch",
      "content": "<a no-op unified diff>",
      "dryRun": true,
      "buildIncludeCallees": "none"
    }
  }
}
```

Expected response shape:
```json
{
  "status": "Ok",
  "edit":   { "status": "Ok", "diff": "..." },
  "impact": { "status": "Ready", "callers": [...] },
  "build":  { "status": "Skipped", "reason": "..." }
}
```

(`dryRun=true` + `buildIncludeCallees=none` makes the live test safe — no real edit, no real build.)

- [ ] **Step 4: Add CHANGELOG entry**

```markdown
- **New tool**: `genexus_edit_and_build` collapses the edit → analyze impact → build callers
  workflow from 3-5 turns into a single call. Returns a composite envelope with `edit`, `impact`,
  and `build` blocks. The build runs asynchronously and is polled via
  `genexus_lifecycle action=status target=op:<taskId>`.
- **Error UX**: `genexus_edit` now embeds alternative matches inline when an object name is
  ambiguous, so callers no longer need a separate `genexus_list_objects` turn to disambiguate.
```

- [ ] **Step 5: Commit**

```
git add CHANGELOG.md
git commit -m "docs(changelog): note genexus_edit_and_build and inline disambiguation"
```

---

## Done criteria

- [ ] All tasks above completed
- [ ] Gateway and Worker test suites green
- [ ] Smoke probe green and `tools/list` includes `genexus_edit_and_build`
- [ ] Live `dryRun=true` call returns composite envelope on a real KB
- [ ] Sub-plan checkpoint signed off
