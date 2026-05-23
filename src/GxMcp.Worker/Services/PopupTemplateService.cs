using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using GxMcp.Worker.Helpers;

namespace GxMcp.Worker.Services
{
    /// <summary>
    /// W3 (mcp-roadmap-ide-parity 2026-05-19) — first-class tool for the common
    /// "popup WebPanel with radio/combo/text inputs + Confirmar button" pattern.
    /// Takes a domain-level JObject spec and emits a WebPanel KBObject whose
    /// Form type="layout" body renders editable Radio/Combo controls (which a raw
    /// Form type="html" panel cannot — see LayoutGotchaScanner FR#2).
    ///
    /// Layered on top of existing primitives:
    ///   - ObjectService.CreateObject (creates the WebPanel KBObject)
    ///   - WriteService.AddVariable (one per inParm + one per input.varName, W5-validated)
    ///   - WriteService.WriteObject (Rules / WebForm / Events parts)
    ///
    /// Self-validates the generated layout XML through LayoutGotchaScanner; refuses
    /// to write if any structural gotcha would render the popup non-functional.
    /// </summary>
    public class PopupTemplateService
    {
        public interface IPopupBackend
        {
            string CreateObject(string type, string name);
            string AddVariable(string target, string varName, string typeName);
            string WriteObject(string target, string partName, string content);
            bool ObjectExists(string name);
        }

        private sealed class DefaultBackend : IPopupBackend
        {
            private readonly ObjectService _obj;
            private readonly WriteService _write;
            public DefaultBackend(ObjectService obj, WriteService write) { _obj = obj; _write = write; }
            public string CreateObject(string type, string name) => _obj.CreateObject(type, name);
            public string AddVariable(string target, string varName, string typeName)
                => _write.AddVariable(target, varName, typeName);
            public string WriteObject(string target, string partName, string content)
                => _write.WriteObject(target, partName, content, "WebPanel", true, false, true, false);
            public bool ObjectExists(string name)
            {
                try { return _obj.FindObject(name, "WebPanel") != null; }
                catch { return false; }
            }
        }

        private readonly IPopupBackend _backend;

        public PopupTemplateService(ObjectService objectService, WriteService writeService)
            : this(new DefaultBackend(objectService, writeService)) { }

        // Test seam — tests pass a fake backend.
        public PopupTemplateService(IPopupBackend backend)
        {
            _backend = backend;
        }

        public string CreatePopup(string name, JObject spec) => CreatePopup(name, spec, false);

        public string CreatePopup(string name, JObject spec, bool dryRun)
        {
            if (string.IsNullOrWhiteSpace(name))
                return Err("name is required");

            var parsed = PopupLayoutBuilder.ParseSpec(spec);
            if (!parsed.IsValid)
                return new JObject
                {
                    ["status"] = "Error",
                    ["code"] = "InvalidSpec",
                    ["errors"] = new JArray(parsed.Errors)
                }.ToString(Newtonsoft.Json.Formatting.None);

            var pspec = parsed.Spec;

            // 1) Build & self-validate the layout XML BEFORE touching the KB.
            //    LayoutGotchaScanner gates structural problems (Form type, cell-outside-table,
            //    unknown ControlType, duplicate ids). Use varNameResolver returning the matching
            //    input.varName for the placeholder var:N bindings we emit — though we use
            //    "&VarName" AttIDs (not var:N), so resolver is rarely hit.
            string layoutXml = PopupLayoutBuilder.BuildLayoutXml(pspec);
            var gotchas = LayoutGotchaScanner.Scan(layoutXml, _ => null);
            if (gotchas.Count > 0)
            {
                var arr = new JArray();
                foreach (var g in gotchas)
                {
                    arr.Add(new JObject
                    {
                        ["code"] = g.Code,
                        ["severity"] = g.Severity,
                        ["element"] = g.Element,
                        ["controlId"] = g.ControlId,
                        ["message"] = g.Message,
                        ["workaround"] = g.Workaround
                    });
                }
                return new JObject
                {
                    ["status"] = "Error",
                    ["code"] = "LayoutGotcha",
                    ["message"] = "Generated layout XML failed LayoutGotchaScanner self-validation. Refusing to write.",
                    ["gotchas"] = arr
                }.ToString(Newtonsoft.Json.Formatting.None);
            }

            // Item 21 (friction 2026-05-22) — universal dryRun. Spec was parsed +
            // layout XML rendered + LayoutGotchaScanner cleared above; short-circuit
            // before the four mutating backend calls (CreateObject, AddVariable,
            // WriteObject Rules/WebForm/Events). The agent gets the same shape as
            // the live path but with the persisted bytes inlined so it can preview.
            if (dryRun)
            {
                string rulesPreview = PopupLayoutBuilder.BuildRulesSource(pspec) ?? string.Empty;
                string eventsPreview = PopupLayoutBuilder.BuildEventsSource(pspec) ?? string.Empty;
                return new JObject
                {
                    ["status"] = "DryRun",
                    ["dryRun"] = true,
                    ["name"] = name,
                    ["type"] = "WebPanel",
                    ["layoutFormType"] = "layout",
                    ["inputs"] = pspec.Inputs.Count,
                    ["buttons"] = pspec.Buttons.Count,
                    ["wouldCreateObject"] = !_backend.ObjectExists(name),
                    ["webFormXml"] = layoutXml,
                    ["rulesSource"] = rulesPreview,
                    ["eventsSource"] = eventsPreview,
                    ["hint"] = "Re-run without dryRun to persist."
                }.ToString(Newtonsoft.Json.Formatting.None);
            }

            var responseSteps = new JArray();

            // 2) Create the WebPanel if missing (idempotent).
            if (!_backend.ObjectExists(name))
            {
                string createResult = _backend.CreateObject("WebPanel", name);
                responseSteps.Add(new JObject
                {
                    ["step"] = "create_object",
                    ["result"] = TryParse(createResult)
                });
            }
            else
            {
                responseSteps.Add(new JObject
                {
                    ["step"] = "create_object",
                    ["result"] = new JObject { ["status"] = "Skipped", ["reason"] = "already exists" }
                });
            }

            // 3) Add variables — one per inParm + one per input.varName. Dedup by name.
            var addedVars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in pspec.InParms ?? new List<string>())
            {
                var (vn, vt) = SplitParm(p);
                if (string.IsNullOrEmpty(vn) || !addedVars.Add(vn)) continue;
                string r = _backend.AddVariable(name, vn, vt);
                responseSteps.Add(new JObject { ["step"] = "add_variable", ["var"] = vn, ["type"] = vt, ["result"] = TryParse(r) });
            }
            foreach (var inp in pspec.Inputs)
            {
                if (string.IsNullOrEmpty(inp.VarName) || !addedVars.Add(inp.VarName)) continue;
                string typeName = DefaultTypeForInput(inp);
                string r = _backend.AddVariable(name, inp.VarName, typeName);
                responseSteps.Add(new JObject { ["step"] = "add_variable", ["var"] = inp.VarName, ["type"] = typeName, ["result"] = TryParse(r) });
            }

