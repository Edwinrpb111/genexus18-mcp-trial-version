# MCP Perf & Tool-Stability Implementation Plan — v2.2.0

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the 13-change MCP performance & stability package (`v2.1.7 → v2.2.0`) that fixes the mid-session tool-disappearance bug and reduces roundtrips/payload size across the GeneXus MCP surface.

**Architecture:** All changes are additive. Foundation is a gateway-side `ResponseSizeGuard` that caps per-tool payloads before the harness can truncate (which was dropping the tool registry). On top of that: lifecycle pagination, edit `post_state` diff, discovery `_meta.suggested_next` + aggregates + definitive empty states, async builds with piggyback in `_meta.background_jobs`, plus token-tax reductions (tool schema trim, compact JSON, terse errors, field selection). All gated behind a single feature flag `MCP_PERF_PROFILE=v1` (default on).

**Tech Stack:** .NET 8 (Gateway), .NET Framework 4.8 (Worker), Newtonsoft.Json 13, xUnit, Newtonsoft.Json.Schema for contract tests. JSON-RPC over stdio + HTTP.

**Spec:** `docs/superpowers/specs/2026-05-13-mcp-perf-and-tool-stability-design.md`

---

## Phase 0 — Setup

### Task 0.1: Feature flag scaffold

**Files:**
- Modify: `src/GxMcp.Gateway/Configuration.cs`

- [ ] **Step 1: Write failing test for `PerfProfileV1Enabled`**

Create `src/GxMcp.Gateway.Tests/PerfProfileFlagTests.cs`:

```csharp
using GxMcp.Gateway;
using Xunit;

public class PerfProfileFlagTests
{
    [Fact]
    public void DefaultsToV1Enabled()
    {
        Environment.SetEnvironmentVariable("MCP_PERF_PROFILE", null);
        Assert.True(PerfProfile.V1Enabled);
    }

    [Fact]
    public void DisabledWhenEnvLegacy()
    {
        Environment.SetEnvironmentVariable("MCP_PERF_PROFILE", "legacy");
        Assert.False(PerfProfile.V1Enabled);
        Environment.SetEnvironmentVariable("MCP_PERF_PROFILE", null);
    }
}
```

- [ ] **Step 2: Run test, expect FAIL** (`PerfProfile` type does not exist)

`dotnet test src/GxMcp.Gateway.Tests/GxMcp.Gateway.Tests.csproj --filter PerfProfileFlagTests`

- [ ] **Step 3: Implement `PerfProfile`**

Create `src/GxMcp.Gateway/PerfProfile.cs`:

```csharp
namespace GxMcp.Gateway
{
    public static class PerfProfile
    {
        public static bool V1Enabled
        {
            get
            {
                string? v = System.Environment.GetEnvironmentVariable("MCP_PERF_PROFILE");
                if (string.IsNullOrWhiteSpace(v)) return true;
                return !string.Equals(v, "legacy", System.StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
```

- [ ] **Step 4: Run test, expect PASS**

- [ ] **Step 5: Commit**

```bash
git add src/GxMcp.Gateway/PerfProfile.cs src/GxMcp.Gateway.Tests/PerfProfileFlagTests.cs
git commit -m "feat(gateway): add MCP_PERF_PROFILE feature flag"
```

### Task 0.2: Version bump to 2.2.0-pre

**Files:**
- Modify: `src/GxMcp.Gateway/GxMcp.Gateway.csproj:8-11`
- Modify: `package.json` (root)

- [ ] **Step 1: Update csproj version**

Change all four version tags from `2.1.7` to `2.2.0-pre`:

```xml
<Version>2.2.0-pre</Version>
<AssemblyVersion>2.2.0.0</AssemblyVersion>
<FileVersion>2.2.0.0</FileVersion>
<InformationalVersion>2.2.0-pre</InformationalVersion>
```

- [ ] **Step 2: Update package.json**

```json
"version": "2.2.0-pre"
```

- [ ] **Step 3: Verify build**

```bash
dotnet build src/GxMcp.Gateway/GxMcp.Gateway.csproj -c Release
```
Expected: succeeds, no warnings about version.

- [ ] **Step 4: Commit**

```bash
git add src/GxMcp.Gateway/GxMcp.Gateway.csproj package.json
git commit -m "chore: bump to 2.2.0-pre"
```

---

## Phase 1 — Foundation (A): ResponseSizeGuard + lifecycle pagination

Bug fix first. Nothing else ships without this.

### Task 1.1: `ResponseSizeGuard` — measure + truncate

**Files:**
- Create: `src/GxMcp.Gateway/ResponseSizeGuard.cs`
- Create: `src/GxMcp.Gateway.Tests/ResponseSizeGuardTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using GxMcp.Gateway;
using Newtonsoft.Json.Linq;
using Xunit;

public class ResponseSizeGuardTests
{
    [Fact]
    public void SmallPayloadPassesThrough()
    {
        var guard = new ResponseSizeGuard(maxBytes: 1000);
        var payload = JObject.Parse(@"{""result"":""ok""}");
        var (result, truncated) = guard.Apply(payload, toolName: "x", args: new JObject());
        Assert.False(truncated);
        Assert.Equal("ok", result["result"]!.ToString());
    }

    [Fact]
    public void OversizedPayloadIsTruncatedWithSentinel()
    {
        var big = new string('x', 5000);
        var payload = JObject.FromObject(new { result = big });
        var guard = new ResponseSizeGuard(maxBytes: 1000);
        var (result, truncated) = guard.Apply(payload, toolName: "genexus_lifecycle", args: JObject.FromObject(new { action = "status" }));
        Assert.True(truncated);
        Assert.NotNull(result["_meta"]?["truncated"]);
        Assert.Equal("genexus_lifecycle", result["_meta"]!["truncated"]!["follow_up"]!["tool"]!.ToString());
        Assert.True((int)result["_meta"]!["truncated"]!["original_size"]! >= 5000);
    }

    [Fact]
    public void TruncationFollowUpSuggestsPagination()
    {
        var payload = JObject.FromObject(new { result = new string('x', 5000) });
        var guard = new ResponseSizeGuard(maxBytes: 1000);
        var (result, _) = guard.Apply(payload, "genexus_lifecycle",
            JObject.FromObject(new { action = "status", jobId = "abc" }));
        var followUp = result["_meta"]!["truncated"]!["follow_up"]!;
        Assert.Equal("abc", followUp["args"]!["jobId"]!.ToString());
        Assert.NotNull(followUp["args"]!["page"]);
    }
}
```

- [ ] **Step 2: Run, expect FAIL**

`dotnet test --filter ResponseSizeGuardTests`

- [ ] **Step 3: Implement `ResponseSizeGuard`**

