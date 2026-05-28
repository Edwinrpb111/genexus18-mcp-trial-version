using System;
using GxMcp.Worker.Models;
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
            args = NormalizeToolArgs(args);

            string target = args?["name"]?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(target))
            {
                return McpResponse.Err(
                    code: "MissingTarget",
                    message: "name is required.",
                    hint: "Pass the target object name as { name: 'MyObject' }. genexus_edit_and_build does NOT auto-detect from content.");
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
                return McpResponse.Err(
                    code: "InvalidContent",
                    message: "content must be a string for mode=full.",
                    hint: "For source/event/rules edits, pass content as a string. To use {find, replace}, set mode='patch' (or pass patch:{find,replace} as a sibling field — orchestrator auto-normalises).");
            }

            string includeCallees = args?["buildIncludeCallees"]?.ToString() ?? "direct";
            int buildPlanCap = args?["buildPlanCap"]?.ToObject<int?>() ?? 200;
            bool waitForIndex = args?["waitForIndex"]?.ToObject<bool?>() ?? true;
            int waitTimeoutMs = args?["waitTimeoutMs"]?.ToObject<int?>() ?? 30000;

            string editRaw = _write.WriteObject(target, args);
            var edit = JObject.Parse(editRaw);
            if (ShouldReturnEditEnvelope(edit))
            {
                // Edit failed or returned DryRun — surface as canonical error with edit sub-block.
                string editStatusStr = edit?["status"]?.ToString() ?? string.Empty;
                bool isDryRun = string.Equals(editStatusStr, "DryRun", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(editStatusStr, "ok", StringComparison.OrdinalIgnoreCase) && string.Equals(edit?["code"]?.ToString(), "DryRun", StringComparison.OrdinalIgnoreCase);
                if (isDryRun)
                {
                    return McpResponse.Ok(target: target, code: "DryRun", result: new JObject
                    {
                        ["edit"] = edit
                    });
                }
                // edit["error"] may be a JObject (canonical) or a string (legacy mock/service).
                string editMsg = (edit?["error"] is JObject editErr)
                    ? editErr["message"]?.ToString()
                    : edit?["error"]?.ToString()
                    ?? edit?["message"]?.ToString()
                    ?? "Edit phase failed.";
                return McpResponse.Err(code: "EditPhaseFailed", message: editMsg, target: target, extra: new JObject { ["phase"] = "edit", ["edit"] = edit });
            }

            if (IsNoChange(edit))
            {
                return McpResponse.Ok(target: target, code: "NoChange", result: new JObject
                {
                    ["edit"] = edit,
                    ["build"] = new JObject { ["skipped"] = true, ["reason"] = "Edit produced no persisted change." }
                });
            }

            string impactRaw = _analyze.ImpactAnalysis(target, waitForIndex, waitTimeoutMs);
            var impact = JObject.Parse(impactRaw);
            var callers = impact["callers"] as JArray ?? new JArray();

            JObject buildResult;
            if (callers.Count == 0)
            {
                buildResult = new JObject { ["skipped"] = true, ["reason"] = "No callers to rebuild." };
            }
            else
            {
                string targetList = string.Join(",", callers);
                string buildRaw = _build.Build("Build", targetList, includeCallees, buildPlanCap);
                buildResult = JObject.Parse(buildRaw);
            }

            return McpResponse.Ok(target: target, code: "EditAndBuildCompleted", result: new JObject
            {
                ["edit"] = edit,
                ["impact"] = impact,
                ["build"] = buildResult
            });
        }

        internal static JObject NormalizeToolArgs(JObject args)
        {
            if (!(args?["args"] is JObject innerArgs))
            {
                return args ?? new JObject();
            }

            var merged = (JObject)innerArgs.DeepClone();
            foreach (var prop in args.Properties())
            {
                if (prop.Name == "args") continue;
                if (merged[prop.Name] == null) merged[prop.Name] = prop.Value?.DeepClone();
            }

            return merged;
        }

        internal static bool ShouldReturnEditEnvelope(JObject edit)
        {
            string status = edit?["status"]?.ToString() ?? string.Empty;
            if (string.Equals(status, "DryRun", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(status, "Ok", StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(status, "Success", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        internal static bool IsNoChange(JObject edit)
        {
            if (edit?["noChange"]?.ToObject<bool?>() == true) return true;
            return string.Equals(edit?["status"]?.ToString(), "NoChange", StringComparison.OrdinalIgnoreCase);
        }
    }
}
