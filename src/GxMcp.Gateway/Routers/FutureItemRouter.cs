using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace GxMcp.Gateway.Routers
{
    // Future-stub fallback router. As of v2.6.9 the wave-3 sweep promoted all
    // 18 doc-flagged long-term items to real implementations. This router is
    // kept as a no-op safety net so callers of a removed tool still receive a
    // typed { status:"Future", code:"ItemDeferred", hint, docRef } envelope
    // rather than a hard tool-not-found error. The _items map is intentionally
    // empty — add an entry here if you need to temporarily quarantine a tool.
    public class FutureItemRouter : IMcpModuleRouter
    {
        public string ModuleName => "Future";

        private static readonly Dictionary<string, (int Number, string Hint)> _items =
            new Dictionary<string, (int, string)>
            {
                // Empty by design — all wave-3 future items have real implementations.
                // Add an entry here only if a tool must be temporarily quarantined.
            };

        public static IReadOnlyDictionary<string, (int Number, string Hint)> Items => _items;

        public object? ConvertToolCall(string toolName, JObject? args)
        {
            if (toolName == null || !_items.TryGetValue(toolName, out var entry)) return null;
            return new
            {
                module = "Future",
                action = "Deferred",
                itemNumber = entry.Number,
                hint = entry.Hint
            };
        }
    }
}
