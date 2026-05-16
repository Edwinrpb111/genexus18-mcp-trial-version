using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using GxMcp.Worker.Helpers;
using GxMcp.Worker.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Security;

namespace GxMcp.Worker.Services
{
    public class BuildService : IBuildServiceFacade
    {
        private string _msbuildPath;
        private string _gxDir;
        private KbService _kbService;
        private IndexCacheService _indexCacheService;
        private CallerGraphService _callerGraphService;
        private static readonly ConcurrentDictionary<string, BuildTaskStatus> _tasks = new ConcurrentDictionary<string, BuildTaskStatus>();

        private static readonly System.Collections.Generic.Dictionary<string, int> _phaseProgressMap =
            new System.Collections.Generic.Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase)
            {
                { "Starting",   5  },
                { "OpeningKB",  10 },
                { "Specifying", 15 },
                { "Generating", 35 },
                { "Compiling",  50 },
                { "Finishing",  85 },
                { "Linking",    85 },
                { "Done",       100 },
                { "Completed",  100 }
            };

        private static void EmitPhaseProgress(string phase, int total = 100)
        {
            int p;
            if (_phaseProgressMap.TryGetValue(phase, out p))
            {
                GxMcp.Worker.Helpers.ProgressEmitter.Emit(p, total, "Build phase: " + phase);
            }
        }

        private const int TailBufferSize = 30;

