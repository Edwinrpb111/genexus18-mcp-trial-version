using Newtonsoft.Json.Linq;
using GxMcp.Worker.Models;

namespace GxMcp.Worker.Helpers
{
    /// <summary>
    /// v2.8.0 — embed a `resources/read` next-step pointing at the relevant
    /// curated skill whenever an error code is likely the result of an LLM
    /// hallucinating a property / method / event. The skill bodies are
    /// authored on the gateway side (<c>SkillCatalog</c>) and live at stable
    /// URIs under <c>genexus://kb/skills/&lt;key&gt;</c>; the worker just
    /// produces the nextStep entry.
    ///
    /// Codes mapped to skills (extend as new hallucination paths are
    /// observed):
    /// </summary>
    public static class SkillHint
    {
        public const string Navigation = "genexus://kb/skills/navigation";
        public const string Gam = "genexus://kb/skills/gam-integrated-security";
        public const string SdPanel = "genexus://kb/skills/sd-panel-mobile";
        public const string WebPanelEvents = "genexus://kb/skills/webpanel-events";

        /// <summary>
        /// Returns a single nextStep JObject pointing at the given skill URI,
        /// formatted to match the canonical envelope's nextSteps shape.
        /// </summary>
        public static JObject ReadStep(string skillUri, string why = null)
        {
            return McpResponse.NextStep(
                tool: "resources/read",
                args: new JObject { ["uri"] = skillUri },
                why: why ?? "Curated, source-verified GeneXus reference — read before retrying.");
        }
    }
}
