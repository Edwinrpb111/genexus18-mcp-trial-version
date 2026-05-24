using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GxMcp.Worker.Services
{
    /// <summary>
    /// Health / triage probe. Returns one envelope covering version, GX install,
    /// KB state, worker process stats, cache freshness, recent telemetry, warnings.
    /// Designed for "MCP is acting weird → call this and paste the output".
    ///
    /// Doctor must NEVER throw — any sub-block exception nulls that block and adds
    /// a warning. The telemetry block is best-effort on the worker side (the live
    /// OperationTracker lives in the Gateway; when a tracker handle is passed in
    /// — currently null — we reflect into BuildMetricsPayload to fill the block).
    /// </summary>
    public class DoctorService
    {
        private static readonly DateTime _processStartUtc = DateTime.UtcNow;

        private readonly KbService _kbService;
        private readonly IndexCacheService _indexCacheService;
        private readonly object _operationTracker; // Gateway-owned; loosely typed here.

        public DoctorService(KbService kbService, IndexCacheService indexCacheService, object operationTracker = null)
        {
            _kbService = kbService;
            _indexCacheService = indexCacheService;
            _operationTracker = operationTracker;
        }

        public string Diagnose()
        {
            var envelope = new JObject
            {
                ["status"] = "Success",
                ["checkedAt"] = DateTime.UtcNow.ToString("o")
            };
            var warnings = new List<string>();

            envelope["version"] = SafeBlock(BuildVersionBlock, warnings, "version");
            JObject gx = SafeBlock(BuildGxBlock, warnings, "geneXus");
            envelope["geneXus"] = gx;
            JObject kb = SafeBlock(BuildKbBlock, warnings, "kb");
            envelope["kb"] = kb;
            JObject worker = SafeBlock(BuildWorkerBlock, warnings, "worker");
            envelope["worker"] = worker;
            JObject cache = SafeBlock(BuildCacheBlock, warnings, "cache");
            envelope["cache"] = cache;
            JObject telemetry = SafeBlock(BuildTelemetryBlock, warnings, "telemetry");
            envelope["telemetry"] = telemetry;

            // ---- Warnings ----------------------------------------------------
            try
            {
                if (gx != null && gx["found"] != null && gx["found"].ToObject<bool>() == false)
                    warnings.Add("CRITICAL: GeneXus SDK install not found.");

                if (kb != null && kb["opened"] != null && kb["opened"].ToObject<bool>() == false)
                    warnings.Add("Call genexus_open_kb first.");

                if (cache != null && cache["ageHours"] != null && cache["ageHours"].Type != JTokenType.Null)
                {
                    double ageHours = cache["ageHours"].ToObject<double>();
                    if (ageHours > 24.0)
                        warnings.Add("Index cache stale; consider force-rebuild.");
                }

                if (worker != null && worker["memoryMb"] != null && worker["memoryMb"].ToObject<int>() > 1024)
                    warnings.Add("Worker memory high; consider genexus_worker_reload.");

                if (telemetry != null && telemetry["errorRate"] != null && telemetry["errorRate"].Type != JTokenType.Null)
                {
                    double er = telemetry["errorRate"].ToObject<double>();
                    if (er > 0.20)
                        warnings.Add("Tool error rate elevated.");
                }
            }
            catch (Exception ex)
            {
                warnings.Add("warning-computation failed: " + ex.Message);
            }

            envelope["warnings"] = new JArray(warnings.Cast<object>().ToArray());
            envelope["hint"] = warnings.Count > 0
                ? (JToken)new JValue(warnings[0])
                : JValue.CreateNull();

            return envelope.ToString(Formatting.None);
        }

        // -------------------------------------------------------------------
        // Sub-blocks. Each is wrapped by SafeBlock — failures null the block
        // and append a warning. Doctor itself never throws.
        // -------------------------------------------------------------------

        private static JObject BuildVersionBlock()
        {
            string version = typeof(DoctorService).Assembly.GetName().Version?.ToString() ?? "unknown";
            var info = typeof(DoctorService).Assembly
                .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
                .OfType<AssemblyInformationalVersionAttribute>().FirstOrDefault();
            if (info != null && !string.IsNullOrWhiteSpace(info.InformationalVersion))
                version = info.InformationalVersion;
            return new JObject { ["current"] = version };
        }

        private static JObject BuildGxBlock()
        {
            string gxPath = Environment.GetEnvironmentVariable("GX_PATH");
            bool found = false;
            int sdkDllCount = 0;
            if (!string.IsNullOrEmpty(gxPath) && Directory.Exists(gxPath))
            {
                try
                {
                    sdkDllCount = Directory.EnumerateFiles(gxPath, "Artech.*.dll", SearchOption.TopDirectoryOnly).Count();
                    found = sdkDllCount > 0;
                }
                catch { /* fall through */ }
            }
            return new JObject
            {
                ["installationPath"] = string.IsNullOrEmpty(gxPath) ? (JToken)JValue.CreateNull() : new JValue(gxPath),
                ["found"] = found,
                ["sdkDllCount"] = sdkDllCount
            };
        }

        private JObject BuildKbBlock()
        {
            if (_kbService == null)
            {
                return new JObject
                {
                    ["alias"] = JValue.CreateNull(),
                    ["path"] = JValue.CreateNull(),
                    ["opened"] = false,
                    ["objectCount"] = 0
                };
            }

            bool opened = false;
            try { opened = _kbService.IsOpen; } catch { /* ignore */ }

            string path = null;
            try { path = _kbService.GetKbPath(); } catch { /* ignore */ }

            string alias = null;
            if (!string.IsNullOrEmpty(path))
            {
                try { alias = new DirectoryInfo(path).Name; } catch { /* ignore */ }
            }

            int objectCount = 0;
            if (_indexCacheService != null)
            {
                try
                {
                    var idx = _indexCacheService.GetIndex();
                    if (idx != null && idx.Objects != null)
                        objectCount = idx.Objects.Count;
                }
                catch { /* ignore */ }
            }

            return new JObject
            {
                ["alias"] = string.IsNullOrEmpty(alias) ? (JToken)JValue.CreateNull() : new JValue(alias),
                ["path"] = string.IsNullOrEmpty(path) ? (JToken)JValue.CreateNull() : new JValue(path),
                ["opened"] = opened,
                ["objectCount"] = objectCount
            };
        }

        private static JObject BuildWorkerBlock()
        {
            using (var proc = Process.GetCurrentProcess())
            {
                int pid = proc.Id;
                int uptimeSec = (int)Math.Max(0, (DateTime.UtcNow - _processStartUtc).TotalSeconds);
                int memoryMb = (int)(proc.WorkingSet64 / (1024 * 1024));
                int threadCount = proc.Threads.Count;
                return new JObject
                {
                    ["pid"] = pid,
                    ["uptimeSec"] = uptimeSec,
                    ["memoryMb"] = memoryMb,
                    ["threadCount"] = threadCount
                };
            }
        }

        private JObject BuildCacheBlock()
        {
            int entries = 0;
            double? ageHours = null;
            if (_indexCacheService != null)
            {
                try
                {
                    var idx = _indexCacheService.GetIndex();
                    if (idx != null && idx.Objects != null)
                        entries = idx.Objects.Count;
                }
                catch { /* ignore */ }

                try
                {
                    string kbPath = _kbService != null ? _kbService.GetKbPath() : null;
                    if (!string.IsNullOrEmpty(kbPath))
                    {
                        string gxDir = Path.Combine(kbPath, ".gx");
                        if (Directory.Exists(gxDir))
                        {
                            DateTime? newest = null;
                            foreach (var f in Directory.EnumerateFiles(gxDir, "search-index*", SearchOption.AllDirectories))
                            {
                                DateTime t = File.GetLastWriteTimeUtc(f);
                                if (newest == null || t > newest.Value) newest = t;
                            }
                            if (newest.HasValue)
                                ageHours = Math.Round((DateTime.UtcNow - newest.Value).TotalHours, 2);
                        }
                    }
                }
                catch { /* ignore */ }
            }
            return new JObject
            {
                ["indexEntries"] = entries,
                ["ageHours"] = ageHours.HasValue ? (JToken)new JValue(ageHours.Value) : JValue.CreateNull()
            };
        }

        private JObject BuildTelemetryBlock()
        {
            JObject result = new JObject
            {
                ["totalToolCalls"] = 0,
                ["errorRate"] = 0.0,
                ["slowestTools"] = new JArray(),
                ["mostCalled"] = new JArray()
            };

            if (_operationTracker == null) return result;

            // Reflection-driven optional integration. Gateway's OperationTracker
            // exposes `BuildMetricsPayload` returning `{tools:[{toolName,count,errors,p95Ms,...}]}`.
            try
            {
                var mi = _operationTracker.GetType().GetMethod("BuildMetricsPayload", BindingFlags.Public | BindingFlags.Instance);
                if (mi == null) return result;
                var payload = mi.Invoke(_operationTracker, null) as JObject;
                if (payload == null) return result;
                var tools = payload["tools"] as JArray;
                if (tools == null) return result;

                long totalCalls = 0, totalErrors = 0;
                var slowest = new List<Tuple<string, long, long>>();
                var mostCalled = new List<Tuple<string, long>>();
                foreach (var t in tools.OfType<JObject>())
                {
                    long count = t["count"] != null ? t["count"].ToObject<long?>() ?? 0 : 0;
                    long errors = t["errors"] != null ? t["errors"].ToObject<long?>() ?? 0 : 0;
                    long p95 = t["p95Ms"] != null ? t["p95Ms"].ToObject<long?>() ?? 0 : 0;
                    string name = t["toolName"] != null ? t["toolName"].ToString() : "(unknown)";
                    totalCalls += count;
                    totalErrors += errors;
                    if (count > 0)
                    {
                        slowest.Add(Tuple.Create(name, p95, count));
                        mostCalled.Add(Tuple.Create(name, count));
                    }
                }

                var slowestArr = new JArray();
                foreach (var s in slowest.OrderByDescending(x => x.Item2).Take(5))
                    slowestArr.Add(new JObject { ["name"] = s.Item1, ["p95Ms"] = s.Item2, ["calls"] = s.Item3 });
                var mostArr = new JArray();
                foreach (var m in mostCalled.OrderByDescending(x => x.Item2).Take(5))
                    mostArr.Add(new JObject { ["name"] = m.Item1, ["calls"] = m.Item2 });

                result["totalToolCalls"] = totalCalls;
                result["errorRate"] = totalCalls > 0
                    ? Math.Round((double)totalErrors / totalCalls, 3)
                    : 0.0;
                result["slowestTools"] = slowestArr;
                result["mostCalled"] = mostArr;
            }
            catch { /* swallow — telemetry is best-effort */ }
            return result;
        }

        private static JObject SafeBlock(Func<JObject> build, List<string> warnings, string blockName)
        {
            try { return build() ?? new JObject(); }
            catch (Exception ex)
            {
                warnings.Add(blockName + " block failed: " + ex.Message);
                return null;
            }
        }
    }
}
