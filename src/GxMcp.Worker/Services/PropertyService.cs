using System;
using System.Collections.Generic;
using System.Linq;
using Artech.Architecture.Common.Objects;
using Artech.Genexus.Common.Objects;
using Artech.Common.Properties;
using Artech.Genexus.Common.Parts;
using Newtonsoft.Json.Linq;
using GxMcp.Worker.Helpers;

namespace GxMcp.Worker.Services
{
    public class PropertyService
    {
        private readonly ObjectService _objectService;

        public PropertyService(ObjectService objectService)
        {
            _objectService = objectService;
        }

        public string GetProperties(string target, string controlName = null, string typeFilter = null)
        {
            try
            {
                var obj = _objectService.FindObject(target, typeFilter);
                if (obj == null) return Models.McpResponse.Error("Object not found", target);

                dynamic container = obj;
                if (!string.IsNullOrEmpty(controlName))
                {
                    container = FindControl(obj, controlName);
                    if (container == null) return Models.McpResponse.Error($"Control '{controlName}' not found in {obj.Name}", target);
                }

                return SerializeProperties(container).ToString();
            }
            catch (Exception ex)
            {
                return "{\"error\": \"" + CommandDispatcher.EscapeJsonString(ex.Message) + "\"}";
            }
        }

        public string SetProperty(string target, string propName, string value, string controlName = null, string typeFilter = null)
        {
            try
            {
                var obj = _objectService.FindObject(target, typeFilter);
                if (obj == null) return Models.McpResponse.Error("Object not found", target);

                dynamic container = obj;
                if (!string.IsNullOrEmpty(controlName))
                {
                    container = FindControl(obj, controlName);
                    if (container == null) return Models.McpResponse.Error($"Control '{controlName}' not found in {obj.Name}", target);
                }
                
                using (var trans = obj.Model.KB.BeginTransaction())
                {
                    bool committed = false;
                    try
                    {
                        try {
                            container.SetPropertyValue(propName, value);
                        } catch (Exception setEx) {
                            var pInfo = container.GetType().GetProperty(propName);
                            if (pInfo != null && pInfo.CanWrite) pInfo.SetValue(container, value);
                            else throw new Exception($"Property '{propName}' not found or not writable on {controlName ?? obj.Name}. Underlying error: {setEx.Message}");
                        }

                        try { if (container != obj) container.Dirty = true; } catch { }
                        obj.EnsureSave();
                        trans.Commit();
                        committed = true;
                    }
                    finally
                    {
                        if (!committed)
                        {
                            try { trans.Rollback(); } catch (Exception rbEx) { Logger.Warn("[PROPERTY] Rollback failed: " + rbEx.Message); }
                        }
                    }
                }

                return "{\"status\": \"Success\"}";
            }
            catch (Exception ex)
            {
                return "{\"error\": \"" + CommandDispatcher.EscapeJsonString(ex.Message) + "\"}";
            }
        }

        private dynamic FindControl(KBObject obj, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            // FR#4 + FR#5 (friction-report 2026-05-14): accept three scope forms in `control`:
            //   1. Layout control name (e.g. "BtnConfirmar") — existing behavior.
            //   2. Variable reference with & prefix (e.g. "&Alu2RegProf") — new.
            //   3. Plain variable name when starting with '&' is stripped.
            // The variable form returns the SDK Variable instance so its properties
            // (ControlType, ControlValues, Enabled, Visible, …) can be read/set.
            string trimmed = name.Trim();
            if (trimmed.StartsWith("&"))
            {
                return FindVariable(obj, trimmed.Substring(1));
            }

            // Support qualified paths: "Documento.DocCod" or "Documento/DocCod"
            var segments = name.Split(new[] { '.', '/' }, StringSplitOptions.RemoveEmptyEntries);
            string leaf = segments[segments.Length - 1];

            var webFormPart = obj.Parts.Cast<KBObjectPart>().FirstOrDefault(p => p.TypeDescriptor.Name == "WebForm");
            if (webFormPart != null)
            {
                dynamic dPart = webFormPart;

                dynamic root = null;
                try { if (dPart.Form != null) root = dPart.Form; } catch { }
                if (root == null) { try { if (dPart.WebForm != null && dPart.WebForm.Form != null) root = dPart.WebForm.Form; } catch { } }

                if (root != null)
                {
                    if (segments.Length > 1)
                    {
                        var qualified = FindByPath(root, segments);
                        if (qualified != null) return qualified;
                    }
                    var ctrl = FindInControlCollection(root, leaf);
                    if (ctrl != null) return ctrl;
                }
            }

            // FR#4 + FR#5 last-resort: if the user passed a bare name that matches a Variable,
            // accept it. This is mostly to keep error messages sane when the agent forgets the
            // `&` prefix; explicit `&Name` is still the recommended form.
            var fallbackVar = FindVariable(obj, leaf);
            if (fallbackVar != null) return fallbackVar;

            return null;
        }

        // FR#4 + FR#5: resolve a Variable from the VariablesPart by name.
        private dynamic FindVariable(KBObject obj, string varName)
        {
            if (string.IsNullOrEmpty(varName)) return null;
            try
            {
                var vPart = obj.Parts.Cast<KBObjectPart>().FirstOrDefault(p => p.GetType().Name.Equals("VariablesPart"));
                if (vPart == null) return null;
                dynamic dPart = vPart;
                foreach (dynamic v in dPart.Variables)
                {
                    string n = null;
                    try { n = (string)v.Name; } catch { }
                    if (!string.IsNullOrEmpty(n) && string.Equals(n, varName, StringComparison.OrdinalIgnoreCase))
                    {
                        return v;
                    }
                }
            }
            catch (Exception ex) { Logger.Debug("FindVariable: " + ex.Message); }
            return null;
        }

        private dynamic FindByPath(dynamic root, string[] segments)
        {
            dynamic current = root;
            foreach (var seg in segments)
            {
                if (current == null) return null;
                current = FindInControlCollection(current, seg);
                if (current == null) return null;
            }
            return current;
        }

        private dynamic FindInControlCollection(dynamic root, string name)
        {
            if (root == null) return null;
            try { if (string.Equals(root.Name, name, StringComparison.OrdinalIgnoreCase)) return root; } catch {}

            try {
                if (root.Controls != null) {
                    foreach (dynamic child in root.Controls) {
                        var found = FindInControlCollection(child, name);
                        if (found != null) return found;
                    }
                }
            } catch {}
            return null;
        }

        private JObject SerializeProperties(dynamic container)
        {
            var result = new JObject();
            var props = new JArray();

            try
            {
                if (container != null && container.Properties != null)
                {
                    foreach (dynamic prop in container.Properties)
                    {
                        try {
                            var pObj = new JObject();
                            pObj["name"] = prop.Name.ToString();
                            pObj["value"] = prop.Value?.ToString() ?? "";
                            
                            try {
                                if (prop.Definition != null) {
                                    pObj["type"] = prop.Definition.Type.ToString();
                                    pObj["readOnly"] = prop.Definition.ReadOnly;
                                }
                            } catch {}

                            props.Add(pObj);
                        } catch { }
                    }
                }
            }
            catch (Exception ex) { Logger.Debug($"General error in SerializeProperties: {ex.Message}"); }

            result["properties"] = props;
            return result;
        }
    }
}