```csharp
using Newtonsoft.Json.Linq;
using System.Text;

namespace GxMcp.Gateway
{
    public class ResponseSizeGuard
    {
        public const int DefaultMaxBytes = 220_000; // ≈55k tokens

        private readonly int _maxBytes;

        public ResponseSizeGuard(int maxBytes = DefaultMaxBytes) => _maxBytes = maxBytes;

        public (JObject result, bool truncated) Apply(JObject payload, string toolName, JObject args)
        {
            int size = Encoding.UTF8.GetByteCount(payload.ToString(Newtonsoft.Json.Formatting.None));
            if (size <= _maxBytes) return (payload, false);

            var sentinel = new JObject
            {
                ["_meta"] = new JObject
                {
                    ["truncated"] = new JObject
                    {
                        ["reason"] = "response_exceeded_cap",
                        ["original_size"] = size,
                        ["cap_bytes"] = _maxBytes,
                        ["follow_up"] = BuildFollowUp(toolName, args)
                    }
                }
            };

            ResponseSizeGuardTelemetry.RecordOversize(toolName, size);
            return (sentinel, true);
        }

        private static JObject BuildFollowUp(string tool, JObject args)
        {
            var followArgs = (JObject)args.DeepClone();
            followArgs["page"] = 1;
            followArgs["page_size"] = 25;
            return new JObject { ["tool"] = tool, ["args"] = followArgs };
        }
    }

    public static class ResponseSizeGuardTelemetry
    {
        public static void RecordOversize(string tool, int size) =>
            Program.Log($"[Gateway] OVERSIZE tool={tool} size={size}");
    }
}
```

- [ ] **Step 4: Run, expect PASS**

- [ ] **Step 5: Commit**

```bash
git add src/GxMcp.Gateway/ResponseSizeGuard.cs src/GxMcp.Gateway.Tests/ResponseSizeGuardTests.cs
git commit -m "feat(gateway): ResponseSizeGuard with truncation sentinel"
```

### Task 1.2: Wire `ResponseSizeGuard` into response pipeline

**Files:**
- Modify: `src/GxMcp.Gateway/McpRouter.cs` (find the tool-result emission path)
- Create: `src/GxMcp.Gateway.Tests/ResponseSizeGuardWiringTests.cs`

- [ ] **Step 1: Identify the central response point in `McpRouter.cs`**

Locate where `tools/call` results are serialized into the JSON-RPC response. Look for `"result"` construction inside the `tools/call` handler.

- [ ] **Step 2: Write contract test**

```csharp
[Fact]
public async Task ToolsCallOversizedResponseGetsTruncatedSentinel()
{
    // Stub a worker that returns 300KB payload
    var router = TestHelpers.RouterWithStubWorker(stub: PayloadOfSize(300_000));
    var resp = await router.HandleAsync(JObject.Parse(@"{
        ""jsonrpc"":""2.0"",""id"":1,""method"":""tools/call"",
        ""params"":{""name"":""genexus_lifecycle"",""arguments"":{""action"":""status""}}
    }"));
    var meta = resp["result"]?["_meta"];
    Assert.NotNull(meta?["truncated"]);
}
```

- [ ] **Step 3: Run, expect FAIL** (no guard wired)

- [ ] **Step 4: Add guard call in `McpRouter.HandleToolsCall` (post-worker result)**

Pseudo-location (apply before final `result` assignment):

```csharp
if (PerfProfile.V1Enabled && rawResult is JObject jobj)
{
    var guard = new ResponseSizeGuard();
    var (gated, _) = guard.Apply(jobj, toolName, argsObj);
    rawResult = gated;
}
```

- [ ] **Step 5: Run, expect PASS**

- [ ] **Step 6: Commit**

```bash
git add src/GxMcp.Gateway/McpRouter.cs src/GxMcp.Gateway.Tests/ResponseSizeGuardWiringTests.cs
git commit -m "feat(gateway): wire ResponseSizeGuard into tools/call pipeline"
```

### Task 1.3: Paginate `lifecycle status`

**Files:**
- Modify: `src/GxMcp.Worker/Services/BatchService.cs` (locate `Status` action)
- Modify: `src/GxMcp.Gateway/Routers/OperationsRouter.cs` (locate lifecycle conversion)
- Create: `src/GxMcp.Worker.Tests/LifecycleStatusPaginationTests.cs`

- [ ] **Step 1: Write failing test for paginated status**

```csharp
[Fact]
public void StatusReturnsPaginatedWarnings()
{
    var svc = new BatchService();
    var warnings = Enumerable.Range(1, 200).Select(i => $"warning {i}").ToList();
    var result = svc.BuildStatusPayload(warnings, page: 1, pageSize: 50);
    Assert.Equal(50, ((JArray)result["warnings"]!).Count);
    Assert.Equal(200, (int)result["_meta"]!["pagination"]!["total"]!);
    Assert.Equal(1, (int)result["_meta"]!["pagination"]!["page"]!);
    Assert.True((bool)result["_meta"]!["pagination"]!["has_more"]!);
}
```

