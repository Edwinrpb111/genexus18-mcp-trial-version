using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml;

namespace GxMcp.Worker.Helpers
{
    /// <summary>
    /// Applies a set of WebFormPropertyDelta items to a live WebFormPart by going through
    /// the canonical SDK path:
    ///   1. Enumerate IWebTag instances via WebFormHelper.EnumerateWebTag(part).
    ///   2. Match by tag.Node id / ControlName attribute.
    ///   3. Call WebFormEditable.SetTagProperty(tag, tag.Properties, null, propName, value, ref changed, null)
    ///      so the SDK runs the proper PropertyValueConverter and updates typed Properties.
    ///
    /// On save (later), WebFormPart.BeforeSaveKBObject iterates tags and calls
    /// tag.SaveProperties() — which writes the typed Properties back into the tag's XmlNode
    /// inside m_Document. Result: persisted XML matches the requested change WITHOUT us
    /// touching the raw Document.
    /// </summary>
    public static class WebFormTypedPropertyWriter
    {
        private const string HelperTypeName = "Artech.Genexus.Common.Parts.WebForm.WebFormHelper";
        private const string EditableTypeName = "Artech.Genexus.Common.Parts.WebForm.WebFormEditable";

        public static bool TryApply(object webFormPart, IReadOnlyList<WebFormPropertyDelta> deltas, out string failure)
        {
            failure = null;
            if (webFormPart == null) { failure = "part is null"; return false; }
            if (deltas == null || deltas.Count == 0) { failure = "no deltas"; return false; }

            Type helperType = FindType(HelperTypeName);
            if (helperType == null) { failure = "WebFormHelper type not loaded"; return false; }

            Type editableType = FindType(EditableTypeName);
            if (editableType == null) { failure = "WebFormEditable type not loaded"; return false; }

            // Pick the EnumerateWebTag overload that takes (KBObject, XmlDocument) so tags are rooted
            // in part.Document — the SAME document the SDK's BeforeSaveKBObject iterates.
            // Access the m_Document FIELD (not the property) — the property may clone.
            XmlDocument partDocForEnum = null;
            try
            {
                var docField = webFormPart.GetType().GetField("m_Document",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                partDocForEnum = docField?.GetValue(webFormPart) as XmlDocument;
            }
            catch { }
            if (partDocForEnum == null) partDocForEnum = GetReadProperty(webFormPart, "Document") as XmlDocument;
            Logger.Info("[TypedWriter] m_Document field length=" + (partDocForEnum?.OuterXml.Length ?? -1));
            var partKbObj = GetReadProperty(webFormPart, "KBObject") ?? GetReadProperty(webFormPart, "ContainerObject") ?? GetReadProperty(webFormPart, "Parent") ?? GetReadProperty(webFormPart, "Container");
            MethodInfo enumerate = null;
            object[] enumArgs = null;
            if (partDocForEnum != null && partKbObj != null)
            {
                enumerate = helperType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m =>
                    {
                        if (m.Name != "EnumerateWebTag") return false;
                        var ps = m.GetParameters();
                        return ps.Length == 2 && ps[1].ParameterType == typeof(XmlDocument) && ps[0].ParameterType.IsInstanceOfType(partKbObj);
                    });
                if (enumerate != null) enumArgs = new object[] { partKbObj, partDocForEnum };
            }
            if (enumerate == null)
            {
                enumerate = helperType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m =>
                    {
                        if (m.Name != "EnumerateWebTag") return false;
                        var ps = m.GetParameters();
                        return ps.Length == 1 && ps[0].ParameterType.IsInstanceOfType(webFormPart);
                    });
                enumArgs = new object[] { webFormPart };
            }
            if (enumerate == null) { failure = "no EnumerateWebTag overload found"; return false; }
            Logger.Info("[TypedWriter] using " + enumerate.Name + "(" + string.Join(",", enumerate.GetParameters().Select(p => p.ParameterType.Name)) + ")");

