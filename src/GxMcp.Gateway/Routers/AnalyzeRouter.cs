using Newtonsoft.Json.Linq;
namespace GxMcp.Gateway.Routers
{
    public class AnalyzeRouter : IMcpModuleRouter
    {
        public string ModuleName => "Analyze";

        public object? ConvertToolCall(string toolName, JObject? args)
        {
            string? target = args?["name"]?.ToString();
            string? type = args?["type"]?.ToString();

            switch (toolName)
            {
                case "genexus_inspect":
                    return new { module = "Analyze", action = "GetConversionContext", target = target, include = args?["include"], type = type };

                case "genexus_inject_context":
                    bool recursive = args?["recursive"]?.Value<bool>() ?? false;
                    return new { module = "Analyze", action = "InjectContext", target = target, recursive = recursive, type = type };

                case "genexus_analyze":
                    string? mode = args?["mode"]?.ToString();
                    switch (mode)
                    {
                        case "linter":
                            bool linterFix = args?["fix"]?.ToObject<bool?>() ?? false;
                            return new { module = "Linter", action = "Analyze", target = target, type = type, @params = new JObject { ["fix"] = linterFix } };
                        case "navigation":
                            return new { module = "Analyze", action = "GetNavigation", target = target, type = type };
                        case "hierarchy":
                            return new { module = "Analyze", action = "GetHierarchy", target = target, type = type };
                        case "impact":
                            // v2.3.8 (Task 1.4 + post-self-review): delegate to ImpactAnalysis
                            // with index-readiness envelope. Flags must be flattened at the top
                            // level — BuildWorkerRpcRequest clones the entire workerCommand into
                            // request.params, so a nested @params would land at
                            // request.params.params.waitForIndex (two levels deep) and the
                            // worker's `args["waitForIndex"]` lookup would miss it. That's
                            // exactly why the pre-fix flag was always seen as `true`, forcing
                            // a 30s wait loop even with waitForIndex=false.
                            bool waitForIndex = args?["waitForIndex"]?.ToObject<bool?>() ?? true;
                            int? waitTimeoutMs = args?["waitTimeoutMs"]?.ToObject<int?>();
                            string? cancelToken = args?["cancelToken"]?.ToString();
                            return new {
                                module = "Analyze",
                                action = "ImpactAnalysis",
                                target = target,
                                type = type,
                                waitForIndex = waitForIndex,
                                waitTimeoutMs = waitTimeoutMs,
                                cancelToken = cancelToken
                            };
                        case "data_context":
                            return new { module = "Analyze", action = "GetDataContext", target = target, type = type };
                        case "ui_context":
                            return new { module = "UI", action = "GetUIContext", target = target, type = type };
                        case "pattern_metadata":
                            return new { module = "Analyze", action = "GetPatternMetadata", target = target, type = type };
                        case "summary":
                            return new { module = "Analyze", action = "Summarize", target = target, type = type };
                        case "explain":
                            return new { module = "Analyze", action = "ExplainCode", target = target, payload = args?["code"]?.ToString(), type = type };
                        default:
                            return new { module = "Analyze", action = "Analyze", target = target, type = type };
                    }

                case "genexus_sql":
                    string? sqlAction = args?["action"]?.ToString()?.ToLowerInvariant();
                    if (sqlAction == "navigation")
                    {
                        int? levelNumber = args?["levelNumber"]?.ToObject<int?>();
                        return new { module = "Analyze", action = "GetSqlForNavigation", target = target, levelNumber = levelNumber, type = type };
                    }
                    // default = ddl
                    bool includeSub = args?["includeSubordinated"]?.Value<bool>() ?? false;
                    return new { module = "Analyze", action = "GetSQL", target = target, includeSubordinated = includeSub, type = type };

                case "genexus_get_signature":
                    return new { module = "Analyze", action = "GetParameters", target = target, type = type };
                case "genexus_linter":
                    return new { module = "Linter", action = "Analyze", target = target, type = type };
                case "genexus_get_navigation":
                    return new { module = "Analyze", action = "GetNavigation", target = target, type = type };

                default:
                    return null;
            }
        }
    }
}
