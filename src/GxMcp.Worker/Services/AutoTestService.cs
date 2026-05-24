using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GxMcp.Worker.Services
{
    /// <summary>
    /// Item 95 — `genexus_auto_test action=generate_from_prod_log path=&lt;file&gt;`.
    /// Parses a JSONL log (one `{atUtc, tool, target, params}` object per line)
    /// and emits GXtest-shaped stubs for each unique (tool × target). NO writes
    /// to the KB — caller decides what to do with the generated source.
    /// </summary>
    public class AutoTestService
    {
        public string Generate(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return Err("path is required.");
            if (!File.Exists(path))
                return Err("path does not exist: " + path);

            var stubs = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            var skipped = new JArray();
            int lineNum = 0;
            try
            {
                foreach (var raw in File.ReadAllLines(path))
                {
                    lineNum++;
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    JObject entry;
                    try { entry = JObject.Parse(raw); }
                    catch
                    {
                        skipped.Add(new JObject { ["line"] = lineNum, ["reason"] = "malformed-json" });
                        continue;
                    }
                    string tool = entry["tool"]?.ToString();
                    string target = entry["target"]?.ToString() ?? entry["params"]?["target"]?.ToString() ?? entry["params"]?["name"]?.ToString();
                    if (string.IsNullOrEmpty(tool) || string.IsNullOrEmpty(target))
                    {
                        skipped.Add(new JObject { ["line"] = lineNum, ["reason"] = "missing-tool-or-target" });
                        continue;
                    }
                    string key = tool + "::" + target;
                    if (!stubs.ContainsKey(key))
                    {
                        stubs[key] = new JObject
                        {
                            ["name"] = SanitizeIdent(tool + "_" + target + "_Test"),
                            ["sourceTool"] = tool,
                            ["sourceTarget"] = target,
                            ["source"] = BuildGxTestStub(tool, target, entry["params"] as JObject)
                        };
                    }
                }
            }
            catch (Exception ex) { return Err("Failed to read log: " + ex.Message); }

            var arr = new JArray();
            foreach (var kv in stubs) arr.Add(kv.Value);
            return new JObject
            {
                ["status"] = "Success",
                ["linesRead"] = lineNum,
                ["stubsGenerated"] = arr,
                ["skipped"] = skipped
            }.ToString(Formatting.None);
        }

        internal static string BuildGxTestStub(string tool, string target, JObject parms)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("// GXtest stub — generated from production log");
            sb.AppendLine("// Source tool: " + tool);
            sb.AppendLine("// Source target: " + target);
            sb.AppendLine();
            sb.AppendLine("&result = " + target + ".Call(");
            if (parms != null)
            {
                bool first = true;
                foreach (var p in parms.Properties())
                {
                    if (!first) sb.Append(", ");
                    sb.Append(p.Value?.ToString(Formatting.None) ?? "''");
                    first = false;
                }
            }
            sb.AppendLine(")");
            sb.AppendLine("// TODO: replace assertion below with real expected value");
            sb.AppendLine("Assert.True(not &result.IsEmpty(), '" + target + " returned empty')");
            return sb.ToString();
        }

        private static string SanitizeIdent(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var c in s ?? string.Empty)
            {
                if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
                else sb.Append('_');
            }
            return sb.ToString();
        }

        private static string Err(string m) => new JObject { ["status"] = "Error", ["message"] = m }.ToString(Formatting.None);
    }
}