            MethodInfo setTagProperty = editableType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "SetTagProperty");
            if (setTagProperty == null) { failure = "WebFormEditable.SetTagProperty not found"; return false; }

            // Alternative path: IWebTag.SetProperties(IDictionary) — higher-level API exposed by the
            // interface itself. Used when SetTagProperty throws because of TypeDescriptorContext=null.
            Type webTagInterface = FindType("Artech.Genexus.Common.Parts.WebForm.IWebTag");
            MethodInfo setPropertiesDict = webTagInterface?.GetMethod("SetProperties", new[] { typeof(IDictionary) });

            IDictionary<string, object> byId = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            IDictionary<string, object> byControlName = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            int total = 0;
            try
            {
                IEnumerable tags = (IEnumerable)enumerate.Invoke(null, enumArgs);
                foreach (var tag in tags)
                {
                    total++;
                    var node = GetReadProperty(tag, "Node") as XmlNode;
                    if (node?.Attributes == null) continue;
                    string id = node.Attributes["id"]?.Value;
                    string cn = node.Attributes["ControlName"]?.Value ?? node.Attributes["controlName"]?.Value;
                    if (!string.IsNullOrEmpty(id) && !byId.ContainsKey(id)) byId[id] = tag;
                    if (!string.IsNullOrEmpty(cn) && !byControlName.ContainsKey(cn)) byControlName[cn] = tag;
                }
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException ?? ex;
                failure = "EnumerateWebTag threw: " + inner.GetType().Name + ": " + inner.Message;
                return false;
            }
            Logger.Info("[TypedWriter] Indexed " + total + " IWebTag(s): " + byId.Count + " by id, " + byControlName.Count + " by ControlName.");

