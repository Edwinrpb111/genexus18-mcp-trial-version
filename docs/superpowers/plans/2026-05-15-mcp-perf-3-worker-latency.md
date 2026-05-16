# Sub-plan 3 — Worker Latency Instrumentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans. Steps use `- [ ]` syntax.

**Goal:** Measure exactly where the worker spends time on cold-start and SDK init, expose those numbers through `genexus://kb/health` and (in `--full` mode) the `genexus-mcp doctor` CLI, and establish a BenchmarkDotNet baseline for hot paths. **This sub-plan does not change behavior** — it only instruments and surfaces existing latency, so sub-plans 4-6 can be validated quantitatively.

**Architecture:** Three pieces. (1) Gateway side: wrap `WorkerProcess.Start()` in a `Stopwatch`, persist `SpawnMs` per worker, and add a moving-window `ToolMetricState`-style record for spawn samples on the `OperationTracker`. (2) Worker side: emit two new structured log lines bracketing the SDK init block (`WORKER_HANDSHAKE_START` at `src/GxMcp.Worker/Program.cs:85` → `Full SDK Initialization SUCCESS.` at `:292`). Gateway parses those into a `SdkInitMs` per worker. (3) New `BenchmarkDotNet` project `src/GxMcp.Benchmarks` with three baseline benchmarks that produce a JSON artifact under `artifacts/benchmarks/`.

**Tech Stack:** .NET 8 (Gateway + Benchmarks), .NET Framework 4.8 (Worker), `System.Diagnostics.Stopwatch`, `BenchmarkDotNet 0.13+`, xUnit.

---

## File Structure

- **Modify:** `src/GxMcp.Gateway/WorkerProcess.cs` (add `SpawnStopwatch`, expose `SpawnMs`/`SdkInitMs` properties, parse worker log lines)
- **Modify:** `src/GxMcp.Gateway/WorkerPool.cs` (surface latest spawn samples per KB)
- **Modify:** `src/GxMcp.Gateway/OperationTracker.cs` (add `RegisterSpawnSample(string kbAlias, double ms)` + percentile readout)
- **Modify:** `src/GxMcp.Gateway/McpRouter.cs` (extend `BuildHealthReport()` — the existing builder behind `genexus://kb/health` — with `spawnMs` and `sdkInitMs` blocks)
- **Create:** `src/GxMcp.Benchmarks/GxMcp.Benchmarks.csproj`
- **Create:** `src/GxMcp.Benchmarks/Program.cs`
- **Create:** `src/GxMcp.Benchmarks/EnvelopeProjectionBenchmark.cs`
- **Create:** `src/GxMcp.Benchmarks/ToolDefinitionsLoadBenchmark.cs`
- **Create:** `src/GxMcp.Benchmarks/WorkerSpawnSimulationBenchmark.cs`
- **Modify:** `src/GxMcp.Gateway.Tests/OperationTrackerTests.cs` (add tests for spawn sample percentiles)
- **Modify:** `CHANGELOG.md`

---

### Task 1: Instrument worker spawn time

**Files:**
- Modify: `src/GxMcp.Gateway/WorkerProcess.cs` (around the `Start()` method, lines 460-545)

- [ ] **Step 1: Write the failing test for `SpawnMs` exposure**

Add to `src/GxMcp.Gateway.Tests/WorkerProcessTests.cs` (create the file if it does not exist):

```csharp
using System.Threading.Tasks;
using GxMcp.Gateway;
using Xunit;

namespace GxMcp.Gateway.Tests
{
    public class WorkerProcessLatencyTests
    {
        [Fact]
        public void SpawnMs_DefaultsToNull_BeforeStart()
        {
            var kb = new KbHandle("test", "C:\\fake\\path");
            var config = new Configuration();
            var worker = new WorkerProcess(kb, config);

            Assert.Null(worker.SpawnMs);
            Assert.Null(worker.SdkInitMs);
        }
    }
}
```

