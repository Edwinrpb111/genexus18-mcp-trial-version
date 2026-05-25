using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security;
using System.Text;
using Newtonsoft.Json.Linq;

namespace GxMcp.Worker.Helpers
{
    /// <summary>
    /// W3 (mcp-roadmap-ide-parity 2026-05-19) — pure layout XML builder for the
    /// genexus_create_popup tool. Reverse-engineered from IDE-authored popups in the
    /// AcademicoHomolog1 KB (e.g. ListaAtiCPAlunoUniGra). Emits a Form type="layout"
    /// table-responsive structure so that Radio Button / Combo Box bindings render
    /// editable at runtime — see LayoutGotchaScanner GotchaGxAttributeHtmlFormDiscreteReadOnly.
    ///
    /// Theme classes (Attribute, Button, Form) are referenced by NAME — the generator/IDE
    /// resolves them at runtime against the KB's active Theme. No GUIDs required.
    ///
    /// Pure; no SDK / KB dependency, fully unit-testable.
    /// </summary>
    public static class PopupLayoutBuilder
    {
        public sealed class PopupSpec
        {
            public string Title { get; set; }
            public string Description { get; set; }
            public List<PopupInput> Inputs { get; set; } = new List<PopupInput>();
            public List<PopupButton> Buttons { get; set; } = new List<PopupButton>();
            public List<string> InParms { get; set; } = new List<string>();
            public List<string> OutParms { get; set; } = new List<string>();
        }

        public sealed class PopupInput
        {
            public string Type { get; set; }      // "radio" | "combo" | "text"
            public string VarName { get; set; }
            public string Label { get; set; }
            public List<PopupOption> Options { get; set; } = new List<PopupOption>();
            public string ShowWhen { get; set; }
        }

        public sealed class PopupOption
        {
            public string Value { get; set; }
            public string Label { get; set; }
        }

        public sealed class PopupButton
        {
            public string Caption { get; set; }
            public string Event { get; set; }
        }

        public sealed class ParsedSpec
        {
            public PopupSpec Spec { get; set; }
            public List<string> Errors { get; set; } = new List<string>();
            public bool IsValid => Errors.Count == 0;
        }

        public static ParsedSpec ParseSpec(JObject spec)
        {
            var result = new ParsedSpec { Spec = new PopupSpec() };
            if (spec == null)
            {
                result.Errors.Add("spec is required");
                return result;
            }

            result.Spec.Title = spec["title"]?.ToString();
            result.Spec.Description = spec["description"]?.ToString();

            if (spec["inputs"] is JArray inputs)
            {
                foreach (var t in inputs.OfType<JObject>())
                {
                    var inp = new PopupInput
                    {
                        Type = t["type"]?.ToString(),
                        VarName = t["varName"]?.ToString(),
                        Label = t["label"]?.ToString(),
                        ShowWhen = t["showWhen"]?.ToString()
                    };
                    if (t["options"] is JArray opts)
                    {
                        foreach (var o in opts.OfType<JObject>())
                        {
                            inp.Options.Add(new PopupOption
                            {
                                Value = o["value"]?.ToString(),
                                Label = o["label"]?.ToString()
                            });
                        }
                    }
                    result.Spec.Inputs.Add(inp);
                }
            }

            if (spec["buttons"] is JArray buttons)
            {
                foreach (var b in buttons.OfType<JObject>())
                {
                    result.Spec.Buttons.Add(new PopupButton
                    {
                        Caption = b["caption"]?.ToString(),
                        Event = b["event"]?.ToString()
                    });
                }
            }

            if (spec["inParms"] is JArray inP)
                result.Spec.InParms = inP.Select(t => t.ToString()).ToList();
            if (spec["outParms"] is JArray outP)
                result.Spec.OutParms = outP.Select(t => t.ToString()).ToList();

            // Validation
            if (result.Spec.Inputs.Count == 0)
                result.Errors.Add("At least one input is required");

            int i = 0;
            foreach (var inp in result.Spec.Inputs)
            {
                if (string.IsNullOrWhiteSpace(inp.VarName))
                    result.Errors.Add($"inputs[{i}].varName is required");
                if (string.IsNullOrWhiteSpace(inp.Type))
                    result.Errors.Add($"inputs[{i}].type is required");
                else if (!IsKnownType(inp.Type))
                    result.Errors.Add($"inputs[{i}].type '{inp.Type}' is not one of radio|combo|text");
                if ((inp.Type == "radio" || inp.Type == "combo") && inp.Options.Count == 0)
                    result.Errors.Add($"inputs[{i}] (type={inp.Type}) requires non-empty options[]");
                i++;
            }

            if (result.Spec.Buttons.Count == 0)
            {
                // Default a Confirmar/Enter button if none supplied — matches the IDE
                // popup convention.
                result.Spec.Buttons.Add(new PopupButton { Caption = "Confirmar", Event = "Enter" });
            }

            return result;
        }

