using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace GxMcp.Worker.Services
{
    /// <summary>
    /// Item 83 — voice-driven NL command mapper. Speech recognition is upstream in the
    /// client; this service receives a plain text transcript and maps it to a concrete
    /// MCP tool call via a built-in regex intent table. NO live dispatch — the agent
    /// inspects the response and decides whether to follow up with the suggested tool.
    /// </summary>
    public class VoiceIntentService
    {
        private sealed class Intent
        {
            public string Tool;
            public Regex Pattern;
            public Func<Match, JObject> ArgsBuilder;
        }

        private static readonly List<Intent> _intents = BuildIntents();

        public JObject Map(string transcript)
        {
            if (string.IsNullOrWhiteSpace(transcript))
            {
                return new JObject
                {
                    ["matched"] = false,
                    ["unrecognised"] = true,
                    ["hint"] = "Empty transcript."
                };
            }

            transcript = transcript.Trim();
            var hits = new List<(Intent intent, Match match)>();
            foreach (var intent in _intents)
            {
                var m = intent.Pattern.Match(transcript);
                if (m.Success) hits.Add((intent, m));
            }

            if (hits.Count == 0)
            {
                return new JObject
                {
                    ["matched"] = false,
                    ["unrecognised"] = true,
                    ["transcript"] = transcript,
                    ["hint"] = "No built-in pattern matched. Patterns recognised: add button, add attribute, open object, save, build, delete, rename, list objects, run, screenshot."
                };
            }

            if (hits.Count > 1)
            {
                var ambig = new JArray();
                foreach (var h in hits)
                {
                    ambig.Add(new JObject
                    {
                        ["tool"] = h.intent.Tool,
                        ["args"] = h.intent.ArgsBuilder(h.match)
                    });
                }
                return new JObject
                {
                    ["matched"] = false,
                    ["ambiguous"] = ambig,
                    ["transcript"] = transcript,
                    ["hint"] = "Multiple intent patterns matched the transcript."
                };
            }

            var only = hits[0];
            return new JObject
            {
                ["matched"] = true,
                ["dispatchedTool"] = only.intent.Tool,
                ["dispatchedArgs"] = only.intent.ArgsBuilder(only.match),
                ["transcript"] = transcript,
                ["hint"] = "Agent should follow up with the suggested tool. NO live dispatch from this call."
            };
        }

        private static List<Intent> BuildIntents()
        {
            var list = new List<Intent>();
            const RegexOptions opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

            // 1. "add button called <Caption>" / "add a button named <Caption>"
            list.Add(new Intent
            {
                Tool = "genexus_edit_form",
                Pattern = new Regex(@"^add (?:a )?button (?:called|named) (?<caption>[\w\- ]+?)(?: on (?<target>\w+))?$", opts),
                ArgsBuilder = m =>
                {
                    var args = new JObject
                    {
                        ["action"] = "add_button",
                        ["caption"] = m.Groups["caption"].Value.Trim()
                    };
                    if (m.Groups["target"].Success) args["name"] = m.Groups["target"].Value;
                    return args;
                }
            });

            // 2. "add attribute <Name> of type <Type>"
            list.Add(new Intent
            {
                Tool = "genexus_create_object",
                Pattern = new Regex(@"^add attribute (?<name>\w+)(?: of type (?<dt>\w+))?$", opts),
                ArgsBuilder = m =>
                {
                    var args = new JObject
                    {
                        ["type"] = "Attribute",
                        ["name"] = m.Groups["name"].Value
                    };
                    if (m.Groups["dt"].Success) args["dataType"] = m.Groups["dt"].Value;
                    return args;
                }
            });

            // 3. "open <Object>" / "open object <Object>"
            list.Add(new Intent
            {
                Tool = "genexus_query",
                Pattern = new Regex(@"^open(?: object)? (?<name>\w+)$", opts),
                ArgsBuilder = m => new JObject { ["name"] = m.Groups["name"].Value }
            });

            // 4. "save" / "save object <Name>"
            list.Add(new Intent
            {
                Tool = "genexus_edit",
                Pattern = new Regex(@"^save(?: object (?<name>\w+))?$", opts),
                ArgsBuilder = m =>
                {
                    var args = new JObject { ["action"] = "save" };
                    if (m.Groups["name"].Success) args["name"] = m.Groups["name"].Value;
                    return args;
                }
            });

            // 5. "build <Object>" / "build all"
            list.Add(new Intent
            {
                Tool = "genexus_lifecycle",
                Pattern = new Regex(@"^build(?: (?<name>\w+|all))?$", opts),
                ArgsBuilder = m =>
                {
                    var args = new JObject { ["action"] = "build" };
                    if (m.Groups["name"].Success && !string.Equals(m.Groups["name"].Value, "all", StringComparison.OrdinalIgnoreCase))
                        args["target"] = m.Groups["name"].Value;
                    return args;
                }
            });

            // 6. "delete <Object>"
            list.Add(new Intent
            {
                Tool = "genexus_delete_object",
                Pattern = new Regex(@"^delete (?<name>\w+)$", opts),
                ArgsBuilder = m => new JObject
                {
                    ["name"] = m.Groups["name"].Value,
                    ["confirm"] = true
                }
            });

            // 7. "rename <Old> to <New>"
            list.Add(new Intent
            {
                Tool = "genexus_refactor",
                Pattern = new Regex(@"^rename (?<old>\w+) to (?<new>\w+)$", opts),
                ArgsBuilder = m => new JObject
                {
                    ["action"] = "Rename",
                    ["target"] = m.Groups["old"].Value,
                    ["newName"] = m.Groups["new"].Value
                }
            });

            // 8. "list objects" / "list <type>s"
            list.Add(new Intent
            {
                Tool = "genexus_list_objects",
                Pattern = new Regex(@"^list (?:all )?(?<kind>objects|transactions|procedures|webpanels|attributes|domains)$", opts),
                ArgsBuilder = m =>
                {
                    var kind = m.Groups["kind"].Value.ToLowerInvariant();
                    var args = new JObject();
                    if (kind != "objects")
                    {
                        // Map plural → singular type name.
                        var typeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["transactions"] = "Transaction",
                            ["procedures"] = "Procedure",
                            ["webpanels"] = "WebPanel",
                            ["attributes"] = "Attribute",
                            ["domains"] = "Domain"
                        };
                        if (typeMap.TryGetValue(kind, out var t)) args["type"] = t;
                    }
                    return args;
                }
            });

            // 9. "run <Object>"
            list.Add(new Intent
            {
                Tool = "genexus_run_object",
                Pattern = new Regex(@"^run (?<name>\w+)$", opts),
                ArgsBuilder = m => new JObject { ["name"] = m.Groups["name"].Value }
            });

            // 10. "screenshot <Object>" / "take screenshot of <Object>"
            list.Add(new Intent
            {
                Tool = "genexus_preview",
                Pattern = new Regex(@"^(?:take )?screenshot(?: of)? (?<name>\w+)$", opts),
                ArgsBuilder = m => new JObject
                {
                    ["action"] = "render",
                    ["name"] = m.Groups["name"].Value,
                    ["capture"] = new JArray { "screenshot" }
                }
            });

            return list;
        }
    }
}