If `KbHandle` or `Configuration` constructors need different parameters, mirror an existing test under `src/GxMcp.Gateway.Tests/` that builds these types — adjust the test accordingly.

- [ ] **Step 2: Run test to verify it fails**

```
dotnet test src/GxMcp.Gateway.Tests --filter "FullyQualifiedName~WorkerProcessLatencyTests" --nologo --verbosity minimal
```

Expected: FAIL — `SpawnMs` / `SdkInitMs` properties do not exist yet.

- [ ] **Step 3: Add the properties and wire the stopwatch**

In `src/GxMcp.Gateway/WorkerProcess.cs`, **inside the class** (e.g., right after the field declarations around line 40), add:

```csharp
        private long? _spawnMs;
        private long? _sdkInitMs;
        private System.Diagnostics.Stopwatch? _spawnWatch;
        private System.Diagnostics.Stopwatch? _sdkInitWatch;

        public long? SpawnMs => System.Threading.Volatile.Read(ref _spawnMs);
        public long? SdkInitMs => System.Threading.Volatile.Read(ref _sdkInitMs);
```

- [ ] **Step 4: Start the stopwatch around `_process.Start()`**

In `Start()` (line 472 area), wrap the retry loop:

```csharp
                _spawnWatch = System.Diagnostics.Stopwatch.StartNew();
                _sdkInitWatch = System.Diagnostics.Stopwatch.StartNew();

                for (int attempt = 1; attempt <= 10; attempt++)
                {
                    try
                    {
                        _process.Start();
                        _spawnWatch.Stop();
                        System.Threading.Volatile.Write(ref _spawnMs, _spawnWatch.ElapsedMilliseconds);
                        Program.Log($"[Gateway] worker_spawned pid={_process.Id} attempt={attempt} spawnMs={_spawnWatch.ElapsedMilliseconds} idleTimeoutMinutes={_workerIdleTimeout.TotalMinutes}");
                        break;
                    }
                    // ... existing catch blocks unchanged
```

The existing log line at line 477 is replaced with the one above (note the added `spawnMs=` field). All other lines in the catch blocks remain identical.

- [ ] **Step 5: Parse `Full SDK Initialization SUCCESS.` from worker output**

In `src/GxMcp.Gateway/WorkerProcess.cs`, find the `_process.OutputDataReceived` handler (lines 500-515). Inside the handler, **before** the `if (e.Data.TrimStart().StartsWith("{")` branch, add:

```csharp
                        if (_sdkInitWatch != null && _sdkInitWatch.IsRunning &&
                            e.Data.Contains("Full SDK Initialization SUCCESS"))
                        {
                            _sdkInitWatch.Stop();
                            System.Threading.Volatile.Write(ref _sdkInitMs, _sdkInitWatch.ElapsedMilliseconds);
                            Program.Log($"[Gateway] worker_sdk_init pid={_process?.Id} sdkInitMs={_sdkInitWatch.ElapsedMilliseconds}");
                        }
```

- [ ] **Step 6: Run test to verify it passes**

```
dotnet test src/GxMcp.Gateway.Tests --filter "FullyQualifiedName~WorkerProcessLatencyTests" --nologo --verbosity minimal
```

Expected: PASS.

- [ ] **Step 7: Commit**

```
git add src/GxMcp.Gateway/WorkerProcess.cs src/GxMcp.Gateway.Tests/WorkerProcessTests.cs
git commit -m "feat(metrics): instrument worker spawn and SDK init time"
```

---

### Task 2: Add percentile aggregation to `OperationTracker`

**Files:**
- Modify: `src/GxMcp.Gateway/OperationTracker.cs`
- Modify: `src/GxMcp.Gateway/WorkerPool.cs`
- Modify: `src/GxMcp.Gateway.Tests/OperationTrackerTests.cs`

- [ ] **Step 1: Write the failing test for spawn percentiles**

Append to `src/GxMcp.Gateway.Tests/OperationTrackerTests.cs`:

```csharp
[Fact]
public void RegisterSpawnSample_ReturnsP50AndP95_AfterEnoughSamples()
{
    var tracker = new OperationTracker(System.TimeSpan.FromMinutes(5));

    // Feed 100 samples between 100ms and 200ms (so P50≈150, P95≈195)
    for (int i = 1; i <= 100; i++)
    {
        tracker.RegisterSpawnSample("test-kb", 100.0 + i);
    }

    var (count, p50, p95) = tracker.GetSpawnStats("test-kb");
    Assert.Equal(100, count);
    Assert.InRange(p50, 145, 155);
    Assert.InRange(p95, 190, 200);
}

[Fact]
public void GetSpawnStats_ReturnsZeros_ForUnknownKb()
{
    var tracker = new OperationTracker(System.TimeSpan.FromMinutes(5));
    var (count, p50, p95) = tracker.GetSpawnStats("never-seen");
    Assert.Equal(0, count);
    Assert.Equal(0, p50);
    Assert.Equal(0, p95);
}
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet test src/GxMcp.Gateway.Tests --filter "FullyQualifiedName~RegisterSpawnSample|FullyQualifiedName~GetSpawnStats" --nologo --verbosity minimal
```

Expected: FAIL — methods do not exist.

- [ ] **Step 3: Implement spawn-sample storage in `OperationTracker`**

Append to `src/GxMcp.Gateway/OperationTracker.cs` (inside the `OperationTracker` class):

```csharp
        private readonly ConcurrentDictionary<string, SpawnSampleRing> _spawnSamples =
            new ConcurrentDictionary<string, SpawnSampleRing>(StringComparer.OrdinalIgnoreCase);

        public void RegisterSpawnSample(string kbAlias, double ms)
        {
            if (string.IsNullOrWhiteSpace(kbAlias)) return;
            var ring = _spawnSamples.GetOrAdd(kbAlias, _ => new SpawnSampleRing(capacity: 256));
            ring.Add(ms);
        }

        public (int Count, double P50, double P95) GetSpawnStats(string kbAlias)
        {
            if (!_spawnSamples.TryGetValue(kbAlias, out var ring)) return (0, 0, 0);
            return ring.Snapshot();
        }

        private sealed class SpawnSampleRing
        {
            private readonly int _capacity;
            private readonly double[] _buffer;
            private int _count;
            private int _next;
            private readonly object _lock = new object();

            public SpawnSampleRing(int capacity)
            {
                _capacity = capacity;
                _buffer = new double[capacity];
            }

            public void Add(double sample)
            {
                lock (_lock)
                {
                    _buffer[_next] = sample;
                    _next = (_next + 1) % _capacity;
                    if (_count < _capacity) _count++;
                }
            }

            public (int Count, double P50, double P95) Snapshot()
            {
                double[] snapshot;
                int count;
                lock (_lock)
                {
                    count = _count;
                    snapshot = new double[count];
                    System.Array.Copy(_buffer, snapshot, count);
                }
                if (count == 0) return (0, 0, 0);
                System.Array.Sort(snapshot);
                double p50 = snapshot[(int)(count * 0.50)];
                double p95 = snapshot[System.Math.Min(count - 1, (int)(count * 0.95))];
                return (count, p50, p95);
            }
        }
```

- [ ] **Step 4: Wire the call site in `WorkerPool`**

In `src/GxMcp.Gateway/WorkerPool.cs`, find `AcquireAsync` (around line 80-134). After `worker.Start()` succeeds (line 125 area), add:

```csharp
                if (worker.SpawnMs.HasValue)
                {
                    Program.OperationTracker.RegisterSpawnSample(kbAlias, worker.SpawnMs.Value);
                }
```

Replace `kbAlias` with the actual variable name used inside `AcquireAsync` (search the method for the KB key parameter). If `Program.OperationTracker` is not the canonical reference, search `Program.cs` for `_operationTracker` and use the existing public/internal accessor.

- [ ] **Step 5: Run tests to verify they pass**

