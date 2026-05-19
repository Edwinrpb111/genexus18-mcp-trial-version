using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Artech.Architecture.Common.Objects;

namespace GxMcp.Worker.Helpers
{
    /// <summary>
    /// W6 (friction-report 2026-05-19 roadmap): Theme introspection. Walks a GeneXus
    /// Theme KBObject and surfaces its class catalog so the agent can pick CSS classes
    /// by name instead of raw GUID references like "d4876646-…-4".
    ///
    /// Reflection-only — does not link against Artech.Genexus.Common.Objects.Theme
    /// directly (would force a build dep on the version-specific assembly). All access
    /// goes through PropertyInfo / MethodInfo to stay version-tolerant.
    /// </summary>
    public static class ThemeInspector
    {
        /// <summary>
        /// Returns true if the KBObject is a Theme (case-insensitive type name match).
        /// Used by AnalyzeService to route inspect calls.
        /// </summary>
        public static bool IsTheme(KBObject obj)
        {
            if (obj == null) return false;
            string typeName = obj.TypeDescriptor?.Name ?? string.Empty;
            // In modern GeneXus the concrete types are ThemeForWeb / ThemeForSmartDevices.
            // The legacy "Theme" name still exists in some KBs as the abstract supertype.
            return string.Equals(typeName, "Theme", StringComparison.OrdinalIgnoreCase)
                || typeName.IndexOf("ThemeFor", StringComparison.OrdinalIgnoreCase) == 0;
        }