            // Group deltas by control so SetProperties is called once per tag with all changes.
            var byControl = new Dictionary<string, List<WebFormPropertyDelta>>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in deltas)
            {
                if (!byControl.TryGetValue(d.ControlName ?? string.Empty, out var list)) { list = new List<WebFormPropertyDelta>(); byControl[d.ControlName ?? string.Empty] = list; }
                list.Add(d);
            }

            foreach (var kv in byControl)
            {
                string controlName = kv.Key;
                object tag = null;
                if (!string.IsNullOrEmpty(controlName))
                {
                    byId.TryGetValue(controlName, out tag);
                    if (tag == null) byControlName.TryGetValue(controlName, out tag);
                }
                if (tag == null) { failure = "control '" + controlName + "' not found in tag enumeration"; return false; }

                // Canonical-XML strategy:
                //  1) Mutate the tag's XmlNode attributes directly. The Node is shared with m_Document,
                //     so this updates the on-disk XML for free.
                //  2) Invalidate the typed Property cache on the tag (m_Props=null, m_PropertiesLoaded=false).
                //     This forces the SDK to re-load typed Properties FROM the now-updated XmlNode the next
                //     time tag.Properties is accessed — which happens during BeforeSaveKBObject/SaveProperties.
                //     Result: SaveProperties writes the SAME values back to m_Document (no-op clobber).
                // `EnumerateWebTag` returns tags whose Node lives in an INTERNAL XmlDocument, not the
                // live part.Document. Mutating tag.Node alone wouldn't propagate to what gets persisted.
                // Find the matching element in part.Document by id/ControlName and mutate THAT instead.
                var partDoc = partDocForEnum;
                if (partDoc == null) { failure = "part m_Document is null"; return false; }
                XmlElement node = FindElementInPartDoc(partDoc, controlName);
                if (node == null) { failure = "no element id='" + controlName + "' (nor ControlName) in part.Document"; return false; }

                // FR#1 (friction-report 2026-05-19): properties whose XML attribute name differs
                // from the descriptor name MUST go through PropertiesObject.SetPropertyValueString,
                // not raw XML mutation. The HTML generator reads the XML attribute the SDK chose
                // (e.g. gxButton: descriptor "OnClickEvent" → XML attr "Event"), so writing the
                // descriptor name as an XML attribute leaves it unread → silent fallback to Enter.
                //
                // Strategy: extract these properties into a "descriptor delta" set; remaining
                // properties still go through raw XML mutation (which works for Caption, Class,
                // Visible, etc. where XML attr name == descriptor name).
                var descriptorDeltas = new List<WebFormPropertyDelta>();
                var rawXmlDeltas = new List<WebFormPropertyDelta>();
                foreach (var d in kv.Value)
                {
                    if (NeedsDescriptorPath(d.PropertyName)) descriptorDeltas.Add(d);
                    else rawXmlDeltas.Add(d);
                }

                // 1. Descriptor path — PropertiesObject.SetPropertyValueString
                if (descriptorDeltas.Count > 0)
                {
                    ApplyDescriptorDeltas(tag, controlName, descriptorDeltas);
                }

                // 2. Raw XML path — direct attribute mutation
                foreach (var d in rawXmlDeltas)
                {
                    if (d.Value == null)
                    {
                        node.Attributes.RemoveNamedItem(d.PropertyName);
                        Logger.Info("[TypedWriter] removed attr " + controlName + "." + d.PropertyName);
                        continue;
                    }
                    var attr = node.Attributes[d.PropertyName];
                    if (attr == null)
                    {
                        attr = node.OwnerDocument.CreateAttribute(d.PropertyName);
                        node.Attributes.Append(attr);
                    }
                    attr.Value = d.Value;
                    Logger.Info("[TypedWriter] node[" + controlName + "]." + d.PropertyName + " <- '" + Truncate(d.Value, 80) + "'");
                }

                // Invalidate the tag's cached typed Properties so the next read reloads from the new XML.
                InvalidateTagPropertyCache(tag, controlName);

                // CANONICAL FIX (session 4): update the typed PropertiesObject via
                // IWebTag.SetProperties(IDictionary). Direct XmlNode mutation alone leaves the
                // typed model untouched; on Save the SDK serializes BOTH layers and inserts
                // TWO EntityVersion rows for the WebFormPart — one from m_Document (our bytes)
                // and one regenerated from the stale typed model (= original bytes). The
                // EntityVersionComposition pointer at the parent WebPanel lands on the
                // regenerated sibling, so reads return the original. By updating the typed
                // model here, both serializations match and composition resolves correctly.
                if (setPropertiesDict != null)
                {
                    try
                    {
                        var dict = new System.Collections.Hashtable();
                        foreach (var d in kv.Value)
                        {
                            // Key is the XML attribute name; SetProperties handles the
                            // attribute↔typed-property mapping internally (e.g.,
                            // CaptionExpression → Caption with Tokens conversion).
                            dict[d.PropertyName] = d.Value;
                        }
                        setPropertiesDict.Invoke(tag, new object[] { dict });
                        Logger.Info("[TypedWriter] tag.SetProperties(IDictionary) invoked for '" + controlName + "' with " + dict.Count + " key(s).");
                    }
                    catch (Exception ex)
                    {
                        var inner = ex.InnerException ?? ex;
                        Logger.Info("[TypedWriter] tag.SetProperties threw: " + inner.GetType().Name + ": " + inner.Message + " — falling back to SetTagProperty per-key.");

                        // Fallback: WebFormEditable.SetTagProperty per key.
                        try
                        {
                            var propsBag = GetReadProperty(tag, "Properties");
                            foreach (var d in kv.Value)
                            {
                                if (propsBag == null) break;
                                bool changed = false;
                                var args = new object[] { tag, propsBag, null, d.PropertyName, (object)d.Value, changed, null };
                                setTagProperty.Invoke(null, args);
                                Logger.Info("[TypedWriter] SetTagProperty fallback: " + controlName + "." + d.PropertyName + " changed=" + args[5]);
                            }
                        }
                        catch (Exception ex2)
                        {
                            var inner2 = ex2.InnerException ?? ex2;
                            Logger.Info("[TypedWriter] SetTagProperty fallback also threw: " + inner2.GetType().Name + ": " + inner2.Message);
                        }
                    }
                }

                // Verify the mutation actually landed in part.Document by re-querying.
                var verify = FindElementInPartDoc(partDoc, controlName);
                foreach (var d in kv.Value)
                {
                    string after = verify?.Attributes?[d.PropertyName]?.Value;
                    Logger.Info("[TypedWriter] verify part.Document <" + node.Name + " id=" + controlName + ">." + d.PropertyName + " = '" + Truncate(after, 80) + "' (wanted '" + Truncate(d.Value, 80) + "', match=" + (after == d.Value) + ")");
                }
            }

            // Bump LastModification so the part is considered dirty and gets persisted on EnsureSave.
            try
            {
                var inv = webFormPart.GetType().GetMethod("InvalidateLastModification",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                inv?.Invoke(webFormPart, null);
            }
            catch { }

            // THE KEY: signal the Udm Entity that we modified data so EnsureSave actually persists.
            // Without this, the SDK considers the part clean (no Property setter fired) and skips it.
            // SetModeModified(Modification.Data, null) is the canonical "data changed" notification.
            try
            {
                Type modEnum = FindType("Artech.Udm.Framework.Entity+Modification");
                object dataMod = modEnum != null ? Enum.Parse(modEnum, "Data") : null;
                if (dataMod != null)
                {
                    var setMode = FindInstanceMethod(webFormPart.GetType(), "SetModeModified", new[] { modEnum, typeof(object) });
                    if (setMode != null)
                    {
                        setMode.Invoke(webFormPart, new[] { dataMod, (object)null });
                        Logger.Info("[TypedWriter] SetModeModified(Modification.Data, null) — part marked dirty.");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Info("[TypedWriter] SetModeModified threw: " + (ex.InnerException ?? ex).Message);
            }

            // Belt-and-suspenders: also set Entity.Dirty = true via property.
            try
            {
                var dirtyProp = webFormPart.GetType().GetProperty("Dirty",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (dirtyProp != null && dirtyProp.CanWrite && dirtyProp.PropertyType == typeof(bool))
                {
                    dirtyProp.SetValue(webFormPart, true, null);
                    Logger.Info("[TypedWriter] Entity.Dirty = true.");
                }
            }
            catch { }

            // Call EditableToStored to sync typed model from XML through the SDK's canonical converter.
            // For pure attribute changes (no new att:NNNN references), this should not throw.
            try
            {
                var etsByPart = webFormPart.GetType().GetMethod("EditableToStored",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                if (etsByPart != null)
                {
                    etsByPart.Invoke(webFormPart, null);
                    Logger.Info("[TypedWriter] EditableToStored() invoked on WebFormPart.");
                }
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException ?? ex;
                Logger.Info("[TypedWriter] EditableToStored() threw: " + inner.GetType().Name + ": " + inner.Message);
            }

            // Clear the editable-to-stored pending flag so EnsureSave doesn't replay old m_EditableContent.
            try
            {
                var flag = webFormPart.GetType().GetField("m_EditableToStoredNeeded",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (flag != null && flag.FieldType == typeof(bool))
                {
                    flag.SetValue(webFormPart, false);
                    Logger.Info("[TypedWriter] cleared m_EditableToStoredNeeded.");
                }
            }
            catch { }

            // Also rewrite m_EditableContent to match the new XML, so any latent path that goes
            // through "editable -> stored" produces the same result we wrote directly to m_Document.
            try
            {
                var docNow = GetReadProperty(webFormPart, "Document") as XmlDocument;
                if (docNow != null)
                {
                    var ecField = webFormPart.GetType().GetField("m_EditableContent",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    if (ecField != null && ecField.FieldType == typeof(string))
                    {
                        ecField.SetValue(webFormPart, docNow.OuterXml);
                        Logger.Info("[TypedWriter] synced m_EditableContent from m_Document (" + docNow.OuterXml.Length + " chars).");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Info("[TypedWriter] failed to sync m_EditableContent: " + ex.Message);
            }

            // CRITICAL: trigger Document property SETTER with a NEW XmlDocument instance.
            // Direct field mutation (m_Document attribute writes) bypasses OnPropertyValueChanged,
            // which is the SDK's hook for registering the entity in the active transaction's
            // unit-of-work / dirty-set. Without that registration, SaveWithParent may serialize
            // correct bytes but the KB persistence layer drops them because the entity wasn't
            // enrolled in the commit batch.
            //
            // Cloning m_Document and re-assigning via the setter triggers OnPropertyValueChanged
            // (which fires base.OnPropertyValueChanged → IPropertyBag notification → transaction
            // dirty-set registration) WITHOUT setting m_EditableToStoredNeeded=true (that would
            // cause EditableToStored to run on save and throw on unresolved att: refs).
            try
            {
                var docNow = GetReadProperty(webFormPart, "Document") as XmlDocument;
                if (docNow != null)
                {
                    var clone = (XmlDocument)docNow.Clone();
                    var docProp = webFormPart.GetType().GetProperty("Document",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (docProp != null && docProp.CanWrite)
                    {
                        docProp.SetValue(webFormPart, clone, null);
                        Logger.Info("[TypedWriter] Document property setter invoked (clone len=" + clone.OuterXml.Length + ") — fired OnPropertyValueChanged for UoW registration.");
                        // Document setter sets m_FixPending=true & m_EditableContent=null. Re-sync m_EditableContent now.
                        var ecField2 = webFormPart.GetType().GetField("m_EditableContent",
                            BindingFlags.NonPublic | BindingFlags.Instance);
                        ecField2?.SetValue(webFormPart, clone.OuterXml);
                        // Setter does NOT set m_EditableToStoredNeeded but make doubly sure:
                        var flag2 = webFormPart.GetType().GetField("m_EditableToStoredNeeded",
                            BindingFlags.NonPublic | BindingFlags.Instance);
                        if (flag2 != null && flag2.FieldType == typeof(bool))
                            flag2.SetValue(webFormPart, false);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Info("[TypedWriter] Document property setter threw: " + (ex.InnerException ?? ex).Message);
            }

            return true;
        }

        // Post-write hook called from WebFormXmlHelper.ApplyEditableXml after the raw XML is
        // persisted and the part has reparsed via DeserializeDataFromDocument. Walks every
        // IWebTag in the part; for any tag whose XmlNode has a descriptor-name attribute
        // (e.g. OnClickEvent), invokes PropertiesObject.SetPropertyValueString so the SDK
        // converter writes the canonical XML attr (e.g. Event=) the HTML generator reads.
        // Idempotent: SDK no-ops when value already correct.
        public static void ApplyDescriptorPathFixup(object webFormPart)
        {
            if (webFormPart == null) return;
            try
            {
                Type helperType = FindType("Artech.Genexus.Common.Parts.WebForm.WebFormHelper");
                if (helperType == null) return;

                XmlDocument partDoc = null;
                try
                {
                    var docField = webFormPart.GetType().GetField("m_Document",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    partDoc = docField?.GetValue(webFormPart) as XmlDocument;
                }
                catch { }
                if (partDoc == null) partDoc = GetReadProperty(webFormPart, "Document") as XmlDocument;
                if (partDoc == null) return;

                var partKbObj = GetReadProperty(webFormPart, "KBObject") ?? GetReadProperty(webFormPart, "ContainerObject") ?? GetReadProperty(webFormPart, "Parent") ?? GetReadProperty(webFormPart, "Container");
                MethodInfo enumerate = null;
                object[] enumArgs = null;
                if (partKbObj != null)
                {
                    enumerate = helperType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(m =>
                        {
                            if (m.Name != "EnumerateWebTag") return false;
                            var ps = m.GetParameters();
                            return ps.Length == 2 && ps[1].ParameterType == typeof(XmlDocument) && ps[0].ParameterType.IsInstanceOfType(partKbObj);
                        });
                    if (enumerate != null) enumArgs = new object[] { partKbObj, partDoc };
                }
                if (enumerate == null)
                {
                    enumerate = helperType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(m =>
                        {
                            if (m.Name != "EnumerateWebTag") return false;
                            var ps = m.GetParameters();
                            return ps.Length == 1 && ps[0].ParameterType.IsInstanceOfType(webFormPart);
                        });
                    enumArgs = new object[] { webFormPart };
                }
                if (enumerate == null) return;

                int fixupCount = 0;
                IEnumerable tags;
                try { tags = (IEnumerable)enumerate.Invoke(null, enumArgs); }
                catch (Exception ex) { Logger.Info("[DescFixup] EnumerateWebTag threw: " + (ex.InnerException ?? ex).Message); return; }

                foreach (var tag in tags)
                {
                    var node = GetReadProperty(tag, "Node") as XmlNode;
                    if (node?.Attributes == null) continue;

                    // Collect descriptor-name attributes present on this tag's XML node.
                    var deltas = new List<WebFormPropertyDelta>();
                    string ctrlId = node.Attributes["id"]?.Value ?? node.Attributes["ControlName"]?.Value ?? node.LocalName;
                    foreach (var descName in _descriptorPathProps)
                    {
                        var attr = node.Attributes[descName];
                        if (attr == null) continue;
                        deltas.Add(new WebFormPropertyDelta {
                            ControlName = ctrlId,
                            PropertyName = descName,
                            Value = attr.Value
                        });
                    }
                    if (deltas.Count == 0) continue;

                    ApplyDescriptorDeltas(tag, ctrlId, deltas);
                    fixupCount += deltas.Count;

                    // Remove the wrong-named XML attribute now that SDK has written the right one.
                    // Otherwise generator may see both and the HTML output is unpredictable.
                    string elementName = node.LocalName;
                    foreach (var d in deltas)
                    {
                        try { node.Attributes.RemoveNamedItem(d.PropertyName); } catch { }
                        // Item 77 — record the rename so the caller can surface a
                        // GotchaWebFormTypedPropertyAutoRouted warning to the agent.
                        RecordAutoRoute(elementName, ctrlId, d.PropertyName, ResolveCanonicalAttr(d.PropertyName, elementName));
                    }
                }

                if (fixupCount > 0)
                    Logger.Info("[DescFixup] routed " + fixupCount + " descriptor-property write(s) through SDK.");
            }
            catch (Exception ex)
            {
                Logger.Info("[DescFixup] outer fault: " + (ex.InnerException ?? ex).Message);
            }
        }

        // FR#1 (friction-report 2026-05-19): properties whose XML attribute name differs from
        // the SDK descriptor name. Writing them as raw XML attributes leaves them unread by the
        // HTML generator (it looks for the descriptor's mapped XML attr — e.g. "Event" for
        // gxButton.OnClickEvent, not "OnClickEvent"). These MUST route through
        // PropertiesObject.SetPropertyValueString so the SDK applies the canonical mapping.
        //
        // List is intentionally conservative — only properties confirmed via probe / friction
        // report. Extend cautiously: a wrong-positive (mapped-when-shouldn't) makes the SDK
        // path swallow user intent.
        private static readonly HashSet<string> _descriptorPathProps =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                "OnClickEvent",      // gxButton → Event; gxAttribute/gxImage → eventGX
                "OnEnterEvent",      // gxAttribute/gxButton → eventGX (Enter override)
                "CaptionExpression", // gxButton/gxTextBlock → Caption (with Tokens XML)
            };

        private static bool NeedsDescriptorPath(string propertyName)
        {
            return !string.IsNullOrEmpty(propertyName) && _descriptorPathProps.Contains(propertyName);
        }

        // Friction-report 2026-05-22 item 77 — surface auto-routes to the caller.
        // ApplyDescriptorPathFixup pushes a (from, to, element, control) record here
        // each time it rewrites a descriptor-named XML attribute through the SDK.
        // WebFormXmlHelper / WriteService drain the list after the call so the response
        // can attach `GotchaWebFormTypedPropertyAutoRouted` warnings per rewrite.
        [ThreadStatic] private static List<AutoRouteRecord> _autoRoutes;

        public sealed class AutoRouteRecord
        {
            public string Element;
            public string ControlId;
            public string From;
            public string To;
        }

        /// <summary>
        /// Returns and clears the per-thread list of descriptor-name auto-routes
        /// recorded by the most recent ApplyDescriptorPathFixup call. Returns an
        /// empty list when nothing was routed; never null.
        /// </summary>
        public static List<AutoRouteRecord> DrainAutoRoutes()
        {
            var list = _autoRoutes;
            _autoRoutes = null;
            return list ?? new List<AutoRouteRecord>();
        }

        // Canonical XML-attr name table. Key: descriptor name. Value: element-name
        // (case-insensitive) -> XML attr name. "*" is the fallback when the
        // element-specific entry is absent.
        private static readonly Dictionary<string, Dictionary<string, string>> _descriptorCanonical =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["OnClickEvent"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["gxButton"] = "Event",
                    ["gxAttribute"] = "eventGX",
                    ["gxImage"] = "eventGX",
                    ["gxBitmap"] = "eventGX",
                    ["*"] = "Event"
                },
                ["OnEnterEvent"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["*"] = "eventGX"
                },
                ["CaptionExpression"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["*"] = "Caption"
                }
            };

        public static string ResolveCanonicalAttr(string descriptorName, string elementName)
        {
            if (string.IsNullOrEmpty(descriptorName)) return descriptorName;
            if (!_descriptorCanonical.TryGetValue(descriptorName, out var byEl)) return descriptorName;
            if (!string.IsNullOrEmpty(elementName) && byEl.TryGetValue(elementName, out var v)) return v;
            return byEl.TryGetValue("*", out var any) ? any : descriptorName;
        }

        internal static void RecordAutoRoute(string element, string controlId, string from, string to)
        {
            if (_autoRoutes == null) _autoRoutes = new List<AutoRouteRecord>();
            _autoRoutes.Add(new AutoRouteRecord
            {
                Element = element,
                ControlId = controlId,
                From = from,
                To = to
            });
        }

        // Apply deltas via Artech.Common.Properties.PropertiesObject.SetPropertyValueString(desc,
        // value). The SDK runs the registered PropertyValueConverter (e.g. GxEventReferenceConverter
        // for OnClickEvent) which produces the correct XML attribute name and quoted format.
        // SaveProperties() then mirrors the typed model into the tag's XmlNode in m_Document.
        private static void ApplyDescriptorDeltas(object tag, string controlName, List<WebFormPropertyDelta> deltas)
        {
            if (tag == null || deltas == null || deltas.Count == 0) return;

            object propsObj;
            try { propsObj = GetReadProperty(tag, "Properties"); }
            catch (Exception ex)
            {
                Logger.Info("[TypedWriter] descriptor path: GetProperties on " + controlName + " threw: " + (ex.InnerException ?? ex).Message);
                return;
            }
            if (propsObj == null)
            {
                Logger.Info("[TypedWriter] descriptor path: tag " + controlName + " has no Properties — skipping " + deltas.Count + " delta(s).");
                return;
            }

            // Probe candidate methods: SetPropertyValueString(name,value), SetPropertyValue(name,object).
            // The exact signature varies across SDK versions; try several.
            var poType = propsObj.GetType();
            var candidates = new List<MethodInfo>();
            foreach (var bindAttrs in new[] {
                BindingFlags.Public | BindingFlags.Instance,
                BindingFlags.NonPublic | BindingFlags.Instance,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance })
            {
                foreach (var name in new[] { "SetPropertyValueString", "SetPropertyValue" })
                {
                    var m = poType.GetMethods(bindAttrs).Where(mi => mi.Name == name && mi.GetParameters().Length == 2).ToList();
                    candidates.AddRange(m);
                }
            }
            candidates = candidates.Distinct().ToList();
            if (candidates.Count == 0)
            {
                Logger.Info("[TypedWriter] descriptor path: no SetPropertyValue* found on " + poType.FullName + " — methods: " +
                    string.Join(",", poType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .Where(mi => mi.Name.StartsWith("Set", StringComparison.OrdinalIgnoreCase))
                        .Select(mi => mi.Name + "(" + string.Join(",", mi.GetParameters().Select(p => p.ParameterType.Name)) + ")")));
                return;
            }

            foreach (var d in deltas)
            {
                bool applied = false;
                foreach (var setMethod in candidates)
                {
                    try
                    {
                        var ps = setMethod.GetParameters();
                        object[] args;
                        if (ps[1].ParameterType == typeof(string))
                            args = new object[] { d.PropertyName, d.Value ?? string.Empty };
                        else
                            args = new object[] { d.PropertyName, (object)(d.Value ?? string.Empty) };
                        setMethod.Invoke(propsObj, args);
                        Logger.Info("[TypedWriter] descriptor " + controlName + "." + d.PropertyName + " <- '" + Truncate(d.Value, 80) + "' via " + setMethod.Name + "(" + ps[1].ParameterType.Name + ")");
                        applied = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        var inner = ex.InnerException ?? ex;
                        Logger.Info("[TypedWriter] descriptor " + setMethod.Name + " threw: " + inner.GetType().Name + ": " + inner.Message);
                    }
                }
                if (!applied)
                    Logger.Info("[TypedWriter] descriptor path: no candidate accepted " + controlName + "." + d.PropertyName);
            }

            // Flush typed model back to tag.Node XML (mirrors into m_Document).
            try
            {
                var save = tag.GetType().GetMethod("SaveProperties", Type.EmptyTypes);
                save?.Invoke(tag, null);
            }
            catch (Exception ex)
            {
                Logger.Info("[TypedWriter] descriptor path: SaveProperties on " + controlName + " threw: " + (ex.InnerException ?? ex).Message);
            }
        }

        private static XmlElement FindElementInPartDoc(XmlDocument doc, string controlName)
        {
            if (doc?.DocumentElement == null || string.IsNullOrEmpty(controlName)) return null;
            // Prefer id, fall back to ControlName / controlName.
            foreach (var attr in new[] { "id", "ControlName", "controlName", "InternalName" })
            {
                var xp = string.Format("//*[@{0}='{1}']", attr, controlName.Replace("'", "&apos;"));
                var node = doc.SelectSingleNode(xp) as XmlElement;
                if (node != null) return node;
            }
            return null;
        }

        private static void InvalidateTagPropertyCache(object tag, string controlName)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            int invalidated = 0;
            for (var t = tag.GetType(); t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (var f in t.GetFields(flags))
                {
                    string n = f.Name;
                    if (string.Equals(n, "m_Props", StringComparison.Ordinal) ||
                        string.Equals(n, "m_Properties", StringComparison.Ordinal))
                    {
                        try { f.SetValue(tag, null); invalidated++; }
                        catch { }
                    }
                    else if (string.Equals(n, "m_PropertiesLoaded", StringComparison.Ordinal) ||
                             string.Equals(n, "m_PropsLoaded", StringComparison.Ordinal))
                    {
                        try { f.SetValue(tag, false); invalidated++; }
                        catch { }
                    }
                }
            }
            Logger.Info("[TypedWriter] invalidated " + invalidated + " cache field(s) on tag '" + controlName + "'.");
        }

        private static MethodInfo FindInstanceMethod(Type type, string name, Type[] paramTypes)
        {
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                var m = t.GetMethod(name, flags, null, paramTypes, null);
                if (m != null) return m;
            }
            return null;
        }

        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = null;
                try { t = asm.GetType(fullName, false); } catch { }
                if (t != null) return t;
            }
            return null;
        }

        private static object GetReadProperty(object instance, string name)
        {
            if (instance == null) return null;
            try
            {
                var pi = instance.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (pi != null && pi.CanRead && pi.GetIndexParameters().Length == 0) return pi.GetValue(instance);
            }
            catch { }
            return null;
        }

        private static string Truncate(string s, int n) => string.IsNullOrEmpty(s) ? s : (s.Length > n ? s.Substring(0, n) + "…" : s);
    }
}
