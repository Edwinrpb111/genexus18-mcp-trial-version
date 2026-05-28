using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GxMcp.Worker.Models;
using Newtonsoft.Json.Linq;

namespace GxMcp.Worker.Services
{
    /// <summary>
    /// genexus_playbook — deferred-load skill packs. Returns the markdown body
    /// of an embedded playbook for a given topic (popup_layout, wwp_dual_form,
    /// pattern_reapply, ...). NO live KB state; pure docs delivery.
    /// </summary>
    public class PlaybookService
    {
        private const string ResourcePrefix = "GxMcp.Worker.Playbooks.";
        private const string ResourceSuffix = ".md";

        private static readonly Lazy<Dictionary<string, string>> _topics =
            new Lazy<Dictionary<string, string>>(LoadEmbedded);

        private static Dictionary<string, string> LoadEmbedded()
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var asm = typeof(PlaybookService).Assembly;
            foreach (var resName in asm.GetManifestResourceNames())
            {
                if (!resName.StartsWith(ResourcePrefix, StringComparison.Ordinal)) continue;
                if (!resName.EndsWith(ResourceSuffix, StringComparison.Ordinal)) continue;
                string topic = resName.Substring(ResourcePrefix.Length,
                    resName.Length - ResourcePrefix.Length - ResourceSuffix.Length);
                using (var s = asm.GetManifestResourceStream(resName))
                using (var r = new StreamReader(s))
                {
                    dict[topic] = r.ReadToEnd();
                }
            }
            return dict;
        }

        public string Read(string topic, bool listOnly)
        {
            var map = _topics.Value;
            if (listOnly || string.IsNullOrWhiteSpace(topic))
            {
                return McpResponse.Ok(
                    code: "PlaybookTopicsListed",
                    result: new JObject
                    {
                        ["topics"] = new JArray(map.Keys.OrderBy(k => k, StringComparer.Ordinal)
                            .Select(k => (JToken)k)),
                        ["hint"] = "Call genexus_playbook topic=<one of the above> to read the full markdown."
                    });
            }

            if (!map.TryGetValue(topic, out var body))
            {
                return McpResponse.Err(
                    code: "PlaybookTopicNotFound",
                    message: "Unknown playbook topic: " + topic,
                    hint: "Call genexus_playbook listOnly=true to see available topics.",
                    nextSteps: new JArray { McpResponse.NextStep("genexus_playbook", new JObject { ["listOnly"] = true }, "List available playbook topics.") });
            }

            return McpResponse.Ok(
                code: "PlaybookFound",
                result: new JObject
                {
                    ["topic"] = topic,
                    ["content"] = body,
                    ["bytes"] = body.Length
                });
        }
    }
}
