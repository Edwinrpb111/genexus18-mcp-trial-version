using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Artech.Architecture.Common.Objects;
using Artech.Architecture.Common.Services;
using Newtonsoft.Json.Linq;
using SdkServices = Artech.Architecture.Common.Services.Services;

namespace GxMcp.Worker.Services
{
    /// <summary>
    /// GxServer (Team Development) sync-state surface for genexus_gxserver.
    ///
    /// Primary path is the GeneXus SDK: it resolves
    /// <c>ITeamDevClientService</c> (the model-level Team Development service)
    /// and answers connection / pending / conflict queries from the live KB —
    /// the same source the IDE's Team Development tab reads. This replaces the
    /// old filesystem-heuristic that produced false "not connected" results on
    /// KBs that ARE linked to a GeneXus Server (the link lives in the KB
    /// metadata DB, not in well-known files on disk).
    ///
    /// If the Team Development service isn't loaded in the worker session, or
    /// the SDK throws, it falls back to the legacy file-probe envelopes so the
    /// caller still gets a stable (if coarser) answer. All read-only — no
    /// update/commit is executed here.
    /// </summary>
    public class GxServerSyncService
    {
        private readonly KbService _kb;

        public GxServerSyncService(KbService kb)
        {
            _kb = kb;
        }

        public string Run(JObject args)
        {
            string action = args?["action"]?.ToString();
            if (string.IsNullOrWhiteSpace(action)) action = "status";
            action = action.Trim().ToLowerInvariant();

            string kbPath = _kb?.GetKbPath();
            string kbAlias = Environment.GetEnvironmentVariable("GX_KB_ALIAS")
                             ?? (string.IsNullOrEmpty(kbPath) ? null : Path.GetFileName(kbPath.TrimEnd('\\', '/')));

            switch (action)
            {
                case "status":
                case "pending":
                case "conflicts":
                case "history":
                    break;
                default:
                    return new JObject
                    {
                        ["status"] = "Error",
                        ["code"] = "BadAction",
                        ["message"] = "Unknown action '" + action + "'. Expected one of: status, pending, conflicts, history."
                    }.ToString(Newtonsoft.Json.Formatting.None);
            }

            // Primary: SDK-backed answer from the live KB. Returns null when the
            // Team Development service is unavailable or any SDK call throws, in
            // which case we drop to the legacy file-heuristic below.
            int limit = args?["limit"]?.ToObject<int?>() ?? 10;
            string sdk = TrySdkEnvelope(action, kbAlias, limit);
            if (sdk != null) return sdk;

            switch (action)
            {
                case "status": return StatusEnvelope(kbPath, kbAlias);
                case "pending": return PendingEnvelope(kbPath);
                case "conflicts": return ConflictsEnvelope(kbPath);
                default: return HistoryEnvelope(kbPath, limit);
            }
        }

        // ----- SDK-backed path (authoritative) -----

