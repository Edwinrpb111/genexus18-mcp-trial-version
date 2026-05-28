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
