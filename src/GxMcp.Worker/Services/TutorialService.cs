using System.Collections.Generic;
using GxMcp.Worker.Models;
using Newtonsoft.Json.Linq;

namespace GxMcp.Worker.Services
{
    /// <summary>
    /// Item 66 — `genexus_tutorial step=N`. Static, deterministic step-by-step
    /// walkthrough for a brand-new MCP agent. NO live state. Each step returns
    /// title + narrative + suggested next call.
    /// </summary>
    public class TutorialService
    {
        private static readonly List<Step> _steps = new List<Step>
        {
            new Step { Title = "Orient", Narrative = "Run `genexus_orient` and `genexus_whoami` to learn the KB name, recent edits and known gotchas.", SuggestedCall = new JObject { ["tool"] = "genexus_whoami", ["args"] = new JObject() } },
            new Step { Title = "List objects", Narrative = "Browse the KB with `genexus_list_objects` (default compact projection).", SuggestedCall = new JObject { ["tool"] = "genexus_list_objects", ["args"] = new JObject { ["limit"] = 10, ["projection"] = "minimal" } } },
            new Step { Title = "Inspect", Narrative = "Pick an object and call `genexus_inspect include=['metadata','parts','variables']` for the structural snapshot.", SuggestedCall = new JObject { ["tool"] = "genexus_inspect", ["args"] = new JObject { ["name"] = "<your-object>", ["include"] = new JArray("metadata", "parts", "variables") } } },
            new Step { Title = "Read source", Narrative = "Read the Events part to see what the object does.", SuggestedCall = new JObject { ["tool"] = "genexus_read", ["args"] = new JObject { ["name"] = "<your-object>", ["part"] = "Events" } } },
            new Step { Title = "Edit with dryRun", Narrative = "Edit a part with `dryRun=true` first — always preview before persisting.", SuggestedCall = new JObject { ["tool"] = "genexus_edit", ["args"] = new JObject { ["name"] = "<your-object>", ["part"] = "Events", ["mode"] = "patch", ["dryRun"] = true } } },
            new Step { Title = "Build + smoke", Narrative = "Run `genexus_lifecycle action=build wait_until_done=true` then `genexus_smoke_test` to verify the runtime works.", SuggestedCall = new JObject { ["tool"] = "genexus_lifecycle", ["args"] = new JObject { ["action"] = "build", ["wait_until_done"] = true } } }
        };

        private class Step { public string Title; public string Narrative; public JObject SuggestedCall; }

        public string GetStep(int step)
        {
            int total = _steps.Count;
            if (step < 1 || step > total)
            {
                return McpResponse.Err(
                    code: "StepOutOfRange",
                    message: "step must be between 1 and " + total,
                    extra: new JObject { ["totalSteps"] = total });
            }
            var s = _steps[step - 1];
            return McpResponse.Ok(code: "TutorialStep", result: new JObject
            {
                ["stepNumber"] = step,
                ["totalSteps"] = total,
                ["title"] = s.Title,
                ["narrative"] = s.Narrative,
                ["suggestedCall"] = s.SuggestedCall,
                ["next"] = step < total ? (JToken)(step + 1) : null
            });
        }
    }
}