```
dotnet test src/GxMcp.Gateway.Tests --filter "FullyQualifiedName~RegisterSpawnSample|FullyQualifiedName~GetSpawnStats" --nologo --verbosity minimal
```

Expected: PASS.

- [ ] **Step 6: Commit**

```
git add src/GxMcp.Gateway/OperationTracker.cs src/GxMcp.Gateway/WorkerPool.cs src/GxMcp.Gateway.Tests/OperationTrackerTests.cs
git commit -m "feat(metrics): record worker spawn samples and expose p50/p95 per KB"
```

---

### Task 3: Surface latency in `genexus://kb/health`

**Files:**
- Modify: `src/GxMcp.Gateway/McpRouter.cs` (locate `BuildHealthReport` — search the file for `genexus://kb/health`)

- [ ] **Step 1: Locate the health builder**

```
grep -n "BuildHealthReport\|kb/health" src/GxMcp.Gateway/McpRouter.cs
```

This identifies the method or branch that constructs the health resource body. (Per recon, `genexus://kb/health` is listed at line 222 and its body is built inside `BuildStaticResourceResponse` or a dedicated builder — confirm by reading the file.)

- [ ] **Step 2: Write the failing test**

Append to `src/GxMcp.Gateway.Tests/McpRouterTests.cs`:

```csharp
[Fact]
public void HealthResource_IncludesSpawnAndSdkInitBlocks()
{
    var request = JObject.Parse(@"{
        ""method"": ""resources/read"",
        ""params"": { ""uri"": ""genexus://kb/health"" }
    }");

    var result = McpRouter.HandleMethod(request);
    Assert.NotNull(result);

    var json = JObject.FromObject(result!);
    var contents = (JArray)json["contents"]!;
    var first = (JObject)contents[0];
    var body = first["text"]!.ToString();

    Assert.Contains("spawnMs", body);
    Assert.Contains("sdkInitMs", body);
}
```

- [ ] **Step 3: Run test to verify it fails**

```
dotnet test src/GxMcp.Gateway.Tests --filter "FullyQualifiedName~HealthResource_IncludesSpawnAndSdkInit" --nologo --verbosity minimal
```

Expected: FAIL — current health body has no `spawnMs` / `sdkInitMs` fields.

- [ ] **Step 4: Augment the health body builder**

Inside the health-report construction site (e.g., a `BuildHealthReport()` method or the `kb/health` branch of `BuildStaticResourceResponse`), append a `latency` block. For each known KB alias in the pool, include:

```csharp
            var latency = new System.Collections.Generic.Dictionary<string, object>();
            foreach (var kbAlias in WorkerPool.GetKnownAliases())
            {
                var (count, p50, p95) = Program.OperationTracker.GetSpawnStats(kbAlias);
                var worker = WorkerPool.TryGetWorker(kbAlias);
                latency[kbAlias] = new
                {
                    spawnMs = new { samples = count, p50, p95, lastMs = worker?.SpawnMs },
                    sdkInitMs = new { lastMs = worker?.SdkInitMs }
                };
            }
            // serialize `latency` into the existing health body next to the other blocks
```

`WorkerPool.GetKnownAliases()` and `WorkerPool.TryGetWorker(string)` are new helpers — add them to `src/GxMcp.Gateway/WorkerPool.cs` as small read-only accessors over the existing `ConcurrentDictionary<string, Entry>`. They return `IReadOnlyList<string>` and `WorkerProcess?` respectively.

- [ ] **Step 5: Run test to verify it passes**

```
dotnet test src/GxMcp.Gateway.Tests --filter "FullyQualifiedName~HealthResource_IncludesSpawnAndSdkInit" --nologo --verbosity minimal
```

Expected: PASS.

- [ ] **Step 6: Commit**

```
git add src/GxMcp.Gateway/McpRouter.cs src/GxMcp.Gateway/WorkerPool.cs src/GxMcp.Gateway.Tests/McpRouterTests.cs
git commit -m "feat(metrics): expose worker spawn/SDK-init latency in kb/health resource"
```

---

### Task 4: Add the BenchmarkDotNet baseline project

