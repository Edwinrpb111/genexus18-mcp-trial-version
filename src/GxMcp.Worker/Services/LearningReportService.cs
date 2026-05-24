using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace GxMcp.Worker.Services
{
    /// <summary>
    /// Item 76 — genexus_learning action=report. Aggregates the local friction
    /// journal (<c>&lt;kbPath&gt;/.gx/friction.jsonl</c>) into a structured
    /// summary: top friction tools, most-common error codes, recent severity
    /// distribution. Read-only over the JSONL written by genexus_friction_log.
    /// </summary>
    public class LearningReportService
    {
        private readonly KbService _kbService;

        public LearningReportService(KbService kbService)
        {
            _kbService = kbService;
        }

        public string Report(string sinceIso = null, string untilIso = null, string kbPathOverride = null)
        {
            string kbPath = ResolveKbPath(kbPathOverride);
            if (string.IsNullOrEmpty(kbPath))
            {
                return Error("NoKbOpen", "No KB is currently open.");
            }
            return ReportCore(kbPath, sinceIso, untilIso);
        }

        public static string ReportCore(string kbPath, string sinceIso, string untilIso)
        {
            try
            {
                string filePath = Path.Combine(kbPath, ".gx", "friction.jsonl");
                if (!File.Exists(filePath))
                {
                    return new JObject
                    {
                        ["status"] = "Success",
                        ["path"] = filePath,
                        ["totalEntries"] = 0,
                        ["byTool"] = new JArray(),
                        ["byCode"] = new JArray(),
                        ["severityHistogram"] = new JObject(),
                        ["since"] = sinceIso ?? "",
                        ["until"] = untilIso ?? "",
                        ["note"] = "No friction.jsonl file — nothing to report yet."
                    }.ToString(Newtonsoft.Json.Formatting.None);
                }

                DateTime? since = ParseIso(sinceIso);
                DateTime? until = ParseIso(untilIso);

                var byTool = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var byCode = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var sevHisto = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                int total = 0;

                foreach (var line in File.ReadAllLines(filePath))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    JObject entry;
                    try { entry = JObject.Parse(line); }
                    catch { continue; }

                    if (since.HasValue || until.HasValue)
                    {
                        DateTime? at = ParseIso(entry["atUtc"]?.ToString());
                        if (!at.HasValue) continue;
                        if (since.HasValue && at.Value < since.Value) continue;
                        if (until.HasValue && at.Value > until.Value) continue;
                    }

                    total++;
                    string tool = entry["tool"]?.ToString();
                    if (!string.IsNullOrEmpty(tool))
                    {
                        byTool.TryGetValue(tool, out int n);
                        byTool[tool] = n + 1;
                    }

                    string code = ExtractCode(entry["message"]?.ToString());
                    if (!string.IsNullOrEmpty(code))
                    {
                        byCode.TryGetValue(code, out int n);
                        byCode[code] = n + 1;
                    }

                    string sev = entry["severity"]?.ToString() ?? "info";
                    sevHisto.TryGetValue(sev, out int sn);
                    sevHisto[sev] = sn + 1;
                }

                var byToolArr = new JArray(byTool
                    .OrderByDescending(kv => kv.Value)
                    .Take(20)
                    .Select(kv => new JObject { ["tool"] = kv.Key, ["count"] = kv.Value }));
                var byCodeArr = new JArray(byCode
                    .OrderByDescending(kv => kv.Value)
                    .Take(20)
                    .Select(kv => new JObject { ["code"] = kv.Key, ["count"] = kv.Value }));
                var sevObj = new JObject();
                foreach (var kv in sevHisto.OrderByDescending(kv => kv.Value))
                    sevObj[kv.Key] = kv.Value;

                return new JObject
                {
                    ["status"] = "Success",
                    ["path"] = filePath,
                    ["totalEntries"] = total,
                    ["byTool"] = byToolArr,
                    ["byCode"] = byCodeArr,
                    ["severityHistogram"] = sevObj,
                    ["since"] = sinceIso ?? "",
                    ["until"] = untilIso ?? ""
                }.ToString(Newtonsoft.Json.Formatting.None);
            }
            catch (Exception ex)
            {
                return Error("ReportFailed", ex.Message);
            }
        }

        // Pull an UPPER_SNAKE-ish error code out of a message like "Edit failed
        // (PatchNoMatch): unable to apply". Cheap heuristic: parenthesised
        // CamelCase / SCREAMING_SNAKE, else first CamelCase token, else null.
        private static string ExtractCode(string message)
        {
            if (string.IsNullOrEmpty(message)) return null;
            var paren = System.Text.RegularExpressions.Regex.Match(message, @"\(([A-Z][A-Za-z0-9_]{2,})\)");
            if (paren.Success) return paren.Groups[1].Value;
            var camel = System.Text.RegularExpressions.Regex.Match(message, @"\b([A-Z][a-z]+(?:[A-Z][a-z0-9]+){1,})\b");
            if (camel.Success) return camel.Groups[1].Value;
            return null;
        }

        private static DateTime? ParseIso(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var dt))
                return dt;
            return null;
        }

        private string ResolveKbPath(string kbPathOverride)
        {
            if (!string.IsNullOrEmpty(kbPathOverride)) return kbPathOverride;
            try { return _kbService?.GetKbPath(); } catch { return null; }
        }

        private static string Error(string code, string message) =>
            new JObject
            {
                ["status"] = "Error",
                ["code"] = code,
                ["message"] = message
            }.ToString(Newtonsoft.Json.Formatting.None);
    }
}