        // Regex parsers for MSBuild/GeneXus output
        private static readonly Regex _rxSpecifying = new Regex(@"^\s*Specifying\s+(?<obj>.+?)\s*\.\.\.\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // "Generating ObjectName ..." — exclude "Generating to <path>" (those are output-file lines, not objects)
        private static readonly Regex _rxGenerating = new Regex(@"^\s*Generating\s+(?!to\b)(?<obj>.+?)(?:\s*\.\.\.)?\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex _rxCompiling  = new Regex(@"^\s*Compil(ation|ing)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // GeneXus spec/gen errors are 'error spc####:', C# compiler errors are 'error CS####:',
        // and MSBuild surfaces some lines as 'error : <message>' (no code). Capture all three forms.
        private static readonly Regex _rxError      = new Regex(@"\berror\s*(:|[A-Z]{2,4}\d+\s*:)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex _rxWarning    = new Regex(@"\bwarning\s*(:|[A-Z]{2,4}\d+\s*:)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex _rxOpenKb     = new Regex(@"^\s*Opening Knowledge Base\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex _rxBuildDone  = new Regex(@"^\s*(Build|Specification|Compilation)\s+(succeeded|failed|complete)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex _rxBuildOneEnd = new Regex(@"Build One Task\s+(terminado|finished|Sucesso|completed|ended)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // FR#12 (friction-report 2026-05-14): MSBuild + GeneXus emit dozens of
        // "Copiando módulo X" / "Copying module X" / "Restoring NuGet packages" lines per build.
        // They go to FullOutput (terminal payload) but get filtered out of TailLines so the
        // live tail shows actionable signal. Phase change / Errors / Warnings always pass.
        private static readonly Regex _rxModuleCopyNoise = new Regex(
            @"^\s*(Copiando|Copying|Restoring NuGet|Restaurando NuGet|Wrote\s+|Touching\s+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public class BuildTaskStatus
        {
            public string TaskId { get; set; }
            public string Status { get; set; }            // Accepted | Running | Succeeded | Failed | Error | Cancelled
            public string Phase { get; set; }             // Starting | OpeningKB | Specifying | Generating | Compiling | Finishing | Done
            public string Action { get; set; }
            public string Target { get; set; }
            public List<string> Targets { get; set; }   // populated when batch build (multi-target)
            public int? TargetsTotal { get; set; }
            public int? TargetsDone { get; set; }
            public string CurrentObject { get; set; }
            public int ErrorCount { get; set; }
            public int WarningCount { get; set; }
            public int LineCount { get; set; }
            public string LastLine { get; set; }
            public List<string> TailLines { get; set; } = new List<string>();
            public List<string> Errors { get; set; } = new List<string>();
            public List<string> Warnings { get; set; } = new List<string>();
            public string Output { get; set; }
            public string StartTime { get; set; }
            public string EndTime { get; set; }
            public double? ElapsedSeconds { get; set; }
            public int ExitCode { get; set; }
            public string Error { get; set; }
            public List<string> CallersToAlsoBuild { get; set; }
            public string Hint { get; set; }
            // v2.3.8 (Task 5.1/5.2): expansion plan applied to the BuildOne
            // sequence. Surfaced under _meta.buildPlan in status/result.
            public BuildPlan BuildPlan { get; set; }

            [JsonIgnore] internal Process Process { get; set; }
            [JsonIgnore] internal DateTime StartedAt { get; set; }
            [JsonIgnore] internal StringBuilder FullOutput { get; set; } = new StringBuilder();
            [JsonIgnore] internal object _lock = new object();
        }

        public BuildService()
        {
            _gxDir = Environment.GetEnvironmentVariable("GX_PROGRAM_DIR") ?? @"C:\Program Files (x86)\GeneXus\GeneXus18";

            string[] searchPaths = new[] {
                @"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
                @"C:\Program Files\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe",
                Path.Combine(_gxDir, "MSBuild.exe"),
                @"C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe"
            };

            foreach (var p in searchPaths) { if (File.Exists(p)) { _msbuildPath = p; break; } }
        }

        public void SetKbService(KbService kbService) { _kbService = kbService; }
        public void SetIndexCacheService(IndexCacheService ics) { _indexCacheService = ics; }
        public void SetCallerGraphService(CallerGraphService graph) { _callerGraphService = graph; }
        public KbService KbService => _kbService;

        // v2.3.8 (Task 5.1) — friction report 2026-05-15 #7. Building a
        // WebPanel that calls N procedures emitted CS0246 because each
        // generated csproj lacked references to callee DLLs. Fix: expand the
        // target list via CallerGraphService and order it reverse-topologically
        // (deepest callees first, originally requested targets last), then
        // emit BuildOne for each — every csproj sees its dependencies on
        // disk by the time GeneXus generates it.
        public class BuildPlan
        {
            public List<string> Expanded { get; set; } = new List<string>();
            public List<string> Skipped { get; set; } = new List<string>();
            public bool Truncated { get; set; }
            public int NodeCap { get; set; }
            public int RequestedNodes { get; set; }
            public string IncludeCallees { get; set; }
        }

        public BuildPlan ExpandTargets(IEnumerable<string> targets, string includeCallees = "transitive", int cap = 200)
        {
            var plan = new BuildPlan { NodeCap = cap, IncludeCallees = includeCallees ?? "transitive" };
            var originalList = (targets ?? Enumerable.Empty<string>())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .ToList();
            var originalSet = new HashSet<string>(originalList, StringComparer.OrdinalIgnoreCase);

            // No graph or "none" → preserve original order, no expansion.
            if (_callerGraphService == null || string.Equals(plan.IncludeCallees, "none", StringComparison.OrdinalIgnoreCase))
            {
                plan.Expanded = originalList;
                return plan;
            }

            // Collect callees per requested target. We over-fetch (cap+1) to
            // detect truncation before mixing in the originally requested set.
            var calleeOrder = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int requestedTotal = 0;

            foreach (var t in originalList)
            {
                if (string.Equals(plan.IncludeCallees, "direct", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var c in _callerGraphService.GetCallees(t))
                    {
                        requestedTotal++;
                        if (originalSet.Contains(c)) continue;
                        if (seen.Add(c)) calleeOrder.Add(c);
                    }
                }
                else // transitive (default)
                {
                    var trans = _callerGraphService.GetCalleesTransitive(t, maxNodes: cap + 1);
                    requestedTotal += trans.Nodes.Count;
                    if (trans.Truncated)
                    {
                        plan.Truncated = true;
                        plan.RequestedNodes = requestedTotal;
                    }
                    foreach (var c in trans.Nodes)
                    {
                        if (originalSet.Contains(c)) continue;
                        if (seen.Add(c)) calleeOrder.Add(c);
                    }
                }
            }

            if (plan.Truncated)
            {
                // Don't emit a partial plan — caller decides (BuildPlanTooLarge response).
                plan.Expanded = originalList;
                return plan;
            }

            // Reverse so the deepest leaves come first (callees before callers).
            // BFS yields shallowest-first; reversing gives the topological order
            // we want for sequential BuildOne emission.
            calleeOrder.Reverse();

            plan.Expanded.AddRange(calleeOrder);
            plan.Expanded.AddRange(originalList);
            return plan;
        }

        public string Build(string action, string target)
        {
            return Build(action, target, includeCallees: "transitive", buildPlanCap: 200);
        }

        public string Build(string action, string target, string includeCallees, int buildPlanCap)
        {
            // Parse target: single name OR comma/semicolon-separated list for batch "Build With These Only"
            var targets = ParseTargets(target);

            // v2.3.8 (Task 5.1) — expand via CallerGraphService so callees compile
            // before callers and every per-object csproj sees its dependency DLLs.
            // Only applies to Build action with concrete targets; RebuildAll/Reorg/etc. ignore.
            BuildPlan plan = null;
            if (action != null && action.Equals("Build", StringComparison.OrdinalIgnoreCase) && targets.Count > 0)
            {
                plan = ExpandTargets(targets, includeCallees ?? "transitive", buildPlanCap);
                if (plan.Truncated)
                {
                    return JsonConvert.SerializeObject(new
                    {
                        status = "BuildPlanTooLarge",
                        suggested = "Build All from IDE, or set includeCallees='none' / 'direct' / reduce the target set",
                        graph = new { requestedNodes = plan.RequestedNodes, cap = plan.NodeCap },
                        requested = targets,
                        includeCallees = plan.IncludeCallees
                    });
                }
                targets = plan.Expanded;
            }

            string taskId = Guid.NewGuid().ToString().Substring(0, 8);
            var status = new BuildTaskStatus {
                TaskId = taskId,
                Action = action,
                Target = target,
                // Always echo the parsed list so the agent can confirm what got dispatched,
                // including the single-target case. Doc contract says "the parsed list".
                Targets = targets.Count > 0 ? targets : null,
                Status = "Running",
                Phase = "Starting",
                StartTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                StartedAt = DateTime.UtcNow,
                BuildPlan = plan
            };

            // Best-effort caller lookup for hint (only meaningful for single-object builds)
            if (targets.Count == 1)
            {
                var callers = TryGetDirectCallers(targets[0]);
                if (callers != null && callers.Count > 0)
                {
                    status.CallersToAlsoBuild = callers;
                    status.Hint = "After this build finishes, also build the caller(s) listed in 'callersToAlsoBuild' — generated DLLs of callers are NOT regenerated by an individual object build. You can pass them all to a single 'build' call as a comma-separated 'target' to run them in one MSBuild cycle (saves ~30s of OpenKB per object).";
                }
            }

            _tasks[taskId] = status;

            Task.Run(() => RunBuild(status, action, targets));

            return JsonConvert.SerializeObject(new {
                status = "Accepted",
                message = targets.Count > 1
                    ? $"Batch build started for {targets.Count} objects in a single KB-open cycle. Poll action='status' target=<taskId> for progress."
                    : "Build task started in background. Poll genexus_lifecycle action='status' with target=<taskId> for progress.",
                taskId = taskId,
                targets = targets.Count > 0 ? targets : null,
                callersToAlsoBuild = status.CallersToAlsoBuild,
                hint = status.Hint,
                _meta = plan != null ? new {
                    buildPlan = new {
                        requested = ParseTargets(target),
                        expanded = plan.Expanded,
                        includeCallees = plan.IncludeCallees,
                        cap = plan.NodeCap
                    }
                } : null
            });
        }

        private static List<string> ParseTargets(string target)
        {
            if (string.IsNullOrWhiteSpace(target)) return new List<string>();
            return target
                .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint GetOEMCP();

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint GetConsoleOutputCP();

        private static Encoding _msbuildEncodingCached;
        private static Encoding ResolveMsbuildEncoding()
        {
            if (_msbuildEncodingCached != null) return _msbuildEncodingCached;
            try
            {
                // Worker runs detached from a console — Console.OutputEncoding falls back to
                // UTF-8 in that mode. MSBuild however emits bytes using the host's OEM /
                // console code page (CP850/CP1252 on PT-BR Windows, CP437 on en-US). Ask
                // Win32 directly so we don't trust the framework's stub: GetConsoleOutputCP
                // → live console CP, GetOEMCP → system-wide OEM CP. Either is right; OEM is
                // a stable fallback when no console is attached.
                uint cp = 0;
                try { cp = GetConsoleOutputCP(); } catch { }
                if (cp == 0 || cp == 65001) { try { cp = GetOEMCP(); } catch { } }
                if (cp != 0)
                {
                    // .NET Framework 4.8 ships all OEM/ANSI code pages natively, so no
                    // CodePagesEncodingProvider registration is required (that lives in the
                    // System.Text.Encoding.CodePages NuGet package, .NET 5+ only).
                    _msbuildEncodingCached = Encoding.GetEncoding((int)cp);
                }
                else
                {
                    _msbuildEncodingCached = Console.OutputEncoding ?? Encoding.UTF8;
                }
            }
            catch
            {
                _msbuildEncodingCached = Encoding.UTF8;
            }
            return _msbuildEncodingCached;
        }

        public string GetStatus(string taskId, int page = 1, int pageSize = 50)
        {
            if (string.IsNullOrEmpty(taskId))
            {
                return JsonConvert.SerializeObject(new { tasks = _tasks.Values.OrderByDescending(t => t.StartTime).Take(10) });
            }

            if (_tasks.TryGetValue(taskId, out var status))
            {
                lock (status._lock)
                {
                    if (status.Status == "Running" && status.StartedAt != default(DateTime))
                        status.ElapsedSeconds = Math.Round((DateTime.UtcNow - status.StartedAt).TotalSeconds, 1);

                    // Serialize the status without the full warnings list, then inject paginated warnings
                    var jo = JObject.FromObject(status, new JsonSerializer { NullValueHandling = NullValueHandling.Ignore });
                    StripOutputWhileRunning(jo, status.Status);

                    // Replace the flat warnings array with a paginated wrapper
                    var paginatedWarnings = BatchService.BuildStatusPayload(status.Warnings, page, pageSize);
                    jo["warnings"] = paginatedWarnings["warnings"];
                    jo["_meta"] = paginatedWarnings["_meta"];

                    return jo.ToString(Formatting.None);
                }
            }
            return "{\"error\": \"Task ID not found\"}";
        }

        // FR#19 (friction-report 2026-05-14): during long-poll each `status` call used to return
        // the full Output blob (often 200+ lines), repeated 3× per build. Drop it while Running
        // — TailLines already gives the live tail in a much smaller surface.
        private static void StripOutputWhileRunning(JObject jo, string status)
        {
            if (!string.Equals(status, "Running", StringComparison.OrdinalIgnoreCase)) return;
            jo.Remove("Output");
            jo.Remove("output");
        }

        public string GetResult(string taskId, int page = 1, int pageSize = 50)
        {
            if (string.IsNullOrEmpty(taskId))
                return "{\"error\": \"taskId required\"}";

            if (_tasks.TryGetValue(taskId, out var status))
            {
                lock (status._lock)
                {
                    if (status.Status == "Running" && status.StartedAt != default(DateTime))
                        status.ElapsedSeconds = Math.Round((DateTime.UtcNow - status.StartedAt).TotalSeconds, 1);

                    var jo = JObject.FromObject(status, new JsonSerializer { NullValueHandling = NullValueHandling.Ignore });
                    StripOutputWhileRunning(jo, status.Status);

                    // Replace the flat errors/items array with a paginated wrapper
                    var paginatedResult = BatchService.BuildResultPayload(status.Errors, page, pageSize);
                    jo["items"] = paginatedResult["items"];
                    jo["_meta"] = paginatedResult["_meta"];

                    return jo.ToString(Formatting.None);
                }
            }
            return "{\"error\": \"Task ID not found\"}";
        }

        public string Cancel(string taskId)
        {
            if (string.IsNullOrEmpty(taskId)) return "{\"error\": \"taskId required\"}";
            // FR#7 (friction-report 2026-05-14): the old "Task ID not found" was ambiguous —
            // the operation might have completed and been pruned, or never existed, or
            // expired from the registry. Return a more specific message + hint so the LLM
            // does not silently lose state.
            if (!_tasks.TryGetValue(taskId, out var status))
                return JsonConvert.SerializeObject(new
                {
                    error = "Unknown build taskId",
                    taskId,
                    hint = "Task may have completed and been pruned, or the worker restarted. " +
                           "Call lifecycle action=status without a target to list recent tasks."
                });

            try
            {
                var p = status.Process;
                if (p != null && !p.HasExited)
                {
                    p.Kill();
                    status.Status = "Cancelled";
                    status.Phase = "Done";
                    EmitPhaseProgress(status.Phase);
                    status.EndTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    return JsonConvert.SerializeObject(new { status = "Cancelled", taskId = taskId });
                }
                return JsonConvert.SerializeObject(new { status = status.Status, message = "Task already finished" });
            }
            catch (Exception ex)
            {
                return "{\"error\": \"" + CommandDispatcher.EscapeJsonString(ex.Message) + "\"}";
            }
        }

        private List<string> TryGetDirectCallers(string target)
        {
            if (string.IsNullOrEmpty(target) || _indexCacheService == null) return null;
            try
            {
                var index = _indexCacheService.GetIndex();
                if (index == null || index.Objects == null) return null;

                SearchIndex.IndexEntry entry = null;
                if (target.Contains(":")) index.Objects.TryGetValue(target, out entry);
                if (entry == null)
                {
                    var key = index.Objects.Keys.FirstOrDefault(k => k.EndsWith(":" + target, StringComparison.OrdinalIgnoreCase));
                    if (key != null) index.Objects.TryGetValue(key, out entry);
                }
                if (entry == null || entry.CalledBy == null || entry.CalledBy.Count == 0) return null;
                return entry.CalledBy.Distinct(StringComparer.OrdinalIgnoreCase).Take(50).ToList();
            }
            catch { return null; }
        }

        private void RunBuild(BuildTaskStatus status, string action, List<string> targets)
        {
            string tempFile = null;
            try
            {
                if (_kbService != null)
                {
                    int waits = 0;
                    while (_kbService.IsInitializing && waits < 15) { System.Threading.Thread.Sleep(1000); waits++; }
                }

                string kbPath = GetKBPath();
                if (string.IsNullOrEmpty(kbPath))
                {
                    SetFailure(status, "KB Path not found in Environment (GX_KB_PATH)");
                    return;
                }

                tempFile = Path.Combine(Path.GetTempPath(), "GxBuild_" + Guid.NewGuid().ToString().Substring(0, 8) + ".msbuild");
                string importPath = SecurityElement.Escape(Path.Combine(_gxDir, "Genexus.Tasks.targets"));
                string kbPathEsc = SecurityElement.Escape(kbPath);

                var sb = new StringBuilder();
                sb.AppendLine("<Project DefaultTargets=\"Execute\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">");
                sb.AppendLine("  <Import Project=\"" + importPath + "\" />");
                sb.AppendLine("  <Target Name=\"Execute\">");
                sb.AppendLine("    <OpenKnowledgeBase Directory=\"" + kbPathEsc + "\" />");

                if (action.Equals("Build", StringComparison.OrdinalIgnoreCase) && targets != null && targets.Count > 0)
                {
                    status.TargetsTotal = targets.Count;
                    status.TargetsDone = 0;
                    // Multiple BuildOne tasks within the same KB-open cycle (mimics IDE "Build With These Only").
                    // ForceRebuild=true is required: without it GeneXus skips regenerating the .cs files when
                    // it thinks "nothing changed", which leaves callers compiled against stale signatures and
                    // produces phantom CS1501 / CS0246 errors. The IDE's "Build With These Only" uses force=true.
                    foreach (var t in targets)
                    {
                        string esc = SecurityElement.Escape(t);
                        sb.AppendLine("    <BuildOne BuildCalled=\"false\" ObjectName=\"" + esc + "\" ForceRebuild=\"true\" />");
                    }
                }
                else if (action.Equals("RebuildAll", StringComparison.OrdinalIgnoreCase))
                    sb.AppendLine("    <RebuildAll />");
                else if (action.Equals("Reorg", StringComparison.OrdinalIgnoreCase))
                    sb.AppendLine("    <CheckAndInstallDatabase />");
                else if (action.Equals("Validate", StringComparison.OrdinalIgnoreCase) || action.Equals("Check", StringComparison.OrdinalIgnoreCase))
                    sb.AppendLine("    <CheckKnowledgeBase />");
                else if (action.Equals("Sync", StringComparison.OrdinalIgnoreCase))
                    sb.AppendLine("    <BuildAll />");
                else
                    sb.AppendLine("    <BuildAll />");

                sb.AppendLine("    <CloseKnowledgeBase />");
                sb.AppendLine("  </Target></Project>");
                File.WriteAllText(tempFile, sb.ToString());

                // Use /v:n (normal) so we get per-object progress lines, not /v:q
                var psi = new ProcessStartInfo
                {
                    FileName = _msbuildPath,
                    Arguments = "/nologo /m /v:n /nodeReuse:false /target:Execute \"" + tempFile + "\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = _gxDir,
                    // MSBuild on Windows writes via the system OEM/ANSI code page (PT-BR usually
                    // 850/1252). Reading the streams as UTF-8 mangles accented characters
                    // ("Compila��o", "n�", etc.) which is unreadable for LLM consumers. Pin
                    // both streams to the console's actual output encoding so TailLines/Output
                    // stay legible. Fall back to UTF-8 only when CodePagesEncodingProvider
                    // isn't registered.
                    StandardOutputEncoding = ResolveMsbuildEncoding(),
                    StandardErrorEncoding = ResolveMsbuildEncoding()
                };

                using (var process = new Process { StartInfo = psi, EnableRaisingEvents = true })
                {
                    status.Process = process;
                    process.OutputDataReceived += (s, e) => HandleLine(status, e.Data, false);
                    process.ErrorDataReceived  += (s, e) => HandleLine(status, e.Data, true);

                    process.Start();
                    status.Phase = "OpeningKB";
                    EmitPhaseProgress(status.Phase);
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    process.WaitForExit();

                    status.ExitCode = process.ExitCode;
                    status.EndTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    status.ElapsedSeconds = Math.Round((DateTime.UtcNow - status.StartedAt).TotalSeconds, 1);
                    status.Output = status.FullOutput.ToString();
                    status.Phase = "Done";
                    EmitPhaseProgress(status.Phase);

                    if (process.ExitCode == 0 && status.ErrorCount == 0)
                        status.Status = "Succeeded";
                    else
                        status.Status = "Failed";

                    Logger.Info("Background Build " + status.TaskId + " " + status.Status +
                                " (errors=" + status.ErrorCount + ", warnings=" + status.WarningCount +
                                ", " + status.ElapsedSeconds + "s)");
                }
            }
            catch (Exception ex)
            {
                SetFailure(status, ex.Message);
                Logger.Error("Background Build " + status.TaskId + " EXCEPTION: " + ex.Message);
            }
            finally
            {
                try { if (tempFile != null && File.Exists(tempFile)) File.Delete(tempFile); } catch { }
                status.Process = null;
            }
        }

        private void HandleLine(BuildTaskStatus status, string line, bool isError)
        {
            if (line == null) return;
            lock (status._lock)
            {
                status.LineCount++;
                status.LastLine = line;
                status.FullOutput.AppendLine(line);

                // FR#12: keep noise out of TailLines but always preserve it in FullOutput.
                // Errors/warnings/phase-change lines never count as noise.
                bool isNoise = _rxModuleCopyNoise.IsMatch(line) &&
                               !_rxError.IsMatch(line) &&
                               !_rxWarning.IsMatch(line);
                if (!isNoise)
                {
                    status.TailLines.Add(line);
                    if (status.TailLines.Count > TailBufferSize) status.TailLines.RemoveAt(0);
                }

                if (_rxOpenKb.IsMatch(line))      { status.Phase = "OpeningKB"; EmitPhaseProgress(status.Phase); return; }
                var m = _rxSpecifying.Match(line); if (m.Success) { status.Phase = "Specifying"; EmitPhaseProgress(status.Phase); status.CurrentObject = m.Groups["obj"].Value.Trim(); return; }
                m = _rxGenerating.Match(line);     if (m.Success) { status.Phase = "Generating"; EmitPhaseProgress(status.Phase); status.CurrentObject = m.Groups["obj"].Value.Trim(); return; }
                if (_rxCompiling.IsMatch(line))   { status.Phase = "Compiling"; EmitPhaseProgress(status.Phase); return; }
                if (_rxBuildDone.IsMatch(line))   { status.Phase = "Finishing"; EmitPhaseProgress(status.Phase); }
                if (_rxBuildOneEnd.IsMatch(line) && status.TargetsTotal.HasValue)
                {
                    status.TargetsDone = (status.TargetsDone ?? 0) + 1;
                }

                if (_rxError.IsMatch(line))
                {
                    status.ErrorCount++;
                    if (status.Errors.Count < 50) status.Errors.Add(line.Trim());
                }
                else if (_rxWarning.IsMatch(line))
                {
                    status.WarningCount++;
                    if (status.Warnings.Count < 50) status.Warnings.Add(line.Trim());
                }
            }
        }

        private void SetFailure(BuildTaskStatus status, string error)
        {
            status.Status = "Error";
            status.Phase = "Done";
            EmitPhaseProgress(status.Phase);
            status.Error = error;
            status.EndTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            status.ElapsedSeconds = Math.Round((DateTime.UtcNow - status.StartedAt).TotalSeconds, 1);
        }

        public string GetKBPath()
        {
            return Environment.GetEnvironmentVariable("GX_KB_PATH") ?? "";
        }
    }
}
