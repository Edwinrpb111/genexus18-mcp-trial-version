using System;
using GxMcp.Worker.Helpers;
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
                    return new JObject { ["status"] = "Error", ["message"] = "Object not found: " + target }.ToString();

                var available = PartAccessor.GetAvailableParts(obj);
                var raw = _objectService.ReadObjectSourceParts(obj.Name, available, typeFilter);
                var inner = JsonUtil.SafeParse(raw) as JObject ?? new JObject();
                inner["status"] = "Success";
                inner["description"] = obj.Description;
                inner["availableParts"] = new JArray(available);
                return inner.ToString();
            }
            catch (Exception ex)
            {
                return new JObject { ["status"] = "Error", ["message"] = ex.Message }.ToString();
            }
        }
    }
}