        private static bool IsKnownType(string t) =>
            string.Equals(t, "radio", StringComparison.OrdinalIgnoreCase)
            || string.Equals(t, "combo", StringComparison.OrdinalIgnoreCase)
            || string.Equals(t, "text", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Builds the WebForm part XML for a Form type="layout" popup body.
        /// </summary>
        public static string BuildLayoutXml(PopupSpec spec)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));

            var sb = new StringBuilder();
            sb.Append("<GxMultiForm>");
            sb.Append("<Form id=\"1\" type=\"layout\" Class=\"Form\">");
            sb.Append("<table id=\"TblMain\" class=\"Table\">");

            // Title row
            if (!string.IsNullOrWhiteSpace(spec.Title))
            {
                sb.Append("<row id=\"RowTitle\"><cell id=\"CellTitle\" ColSpan=\"2\">");
                sb.Append("<gxTextBlock id=\"LblTitle\" CaptionExpression=\"")
                  .Append(Esc(spec.Title))
                  .Append("\" Class=\"TextBlock\"/>");
                sb.Append("</cell></row>");
            }

            // Description row
            if (!string.IsNullOrWhiteSpace(spec.Description))
            {
                sb.Append("<row id=\"RowDescription\"><cell id=\"CellDescription\" ColSpan=\"2\">");
                sb.Append("<gxTextBlock id=\"LblDescription\" CaptionExpression=\"")
                  .Append(Esc(spec.Description))
                  .Append("\" Class=\"TextBlock\"/>");
                sb.Append("</cell></row>");
            }

            // Inputs
            int idx = 0;
            foreach (var inp in spec.Inputs)
            {
                string safeVar = SafeId(inp.VarName);
                string groupId = "GrpInput" + idx;
                string rowId = "RowInput" + idx;
                string lblId = "LblInput" + idx;
                string attId = "AttInput" + idx;

                sb.Append("<row id=\"").Append(rowId).Append("\">");
                sb.Append("<cell id=\"CellInput").Append(idx).Append("\" ColSpan=\"2\">");
                sb.Append("<group id=\"").Append(groupId).Append("\" Class=\"Group\">");
                sb.Append("<table id=\"Tbl").Append(groupId).Append("\" class=\"Table\">");

                // Label
                if (!string.IsNullOrWhiteSpace(inp.Label))
                {
                    sb.Append("<row id=\"").Append(rowId).Append("L\"><cell id=\"")
                      .Append("Cell").Append(lblId).Append("\">");
                    sb.Append("<gxTextBlock id=\"").Append(lblId).Append("\" CaptionExpression=\"")
                      .Append(Esc(inp.Label))
                      .Append("\" Class=\"TextBlockLabel\"/>");
                    sb.Append("</cell></row>");
                }

                // Control
                sb.Append("<row id=\"").Append(rowId).Append("C\"><cell id=\"")
                  .Append("Cell").Append(attId).Append("\">");

                string controlType = TypeToControlType(inp.Type);
                sb.Append("<gxAttribute id=\"").Append(attId)
                  .Append("\" AttID=\"&amp;").Append(safeVar).Append("\"");
                if (!string.IsNullOrEmpty(controlType))
                {
                    sb.Append(" ControlType=\"").Append(Esc(controlType)).Append("\"");
                    if (controlType == "Radio Button" || controlType == "Combo Box")
                    {
                        sb.Append(" ControlValues=\"").Append(Esc(BuildControlValues(inp.Options))).Append("\"");
                    }
                }
                sb.Append(" Class=\"Attribute\"/>");
                sb.Append("</cell></row>");

                sb.Append("</table></group>");
                sb.Append("</cell></row>");
                idx++;
            }

