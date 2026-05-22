using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GxMcp.Worker.Services
{
    public class EditAndBuildOrchestrator
    {
        private readonly IWriteServiceFacade _write;
        private readonly IAnalyzeServiceFacade _analyze;
        private readonly IBuildServiceFacade _build;

        public EditAndBuildOrchestrator(WriteService write, AnalyzeService analyze, BuildService build)
            : this((IWriteServiceFacade)write, (IAnalyzeServiceFacade)analyze, (IBuildServiceFacade)build)
        {
        }

        public EditAndBuildOrchestrator(IWriteServiceFacade write, IAnalyzeServiceFacade analyze, IBuildServiceFacade build)
        {
            _write = write;
            _analyze = analyze;
            _build = build;
        }

        public string Orchestrate(JObject args)
        {
            string target = args?["name"]?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(target))
            {
                return JsonConvert.SerializeObject(new
                {
                    status = "Error",
                    error = "name is required",
                    hint = "Pass the target object name as { name: 'MyObject' }. genexus_edit_and_build does NOT auto-detect from content."
                });
            }

            // Friction 2026-05-22: edit_and_build used to reject patch:{find,replace}
            // because args was forwarded to WriteService.WriteObject(target, args)
            // which only consulted args.content. Normalize the patch shape here so
            // the API matches genexus_edit's input contract.
            if (args["patch"] != null && args["content"] == null)
            {
                args["mode"] = args["mode"] ?? "patch";
                if (args["patch"] is JObject patchObj && patchObj["find"] != null && patchObj["replace"] != null)
                {
                    args["content"] = new JObject
                    {
                        ["find"] = patchObj["find"],
                        ["replace"] = patchObj["replace"]
                    };
                }
                else if (args["patch"].Type == JTokenType.String)
                {
                    // Legacy: bare string patch. Keep verbatim — WritePatch knows how to consume it.
                    args["content"] = args["patch"];
                }
            }

            // Friction 2026-05-22: callers occasionally passed `content: {...}`
            // expecting JSON-shaped input where a string was required. The
            // downstream stringification produced opaque failures; surface the
            // type mismatch up front.
            if (args["content"] != null
                && args["content"].Type == JTokenType.Object
                && args["mode"]?.ToString() != "patch")
            {
                return JsonConvert.SerializeObject(new
                {
                    status = "Error",
                    error = "content must be a string for mode=full",
                    hint = "For source/event/rules edits, pass content as a string. To use {find, replace}, set mode='patch' (or pass patch:{find,replace} as a sibling field — orchestrator auto-normalises)."
                });
            }

            string includeCallees = args?["buildIncludeCallees"]?.ToString() ?? "direct";
            int buildPlanCap = args?["buildPlanCap"]?.ToObject<int?>() ?? 200;
            bool waitForIndex = args?["waitForIndex"]?.ToObject<bool?>() ?? true;
            int waitTimeoutMs = args?["waitTimeoutMs"]?.ToObject<int?>() ?? 30000;

            string editRaw = _write.WriteObject(target, args);
            var edit = JObject.Parse(editRaw);
            if (!string.Equals(edit["status"]?.ToString(), "Ok", StringComparison.OrdinalIgnoreCase))
            {
                edit["phase"] = "edit";
                return edit.ToString();
            }

            string impactRaw = _analyze.ImpactAnalysis(target, waitForIndex, waitTimeoutMs);
            var impact = JObject.Parse(impactRaw);
            var callers = impact["callers"] as JArray ?? new JArray();

            JObject buildResult;
            if (callers.Count == 0)
            {
                buildResult = new JObject
                {
                    ["status"] = "Skipped",
                    ["reason"] = "No callers to rebuild."
                };
            }
            else
            {
                string targetList = string.Join(",", callers);
                string buildRaw = _build.Build("Build", targetList, includeCallees, buildPlanCap);
                buildResult = JObject.Parse(buildRaw);
            }

            return new JObject
            {
                ["status"] = "Ok",
                ["target"] = target,
                ["edit"] = edit,
                ["impact"] = impact,
                ["build"] = buildResult
            }.ToString();
        }
    }
}
