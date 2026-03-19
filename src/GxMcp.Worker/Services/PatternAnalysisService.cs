using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using Artech.Architecture.Common.Objects;
using Artech.Genexus.Common.Objects;
using GxMcp.Worker.Helpers;

namespace GxMcp.Worker.Services
{
    public class PatternAnalysisService
    {
        private readonly ObjectService _objectService;

        public PatternAnalysisService(ObjectService objectService)
        {
            _objectService = objectService;
        }

        public string GetWWPStructure(string target)
        {
            try
            {
                var obj = _objectService.FindObject(target);
                if (obj == null) return Models.McpResponse.Error("Object not found", target, null, "The requested object is not available in the active Knowledge Base.");

                KBObject instanceObj = null;
                if (obj is Transaction)
                {
                    instanceObj = FindWWPInstance(obj as Transaction);
                }
                else if (obj.TypeDescriptor.Name.Equals("WorkWithPlus", StringComparison.OrdinalIgnoreCase))
                {
                    instanceObj = obj;
                }

                if (instanceObj == null) return Models.McpResponse.Error("WorkWithPlus instance not found", target, null, "No WorkWithPlus instance was resolved for the requested object.");

                var part = instanceObj.Parts.Cast<KBObjectPart>().FirstOrDefault(p => 
                    p.Name.Equals("PatternInstance", StringComparison.OrdinalIgnoreCase) ||
                    p.GetType().Name.Contains("PatternInstance") ||
                    p.Type.Equals(new Guid("a51ced48-7bee-0001-ab12-04e9e32123d1")));

                if (part == null) return Models.McpResponse.Error("PatternInstance part not found", target, "PatternInstance", "The WorkWithPlus instance does not expose a PatternInstance part.", instanceObj.Name, instanceObj.TypeDescriptor?.Name, new JArray(GxMcp.Worker.Structure.PartAccessor.GetAvailableParts(instanceObj)));

                string xml = "";
                if (part is ISource sourcePart)
                {
                    xml = sourcePart.Source;
                }
                
                if (string.IsNullOrEmpty(xml))
                {
                    // Try to get from properties if it's not ISource
                    dynamic dPart = part;
                    try { xml = dPart.Properties.Get<string>("InstanceXml"); } catch {}
                    if (string.IsNullOrEmpty(xml)) {
                         try { xml = dPart.Properties.Get<string>("Specification"); } catch {}
                    }
                    if (string.IsNullOrEmpty(xml)) {
                         try { xml = dPart.Properties.Get<string>("Settings"); } catch {}
                    }
                }

                // If still empty, try to serialize the object itself
                if (string.IsNullOrEmpty(xml))
                {
                    try 
                    {
                        using (var writer = new System.IO.StringWriter())
                        {
                            instanceObj.Serialize(writer);
                            xml = writer.ToString();
                        }
                    } 
                    catch { }
                }

                if (string.IsNullOrEmpty(xml)) return Models.McpResponse.Error("PatternInstance XML not available", target, "PatternInstance", "The PatternInstance content could not be extracted from the resolved WorkWithPlus instance.", instanceObj.Name, instanceObj.TypeDescriptor?.Name);

                var result = ParseWWPXml(xml);
                result["rawSnippet"] = xml.Length > 5000 ? xml.Substring(0, 5000) : xml;
                return result.ToString();
            }
            catch (Exception ex)
            {
                return "{\"error\": \"" + CommandDispatcher.EscapeJsonString(ex.Message) + "\"}";
            }
        }

        private KBObject FindWWPInstance(Transaction trn)
        {
            var model = trn.Model;
            
            // Search by name using SDK compliant way
            // In GX SDK, names are resolved via ResolveName or looking into the collection
            var instance = model.Objects.GetAll()
                                .FirstOrDefault(o => o.Name.Equals("WorkWithPlus" + trn.Name, StringComparison.OrdinalIgnoreCase));
            
            if (instance != null) return instance;

            // Search by children
            return model.Objects.GetChildren(trn)
                        .FirstOrDefault(o => o.TypeDescriptor.Name.Equals("WorkWithPlus", StringComparison.OrdinalIgnoreCase));
        }

        private JObject ParseWWPXml(string xml)
        {
            var result = new JObject();
            try
            {
                string realXml = xml;
                if (xml.Contains("<![CDATA["))
                {
                    int start = xml.IndexOf("<![CDATA[") + 9;
                    int end = xml.LastIndexOf("]]>");
                    if (end > start)
                    {
                        realXml = xml.Substring(start, end - start);
                    }
                }

                XDocument doc = XDocument.Parse(realXml);
                var root = doc.Root;
                
                result["template"] = root?.Attribute("template")?.Value ?? root?.Attribute("Template")?.Value;
                
                var attributes = new JArray();
                foreach (var att in doc.Descendants().Where(e => e.Name.LocalName.Equals("attribute", StringComparison.OrdinalIgnoreCase)))
                {
                    var aObj = new JObject();
                    string rawAtt = att.Attribute("attribute")?.Value ?? "";
                    // WWP format is often GUID-AttributeName
                    string name = rawAtt.Contains("-") ? rawAtt.Substring(rawAtt.LastIndexOf("-") + 1) : rawAtt;
                    
                    aObj["name"] = name;
                    aObj["description"] = att.Attribute("description")?.Value;
                    aObj["visible"] = att.Attribute("visible")?.Value;
                    aObj["readOnly"] = att.Attribute("readOnly")?.Value;
                    attributes.Add(aObj);
                }
                result["attributes"] = attributes;

                var variables = new JArray();
                foreach (var varNode in doc.Descendants().Where(e => e.Name.LocalName.Equals("variable", StringComparison.OrdinalIgnoreCase)))
                {
                    var vObj = new JObject();
                    vObj["name"] = varNode.Attribute("name")?.Value ?? varNode.Attribute("Name")?.Value;
                    vObj["description"] = varNode.Attribute("description")?.Value;
                    vObj["readOnly"] = varNode.Attribute("readOnly")?.Value;
                    variables.Add(vObj);
                }
                result["variables"] = variables;

                var actions = new JArray();
                foreach (var act in doc.Descendants().Where(e => e.Name.LocalName.Contains("Action")))
                {
                    var actObj = new JObject();
                    actObj["name"] = act.Attribute("name")?.Value ?? act.Attribute("Name")?.Value;
                    actObj["caption"] = act.Attribute("caption")?.Value ?? act.Attribute("Caption")?.Value;
                    actions.Add(actObj);
                }
                result["actions"] = actions;

                var tabs = new JArray();
                foreach (var tab in doc.Descendants().Where(e => e.Name.LocalName.Equals("tab", StringComparison.OrdinalIgnoreCase)))
                {
                    var tObj = new JObject();
                    tObj["name"] = tab.Attribute("Name")?.Value ?? tab.Attribute("caption")?.Value;
                    tObj["caption"] = tab.Attribute("caption")?.Value;
                    tabs.Add(tObj);
                }
                result["tabs"] = tabs;

                var grids = new JArray();
                foreach (var grid in doc.Descendants().Where(e => e.Name.LocalName.Equals("grid", StringComparison.OrdinalIgnoreCase)))
                {
                    var gObj = new JObject();
                    gObj["name"] = grid.Attribute("name")?.Value ?? grid.Attribute("Name")?.Value;
                    grids.Add(gObj);
                }
                result["grids"] = grids;
            }
            catch (Exception ex)
            {
                result["parsingError"] = ex.Message;
            }
            return result;
        }
    }
}
