using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GxMcp.Worker.Services
{
    /// <summary>
    /// Item 96 — `genexus_reverse_pattern action=infer source=[X, Y, ...]`.
    /// Walks N similar objects and identifies common structural elements:
    /// shared variables, shared event names, shared parm signatures. Does NOT
    /// generate a real WWP pattern — just surfaces the commonalities so the
    /// caller can decide whether they form a real candidate pattern.
    /// </summary>
    public class ReversePatternService
    {
        private readonly ObjectService _objectService;
        private readonly UIService _uiService;

        public ReversePatternService(ObjectService objectService, UIService uiService)
        {
            _objectService = objectService;
            _uiService = uiService;
        }

        public string Infer(JArray source)
        {
            if (source == null || source.Count < 2)
                return Err("source must list at least 2 object names.");

            var sets = new List<(string name, HashSet<string> vars, HashSet<string> events, string parmSig)>();
            var unresolved = new JArray();
            foreach (var item in source)
            {
                string n = item?.ToString();
                if (string.IsNullOrWhiteSpace(n)) continue;
                try
                {
                    var obj = _objectService.FindObject(n);
                    if (obj == null) { unresolved.Add(n); continue; }
                    var vars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var events = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    string parmSig = "";
                    try
                    {
                        // Variables: scan the Variables part for <Variable Name="..." /> entries.
                        string varsXml = _objectService.ReadObjectSource(n, "Variables");
                        if (!string.IsNullOrEmpty(varsXml))
                        {
                            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(varsXml, "<Variable[^>]*\\bName=\"([^\"]+)\""))
                            {
                                vars.Add(m.Groups[1].Value);
                            }
                        }
                        // Parm signature: scan Rules for `parm(...)` declarations.
                        string rulesSrc = _objectService.ReadObjectSource(n, "Rules");
                        if (!string.IsNullOrEmpty(rulesSrc))
                        {
                            var m2 = System.Text.RegularExpressions.Regex.Match(rulesSrc, @"parm\s*\(([^)]*)\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            if (m2.Success) parmSig = m2.Groups[1].Value.Trim();
                        }
                    }
                    catch { }
                    try
                    {
                        // Events repertoire — heuristic: read Events part and scrape `Event <Name>`.
                        string events_src = _objectService.ReadObjectSource(n, "Events");
                        if (!string.IsNullOrEmpty(events_src))
                        {
                            foreach (var ln in events_src.Split('\n'))
                            {
                                var trimmed = ln.TrimStart();
                                if (trimmed.StartsWith("Event ", StringComparison.OrdinalIgnoreCase))
                                {
                                    var rest = trimmed.Substring(6).Trim();
                                    var ev = new string(rest.TakeWhile(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
                                    if (!string.IsNullOrEmpty(ev)) events.Add(ev);
                                }
                            }
                        }
                    }
                    catch { }
                    sets.Add((n, vars, events, parmSig));
                }
                catch { unresolved.Add(n); }
            }

            if (sets.Count < 2)
                return new JObject { ["status"] = "Error", ["code"] = "InsufficientResolved", ["unresolved"] = unresolved, ["resolved"] = sets.Count }.ToString(Formatting.None);

            HashSet<string> commonVars = new HashSet<string>(sets[0].vars, StringComparer.OrdinalIgnoreCase);
            HashSet<string> commonEvents = new HashSet<string>(sets[0].events, StringComparer.OrdinalIgnoreCase);
            foreach (var s in sets.Skip(1))
            {
                commonVars.IntersectWith(s.vars);
                commonEvents.IntersectWith(s.events);
            }

            // Parm signature: report whether all match.
            string parmSignature = sets[0].parmSig;
            bool parmsAllMatch = sets.All(s => string.Equals(s.parmSig, parmSignature, StringComparison.Ordinal));

            var divergence = new JArray();
            foreach (var s in sets)
            {
                var uniqueVars = s.vars.Except(commonVars, StringComparer.OrdinalIgnoreCase).ToList();
                var uniqueEvents = s.events.Except(commonEvents, StringComparer.OrdinalIgnoreCase).ToList();
                if (uniqueVars.Count > 0 || uniqueEvents.Count > 0)
                {
                    divergence.Add(new JObject
                    {
                        ["object"] = s.name,
                        ["uniqueVariables"] = new JArray(uniqueVars),
                        ["uniqueEvents"] = new JArray(uniqueEvents)
                    });
                }
            }

            return new JObject
            {
                ["status"] = "Success",
                ["scanned"] = sets.Count,
                ["unresolved"] = unresolved,
                ["commonVariables"] = new JArray(commonVars),
                ["commonEvents"] = new JArray(commonEvents),
                ["commonParmSignature"] = parmSignature,
                ["parmSignatureMatchesAll"] = parmsAllMatch,
                ["divergencePoints"] = divergence,
                ["hint"] = (commonVars.Count + commonEvents.Count) >= 3 ? "Strong commonality — candidate for a custom pattern." : "Weak commonality — these objects may not share a real pattern."
            }.ToString(Formatting.None);
        }

        private static string Err(string m) => new JObject { ["status"] = "Error", ["message"] = m }.ToString(Formatting.None);
    }
}
