using System;
using GxMcp.Worker.Helpers;
using GxMcp.Worker.Models;
using GxMcp.Worker.Structure;
using Newtonsoft.Json.Linq;

namespace GxMcp.Worker.Services
{
    public class ExportObjectService
    {
        private readonly ObjectService _objectService;

        public ExportObjectService(ObjectService objectService)
        {
            _objectService = objectService;
        }

        public string Export(string target, string typeFilter = null)
        {
            try
            {
                var obj = _objectService.FindObject(target, typeFilter);
                if (obj == null)
                    return McpResponse.Err(
                        code: "ObjectNotFound",
                        message: "Object not found: " + target,
                        hint: "Check the object name and type filter. Use genexus_list_objects to browse.",
                        nextSteps: new JArray(
                            McpResponse.NextStep(
                                tool: "genexus_list_objects",
                                args: new JObject { ["name_contains"] = target },
                                why: "Lists objects whose names match, in case of a typo."),
                            McpResponse.NextStep(
                                tool: "genexus_lifecycle",
                                args: new JObject { ["action"] = "index", ["force"] = true },
                                why: "Rebuilds the SearchIndex if the object exists but isn't indexed.")),
                        target: target);

                var available = PartAccessor.GetAvailableParts(obj);
                var raw = _objectService.ReadObjectSourceParts(obj.Name, available, typeFilter);
                var inner = JsonUtil.SafeParse(raw) as JObject ?? new JObject();
                inner["description"] = obj.Description;
                inner["availableParts"] = new JArray(available);
                return McpResponse.Ok(target: obj.Name, code: "ExportCompleted", result: inner);
            }
            catch (Exception ex)
            {
                return McpResponse.Err(
                    code: "ExportFailed",
                    message: ex.Message,
                    hint: "Check that the object exists and parts are readable.",
                    target: target);
            }
        }
    }
}
