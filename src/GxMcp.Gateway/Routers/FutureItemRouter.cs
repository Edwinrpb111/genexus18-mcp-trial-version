using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace GxMcp.Gateway.Routers
{
    // Wave-3 doc-flagged "Long-term / strategic XL" + "Skip / wait for user
    // feedback" items (18 tools). Schema is declared in tool_definitions.json
    // and each tool routes through here to FutureItemStub.Deferred in the
    // worker, which returns a typed { status:"Future", code:"ItemDeferred",
    // hint, docRef } envelope. The map below is the single source of truth
    // for {tool → (itemNumber, hint)}.
    public class FutureItemRouter : IMcpModuleRouter
    {
        public string ModuleName => "Future";

        private static readonly Dictionary<string, (int Number, string Hint)> _items =
            new Dictionary<string, (int, string)>
            {
                ["genexus_watch_event"]            = (35, "Set breakpoints in Events; capture variable state when triggered."),
                // Items 53, 54, 55, 56 promoted to real tools (gateway-side handlers
                // in Program.cs; KB-infrastructure batch). No longer routes here.
                ["genexus_tutorial"]               = (66, "Interactive onboarding tutorial."),
                ["genexus_github"]                 = (71, "Auto-create PR from current branch."),
                ["genexus_learning"]               = (76, "Cross-session friction-learning loop."),
                ["genexus_sd_panel"]               = (78, "SDPanel parity for mobile. Most existing tools assume WebPanel."),
                ["genexus_ai_complete"]            = (81, "AI-prompted code completion in Events Source. Stub for upstream LLM integration."),
                ["genexus_time_travel"]            = (82, "Restore object state from a past timestamp without losing intermediate edits."),
                ["genexus_voice"]                  = (83, "Voice-driven edits via NL command."),
                ["genexus_multi_agent_lock"]       = (84, "Granular lock for parallel multi-agent edits."),
                ["genexus_what_if"]                = (86, "Simulate a type/schema change; report breakage without persisting."),
                // Item 91 promoted to real tool — routes through OperationsRouter to
                // RefactorService.Refactor(action=RenameObject|RenameAttribute).
                ["genexus_auto_test"]              = (95, "Generate tests from real production invocation patterns."),
                ["genexus_reverse_pattern"]        = (96, "Infer a pattern (WWP-like) from existing similar objects."),
                ["genexus_cross_browser"]          = (98, "Render in parallel browsers; diff screenshots.")
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