        /// <summary>
        /// Builds the response from <see cref="ITeamDevClientService"/> against
        /// the open KB. Returns null (caller falls back to the file-heuristic)
        /// when the service isn't registered in this worker or the SDK throws.
        /// </summary>
        private string TrySdkEnvelope(string action, string kbAlias, int limit)
        {
            KnowledgeBase kb;
            ITeamDevClientService svc;
            try
            {
                kb = _kb?.GetKB() as KnowledgeBase;
                if (kb == null) return null;
                svc = SdkServices.TryGetService<ITeamDevClientService>();
                if (svc == null) return null;
            }
            catch { return null; }

            try
            {
                bool linked = svc.IsLinkedKB(kb);
                var model = kb.DesignModel;

                switch (action)
                {
                    case "status":
                    {
                        var jo = new JObject
                        {
                            ["status"] = "Success",
                            ["connected"] = linked,
                            ["kbAlias"] = kbAlias ?? string.Empty,
                            ["source"] = "sdk:ITeamDevClientService"
                        };
                        if (!linked)
                        {
                            jo["hint"] = "This KB is not linked to a GeneXus Server instance.";
                            return jo.ToString(Newtonsoft.Json.Formatting.None);
                        }
                        jo["serverUrl"] = SafeStr(() => svc.GetServerUrl(kb));
                        jo["host"] = SafeStr(() => svc.GetGXserverHost(kb));
                        jo["remoteKbName"] = SafeStr(() => svc.GetRemoteKBName(kb));
                        jo["remoteVersionName"] = SafeStr(() => svc.RemoteVersionName(model));
                        return jo.ToString(Newtonsoft.Json.Formatting.None);
                    }

                    case "pending":
                    {
                        if (!linked) return NotLinked();
                        var objects = new JArray();
                        foreach (var h in EnumLocalChanges(svc, model))
                        {
                            objects.Add(new JObject
                            {
                                ["name"] = SafeStr(() => (string)h.ObjectName) ?? SafeStr(() => h.GetName()),
                                ["operation"] = SafeStr(() => h.Operation.ToString()),
                                ["lastChange"] = SafeStr(() => h.LastChange.ToUniversalTime().ToString("o")),
                                ["user"] = SafeStr(() => (string)h.Username)
                            });
                        }
                        return new JObject
                        {
                            ["status"] = "Success",
                            ["connected"] = true,
                            ["count"] = objects.Count,
                            ["objects"] = objects,
                            ["source"] = "sdk:ITeamDevClientService"
                        }.ToString(Newtonsoft.Json.Formatting.None);
                    }

                    case "conflicts":
                    {
                        if (!linked) return NotLinked();
                        var conflicts = new JArray();
                        foreach (var ct in new[] { UpdateConflict.YesMustOverwrite, UpdateConflict.YesWithAutoMerge })
                        {
                            foreach (var e in EnumConflicts(svc, model, ct))
                            {
                                conflicts.Add(new JObject
                                {
                                    ["object"] = SafeStr(() => e.ToString()),
                                    ["conflictType"] = ct.ToString()
                                });
                            }
                        }
                        return new JObject
                        {
                            ["status"] = "Success",
                            ["connected"] = true,
                            ["count"] = conflicts.Count,
                            ["conflicts"] = conflicts,
                            ["source"] = "sdk:ITeamDevClientService"
                        }.ToString(Newtonsoft.Json.Formatting.None);
                    }

                    default: // history — local change log (most-recent first). Remote
                             // revision history requires server credentials, which this
                             // read-only surface does not collect.
                    {
                        if (!linked) return NotLinked();
                        if (limit <= 0) limit = 10;
                        if (limit > 200) limit = 200;
                        var rows = new System.Collections.Generic.List<JObject>();
                        foreach (var h in EnumLocalChanges(svc, model))
                        {
                            rows.Add(new JObject
                            {
                                ["name"] = SafeStr(() => (string)h.ObjectName) ?? SafeStr(() => h.GetName()),
                                ["operation"] = SafeStr(() => h.Operation.ToString()),
                                ["lastChange"] = SafeStr(() => h.LastChange.ToUniversalTime().ToString("o")),
                                ["user"] = SafeStr(() => (string)h.Username)
                            });
                        }
                        rows.Sort((a, b) => string.CompareOrdinal((string)b["lastChange"], (string)a["lastChange"]));
                        var history = new JArray();
                        for (int i = 0; i < rows.Count && i < limit; i++) history.Add(rows[i]);
                        return new JObject
                        {
                            ["status"] = "Success",
                            ["connected"] = true,
                            ["limit"] = limit,
                            ["history"] = history,
                            ["scope"] = "localChanges",
                            ["note"] = "Local (uncommitted) change log. Remote revision history requires server credentials.",
                            ["source"] = "sdk:ITeamDevClientService"
                        }.ToString(Newtonsoft.Json.Formatting.None);
                    }
                }
            }
            catch { return null; }
        }