        /// <summary>
        /// Build the JSON view of a theme: header (guid, name, description) and the
        /// flattened class list. By default summary only; pass <c>detail="full"</c>
        /// to include CSS rule strings + property dictionaries.
        /// </summary>
        public static JObject InspectTheme(KBObject themeObj, string detail = "summary", string controlTypeFilter = null, string nameFilter = null, int limit = 100)
        {
            var result = new JObject();
            if (themeObj == null) return result;

            // 1. Header — guid + name straight from KBObject.
            result["guid"] = SafeGuid(themeObj);
            result["name"] = themeObj.Name;
            result["description"] = themeObj.Description;

            // 2. Locate the ThemeStylesPart via the KBObject.Parts collection — the part
            // type lives in Artech.Genexus.Common.Parts, name "ThemeStylesPart". We use
            // duck-typing so we don't have to link the assembly: any part whose CLR type
            // name ends with "ThemeStylesPart" qualifies.
            object stylesPart = null;
            try
            {
                foreach (KBObjectPart p in themeObj.Parts)
                {
                    if (p == null) continue;
                    var n = p.GetType().Name ?? string.Empty;
                    if (n.IndexOf("ThemeStyles", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        stylesPart = p;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                result["error"] = "parts enumeration failed: " + ex.Message;
                return result;
            }
            if (stylesPart == null)
            {
                result["classCount"] = 0;
                result["classes"] = new JArray();
                result["warning"] = "ThemeStyles part not found on this Theme — possibly a stub or unsupported variant.";
                return result;
            }

            // 3. Enumerate classes. The part typically exposes:
            //   - `Styles` (a collection of ThemeStyle)
            //   - or `GetAllStyles()` method (returns IEnumerable<ThemeStyle>)
            IEnumerable styles = TryGetStyles(stylesPart);
            if (styles == null)
            {
                result["classCount"] = 0;
                result["classes"] = new JArray();
                result["warning"] = "Could not enumerate styles on " + stylesPart.GetType().FullName;
                return result;
            }

            bool fullDetail = string.Equals(detail, "full", StringComparison.OrdinalIgnoreCase);
            var classes = new JArray();
            int total = 0, emitted = 0, skippedByFilter = 0;
            foreach (object style in styles)
            {
                total++;
                if (style == null) continue;
                // Only emit class-style entries (skip inheritance markers, palettes, etc).
                string clrName = style.GetType().Name;
                if (clrName.IndexOf("ClassThemeStyle", StringComparison.OrdinalIgnoreCase) < 0
                    && clrName.IndexOf("ClassStyle", StringComparison.OrdinalIgnoreCase) < 0
                    && clrName.IndexOf("ThemeClass", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                string name = TryReadString(style, "Name");
                string baseName = TryReadString(style, "BaseClassName") ?? TryReadString(style, "ParentClassName");
                bool isPredefined = TryReadBool(style, "IsPredefined") ?? false;
                JArray controlTypes = ResolveControlTypes(style);

                // Filtering
                if (!string.IsNullOrEmpty(controlTypeFilter)
                    && !controlTypes.Any(ct => string.Equals(ct?.ToString(), controlTypeFilter, StringComparison.OrdinalIgnoreCase)))
                { skippedByFilter++; continue; }
                if (!string.IsNullOrEmpty(nameFilter)
                    && (name == null || name.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) < 0))
                { skippedByFilter++; continue; }

                if (emitted >= limit)
                {
                    // still count remaining for the truncated total
                    continue;
                }

                var entry = new JObject {
                    ["name"] = name,
                    ["parent"] = baseName,
                    ["isPredefined"] = isPredefined,
                    ["category"] = clrName,
                    ["controlTypes"] = controlTypes
                };

                if (fullDetail)
                {
                    string cssRule = TryReadString(style, "GetStyleRule")
                                  ?? TryInvokeString(style, "GetStyleRule", new object[] { false, null });
                    if (!string.IsNullOrEmpty(cssRule)) entry["cssRule"] = cssRule;

                    var props = TryReadProperties(style);
                    if (props != null) entry["properties"] = props;
                }

                classes.Add(entry);
                emitted++;
            }

            result["classCount"] = total;
            result["emittedCount"] = emitted;
            result["truncated"] = emitted < (total - skippedByFilter);
            if (!string.IsNullOrEmpty(controlTypeFilter)) result["controlTypeFilter"] = controlTypeFilter;
            if (!string.IsNullOrEmpty(nameFilter)) result["nameFilter"] = nameFilter;
            result["classes"] = classes;
            return result;
        }

        private static IEnumerable TryGetStyles(object stylesPart)
        {
            // 1) Property "Styles"
            try
            {
                var p = stylesPart.GetType().GetProperty("Styles", BindingFlags.Public | BindingFlags.Instance);
                if (p != null)
                {
                    var v = p.GetValue(stylesPart) as IEnumerable;
                    if (v != null) return v;
                }
            }
            catch { }

            // 2) Method "GetAllStyles()"
            try
            {
                var m = stylesPart.GetType().GetMethod("GetAllStyles", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                if (m != null)
                {
                    var v = m.Invoke(stylesPart, null) as IEnumerable;
                    if (v != null) return v;
                }
            }
            catch { }

            return null;
        }

        // Resolve control compatibility via (in order):
        //   1. ThemeClass.ThemeTypes (IEnumerable<string>) — authoritative per W6 probe.
        //   2. ClassThemeStyle subtype name (AttributeClassThemeStyle → "gxAttribute" inference).
        //   3. GetInternalType() — fallback string.
        private static JArray ResolveControlTypes(object style)
        {
            var arr = new JArray();
            try
            {
                // Probe via Entity property which returns the ThemeClass KBObject.
                var entityProp = style.GetType().GetProperty("Entity", BindingFlags.Public | BindingFlags.Instance);
                object entity = entityProp?.GetValue(style);
                if (entity != null)
                {
                    var tt = entity.GetType().GetProperty("ThemeTypes", BindingFlags.Public | BindingFlags.Instance);
                    var coll = tt?.GetValue(entity) as IEnumerable;
                    if (coll != null)
                    {
                        foreach (var s in coll)
                        {
                            var str = s?.ToString();
                            if (!string.IsNullOrEmpty(str)) arr.Add(str);
                        }
                        if (arr.Count > 0) return arr;
                    }
                }
            }
            catch { }

            // Fallback by CLR name: "AttributeClassThemeStyle" → "gxAttribute"
            string clr = style.GetType().Name;
            string inferred = null;
            if (clr.StartsWith("Attribute", StringComparison.OrdinalIgnoreCase)) inferred = "gxAttribute";
            else if (clr.StartsWith("Button", StringComparison.OrdinalIgnoreCase)) inferred = "gxButton";
            else if (clr.StartsWith("Grid", StringComparison.OrdinalIgnoreCase)) inferred = "gxGrid";
            else if (clr.StartsWith("TextBlock", StringComparison.OrdinalIgnoreCase)) inferred = "gxTextBlock";
            else if (clr.StartsWith("Table", StringComparison.OrdinalIgnoreCase)) inferred = "table";
            else if (clr.StartsWith("Form", StringComparison.OrdinalIgnoreCase)) inferred = "form";
            else if (clr.StartsWith("Image", StringComparison.OrdinalIgnoreCase)) inferred = "gxImage";
            if (!string.IsNullOrEmpty(inferred)) arr.Add(inferred);

            return arr;
        }

        private static string SafeGuid(KBObject obj)
        {
            try
            {
                var keyProp = obj.GetType().GetProperty("Guid") ?? obj.GetType().GetProperty("Key");
                var v = keyProp?.GetValue(obj);
                return v?.ToString();
            }
            catch { return null; }
        }

        private static string TryReadString(object target, string propOrMethodName)
        {
            try
            {
                var p = target.GetType().GetProperty(propOrMethodName, BindingFlags.Public | BindingFlags.Instance);
                if (p != null) return p.GetValue(target) as string;
            }
            catch { }
            try
            {
                var m = target.GetType().GetMethod(propOrMethodName, BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                if (m != null && m.ReturnType == typeof(string)) return m.Invoke(target, null) as string;
            }
            catch { }
            return null;
        }

        private static string TryInvokeString(object target, string methodName, object[] args)
        {
            try
            {
                var ms = target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.Name == methodName && m.GetParameters().Length == args.Length)
                    .ToList();
                foreach (var m in ms)
                {
                    try
                    {
                        var v = m.Invoke(target, args);
                        return v as string;
                    }
                    catch { }
                }
            }
            catch { }
            return null;
        }

        private static bool? TryReadBool(object target, string propName)
        {
            try
            {
                var p = target.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                if (p?.PropertyType == typeof(bool)) return (bool)p.GetValue(target);
            }
            catch { }
            return null;
        }

        // Returns a dict of property-name → value-string by walking SerializedProperties() if
        // available; otherwise null. Best-effort, swallows errors.
        private static JObject TryReadProperties(object style)
        {
            try
            {
                var m = style.GetType().GetMethod("SerializedProperties", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                if (m == null) return null;
                var coll = m.Invoke(style, null) as IEnumerable;
                if (coll == null) return null;
                var obj = new JObject();
                foreach (object p in coll)
                {
                    var nameProp = p.GetType().GetProperty("Name");
                    var valueProp = p.GetType().GetProperty("Value");
                    string n = nameProp?.GetValue(p) as string;
                    object v = valueProp?.GetValue(p);
                    if (!string.IsNullOrEmpty(n)) obj[n] = v?.ToString();
                }
                return obj;
            }
            catch { return null; }
        }
    }
}