**Files:**
- Create: `src/GxMcp.Benchmarks/GxMcp.Benchmarks.csproj`
- Create: `src/GxMcp.Benchmarks/Program.cs`
- Create: `src/GxMcp.Benchmarks/EnvelopeProjectionBenchmark.cs`
- Create: `src/GxMcp.Benchmarks/ToolDefinitionsLoadBenchmark.cs`
- Create: `src/GxMcp.Benchmarks/WorkerSpawnSimulationBenchmark.cs`

- [ ] **Step 1: Create the csproj**

`src/GxMcp.Benchmarks/GxMcp.Benchmarks.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <RootNamespace>GxMcp.Benchmarks</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="BenchmarkDotNet" Version="0.13.12" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\GxMcp.Gateway\GxMcp.Gateway.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create the entry point**

`src/GxMcp.Benchmarks/Program.cs`:

```csharp
using BenchmarkDotNet.Running;

namespace GxMcp.Benchmarks
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        }
    }
}
```

- [ ] **Step 3: Create `EnvelopeProjectionBenchmark`**

`src/GxMcp.Benchmarks/EnvelopeProjectionBenchmark.cs`:

```csharp
using BenchmarkDotNet.Attributes;
using Newtonsoft.Json.Linq;
using System.Reflection;

namespace GxMcp.Benchmarks
{
    [MemoryDiagnoser]
    public class EnvelopeProjectionBenchmark
    {
        private JObject _payload = null!;
        private MethodInfo _method = null!;

        [GlobalSetup]
        public void Setup()
        {
            var arr = new JArray();
            for (int i = 0; i < 500; i++)
            {
                arr.Add(new JObject
                {
                    ["name"] = $"Obj{i}",
                    ["type"] = "Procedure",
                    ["path"] = $"Folder/Obj{i}",
                    ["parentPath"] = "Folder",
                    ["description"] = "Lorem ipsum dolor sit amet, consectetur adipiscing elit."
                });
            }
            _payload = new JObject { ["results"] = arr };

            _method = typeof(GxMcp.Gateway.Program).GetMethod(
                "NormalizeToolPayloadForAxi",
                BindingFlags.NonPublic | BindingFlags.Static)!;
        }

        [Benchmark]
        public object? CompactProjection_500Rows()
        {
            var args = new JObject { ["axiCompact"] = true };
            return _method.Invoke(null, new object?[] { _payload, "genexus_list_objects", args, false });
        }

        [Benchmark(Baseline = true)]
        public object? FullPayload_500Rows()
        {
            var args = new JObject { ["axiCompact"] = false };
            return _method.Invoke(null, new object?[] { _payload, "genexus_list_objects", args, false });
        }
    }
}
```

- [ ] **Step 4: Create `ToolDefinitionsLoadBenchmark`**

`src/GxMcp.Benchmarks/ToolDefinitionsLoadBenchmark.cs`:

```csharp
using BenchmarkDotNet.Attributes;
using Newtonsoft.Json.Linq;
using System.IO;

namespace GxMcp.Benchmarks
{
    [MemoryDiagnoser]
    public class ToolDefinitionsLoadBenchmark
    {
        private string _path = null!;

        [GlobalSetup]
        public void Setup()
        {
            // Resolve via the gateway output dir; falls back to repo source copy.
            string candidate = Path.Combine(
                Path.GetDirectoryName(typeof(GxMcp.Gateway.Program).Assembly.Location)!,
                "tool_definitions.json");
            if (!File.Exists(candidate))
            {
                candidate = Path.Combine("..", "..", "..", "..", "GxMcp.Gateway", "tool_definitions.json");
            }
            _path = candidate;
        }

        [Benchmark]
        public int LoadAndParse()
        {
            string json = File.ReadAllText(_path);
            var arr = JArray.Parse(json);
            return arr.Count;
        }
    }
}
```

- [ ] **Step 5: Create `WorkerSpawnSimulationBenchmark`**

`src/GxMcp.Benchmarks/WorkerSpawnSimulationBenchmark.cs`:

```csharp
using BenchmarkDotNet.Attributes;
using GxMcp.Gateway;
using System;

