using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace GxMcp.Gateway
{
    /// <summary>
    /// Gateway-side auto-injection of the <c>type</c> argument when the LLM omits it
    /// but <c>name</c> resolves to a unique object in the cached index.
    ///
    /// Mutates <paramref name="arguments"/> in-place (adds <c>arguments["type"]</c>) and
    /// returns <c>true</c> when injection succeeded.  Returns <c>false</c> — no mutation —
    /// when the name is ambiguous, unknown, or the tool doesn't accept a <c>type</c> field.
    /// </summary>
    internal static class AutoTypeInjector
    {
        // ── Tools that are exempt from injection ─────────────────────────────
        // Non-object-targeted tools that happen to have a 'name' param but
        // where 'type' means something different or doesn't exist at all.
        private static readonly HashSet<string> _skipTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "genexus_kb",
            "genexus_whoami",
            "genexus_lifecycle",
            "genexus_orient",
            "genexus_doctor",
            "genexus_worker_reload",
            "genexus_worker_pool",
            "genexus_recipe",
            "genexus_telemetry",
            "genexus_security",
            "genexus_format",
            "genexus_github",
            "genexus_ai_complete",
        };

        // ── Name → type lookup cache, populated from index snapshots ─────────
        // Key: object name (lower-invariant).
        // Value: the single type string when the name is unique, OR null when
        //        multiple objects share the same name (ambiguous).
        private static readonly ConcurrentDictionary<string, string?> _nameLookup =
            new ConcurrentDictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        // Tool → inputSchema cache: does this tool declare a "type" property?
        private static readonly ConcurrentDictionary<string, bool> _toolHasTypeCache =
            new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Attempt to auto-inject <c>arguments["type"]</c> for <paramref name="toolName"/>.
        /// Returns <c>true</c> and sets <paramref name="injectedType"/> when injection occurs.
        /// </summary>
        public static bool TryInject(string toolName, JObject? arguments, out string injectedType)
        {
            injectedType = null!;

            if (arguments == null) return false;

            // Already has a type? Don't override caller.
            if (arguments["type"] != null && arguments["type"].Type != JTokenType.Null)
                return false;

            // Is this tool exempt?
            if (_skipTools.Contains(toolName))
                return false;

            // Does the tool's schema even declare a 'type' field?
            if (!ToolAcceptsTypeArg(toolName))
                return false;

            // Does the call have a 'name'?
            string? name = arguments["name"]?.ToString();
            if (string.IsNullOrWhiteSpace(name))
                return false;

            // Unique lookup?
            if (!_nameLookup.TryGetValue(name, out string? resolvedType))
                return false;           // name not in our index cache

            if (resolvedType == null)
                return false;           // ambiguous (multiple objects with this name)

            // Inject!
            arguments["type"] = resolvedType;
            injectedType = resolvedType;
            return true;
        }

        /// <summary>
        /// Refresh the name→type lookup from a <c>RecentlyChanged</c> JArray coming
        /// from <see cref="IndexStateSnapshot.RecentlyChanged"/>.
        /// Each element is expected to have at least <c>Name</c> and <c>Type</c> (or
        /// lower-case equivalents) fields.
        /// Call this whenever a fresh index snapshot arrives.
        /// </summary>
        public static void RefreshFromRecentlyChanged(JArray? recentlyChanged)
        {
            if (recentlyChanged == null) return;

            foreach (var token in recentlyChanged)
            {
                if (token is not JObject obj) continue;
                string? name = obj["Name"]?.ToString() ?? obj["name"]?.ToString();
                string? type = obj["Type"]?.ToString() ?? obj["type"]?.ToString();
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(type))
                    continue;

                _nameLookup.AddOrUpdate(
                    name,
                    addValue: type,
                    updateValueFactory: (_, existing) =>
                        // If existing type differs, mark as ambiguous (null)
                        existing != null && !string.Equals(existing, type, StringComparison.OrdinalIgnoreCase)
                            ? null
                            : type);
            }
        }

        /// <summary>
        /// v2.8.0 (S1) — used by MCP `completion/complete` to autocomplete
        /// object names from a partial prefix. Returns up to <paramref name="cap"/>
        /// matches that start with the prefix (case-insensitive). Empty when
        /// the index hasn't warmed yet.
        /// </summary>
        public static IEnumerable<string> CompleteName(string prefix, int cap = 25)
        {
            if (cap <= 0) yield break;
            prefix = prefix ?? string.Empty;
            int yielded = 0;
            foreach (var kv in _nameLookup)
            {
                if (kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    yield return kv.Key;
                    if (++yielded >= cap) yield break;
                }
            }
        }

        // ── Test helpers ──────────────────────────────────────────────────────

        /// <summary>Test-only: prime the name→type map directly.</summary>
        internal static void PrimeIndex(IEnumerable<(string name, string? type)> entries)
        {
            _nameLookup.Clear();
            foreach (var (name, type) in entries)
                _nameLookup[name] = type;
        }

        /// <summary>Test-only: clear all internal state.</summary>
        internal static void ClearAll()
        {
            _nameLookup.Clear();
            _toolHasTypeCache.Clear();
        }

        /// <summary>Test-only: tell the injector whether a tool accepts a 'type' arg.</summary>
        internal static void PrimeToolAcceptsType(string toolName, bool accepts)
        {
            _toolHasTypeCache[toolName] = accepts;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static bool ToolAcceptsTypeArg(string toolName)
        {
            if (_toolHasTypeCache.TryGetValue(toolName, out bool cached))
                return cached;

            // Fall back to reading the tool_definitions.json schema via GatewayArgsValidator's
            // internal mechanism.  We call GatewayArgsValidator.Validate with a dummy args that
            // only has 'type' present so we can detect whether the schema declares the property.
            // A lighter approach: just check if "type" is in the schema properties.
            bool result = SchemaDeclaresTypeProperty(toolName);
            _toolHasTypeCache[toolName] = result;
            return result;
        }

        private static bool SchemaDeclaresTypeProperty(string toolName)
        {
            // We reuse GatewayArgsValidator's internal schema loader via the public Validate
            // path: if a JObject with only { "type": "dummy" } passes validation without a
            // violation for "type", then the property is known.  But that's fragile.
            //
            // Simpler: call Validate with an empty args and check that "type" is NOT in
            // violations as "none (additionalProperties: false)".  Tools with
            // additionalProperties:false that DON'T declare 'type' will reject it.
            // Tools that DO declare 'type' or have no additionalProperties constraint will accept it.
            //
            // Hardcode the set of tools that are known to accept 'type' based on
            // tool_definitions.json scan — this list covers all object-targeted tools.
            return _toolsWithTypeArg.Contains(toolName);
        }

        // All tools in tool_definitions.json that declare a top-level "type" property.
        // Keeping this as a static set avoids a runtime JSON parse per call.
        private static readonly HashSet<string> _toolsWithTypeArg = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "genexus_read",
            "genexus_edit",
            "genexus_inspect",
            "genexus_delete_object",
            "genexus_refactor",
            "genexus_edit_and_build",
            "genexus_properties",
            "genexus_analyze",
            "genexus_apply_pattern",
            "genexus_variable",
            "genexus_io",
            "genexus_versioning",
            "genexus_db",
            "genexus_rename_across_kb",
            "genexus_multi_agent_lock",
            "genexus_navigation",
            "genexus_layout",
            "genexus_structure",
            "genexus_create",
        };
    }
}