        private static string NotLinked()
        {
            return new JObject
            {
                ["status"] = "Success",
                ["connected"] = false,
                ["hint"] = "This KB is not linked to a GeneXus Server instance.",
                ["source"] = "sdk:ITeamDevClientService"
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static IEnumerable<dynamic> EnumLocalChanges(ITeamDevClientService svc, KBModel model)
        {
            IEnumerable raw = svc.GetLocalChanges(model);
            if (raw == null) yield break;
            foreach (var h in raw) yield return h;
        }

        private static IEnumerable<dynamic> EnumConflicts(ITeamDevClientService svc, KBModel model, UpdateConflict ct)
        {
            IEnumerable raw = svc.GetConflictEntities(model, ct);
            if (raw == null) yield break;
            foreach (var e in raw) yield return e;
        }

        private static string SafeStr(Func<string> f)
        {
            try { return f(); } catch { return null; }
        }

        // ----- legacy file-heuristic fallback (also exercised by unit tests) -----

        internal class DetectionResult
        {
            public bool Connected;
            public string DetectedPath;
        }

        internal static DetectionResult Detect(string kbPath)
        {
            var r = new DetectionResult();
            if (string.IsNullOrEmpty(kbPath) || !Directory.Exists(kbPath)) return r;

            string p1 = Path.Combine(kbPath, "Repository", "Repository.gxs");
            if (File.Exists(p1)) { r.Connected = true; r.DetectedPath = p1; return r; }

            string p2 = Path.Combine(kbPath, ".gx", "gxserver-state.xml");
            if (File.Exists(p2)) { r.Connected = true; r.DetectedPath = p2; return r; }

            string p3 = Path.Combine(kbPath, ".gxserver", "state.xml");
            if (File.Exists(p3)) { r.Connected = true; r.DetectedPath = p3; return r; }

            return r;
        }

        internal static string StatusEnvelope(string kbPath, string kbAlias)
        {
            var det = Detect(kbPath);
            var jo = new JObject
            {
                ["status"] = "Success",
                ["connected"] = det.Connected,
                ["kbAlias"] = kbAlias ?? string.Empty
            };
            if (!det.Connected)
            {
                jo["hint"] = "This KB is not connected to a GeneXus Server instance.";
                return jo.ToString(Newtonsoft.Json.Formatting.None);
            }
            jo["note"] = "metadata parsing pending — connection detected via " + det.DetectedPath;
            jo["detectedVia"] = det.DetectedPath;
            return jo.ToString(Newtonsoft.Json.Formatting.None);
        }

        internal static string PendingEnvelope(string kbPath)
        {
            var det = Detect(kbPath);
            if (!det.Connected)
            {
                return new JObject
                {
                    ["status"] = "Success",
                    ["connected"] = false,
                    ["hint"] = "This KB is not connected to a GeneXus Server instance."
                }.ToString(Newtonsoft.Json.Formatting.None);
            }
            return new JObject
            {
                ["status"] = "Success",
                ["connected"] = true,
                ["objects"] = new JArray(),
                ["note"] = "metadata parsing pending — connection detected via " + det.DetectedPath
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        internal static string ConflictsEnvelope(string kbPath)
        {
            var det = Detect(kbPath);
            if (!det.Connected)
            {
                return new JObject
                {
                    ["status"] = "Success",
                    ["connected"] = false,
                    ["hint"] = "This KB is not connected to a GeneXus Server instance."
                }.ToString(Newtonsoft.Json.Formatting.None);
            }
            return new JObject
            {
                ["status"] = "Success",
                ["connected"] = true,
                ["conflicts"] = new JArray(),
                ["note"] = "metadata parsing pending — connection detected via " + det.DetectedPath
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        internal static string HistoryEnvelope(string kbPath, int limit)
        {
            var det = Detect(kbPath);
            if (!det.Connected)
            {
                return new JObject
                {
                    ["status"] = "Success",
                    ["connected"] = false,
                    ["hint"] = "This KB is not connected to a GeneXus Server instance."
                }.ToString(Newtonsoft.Json.Formatting.None);
            }
            if (limit <= 0) limit = 10;
            if (limit > 200) limit = 200;
            return new JObject
            {
                ["status"] = "Success",
                ["connected"] = true,
                ["history"] = new JArray(),
                ["limit"] = limit,
                ["note"] = "metadata parsing pending — connection detected via " + det.DetectedPath
            }.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
