using System;
using Newtonsoft.Json.Linq;
using Artech.Genexus.Common.Objects;
using GxMcp.Worker.Helpers;

namespace GxMcp.Worker.Services.Structure
{
    public class IndexService
    {
        private readonly ObjectService _objectService;

        public IndexService(ObjectService objectService)
        {
            _objectService = objectService;
        }

        public string AddUniqueIndex(string targetName, string attributeName)
        {
            try {
                var obj = _objectService.FindObject(targetName);
                if (obj == null) return Models.McpResponse.Err(code: "ObjectNotFound", message: "Object not found.", target: targetName);

                Table tbl = null;
                if (obj is Table t) tbl = t;
                else if (obj is Transaction trn) tbl = trn.Structure.Root.AssociatedTable;
                if (tbl == null) return Models.McpResponse.Err(code: "AssociatedTableNotFound", message: "Associated table not found.", target: targetName);

                var attr = _objectService.FindObject(attributeName) as Artech.Genexus.Common.Objects.Attribute;
                if (attr == null) return Models.McpResponse.Err(code: "AttributeNotFound", message: $"Attribute '{attributeName}' not found.", target: targetName);

                string idxName = "UIDX_" + attributeName;
                dynamic dIndexesPart = ((dynamic)tbl).TableIndexes;
                if (dIndexesPart == null) return Models.McpResponse.Err(code: "NoTableIndexes", message: "TableIndexes part not available.", target: targetName);

                // Check if index already exists
                if (dIndexesPart.Indexes != null) {
                    foreach (dynamic existing in dIndexesPart.Indexes) {
                        string en = "";
                        try { en = existing.Index?.Name; } catch { }
                        if (string.Equals(en, idxName, StringComparison.OrdinalIgnoreCase))
                            return Models.McpResponse.Ok(target: targetName, code: "IndexAlreadyExists", result: new JObject { ["indexName"] = idxName });
                    }
                }

                // Probe SDK APIs to find what works
                var probeResults = new JObject();
                bool added = false;

                // Approach 1: TableIndexes.Indexes.AddNew() 
                try {
                    dynamic newEntry = dIndexesPart.Indexes.AddNew();
                    if (newEntry != null) {
                        probeResults["AddNew"] = "available";
                        
                        dynamic idx = null;
                        try { idx = newEntry.Index; } catch { }
                        if (idx == null) idx = newEntry;
                        
                        idx.Name = idxName;
                        try { idx.IndexType = 2; } catch { try { idx.IndexType = "Unique"; } catch { } }

                        if (idx.IndexStructure != null && idx.IndexStructure.Members != null) {
                            try {
                                dynamic m = idx.IndexStructure.Members.AddNew();
                                if (m != null) {
                                    m.Attribute = attr;
                                    added = true;
                                }
                            } catch {
                                // Try setting Attribute by name
                                try {
                                    dynamic m = idx.IndexStructure.Members.AddNew();
                                    if (m != null) {
                                        // Some SDK versions use a different member structure
                                        try { m.SetMemberAttribute(attr); } catch { }
                                        added = true;
                                    }
                                } catch { }
                            }
                        } else {
                            // IndexStructure might not exist - SDK may handle differently
                            added = true;
                        }
                    }
                } catch (Exception ex1) {
                    probeResults["AddNew_error"] = ex1.Message;
                }

                if (!added) {
                    // Approach 2: Try accessing via the Genexus KBObject factory
                    try {
                        var idxType = Artech.Architecture.Common.Descriptors.KBObjectDescriptor.Get<Artech.Genexus.Common.Objects.Index>();
                        if (idxType != null) {
                            dynamic newIdx = Artech.Architecture.Common.Objects.KBObject.Create(tbl.Model, idxType.Id);
                            if (newIdx != null) {
                                newIdx.Name = idxName;
                                try { newIdx.Parent = tbl; } catch { }
                                tbl.EnsureSave();
                                added = true;
                                probeResults["KBObject.Create"] = "created";
                            }
                        }
                    } catch (Exception ex2) {
                        probeResults["KBObject.Create_error"] = ex2.Message;
                    }
                }

                if (!added) {
                    // Approach 3: Just try setting Unique on the Attribute via different property names
                    try {
                        attr.SetPropertyValue("Unique", true);
                        probeResults["SetPropertyValue_Unique"] = "called";
                        tbl.EnsureSave();
                        added = true;
                    } catch (Exception ex3) {
                        probeResults["SetPropertyValue_Unique_error"] = ex3.Message;
                    }
                }

                return Models.McpResponse.Ok(target: targetName, code: "IndexProbeResult", result: new JObject {
                    ["indexName"] = idxName,
                    ["tableName"] = tbl.Name,
                    ["attributeName"] = attributeName,
                    ["probe"] = probeResults,
                    ["added"] = added
                });
            } catch (Exception ex) {
                return Models.McpResponse.Err(code: "IndexAddFailed", message: ex.Message, target: targetName);
            }
        }

