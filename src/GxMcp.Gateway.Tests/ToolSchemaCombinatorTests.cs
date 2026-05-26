using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GxMcp.Gateway.Tests
{
    /// <summary>
    /// Friction 2026-05-26 — Anthropic's API rejects tool input_schemas that
    /// contain <c>oneOf</c> / <c>allOf</c> / <c>anyOf</c> at the top level
    /// (HTTP 400: "input_schema does not support oneOf, allOf, or anyOf at the
    /// top level"). Nested usage at the property level *is* technically
    /// accepted, but we hit cross-MCP indexing issues (different runtimes
    /// flatten / re-emit schemas in ways that promote nested combinators to
    /// the root), so this suite bans them everywhere in our shipping schema.
    /// </summary>
    public class ToolSchemaCombinatorTests
    {
        private static readonly string[] BannedKeywords = { "oneOf", "allOf", "anyOf" };

        private static string FindToolDefinitionsJson()
        {
            string beside = Path.Combine(AppContext.BaseDirectory, "tool_definitions.json");
            if (File.Exists(beside)) return beside;
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 8; i++)
            {
                string candidate = Path.Combine(dir, "GxMcp.Gateway", "tool_definitions.json");
                if (File.Exists(candidate)) return candidate;
                candidate = Path.Combine(dir, "src", "GxMcp.Gateway", "tool_definitions.json");
                if (File.Exists(candidate)) return candidate;
                var parent = Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
            }
            throw new FileNotFoundException("Could not locate tool_definitions.json from " + AppContext.BaseDirectory);
        }

        [Fact]
        public void NoTool_HasTopLevelCombinator_InInputSchema()
        {
            var arr = JArray.Parse(File.ReadAllText(FindToolDefinitionsJson()));
            var failures = new List<string>();
            for (int i = 0; i < arr.Count; i++)
            {
                var tool = (JObject)arr[i];
                var name = tool.Value<string>("name");
                var schema = tool["inputSchema"] as JObject;
                if (schema == null) continue;
                foreach (var kw in BannedKeywords)
                {
                    if (schema.ContainsKey(kw))
                        failures.Add($"tool[{i}] '{name}': '{kw}' at the root of inputSchema");
                }
            }
            Assert.True(failures.Count == 0,
                "Anthropic API rejects top-level combinators. Offenders:\n  " + string.Join("\n  ", failures));
        }

        [Fact]
        public void NoTool_HasNestedCombinator_AnywhereInInputSchema()
        {
            // Hard ban — see class docstring. If you genuinely need a union
            // type, model it as a single property with no `type` (accepts any
            // shape) and document the alternatives in `description`. The
            // dispatcher already handles both shapes for our existing union
            // properties (genexus_edit.patch, genexus_run_object.gamSession,
            // genexus_edit_and_build.patch).
            var arr = JArray.Parse(File.ReadAllText(FindToolDefinitionsJson()));
            var failures = new List<string>();
            for (int i = 0; i < arr.Count; i++)
            {
                var tool = (JObject)arr[i];
                var name = tool.Value<string>("name");
                var schema = tool["inputSchema"] as JObject;
                if (schema == null) continue;
                WalkForCombinators(schema, $"tool[{i}] '{name}'.inputSchema", failures);
            }
            Assert.True(failures.Count == 0,
                "Combinator usage is banned in tool schemas. Offenders:\n  " + string.Join("\n  ", failures));
        }

        private static void WalkForCombinators(JToken token, string path, List<string> failures)
        {
            if (token is JObject obj)
            {
                foreach (var kw in BannedKeywords)
                {
                    if (obj.ContainsKey(kw))
                        failures.Add($"{path}.{kw}");
                }
                foreach (var prop in obj.Properties())
                {
                    WalkForCombinators(prop.Value, path + "." + prop.Name, failures);
                }
            }
            else if (token is JArray a)
            {
                for (int i = 0; i < a.Count; i++)
                    WalkForCombinators(a[i], path + "[" + i + "]", failures);
            }
        }
    }
}