            // 4) Write Rules part.
            string rules = PopupLayoutBuilder.BuildRulesSource(pspec);
            if (!string.IsNullOrEmpty(rules))
            {
                string r = _backend.WriteObject(name, "Rules", rules);
                responseSteps.Add(new JObject { ["step"] = "write_rules", ["result"] = TryParse(r) });
            }

            // 5) Write WebForm (layout) part.
            string wfResult = _backend.WriteObject(name, "WebForm", layoutXml);
            responseSteps.Add(new JObject { ["step"] = "write_webform", ["result"] = TryParse(wfResult) });

            // 6) Write Events part.
            string events = PopupLayoutBuilder.BuildEventsSource(pspec);
            if (!string.IsNullOrEmpty(events))
            {
                string r = _backend.WriteObject(name, "Events", events);
                responseSteps.Add(new JObject { ["step"] = "write_events", ["result"] = TryParse(r) });
            }

            // FR#18 (Stream G, v2.6.6): inline the popup-branch hint so the
            // agent learns the popup conventions (no Link() in Enter,
            // Cancel.OnClick = Hide(), ReturnTo() for values) at create
            // time — same string analyze mode=parent_context surfaces.
            return new JObject
            {
                ["status"] = "Success",
                ["name"] = name,
                ["type"] = "WebPanel",
                ["layoutFormType"] = "layout",
                ["inputs"] = pspec.Inputs.Count,
                ["buttons"] = pspec.Buttons.Count,
                ["popupHint"] = AnalyzeService.HintForOpenedAs("popup"),
                ["steps"] = responseSteps
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static (string name, string type) SplitParm(string parm)
        {
            if (string.IsNullOrWhiteSpace(parm)) return (null, null);
            string s = parm.Trim().TrimStart('&');
            int colon = s.IndexOf(':');
            if (colon < 0) return (s, "Character(40)");
            string nm = s.Substring(0, colon).Trim();
            string tp = s.Substring(colon + 1).Trim();
            return (nm, string.IsNullOrEmpty(tp) ? "Character(40)" : tp);
        }

        private static string DefaultTypeForInput(PopupLayoutBuilder.PopupInput inp)
        {
            // Pick a type based on the option value lengths for radio/combo, fall back
            // to Character(80) for text inputs.
            if (inp.Type == "radio" || inp.Type == "combo")
            {
                int maxLen = 1;
                foreach (var o in inp.Options)
                    if (!string.IsNullOrEmpty(o?.Value)) maxLen = Math.Max(maxLen, o.Value.Length);
                return "Character(" + maxLen + ")";
            }
            return "Character(80)";
        }

        private static JToken TryParse(string json)
        {
            if (string.IsNullOrEmpty(json)) return JValue.CreateNull();
            try { return JToken.Parse(json); }
            catch { return new JValue(json); }
        }

        private static string Err(string message) => new JObject
        {
            ["status"] = "Error",
            ["code"] = "InvalidArgs",
            ["error"] = message
        }.ToString(Newtonsoft.Json.Formatting.None);
    }
}