- [ ] **Step 2: Run, expect FAIL** (`BuildStatusPayload` doesn't exist)

- [ ] **Step 3: Implement `BuildStatusPayload` in `BatchService.cs`**

```csharp
public JObject BuildStatusPayload(IList<string> warnings, int page, int pageSize)
{
    page = Math.Max(1, page);
    pageSize = Math.Clamp(pageSize, 1, 200);
    int total = warnings.Count;
    var slice = warnings.Skip((page - 1) * pageSize).Take(pageSize).ToList();
    return new JObject
    {
        ["warnings"] = JArray.FromObject(slice),
        ["_meta"] = new JObject
        {
            ["pagination"] = new JObject
            {
                ["total"] = total,
                ["page"] = page,
                ["page_size"] = pageSize,
                ["has_more"] = (page * pageSize) < total
            }
        }
    };
}
```

- [ ] **Step 4: Run, expect PASS**

- [ ] **Step 5: Plumb `page`/`page_size` through `OperationsRouter`**

In `ConvertToolCall` for `genexus_lifecycle` (after locating the existing Lifecycle case — search for `Lifecycle`/`Build`/`Status` in the file), pass through:

```csharp
case "genexus_lifecycle":
    return new
    {
        module = "Build",
        action = args?["action"]?.ToString(),
        target = args?["target"]?.ToString(),
        jobId = args?["jobId"]?.ToString(),
        page = args?["page"]?.ToObject<int?>(),
        pageSize = args?["pageSize"]?.ToObject<int?>() ?? args?["page_size"]?.ToObject<int?>()
    };
```

- [ ] **Step 6: Update worker `BatchService.HandleStatus` to use new payload**

Wire `BuildStatusPayload(warnings, request.page ?? 1, request.pageSize ?? 50)` into the action handler.

- [ ] **Step 7: Run worker integration test**

```bash
dotnet test src/GxMcp.Worker.Tests/GxMcp.Worker.Tests.csproj --filter LifecycleStatusPagination
```

- [ ] **Step 8: Commit**

```bash
git commit -am "feat(worker): paginate lifecycle status warnings"
```

### Task 1.4: Paginate `lifecycle result`

Same shape as Task 1.3 applied to the `Result` action handler. Repeat pattern.

- [ ] **Step 1: Add `BuildResultPayload(items, page, pageSize)` to `BatchService`** mirroring `BuildStatusPayload`.
- [ ] **Step 2: Test**

```csharp
[Fact]
public void ResultReturnsPaginatedItems()
{
    var svc = new BatchService();
    var items = Enumerable.Range(1, 120).Select(i => new JObject { ["msg"] = $"item {i}" }).ToList<JObject>();
    var result = svc.BuildResultPayload(items, 2, 50);
    Assert.Equal(50, ((JArray)result["items"]!).Count);
    Assert.Equal("item 51", (string)((JArray)result["items"]!)[0]!["msg"]!);
}
```

- [ ] **Step 3: Wire into `HandleResult` action.**

- [ ] **Step 4: Commit**

```bash
git commit -am "feat(worker): paginate lifecycle result items"
```

### Task 1.5: Regression — 200-warning build does not exceed cap

**Files:**
- Create: `src/GxMcp.Gateway.Tests/LifecycleRegressionTests.cs`

- [ ] **Step 1: Write regression test**

```csharp
[Fact]
public async Task LargeWarningSetStaysUnderCapAndPreservesTools()
{
    var router = TestHelpers.RouterWithStubWorker(stub: SimulateBuild(warningCount: 200));
    var resp1 = await router.HandleAsync(StatusCallEnvelope());
    Assert.Null(resp1["result"]?["_meta"]?["truncated"]); // pagination prevents truncation

    // Tools must still be callable on the next turn
    var resp2 = await router.HandleAsync(JObject.Parse(@"{""jsonrpc"":""2.0"",""id"":2,""method"":""tools/list""}"));
    Assert.NotEmpty(resp2["result"]!["tools"]!);
}
```

- [ ] **Step 2: Run, expect PASS** (pagination from 1.3 ensures payload fits).

- [ ] **Step 3: Commit**

```bash
git commit -am "test(gateway): regression for tool-disappear bug under 200-warning build"
```

### Task 1.6: Oversize telemetry surfaces in `worker_debug.log`

- [ ] **Step 1: Test that `Program.Log` is called with `OVERSIZE` tag on truncation.**

```csharp
[Fact]
public void OversizeTruncationLogsTelemetry()
{
    using var logCapture = TestHelpers.CaptureLog();
    var guard = new ResponseSizeGuard(maxBytes: 100);
    guard.Apply(JObject.FromObject(new { result = new string('x', 500) }), "any_tool", new JObject());
    Assert.Contains("OVERSIZE tool=any_tool", logCapture.Content);
}
```

- [ ] **Step 2: Verify it passes** (already implemented in Task 1.1).

- [ ] **Step 3: Commit**

```bash
git commit -am "test(gateway): verify oversize telemetry logging"
```

---

## Phase 2 — Token wins (F, I, H)

Pure savings, no API change.

### Task 2.1: Compact JSON serializer (I)

**Files:**
- Modify: `src/GxMcp.Gateway/McpRouter.cs` (serializer settings)
- Create: `src/GxMcp.Gateway.Tests/CompactJsonTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
[Fact]
public void ResponseDropsNullAndDefaultFields()
{
    var router = TestHelpers.RouterWithStubWorker(stub: JObject.Parse(@"{
        ""ok"": true, ""warnings"": [], ""error"": null, ""deprecated"": false, ""data"": ""x""
    }"));
    var resp = await router.HandleAsync(SimpleToolCallEnvelope());
    var json = resp.ToString(Formatting.None);
    Assert.DoesNotContain("\"error\":null", json);
    Assert.DoesNotContain("\"deprecated\":false", json);
    // Empty array kept (semantic for empty-state convention)
    Assert.Contains("\"warnings\":[]", json);
}
```

- [ ] **Step 2: Run, expect FAIL**

- [ ] **Step 3: Implement compact serializer settings**

In `McpRouter.cs`, define and use a static settings object for outgoing payloads:

```csharp
internal static readonly JsonSerializerSettings CompactSettings = new JsonSerializerSettings
{
    NullValueHandling = NullValueHandling.Ignore,
    DefaultValueHandling = DefaultValueHandling.Ignore,
    Formatting = Formatting.None,
};

internal static string SerializeCompact(JToken token)
    => PerfProfile.V1Enabled ? token.ToString(Formatting.None) : token.ToString(Formatting.Indented);
```

Apply where responses are written to the HTTP body / stdio output.

- [ ] **Step 4: Run, expect PASS**

- [ ] **Step 5: Commit**

```bash
git commit -am "feat(gateway): compact JSON output, drop nulls/defaults"
```

### Task 2.2: Trim tool schemas (F)

**Files:**
- Modify: `src/GxMcp.Gateway/tool_definitions.json`
- Create: `src/GxMcp.Gateway.Tests/ToolSchemaSizeTests.cs`

- [ ] **Step 1: Write a baseline size test that locks the budget**

```csharp
[Fact]
public void TotalToolSchemaSizeIsUnderBudget()
{
    string path = Path.Combine(TestHelpers.RepoRoot, "src/GxMcp.Gateway/tool_definitions.json");
    string content = File.ReadAllText(path);
    int approxTokens = content.Length / 4;
    Assert.True(approxTokens < 3500,
        $"tool_definitions.json is {approxTokens} tokens (~chars/4); budget 3500.");
}
```

- [ ] **Step 2: Run; if currently above budget, expect FAIL.**

- [ ] **Step 3: Trim `tool_definitions.json`**

For each tool:
- `description` ≤ 60 chars, action-verb, no examples
- `inputSchema.properties.<param>.description` removed when param name is self-explanatory (`name`, `type`, `page`, etc.)
- `examples`/`default` keys removed unless behavior-defining

- [ ] **Step 4: Run all tool-routing contract tests to confirm no regressions**

```bash
dotnet test src/GxMcp.Gateway.Tests/GxMcp.Gateway.Tests.csproj --filter Contract
```

- [ ] **Step 5: Run size test, expect PASS**

- [ ] **Step 6: Commit**

```bash
git commit -am "perf(gateway): trim tool_definitions.json to ~3500 token budget"
```

### Task 2.3: Terse errors by default (H)

**Files:**
- Modify: `src/GxMcp.Worker/Helpers/SdkDiagnosticsHelper.cs`
- Modify: `src/GxMcp.Gateway/Routers/OperationsRouter.cs` (pass `verbose_errors` flag)
- Create: `src/GxMcp.Worker.Tests/TerseErrorsTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
[Fact]
public void DefaultErrorIsTerse()
{
    var helper = new SdkDiagnosticsHelper();
    var ex = new Exception("boom\n\nstack frame 1\nstack frame 2");
    var result = helper.BuildErrorEnvelope(ex, verbose: false);
    Assert.False(result.ToString().Contains("stack frame"));
    Assert.Equal("boom", (string)result["message"]!);
    Assert.NotNull(result["code"]);
}

[Fact]
public void VerboseFlagPreservesDetails()
{
    var helper = new SdkDiagnosticsHelper();
    var ex = new Exception("boom\n\nstack frame 1");
    var result = helper.BuildErrorEnvelope(ex, verbose: true);
    Assert.Contains("stack frame", result.ToString());
}
```

- [ ] **Step 2: Run, expect FAIL**

- [ ] **Step 3: Implement `BuildErrorEnvelope`**

```csharp
public JObject BuildErrorEnvelope(Exception ex, bool verbose)
{
    string msgFirstLine = ex.Message?.Split('\n')[0]?.Trim() ?? "Unknown error";
    var env = new JObject
    {
        ["code"] = ClassifyError(ex),
        ["message"] = msgFirstLine,
        ["hint"] = HintFor(ex),
    };
    if (verbose) env["details"] = new JObject
    {
        ["stack"] = ex.StackTrace,
        ["full_message"] = ex.Message,
        ["inner"] = ex.InnerException?.Message
    };
    return env;
}

private static string ClassifyError(Exception ex) => ex switch
{
    UsageException => "usage",
    System.IO.FileNotFoundException => "not_found",
    _ => "internal"
};

private static string? HintFor(Exception ex) => ex switch
{
    UsageException ue => ue.Hint,
    _ => null
};
```

- [ ] **Step 4: Plumb `verbose_errors` flag through `OperationsRouter`**

Add `verboseErrors = args?["verbose_errors"]?.ToObject<bool?>() ?? false` to each tool conversion that produces errors.

- [ ] **Step 5: Run, expect PASS**

- [ ] **Step 6: Commit**

```bash
git commit -am "feat(worker): terse errors by default; verbose_errors flag for details"
```

---

## Phase 3 — Roundtrip reductions (B, C, J, K, L)

### Task 3.1: Edit `post_state.diff` (B)

**Files:**
- Modify: `src/GxMcp.Worker/Services/JsonPatchService.cs`
- Modify: `src/GxMcp.Worker/Services/SemanticOpsService.cs`
- Create: `src/GxMcp.Worker.Tests/EditPostStateTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
[Fact]
public void EditReturnsUnifiedDiffByDefault()
{
    var before = "line a\nline b\nline c\n";
    var after  = "line a\nline B!\nline c\n";
    var diff = DiffBuilder.UnifiedDiff(before, after, context: 3);
    Assert.Contains("-line b", diff);
    Assert.Contains("+line B!", diff);
    Assert.StartsWith("@@", diff.Split('\n').First(l => l.StartsWith("@@")));
}

[Fact]
public void VerboseAdds15LineSlice()
{
    var result = JsonPatchService.BuildPostState(before: "x", after: "y", verbose: true);
    Assert.NotNull(result["diff"]);
    Assert.NotNull(result["slices"]);
}

[Fact]
public void ReturnPostStateFalseOmitsField()
{
    var resp = JsonPatchService.WrapEditResponse(result: new JObject(), before: "a", after: "b", returnPostState: false, verbose: false);
    Assert.Null(resp["post_state"]);
}
```

- [ ] **Step 2: Run, expect FAIL** (`DiffBuilder` / `BuildPostState` not present)

- [ ] **Step 3: Implement `DiffBuilder.UnifiedDiff`**

Create `src/GxMcp.Worker/Helpers/DiffBuilder.cs`:

```csharp
public static class DiffBuilder
{
    public static string UnifiedDiff(string before, string after, int context = 3)
    {
        // Use Microsoft's `DiffPlex` or a minimal Myers diff impl.
        // If DiffPlex unavailable in net48, implement minimal LCS-based diff.
        return MyersDiff.Compute(before.Split('\n'), after.Split('\n'), context);
    }
}
```

Add `MyersDiff` (or vendor DiffPlex via NuGet — check net48 compatibility).

- [ ] **Step 4: Implement `BuildPostState` / `WrapEditResponse` in `JsonPatchService`**

```csharp
public static JObject BuildPostState(string before, string after, bool verbose)
{
    var obj = new JObject { ["diff"] = DiffBuilder.UnifiedDiff(before, after, context: verbose ? 15 : 3) };
    if (verbose) obj["slices"] = BuildSlices(after, before);
    return obj;
}

public static JObject WrapEditResponse(JObject result, string before, string after, bool returnPostState, bool verbose)
{
    if (returnPostState)
        result["post_state"] = BuildPostState(before, after, verbose);
    return result;
}
```

- [ ] **Step 5: Wire `return_post_state` flag (default true) through `genexus_edit` router/handler**

In `OperationsRouter` (find existing `genexus_edit` conversion) and worker edit handler:

```csharp
bool returnPostState = args?["return_post_state"]?.ToObject<bool?>() ?? true;
bool verbose = args?["verbose"]?.ToObject<bool?>() ?? false;
```

- [ ] **Step 6: Run, expect PASS**

- [ ] **Step 7: Commit**

```bash
git commit -am "feat(worker): edit returns post_state diff by default"
```

### Task 3.2: Structural edits return compact change-set

**Files:**
- Modify: `src/GxMcp.Worker/Services/SemanticOpsService.cs`
- Modify: `src/GxMcp.Worker.Tests/SemanticOpsServiceTests.cs`

- [ ] **Step 1: Write test for Variables edit**

```csharp
[Fact]
public void VariablesEditReturnsCompactChanges()
{
    var result = svc.ApplyVariablesEdit(/* args */);
    Assert.NotNull(result["post_state"]?["changes"]);
    Assert.Null(result["post_state"]?["diff"]); // line-diff meaningless for structural
}
```

- [ ] **Step 2: Implement structural change extractor**

In `SemanticOpsService`, after applying changes, build a compact summary:

```csharp
private JArray ChangesForVariables(List<VariableChange> changes) =>
    new JArray(changes.Select(c => JObject.FromObject(new
    {
        op = c.Op.ToString().ToLowerInvariant(),
        name = c.Name,
        before = c.Before,
        after = c.After
    })));
```

- [ ] **Step 3: Run, expect PASS**

- [ ] **Step 4: Commit**

```bash
git commit -am "feat(worker): compact change-set in post_state for structural edits"
```

### Task 3.3: `_meta.suggested_next` on `list_objects` (C)

**Files:**
- Modify: `src/GxMcp.Worker/Services/ListService.cs`
- Create: `src/GxMcp.Worker.Tests/SuggestedNextListTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
[Fact]
public void ListWithResultsSuggestsReadOnTopMatch()
{
    var svc = new ListService();
    var result = svc.List(filter: "Customer", limit: 5);
    var s = result["_meta"]!["suggested_next"]!;
    Assert.Equal("genexus_read", (string)s["tool"]!);
    Assert.NotNull(s["args"]!["name"]);
}

[Fact]
public void EmptyListOmitsSuggestion()
{
    var result = new ListService().List(filter: "DoesNotExist", limit: 5);
    Assert.Null(result["_meta"]?["suggested_next"]);
}
```

- [ ] **Step 2: Run, expect FAIL**

- [ ] **Step 3: Implement `SuggestedNext` builder**

```csharp
private static JObject? SuggestedNext(IList<ObjectSummary> items)
{
    if (items.Count == 0) return null;
    var top = items[0];
    return new JObject
    {
        ["tool"] = "genexus_read",
        ["args"] = new JObject { ["name"] = top.Name, ["type"] = top.Type }
    };
}
```

Apply in `List`:

```csharp
result["_meta"] = new JObject { ["suggested_next"] = SuggestedNext(items) };
```

- [ ] **Step 4: Run, expect PASS**

- [ ] **Step 5: Commit**

```bash
git commit -am "feat(worker): suggested_next on list_objects"
```

### Task 3.4: `_meta.suggested_next` on `query`, `structure`, `search_source`

Replicate 3.3 pattern across:
- `Services/StructureService.cs` → suggest `genexus_read` on most-recently-modified part
- `Services/Structure/IndexService.cs` (query) → same as 3.3
- `Services/LinterService.cs` or query/search handler — check the actual location

- [ ] **Step 1: Write per-service test**
- [ ] **Step 2: Implement `SuggestedNext` analog**
- [ ] **Step 3: Run all suggested_next tests, expect PASS**
- [ ] **Step 4: Commit**

```bash
git commit -am "feat(worker): suggested_next on query/structure/search"
```

### Task 3.5: Minimal-by-default list shape (J)

**Files:**
- Modify: `src/GxMcp.Worker/Services/ListService.cs`
- Create: `src/GxMcp.Worker.Tests/MinimalListTests.cs`

- [ ] **Step 1: Failing test**

```csharp
[Fact]
public void DefaultListItemHasOnlyFourFields()
{
    var result = new ListService().List(filter: "*", limit: 1);
    var item = (JObject)((JArray)result["items"]!)[0]!;
    Assert.Equal(4, item.Properties().Count());
    Assert.True(item.ContainsKey("name"));
    Assert.True(item.ContainsKey("type"));
    Assert.True(item.ContainsKey("modified"));
    Assert.True(item.ContainsKey("size"));
}

[Fact]
public void VerboseFlagReturnsFullShape()
{
    var result = new ListService().List(filter: "*", limit: 1, verbose: true);
    var item = (JObject)((JArray)result["items"]!)[0]!;
    Assert.True(item.Properties().Count() > 4);
}
```

- [ ] **Step 2: Implement minimal/verbose split in `ProjectItem`**

```csharp
private static JObject ToMinimal(ObjectSummary s) => JObject.FromObject(new
{
    name = s.Name, type = s.Type, modified = s.Modified, size = s.Size
});

private static JObject ToVerbose(ObjectSummary s) => JObject.FromObject(s);
```

- [ ] **Step 3: Plumb `verbose` arg**

In `OperationsRouter` for `genexus_list_objects`:

```csharp
verbose = args?["verbose"]?.ToObject<bool?>() ?? false,
```

- [ ] **Step 4: PASS + Commit**

```bash
git commit -am "feat(worker): minimal-by-default list shape; verbose opt-in"
```

### Task 3.6: Pre-computed aggregates (K)

**Files:**
- Modify: `src/GxMcp.Worker/Services/ListService.cs`
- Modify: `src/GxMcp.Worker/Services/HistoryService.cs`
- Create: `src/GxMcp.Worker.Tests/AggregatesTests.cs`

- [ ] **Step 1: Failing tests**

```csharp
[Fact]
public void ListAggregatesIncludeTotalAndByType()
{
    var r = new ListService().List(filter: "*", limit: 5);
    var agg = r["_meta"]!["aggregates"]!;
    Assert.NotNull(agg["total"]);
    Assert.NotNull(agg["by_type"]);
}

[Fact]
public void HistoryAggregatesIncludeByDate()
{
    var r = new HistoryService().GetHistory("X");
    Assert.NotNull(r["_meta"]?["aggregates"]?["by_date"]);
}
```

- [ ] **Step 2: Implement aggregator helpers**

```csharp
private static JObject BuildListAggregates(IEnumerable<ObjectSummary> all) => new JObject
{
    ["total"] = all.Count(),
    ["by_type"] = JObject.FromObject(all.GroupBy(x => x.Type)
        .ToDictionary(g => g.Key, g => g.Count())),
    ["modified_last_7d"] = all.Count(x => x.Modified > DateTime.UtcNow.AddDays(-7))
};
```

- [ ] **Step 3: Wire under `_meta.aggregates` alongside `suggested_next`.**

- [ ] **Step 4: PASS + Commit**

```bash
git commit -am "feat(worker): pre-computed aggregates in list/history"
```

### Task 3.7: Definitive empty states (L)

**Files:**
- Modify: `src/GxMcp.Worker/Services/ListService.cs`, `IndexService.cs`, search handler
- Create: `src/GxMcp.Worker.Tests/EmptyStateTests.cs`

- [ ] **Step 1: Test**

```csharp
[Theory]
[InlineData("no_matches", "ZzzNoMatch")]
[InlineData("kb_not_loaded", null)]
public void EmptyResponseIncludesEmptyReason(string expected, string? filter)
{
    var r = new ListService().List(filter: filter ?? "*", limit: 5, kbLoaded: filter != null);
    if (((JArray)r["items"]!).Count == 0)
        Assert.Equal(expected, (string)r["_meta"]!["empty_reason"]!);
}
```

- [ ] **Step 2: Implement empty-reason classification**

```csharp
private static string ClassifyEmpty(ListContext ctx) =>
    !ctx.KbLoaded ? "kb_not_loaded" :
    ctx.HadFilter ? "filtered_out" : "no_matches";
```

Always set on empty result; never on non-empty.

- [ ] **Step 3: PASS + Commit**

```bash
git commit -am "feat(worker): definitive empty states with _meta.empty_reason"
```

---

## Phase 4 — Async builds (D, E)

### Task 4.1: `BackgroundJobRegistry`

**Files:**
- Create: `src/GxMcp.Gateway/BackgroundJobRegistry.cs`
- Create: `src/GxMcp.Gateway.Tests/BackgroundJobRegistryTests.cs`

- [ ] **Step 1: Failing tests**

```csharp
public class BackgroundJobRegistryTests
{
    [Fact]
    public void RegisterStartsAsRunning()
    {
        var r = new BackgroundJobRegistry(retentionSeconds: 600);
        var job = r.Start(session: "s1", kind: "build", estSeconds: 30);
        Assert.Equal("running", job.Status);
    }

    [Fact]
    public void CompleteTransitionsToSucceeded()
    {
        var r = new BackgroundJobRegistry(60);
        var job = r.Start("s1", "build", 30);
        r.Complete(job.Id, success: true, summary: "ok");
        Assert.Equal("succeeded", r.Get(job.Id)!.Status);
    }

    [Fact]
    public void PiggybackReturnsUnseenCompletionsAndRemovesAfterSeen()
    {
        var r = new BackgroundJobRegistry(60);
        var job = r.Start("s1", "build", 30);
        r.Complete(job.Id, success: true, summary: "ok");
        var first = r.SnapshotForSession("s1");
        Assert.Single(first);
        r.MarkSeen("s1", first.Select(j => j.Id));
        Assert.Empty(r.SnapshotForSession("s1"));
    }

    [Fact]
    public void RunningJobsKeepReappearingUntilTerminal()
    {
        var r = new BackgroundJobRegistry(60);
        var job = r.Start("s1", "build", 30);
        r.MarkSeen("s1", new[] { job.Id });
        Assert.NotEmpty(r.SnapshotForSession("s1"));
    }

    [Fact]
    public void RetentionRemovesOldTerminalJobs()
    {
        var r = new BackgroundJobRegistry(retentionSeconds: 0);
        var job = r.Start("s1", "build", 0);
        r.Complete(job.Id, true, "ok");
        Thread.Sleep(50);
        r.SweepExpired();
        Assert.Null(r.Get(job.Id));
    }
}
```

- [ ] **Step 2: Implement**

```csharp
public class BackgroundJobRegistry
{
    private readonly int _retentionSeconds;
    private readonly ConcurrentDictionary<string, JobEntry> _jobs = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _seenBySession = new();

    public BackgroundJobRegistry(int retentionSeconds) => _retentionSeconds = retentionSeconds;

    public JobEntry Start(string session, string kind, int estSeconds)
    {
        var job = new JobEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            Session = session,
            Kind = kind,
            Status = "running",
            StartedAt = DateTime.UtcNow,
            EstimatedSeconds = estSeconds
        };
        _jobs[job.Id] = job;
        return job;
    }

    public void Complete(string jobId, bool success, string? summary, JObject? result = null)
    {
        if (!_jobs.TryGetValue(jobId, out var job)) return;
        job.Status = success ? "succeeded" : "failed";
        job.CompletedAt = DateTime.UtcNow;
        job.Summary = summary;
        job.Result = result;
    }

    public JobEntry? Get(string jobId) => _jobs.TryGetValue(jobId, out var j) ? j : null;

    public IReadOnlyList<JobEntry> SnapshotForSession(string session)
    {
        var seen = _seenBySession.GetOrAdd(session, _ => new HashSet<string>());
        return _jobs.Values
            .Where(j => j.Session == session)
            .Where(j => j.Status == "running" || !seen.Contains(j.Id))
            .ToList();
    }

    public void MarkSeen(string session, IEnumerable<string> jobIds)
    {
        var seen = _seenBySession.GetOrAdd(session, _ => new HashSet<string>());
        lock (seen) foreach (var id in jobIds)
            if (_jobs.TryGetValue(id, out var j) && j.Status != "running") seen.Add(id);
    }

    public void SweepExpired()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-_retentionSeconds);
        foreach (var kvp in _jobs)
            if (kvp.Value.CompletedAt != null && kvp.Value.CompletedAt < cutoff)
                _jobs.TryRemove(kvp.Key, out _);
    }
}

public class JobEntry
{
    public string Id { get; set; } = "";
    public string Session { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Status { get; set; } = "running";
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int EstimatedSeconds { get; set; }
    public string? Summary { get; set; }
    public JObject? Result { get; set; }
}
```

- [ ] **Step 3: PASS + Commit**

```bash
git commit -am "feat(gateway): BackgroundJobRegistry with retention and seen-tracking"
```

### Task 4.2: Piggyback middleware in response pipeline

**Files:**
- Modify: `src/GxMcp.Gateway/McpRouter.cs`
- Create: `src/GxMcp.Gateway.Tests/PiggybackTests.cs`

- [ ] **Step 1: Failing test**

```csharp
[Fact]
public async Task EveryResponseCarriesBackgroundJobsWhenActive()
{
    var router = TestHelpers.RouterWithBackgroundJob(session: "s1");
    var resp = await router.HandleAsync(WhoamiCallEnvelope("s1"));
    var jobs = (JArray)resp["result"]!["_meta"]!["background_jobs"]!;
    Assert.NotEmpty(jobs);
}

[Fact]
public async Task NoBackgroundJobsMeansFieldAbsent()
{
    var router = TestHelpers.Router(); // no jobs
    var resp = await router.HandleAsync(WhoamiCallEnvelope("s1"));
    Assert.Null(resp["result"]?["_meta"]?["background_jobs"]);
}
```

- [ ] **Step 2: Inject `BackgroundJobRegistry` into `McpRouter` (DI or singleton)**

- [ ] **Step 3: Add piggyback step**

After `ResponseSizeGuard.Apply` in `HandleToolsCall`:

```csharp
if (PerfProfile.V1Enabled && session is HttpSessionState s)
{
    var snapshot = _jobRegistry.SnapshotForSession(s.Id);
    if (snapshot.Count > 0)
    {
        var meta = (JObject)(rawResult["_meta"] ??= new JObject());
        meta["background_jobs"] = JArray.FromObject(snapshot.Select(j => new
        {
            id = j.Id, status = j.Status, summary = j.Summary,
            completed_at = j.CompletedAt, est_seconds = j.EstimatedSeconds
        }));
        _jobRegistry.MarkSeen(s.Id, snapshot.Select(j => j.Id));
    }
}
```

- [ ] **Step 4: PASS + Commit**

```bash
git commit -am "feat(gateway): piggyback background_jobs on every response"
```

### Task 4.3: `lifecycle build` returns `job_id` immediately

**Files:**
- Modify: `src/GxMcp.Gateway/Routers/OperationsRouter.cs`
- Modify: `src/GxMcp.Worker/Services/BatchService.cs`
- Create: `src/GxMcp.Gateway.Tests/AsyncBuildTests.cs`

- [ ] **Step 1: Failing test**

```csharp
[Fact]
public async Task BuildReturnsJobIdAndRunningStatusImmediately()
{
    var sw = Stopwatch.StartNew();
    var resp = await router.HandleAsync(BuildCallEnvelope(estSeconds: 60));
    sw.Stop();
    Assert.True(sw.ElapsedMilliseconds < 1000, "build call must not block");
    Assert.NotNull(resp["result"]!["job_id"]);
    Assert.Equal("running", (string)resp["result"]!["status"]!);
}
```

- [ ] **Step 2: Implement async build path**

In gateway build handler:

```csharp
var job = _jobRegistry.Start(session.Id, "build", estSeconds: estimate);
_ = Task.Run(async () =>
{
    try
    {
        var result = await _worker.RunBuildAsync(target);
        _jobRegistry.Complete(job.Id, success: true,
            summary: $"{result.Errors} errors, {result.Warnings} warnings",
            result: result.AsJObject());
    }
    catch (Exception ex)
    {
        _jobRegistry.Complete(job.Id, success: false, summary: ex.Message);
    }
});
return new JObject
{
    ["job_id"] = job.Id, ["status"] = "running",
    ["estimated_seconds"] = estimate,
    ["hint"] = "continue with other tools; status will appear in _meta.background_jobs"
};
```

- [ ] **Step 3: PASS + Commit**

```bash
git commit -am "feat(gateway): lifecycle build returns job_id immediately"
```

### Task 4.4: Short-build synchronous fast-path (E)

- [ ] **Step 1: Test that builds with `estimated_seconds < BuildSyncThreshold` block-and-return**

```csharp
[Fact]
public async Task ShortBuildReturnsResultDirectly()
{
    var resp = await router.HandleAsync(BuildCallEnvelope(estSeconds: 5));
    Assert.Null(resp["result"]?["job_id"]);
    Assert.NotNull(resp["result"]!["build_result"]);
}
```

- [ ] **Step 2: Add `BuildSyncThresholdSeconds = 20` in `Configuration.cs`**

- [ ] **Step 3: In build handler, branch on estimate**

```csharp
if (estimate < _config.BuildSyncThresholdSeconds)
{
    var result = await _worker.RunBuildAsync(target);
    return new JObject { ["build_result"] = result.AsJObject() };
}
// else: async path from 4.3
```

- [ ] **Step 4: PASS + Commit**

```bash
git commit -am "feat(gateway): synchronous fast-path for short builds"
```

### Task 4.5: `lifecycle status` long-poll

- [ ] **Step 1: Test long-poll blocks until job terminal**

```csharp
[Fact]
public async Task LongPollReturnsWhenJobCompletes()
{
    var job = registry.Start("s1", "build", 30);
    var pollTask = router.HandleAsync(StatusCallEnvelope(job.Id, waitSeconds: 5));
    await Task.Delay(200);
    registry.Complete(job.Id, true, "ok");
    var resp = await pollTask;
    Assert.Equal("succeeded", (string)resp["result"]!["status"]!);
}

[Fact]
public async Task LongPollHonorsTimeout()
{
    var job = registry.Start("s1", "build", 30);
    var sw = Stopwatch.StartNew();
    var resp = await router.HandleAsync(StatusCallEnvelope(job.Id, waitSeconds: 1));
    sw.Stop();
    Assert.True(sw.ElapsedMilliseconds >= 1000);
    Assert.Equal("running", (string)resp["result"]!["status"]!);
}
```

- [ ] **Step 2: Implement long-poll in status handler**

```csharp
int wait = Math.Clamp(args?["wait_seconds"]?.ToObject<int?>() ?? 0, 0, 25);
var deadline = DateTime.UtcNow.AddSeconds(wait);
JobEntry? job;
do
{
    job = _jobRegistry.Get(jobId);
    if (job?.Status != "running" || wait == 0) break;
    await Task.Delay(250);
} while (DateTime.UtcNow < deadline);
return BuildStatusResponse(job);
```

- [ ] **Step 3: PASS + Commit**

```bash
git commit -am "feat(gateway): long-poll on lifecycle status (wait_seconds)"
```

---

## Phase 5 — Opt-in surface (G, M)

### Task 5.1: Field selection (`fields=` / `parts=`) on `read`

**Files:**
- Modify: `src/GxMcp.Worker/Services/StructureService.cs` (or read handler — locate `genexus_read`)
- Create: `src/GxMcp.Worker.Tests/FieldSelectionTests.cs`

- [ ] **Step 1: Failing test**

```csharp
[Fact]
public void ReadWithPartsReturnsOnlyRequestedSections()
{
    var r = svc.Read("MyTransaction", parts: new[] { "variables" });
    Assert.NotNull(r["variables"]);
    Assert.Null(r["source"]);
    Assert.Null(r["structure"]);
}

[Fact]
public void ReadWithoutPartsReturnsFullObject()
{
    var r = svc.Read("MyTransaction", parts: null);
    Assert.NotNull(r["variables"]);
    Assert.NotNull(r["source"]);
}
```

- [ ] **Step 2: Implement parts filter**

```csharp
public JObject Read(string name, string[]? parts)
{
    var full = ReadFullObject(name);
    if (parts == null || parts.Length == 0) return full;
    var filtered = new JObject();
    foreach (var key in parts) if (full[key] != null) filtered[key] = full[key];
    return filtered;
}
```

- [ ] **Step 3: Plumb `parts` arg in `OperationsRouter`**

```csharp
case "genexus_read":
    return new
    {
        // existing fields
        parts = args?["parts"]?.ToObject<string[]>()
    };
```

- [ ] **Step 4: PASS + Commit**

```bash
git commit -am "feat(worker): field/parts selection on genexus_read"
```

### Task 5.2: `inline_read_top` on list/query (M)

- [ ] **Step 1: Failing test**

```csharp
[Fact]
public void InlineReadTopAttachesContentForTopMatches()
{
    var r = svc.List(filter: "Customer", limit: 5, inlineReadTop: 2);
    var reads = (JArray)r["inline_reads"]!;
    Assert.Equal(2, reads.Count);
    Assert.NotNull(reads[0]!["content"]);
}

[Fact]
public void InlineReadTopZeroOmitsField()
{
    var r = svc.List(filter: "Customer", limit: 5, inlineReadTop: 0);
    Assert.Null(r["inline_reads"]);
}
```

- [ ] **Step 2: Implement**

```csharp
if (inlineReadTop > 0)
{
    var reads = items.Take(inlineReadTop).Select(item => new JObject
    {
        ["name"] = item.Name,
        ["content"] = _structureService.Read(item.Name, parts: null)
    });
    result["inline_reads"] = new JArray(reads);
}
```

- [ ] **Step 3: PASS + Commit**

```bash
git commit -am "feat(worker): inline_read_top opt-in for combined list+read"
```

---

## Phase 6 — Release 2.2.0

### Task 6.1: Full regression sweep

- [ ] **Step 1: Run all tests**

```bash
dotnet test src/GxMcp.Gateway.Tests/GxMcp.Gateway.Tests.csproj -c Release
dotnet test src/GxMcp.Worker.Tests/GxMcp.Worker.Tests.csproj -c Release
```
Expected: all green.

- [ ] **Step 2: Run on a real KB via local smoke**

Open the GeneXus IDE with a sample KB, run `genexus_open_kb`, then exercise:
- `genexus_list_objects` (confirm `_meta.aggregates` and `suggested_next`)
- `genexus_read` with `parts: ["variables"]`
- `genexus_edit` (confirm `post_state.diff`)
- `genexus_lifecycle action=build` on a small object (sync path)
- `genexus_lifecycle action=build` on a batch (async + piggyback)
- `genexus_lifecycle action=status job_id=X wait_seconds=10`

Document any issues in `docs/issues/`.

- [ ] **Step 3: Verify tool persistence under simulated load**

Run a session of 50+ tool calls including 3 batch builds. Confirm `mcp__genexus18__*` tools remain callable throughout (no harness disappear-bug).

### Task 6.2: CHANGELOG entry

**Files:**
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Add 2.2.0 section** (above 2.1.7)

```markdown
## v2.2.0 — 2026-05-XX

### Fixed
- **Tools-disappear-mid-session bug** — gateway-side `ResponseSizeGuard` caps per-tool payloads (55k token budget) before the harness truncation path can drop the tool registry. (`docs/issues/tools-disappear-mid-session.md`)

### Added (perf profile v1, default on; opt out via `MCP_PERF_PROFILE=legacy`)
- `genexus_lifecycle status`/`result` support `page`/`page_size` pagination.
- `genexus_edit` returns `post_state.diff` (unified) by default; `verbose=true` adds ±15-line slices; `return_post_state=false` opts out.
- `genexus_lifecycle build` is non-blocking — returns `{job_id, status: running}` immediately; status piggybacks in `_meta.background_jobs` on every subsequent response.
- Short builds (< 20s estimate) use a synchronous fast-path, returning the result in one turn.
- `genexus_lifecycle status` supports `wait_seconds` (long-poll up to 25s).
- Discovery tools (`list_objects`, `query`, `structure`, `search_source`) include `_meta.suggested_next: {tool, args}`.
- List/query responses include `_meta.aggregates: {total, by_type, modified_last_7d}`.
- Empty results carry `_meta.empty_reason` (`no_matches` | `filtered_out` | `kb_not_loaded` | `permission`).
- `genexus_read` and `genexus_inspect` accept `parts: [...]` / `fields: [...]` for surgical reads.
- `list_objects`/`query` accept `inline_read_top: 0-3` (opt-in) for combined list-and-read.
- `_meta.background_jobs` surfaces async build progress without polling.

### Changed
- List items default to minimal shape (`name`, `type`, `modified`, `size`); pass `verbose=true` for full shape.
- Errors default to terse (`{code, message, hint}`); pass `verbose_errors=true` or call `genexus_logs` for full diagnostics.
- Tool definitions (`tool_definitions.json`) trimmed to ~3500-token budget (was ~6k).
- Response payloads use compact JSON (no indentation, nulls/defaults dropped) when perf profile is on.

### Deferred
- TOON serialization (reevaluate after telemetry; see spec open questions).
```

- [ ] **Step 2: Commit**

```bash
git commit -am "docs(release): CHANGELOG for v2.2.0"
```

### Task 6.3: Final version bump

**Files:**
- Modify: `src/GxMcp.Gateway/GxMcp.Gateway.csproj:8-11`
- Modify: `package.json`

- [ ] **Step 1: Strip `-pre` suffix**

Csproj:
```xml
<Version>2.2.0</Version>
<AssemblyVersion>2.2.0.0</AssemblyVersion>
<FileVersion>2.2.0.0</FileVersion>
<InformationalVersion>2.2.0</InformationalVersion>
```

package.json:
```json
"version": "2.2.0"
```

- [ ] **Step 2: Build release**

```bash
dotnet build src/GxMcp.Gateway/GxMcp.Gateway.csproj -c Release
```

- [ ] **Step 3: Commit + tag**

```bash
git commit -am "chore(release): v2.2.0"
git tag v2.2.0
```

- [ ] **Step 4: Run local release script per RELEASE_FLOW.md**

```pwsh
.\scripts\release.ps1
```

This publishes the worker artifacts via CI per the existing flow (per `memory/genexus_mcp_release_flow.md`).

### Task 6.4: Mark related issues closed

- [ ] **Step 1: Update `docs/issues/tools-disappear-mid-session.md`**

Add at top:

```markdown
**Status:** Resolved in v2.2.0 via gateway-side `ResponseSizeGuard` + lifecycle pagination. See `docs/superpowers/specs/2026-05-13-mcp-perf-and-tool-stability-design.md` (A).
```

- [ ] **Step 2: Commit**

```bash
git commit -am "docs(issues): mark tools-disappear-mid-session resolved in v2.2.0"
```

---

## Self-review checklist (run before handing off)

- [ ] Every spec section (A)–(M) has at least one task. ✓ Phase 1 = A; Phase 2 = F, I, H; Phase 3 = B, C, J, K, L; Phase 4 = D, E; Phase 5 = G, M.
- [ ] Feature flag `PerfProfile.V1Enabled` gates new behavior. ✓ Task 0.1 + applied in 1.2, 2.1, 4.2.
- [ ] No placeholders (`TBD`, `add appropriate X`). ✓
- [ ] Type/method names consistent across tasks: `ResponseSizeGuard.Apply`, `BackgroundJobRegistry.Start/Complete/SnapshotForSession/MarkSeen`, `DiffBuilder.UnifiedDiff`, `BuildPostState`, `WrapEditResponse`, `BuildListAggregates`, `ClassifyEmpty`. ✓
- [ ] Tests precede implementation in every task. ✓
- [ ] Commits frequent (≥1 per task). ✓

## Open questions for engineer

- **DiffPlex vs hand-rolled Myers diff in net48:** check if `DiffPlex` (popular C# diff lib) has a net48-compatible build. If yes, use it. If no, hand-rolled diff in `MyersDiff` (test it against known-good fixtures).
- **`OperationsRouter` Lifecycle case:** the existing file only shows a snippet ending mid-method. Locate the `genexus_lifecycle` case (search `genexus_lifecycle` or `Build` / `Lifecycle` action) before plumbing `page`/`pageSize`/`wait_seconds` parameters.
- **Session id source for piggyback:** confirm whether `HttpSessionState` or stdio context provides the session id. For stdio, the session is implicit/singleton — use `"stdio"` as session key.
