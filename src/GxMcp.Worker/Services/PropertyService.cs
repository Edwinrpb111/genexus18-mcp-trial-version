using System;
using System.Collections.Generic;
using System.Linq;
using Artech.Architecture.Common.Objects;
using Artech.Genexus.Common.Objects;
using Artech.Common.Properties;
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

        public string GetProperties(string target, string controlName = null)
        {
            try
            {
                var obj = _objectService.FindObject(target);
                if (obj == null)
                {
                    return Models.McpResponse.Error(
                        "Object not found",
                        target,
                        null,
                        "The requested object is not available in the active Knowledge Base."
                    );
                }

                // Use dynamic to avoid IPropertyContainer ambiguity
                dynamic container = obj;

                // TODO: Support finding control by name if controlName is provided
                if (!string.IsNullOrEmpty(controlName))
                {
                    // For now, return object properties
                }

                return SerializeProperties(container).ToString();
            }
            catch (Exception ex)
            {
                return "{\"error\": \"" + CommandDispatcher.EscapeJsonString(ex.Message) + "\"}";
            }
        }

        public string SetProperty(string target, string propName, string value, string controlName = null)
        {
            try
            {
                var obj = _objectService.FindObject(target);
                if (obj == null)
                {
                    return Models.McpResponse.Error(
                        "Object not found",
                        target,
                        null,
                        "The requested object is not available in the active Knowledge Base."
                    );
                }

                dynamic container = obj;

                // TODO: Support finding control by name
                
                using (var trans = obj.Model.KB.BeginTransaction())
                {
                    // Actually, for generic PropertyCollection:
                    var pInfo = obj.GetType().GetProperty(propName);
                    if (pInfo != null && pInfo.CanWrite) pInfo.SetValue(obj, value);
                    else container.SetPropertyValue(propName, value);
                    try { container.Dirty = true; } catch { }

                    obj.Save();
                    trans.Commit();
                }

                return "{\"status\": \"Success\"}";
            }
            catch (Exception ex)
            {
                return "{\"error\": \"" + CommandDispatcher.EscapeJsonString(ex.Message) + "\"}";
            }
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
                            
                            // Try to get definition if available
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
            catch (Exception ex)
            {
                Logger.Debug($"General error in SerializeProperties: {ex.Message}");
            }

            result["properties"] = props;
            return result;
        }
    }
}