            // Buttons row
            sb.Append("<row id=\"RowButtons\"><cell id=\"CellButtons\" ColSpan=\"2\" HAlign=\"right\">");
            sb.Append("<table id=\"TblButtons\" class=\"Table\"><row id=\"RowBtns\">");
            int b = 0;
            foreach (var btn in spec.Buttons)
            {
                string caption = string.IsNullOrEmpty(btn.Caption) ? "Confirmar" : btn.Caption;
                string evt = string.IsNullOrEmpty(btn.Event) ? "Enter" : btn.Event;
                sb.Append("<cell id=\"CellBtn").Append(b).Append("\">");
                sb.Append("<action id=\"Btn").Append(SafeId(evt)).Append("\" Caption=\"")
                  .Append(Esc(caption)).Append("\" onClickEvent=\"'")
                  .Append(Esc(evt)).Append("'\" Class=\"Button\"/>");
                sb.Append("</cell>");
                b++;
            }
            sb.Append("</row></table></cell></row>");

            sb.Append("</table>");
            sb.Append("</Form>");
            sb.Append("</GxMultiForm>");
            return sb.ToString();
        }

        /// <summary>
        /// Builds the WebForm part XML for a WorkWithPlus-convention dual-form
        /// layout popup body: <c>&lt;Form type="layout"&gt;&lt;detail&gt;&lt;layout id="GUID"&gt;
        /// &lt;table controlName="LayoutMainTable" tableType="Responsive" class="THEMEGUID-NN"&gt;</c>.
        /// Required by KBs with <c>wwpMetadata.isWorkWithPlusAware: true</c> — the flat schema
        /// emitted by <see cref="BuildLayoutXml"/> is rejected by
        /// <c>WebLayoutHandler.LoadPanelElement</c> on these KBs.
        ///
        /// <paramref name="themeClassPrefix"/> is the 36-char theme GUID harvested from an existing
        /// layout-form WebPanel (e.g. <c>d4876646-98dd-419b-8c1c-896f83c48368</c> in
        /// AcademicoHomolog1). When null/empty, falls back to symbolic theme names
        /// ("Attribute", "Button", …) which most WWP themes also resolve.
        /// Per-element class suffixes: -4 data attribute, -24 textblock, -46 action,
        /// -59 errorviewer.
        /// </summary>
        public static string BuildWwpLayoutXml(PopupSpec spec, string themeClassPrefix)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));

            string ClassRef(int suffix, string symbolicFallback)
            {
                if (!string.IsNullOrEmpty(themeClassPrefix))
                    return themeClassPrefix + "-" + suffix.ToString(CultureInfo.InvariantCulture);
                return symbolicFallback;
            }

            string Guid() => System.Guid.NewGuid().ToString();
            string layoutId = Guid();
            string mainTableId = Guid();

            var sb = new StringBuilder();
            sb.Append("<GxMultiForm rootId=\"1\" version=\"html:15.0.0;layout:17.11.0\">");
            sb.Append("<Form id=\"1\" type=\"layout\" Class=\"Form\">");
            sb.Append("<detail>");
            sb.Append("<layout id=\"").Append(layoutId).Append("\">");
            sb.Append("<table id=\"").Append(mainTableId)
              .Append("\" controlName=\"LayoutMainTable\" tableType=\"Responsive\" responsiveSizes=\"[]\" class=\"")
              .Append(Esc(ClassRef(23, "TableGrid"))).Append("\">");

            // ErrorViewer row
            sb.Append("<row id=\"").Append(Guid()).Append("\">");
            sb.Append("<cell id=\"").Append(Guid()).Append("\" ColSpan=\"2\">");
            sb.Append("<errorviewer id=\"").Append(Guid())
              .Append("\" controlName=\"ErrorViewer\" class=\"")
              .Append(Esc(ClassRef(59, "ErrorViewer"))).Append("\" />");
            sb.Append("</cell></row>");

            // Title
            if (!string.IsNullOrWhiteSpace(spec.Title))
            {
                sb.Append("<row id=\"").Append(Guid()).Append("\">");
                sb.Append("<cell id=\"").Append(Guid()).Append("\" ColSpan=\"2\">");
                sb.Append("<textblock id=\"").Append(Guid())
                  .Append("\" controlName=\"TbTitle\" captionExpression=\"")
                  .Append(Esc(spec.Title)).Append("\" Format=\"Text\" class=\"")
                  .Append(Esc(ClassRef(24, "TextBlock"))).Append("\" />");
                sb.Append("</cell></row>");
            }

            // Description
            if (!string.IsNullOrWhiteSpace(spec.Description))
            {
                sb.Append("<row id=\"").Append(Guid()).Append("\">");
                sb.Append("<cell id=\"").Append(Guid()).Append("\" ColSpan=\"2\">");
                sb.Append("<textblock id=\"").Append(Guid())
                  .Append("\" controlName=\"TbDescription\" captionExpression=\"")
                  .Append(Esc(spec.Description)).Append("\" Format=\"Text\" class=\"")
                  .Append(Esc(ClassRef(24, "TextBlock"))).Append("\" />");
                sb.Append("</cell></row>");
            }

            // Inputs
            int idx = 0;
            foreach (var inp in spec.Inputs)
            {
                string safeVar = SafeId(inp.VarName);
                sb.Append("<row id=\"").Append(Guid()).Append("\">");
                sb.Append("<cell id=\"").Append(Guid()).Append("\" ColSpan=\"2\">");

                if (string.Equals(inp.Type, "radio", StringComparison.OrdinalIgnoreCase))
                {
                    // WWP radio: <data attribute="var:safeVar" labelPosition="None"
                    // class="<theme>-4" PATTERN_ELEMENT_CUSTOM_PROPERTIES="&lt;Properties&gt;…">
                    sb.Append("<data id=\"").Append(Guid())
                      .Append("\" controlName=\"Att").Append(safeVar)
                      .Append("\" attribute=\"&amp;").Append(safeVar)
                      .Append("\" labelPosition=\"None\" class=\"")
                      .Append(Esc(ClassRef(4, "Attribute"))).Append("\" ");
                    sb.Append("PATTERN_ELEMENT_CUSTOM_PROPERTIES=\"")
                      .Append(BuildWwpRadioCustomProps(inp))
                      .Append("\" />");
                }
                else if (string.Equals(inp.Type, "combo", StringComparison.OrdinalIgnoreCase))
                {
                    sb.Append("<data id=\"").Append(Guid())
                      .Append("\" controlName=\"Att").Append(safeVar)
                      .Append("\" attribute=\"&amp;").Append(safeVar)
                      .Append("\" labelCaption=\"").Append(Esc(inp.Label ?? safeVar))
                      .Append("\" class=\"").Append(Esc(ClassRef(4, "Attribute")))
                      .Append("\" PATTERN_ELEMENT_CUSTOM_PROPERTIES=\"")
                      .Append(BuildWwpComboCustomProps(inp))
                      .Append("\" />");
                }
                else
                {
                    // text — default Edit control
                    sb.Append("<data id=\"").Append(Guid())
                      .Append("\" controlName=\"Att").Append(safeVar)
                      .Append("\" attribute=\"&amp;").Append(safeVar)
                      .Append("\" labelCaption=\"").Append(Esc(inp.Label ?? safeVar))
                      .Append("\" class=\"").Append(Esc(ClassRef(4, "Attribute")))
                      .Append("\" />");
                }
                sb.Append("</cell></row>");
                idx++;
            }

            // Buttons row (HAlign right)
            sb.Append("<row id=\"").Append(Guid()).Append("\">");
            sb.Append("<cell id=\"").Append(Guid()).Append("\" ColSpan=\"2\" HAlign=\"right\">");
            foreach (var btn in spec.Buttons)
            {
                string caption = string.IsNullOrEmpty(btn.Caption) ? "Confirmar" : btn.Caption;
                string evt = string.IsNullOrEmpty(btn.Event) ? "Enter" : btn.Event;
                // WWP convention: onClickEvent is the unquoted event name (no single-quote
                // wrap that BuildLayoutXml uses for flat-schema gxButton). Avoids the
                // descriptor-name fixup path entirely.
                sb.Append("<action id=\"").Append(Guid())
                  .Append("\" controlName=\"Btn").Append(SafeId(evt))
                  .Append("\" onClickEvent=\"").Append(Esc(evt))
                  .Append("\" caption=\"").Append(Esc(caption))
                  .Append("\" class=\"").Append(Esc(ClassRef(46, "Button")))
                  .Append("\" />");
            }
            sb.Append("</cell></row>");

            sb.Append("</table>");
            sb.Append("</layout>");
            sb.Append("</detail>");
            sb.Append("</Form>");
            sb.Append("</GxMultiForm>");
            return sb.ToString();
        }

        // WWP radio Properties payload — entity-encoded for the
        // PATTERN_ELEMENT_CUSTOM_PROPERTIES attribute value.
        private static string BuildWwpRadioCustomProps(PopupInput inp)
        {
            // ControlValues separator for WWP radios is "Label:Value,Label:Value"
            // (NOT the ";"+"," form used by flat-schema gxAttribute).
            string controlValues = string.Join(",",
                inp.Options.Select(o => (o.Label ?? "") + ":" + (o.Value ?? "")));
            var inner = new StringBuilder();
            inner.Append("<Properties>");
            inner.Append("<Property><Name>ControlType</Name><Value>Radio Button</Value></Property>");
            inner.Append("<Property><Name>ControlValues</Name><Value>").Append(SecurityElement.Escape(controlValues)).Append("</Value></Property>");
            inner.Append("<Property><Name>RadioDirection</Name><Value>Vertical</Value></Property>");
            inner.Append("</Properties>");
            return SecurityElement.Escape(inner.ToString());
        }

        private static string BuildWwpComboCustomProps(PopupInput inp)
        {
            string controlValues = string.Join(",",
                inp.Options.Select(o => (o.Label ?? "") + ":" + (o.Value ?? "")));
            var inner = new StringBuilder();
            inner.Append("<Properties>");
            inner.Append("<Property><Name>ControlType</Name><Value>Combo Box</Value></Property>");
            inner.Append("<Property><Name>ControlValues</Name><Value>").Append(SecurityElement.Escape(controlValues)).Append("</Value></Property>");
            inner.Append("</Properties>");
            return SecurityElement.Escape(inner.ToString());
        }

        /// <summary>
        /// Builds the Rules part source from inParms / outParms.
        /// </summary>
        public static string BuildRulesSource(PopupSpec spec)
        {
            var parts = new List<string>();
            foreach (var p in spec.InParms ?? new List<string>())
            {
                string varName = ExtractVarName(p);
                if (!string.IsNullOrEmpty(varName))
                    parts.Add("in:&" + varName);
            }
            foreach (var p in spec.OutParms ?? new List<string>())
            {
                string varName = ExtractVarName(p);
                if (!string.IsNullOrEmpty(varName))
                    parts.Add("out:&" + varName);
            }
            if (parts.Count == 0) return string.Empty;
            return "parm(" + string.Join(", ", parts) + ");\n";
        }

        /// <summary>
        /// Builds the Events part source. Includes the Enter event placeholder plus
        /// a Refresh event when any input has a showWhen condition.
        /// </summary>
        public static string BuildEventsSource(PopupSpec spec)
        {
            var sb = new StringBuilder();
            // Refresh event for showWhen visibility
            var conditional = (spec.Inputs ?? new List<PopupInput>())
                .Where(i => !string.IsNullOrWhiteSpace(i.ShowWhen))
                .ToList();
            if (conditional.Count > 0)
            {
                sb.AppendLine("Event Refresh");
                int idx = 0;
                foreach (var inp in spec.Inputs)
                {
                    if (!string.IsNullOrWhiteSpace(inp.ShowWhen))
                    {
                        var cond = TranslateCondition(inp.ShowWhen);
                        sb.AppendLine("    if " + cond);
                        sb.AppendLine("        GrpInput" + idx + ".Visible = True");
                        sb.AppendLine("    else");
                        sb.AppendLine("        GrpInput" + idx + ".Visible = False");
                        sb.AppendLine("    endif");
                    }
                    idx++;
                }
                sb.AppendLine("EndEvent");
                sb.AppendLine();
            }

            // One Enter event placeholder per declared button event (deduped by name).
            var evtNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var btn in spec.Buttons ?? new List<PopupButton>())
            {
                string e = string.IsNullOrEmpty(btn.Event) ? "Enter" : btn.Event;
                if (!evtNames.Add(e)) continue;
                sb.AppendLine("Event " + e);
                sb.AppendLine("    // TODO: agent provides " + e + " event body");
                sb.AppendLine("EndEvent");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        // === helpers ===

        private static string TypeToControlType(string t)
        {
            if (string.Equals(t, "radio", StringComparison.OrdinalIgnoreCase)) return "Radio Button";
            if (string.Equals(t, "combo", StringComparison.OrdinalIgnoreCase)) return "Combo Box";
            return null; // text → default Edit
        }

        private static string BuildControlValues(List<PopupOption> options)
        {
            // Convention: "<label>,<value>;<label>,<value>" — matches IDE-authored Static
            // ControlValues string used by Radio Button / Combo Box gxAttribute controls.
            return string.Join(";", options.Select(o => (o.Label ?? "") + "," + (o.Value ?? "")));
        }

        private static string TranslateCondition(string showWhen)
        {
            // Accept input like "RespRegProf == 'S'" — pass through with a leading &
            // on the variable identifier so it's a valid GeneXus event expression.
            var s = (showWhen ?? "").Trim();
            if (s.Length == 0) return "True";
            // Replace '==' with '=' (GeneXus equality)
            s = s.Replace("==", "=");
            // Prefix the first identifier with & if not already
            int i = 0;
            while (i < s.Length && (char.IsLetter(s[i]) || s[i] == '_')) i++;
            if (i > 0 && s[0] != '&')
                s = "&" + s;
            return s + " then";
        }

        private static string ExtractVarName(string parm)
        {
            if (string.IsNullOrWhiteSpace(parm)) return null;
            // Accept "VarName" or "VarName:Numeric(2)" or "&VarName"
            string s = parm.Trim().TrimStart('&');
            int colon = s.IndexOf(':');
            if (colon > 0) s = s.Substring(0, colon).Trim();
            return s;
        }

        private static string SafeId(string s)
        {
            if (string.IsNullOrEmpty(s)) return "X";
            var chars = s.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray();
            return chars.Length == 0 ? "X" : new string(chars);
        }

        private static string Esc(string s) => SecurityElement.Escape(s ?? "");
    }
}
