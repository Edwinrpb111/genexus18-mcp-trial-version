using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using GxMcp.Worker.Models;

namespace GxMcp.Worker.Helpers
{
    public static class HealingService
    {
        public class HealingResult
        {
            public bool Healed { get; set; }
            public string NewCode { get; set; }
            public string ActionTaken { get; set; }
        }

        public static string FormatNotFoundError(string target, SearchIndex index)
        {
            var suggestions = new List<string>();
            if (index != null && index.Objects != null)
            {
                // Find top 3 similar names using a simple distance or contains
                suggestions = index.Objects.Values
                    .Where(e => e.Name.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0 || 
                                target.IndexOf(e.Name, StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderBy(e => Math.Abs(e.Name.Length - target.Length))
                    .Take(3)
                    .Select(e => $"{e.Type}:{e.Name}")
                    .ToList();
            }

            var result = new JObject();
            result["error"] = $"Object not found: {target}";
            if (suggestions.Count > 0)
            {
                result["suggestion"] = $"Did you mean one of these? {string.Join(", ", suggestions)}";
                result["actionable_tip"] = "Use 'genexus_list_objects' with a broad filter to explore the KB if unsure.";
            }
            else if (index == null)
            {
                // Cold window: the name index isn't loaded yet, so we can't offer
                // "did you mean" suggestions. The lookup itself was authoritative
                // (the SDK name lookup ran), so this is a genuine miss — but never
                // leave the agent at a dead end: tell it the index is warming and
                // how to recover. A retry in a couple seconds gets full suggestions.
                result["code"] = "ObjectNotFoundIndexWarming";
                result["actionable_tip"] = "The KB name index is still loading, so name suggestions aren't available yet. Retry this call in 2-3 seconds to get 'did you mean' suggestions, or call 'genexus_list_objects' with a broad filter to find the exact object name.";
            }
            else
            {
                result["actionable_tip"] = "No similar names found. Call 'genexus_list_objects' with a broad filter to discover the exact object name.";
            }

            return result.ToString();
        }

        public static HealingResult AttemptHealing(string code, JArray messages, SearchIndex index)
        {
            // Placeholder for real healing logic
            return new HealingResult { Healed = false };
        }
    }
}