namespace GxMcp.Benchmarks
{
    [MemoryDiagnoser]
    public class WorkerSpawnSimulationBenchmark
    {
        private OperationTracker _tracker = null!;

        [GlobalSetup]
        public void Setup()
        {
            _tracker = new OperationTracker(TimeSpan.FromMinutes(5));
            // Pre-seed 256 samples to simulate steady state.
            var rng = new Random(42);
            for (int i = 0; i < 256; i++)
            {
                _tracker.RegisterSpawnSample("bench-kb", 800 + rng.NextDouble() * 1200);
            }
        }

        [Benchmark]
        public (int, double, double) PercentileSnapshot()
        {
            return _tracker.GetSpawnStats("bench-kb");
        }

        [Benchmark]
        public void RegisterSample()
        {
            _tracker.RegisterSpawnSample("bench-kb", 1234);
        }
    }
}
```

- [ ] **Step 6: Add the project to the solution**

```
dotnet sln add src/GxMcp.Benchmarks/GxMcp.Benchmarks.csproj
```

(If `dotnet sln` reports the solution is not at the repo root, find the `.sln` and use `dotnet sln <sln-file> add ...`.)

- [ ] **Step 7: Verify it builds**

```
dotnet build src/GxMcp.Benchmarks --configuration Release --nologo --verbosity minimal
```

Expected: build succeeds.

- [ ] **Step 8: Run the benchmarks once and capture the baseline**

```
dotnet run --project src/GxMcp.Benchmarks --configuration Release -- --filter "*"
```

This produces `BenchmarkDotNet.Artifacts/results/*.md` files. Copy them into the repo:

```
mkdir -p artifacts/benchmarks/2026-05-15
cp -r BenchmarkDotNet.Artifacts/results/* artifacts/benchmarks/2026-05-15/
```

- [ ] **Step 9: Commit**

```
git add src/GxMcp.Benchmarks artifacts/benchmarks/2026-05-15 *.sln
git commit -m "feat(bench): add BenchmarkDotNet baseline for envelope/tool-load/spawn-tracker"
```

---

### Task 5: CHANGELOG and full regression

**Files:**
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Add changelog entry**

Under the current unreleased / `v2.4.0` heading:

```markdown
- **Observability**: worker spawn time and SDK init time are now measured per KB and exposed via
  `genexus://kb/health` (`latency.<kb>.spawnMs` and `latency.<kb>.sdkInitMs`, with p50/p95 over
  the last 256 samples). New `src/GxMcp.Benchmarks` project provides a BenchmarkDotNet baseline
  for envelope projection, tool-definition loading, and spawn-tracker hot paths. Baseline
  results live under `artifacts/benchmarks/2026-05-15/`.
```

- [ ] **Step 2: Run all Gateway tests**

```
dotnet test src/GxMcp.Gateway.Tests --nologo --verbosity minimal
```

Expected: PASS.

- [ ] **Step 3: Smoke probe**

```
pwsh -NoProfile -File scripts/mcp_smoke.ps1 -BaseUrl http://127.0.0.1:5000/mcp
```

Expected: exit code 0.

- [ ] **Step 4: Live verification**

Issue an MCP `resources/read` for `genexus://kb/health` from a connected client. The response body should now include a `latency` block with `spawnMs.samples > 0` after a few tool calls have been made.

- [ ] **Step 5: Commit**

```
git add CHANGELOG.md
git commit -m "docs(changelog): note worker latency instrumentation and benchmark baseline"
```

---

## Done criteria

- [ ] All tasks above completed
- [ ] `dotnet test src/GxMcp.Gateway.Tests` green (Worker unaffected)
- [ ] `dotnet run --project src/GxMcp.Benchmarks --configuration Release` produces results
- [ ] `genexus://kb/health` response includes `latency` block
- [ ] Sub-plan checkpoint signed off