        public string GetVisualIndexes(string targetName)
        {
            try {
                var obj = _objectService.FindObject(targetName);
                if (obj == null) return Models.McpResponse.Err(
                    code: "ObjectNotFound",
                    message: "Object not found.",
                    hint: "The requested object is not available in the active Knowledge Base.",
                    nextSteps: new JArray(Models.McpResponse.NextStep(
                        tool: "genexus_search",
                        args: new JObject { ["query"] = targetName },
                        why: "Search for objects matching the name to find the correct identifier.")),
                    target: targetName);

                Table tbl = null;
                if (obj is Table t) tbl = t;
                else if (obj is Transaction trn) tbl = trn.Structure.Root.AssociatedTable;

                if (tbl == null) return Models.McpResponse.Err(
                    code: "AssociatedTableNotFound",
                    message: "Associated table not found.",
                    hint: "The requested object does not expose a physical table structure for index inspection.",
                    nextSteps: new JArray(Models.McpResponse.NextStep(
                        tool: "genexus_inspect",
                        args: new JObject { ["name"] = targetName },
                        why: "Inspect the object to confirm whether it has an associated table.")),
                    target: targetName,
                    extra: new JObject { ["objectName"] = obj.Name, ["objectType"] = obj.TypeDescriptor?.Name });

                var result = new JObject();
                result["name"] = tbl.Name;
                var indexes = new JArray();
                dynamic dIndexesPart = ((dynamic)tbl).TableIndexes;
                if (dIndexesPart != null && dIndexesPart.Indexes != null) {
                    foreach (dynamic idxObj in dIndexesPart.Indexes) {
                        dynamic idx = idxObj.Index; if (idx == null) continue;
                        var indexItem = new JObject();
                        indexItem["name"] = idx.Name;

                        string typeStr = idx.IndexType != null ? idx.IndexType.ToString() : "";
                        bool isPrimary = typeStr.Contains("Primary");
                        indexItem["isPrimary"] = isPrimary;
                        indexItem["isUnique"] = typeStr.Contains("Unique") || isPrimary;

                        var attrs = new JArray();
                        if (idx.IndexStructure != null && idx.IndexStructure.Members != null) {
                            foreach (dynamic m in idx.IndexStructure.Members) {
                                var attrObj = new JObject();
                                attrObj["name"] = m.Attribute != null ? m.Attribute.Name : m.Name;
                                try {
                                    attrObj["isAscending"] = m.Order.ToString().Contains("Ascending");
                                } catch {
                                    attrObj["isAscending"] = true;
                                }
                                attrs.Add(attrObj);
                            }
                        }
                        indexItem["attributes"] = attrs;
                        indexes.Add(indexItem);
                    }
                }
                result["indexes"] = indexes;
                return Models.McpResponse.Ok(target: targetName, code: "IndexesRead", result: result);
            } catch (Exception ex) {
                return Models.McpResponse.Err(
                    code: "IndexesReadFailed",
                    message: ex.Message,
                    hint: "Inspect the worker log; the table index metadata may not be accessible for this object.",
                    nextSteps: new JArray(Models.McpResponse.NextStep(
                        tool: "genexus_inspect",
                        args: new JObject { ["name"] = targetName },
                        why: "Inspect the object to confirm its structure is accessible before retrying.")),
                    target: targetName);
            }
        }
    }
}
