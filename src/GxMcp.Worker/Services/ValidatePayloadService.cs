using System;
using System.Linq;
using System.Xml.Linq;
using GxMcp.Worker.Helpers;
using Newtonsoft.Json.Linq;

namespace GxMcp.Worker.Services
{
    public class ValidatePayloadService
    {
        private readonly ObjectService _objectService;

        public ValidatePayloadService(ObjectService objectService)
        {
            _objectService = objectService;
        }

        public string Validate(string target, string partName, string payload)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(payload))
                    return Error("Empty payload");

                var obj = _objectService.FindObject(target);
                if (obj == null) return Error("Object not found: " + target);

                var result = new JObject
                {
                    ["status"] = "Valid",
                    ["target"] = target,
                    ["part"] = partName,
                };

                try { XDocument.Parse(payload, LoadOptions.PreserveWhitespace); }
                catch (Exception ex)
                {
                    return Error("Payload is not well-formed XML: " + ex.Message);
                }

                var suspects = WebFormSchemaHints.ScanForRejectedAttributes(payload);
                if (suspects.Count > 0)
                {
                    var arr = new JArray();
                    foreach (var s in suspects)
                        arr.Add(new JObject { ["element"] = s.Element, ["attribute"] = s.Attribute, ["reason"] = s.Reason });
                    result["preflightWarnings"] = arr;
                    result["status"] = "Warnings";
                }

                try
                {
                    string currentXml = null;
                    if (WebFormXmlHelper.IsVisualPart(partName))
                        currentXml = WebFormXmlHelper.ReadEditableXml(obj);
                    if (!string.IsNullOrEmpty(currentXml))
                    {
                        if (XmlEquivalence.AreEquivalent(currentXml, payload, out _, out var diff))
                        {
                            result["wouldChange"] = false;
                        }
                        else
                        {
                            result["wouldChange"] = true;
                            if (diff != null)
                            {
                                var d = new JObject();
                                if (!string.IsNullOrEmpty(diff.ElementName)) d["element"] = diff.ElementName;
                                if (!string.IsNullOrEmpty(diff.Path)) d["path"] = diff.Path;
                                if (!string.IsNullOrEmpty(diff.Summary)) d["summary"] = diff.Summary;
                                result["diff"] = d;
                            }
                        }
                    }
                }
                catch { /* current-state read is best-effort */ }

                return result.ToString();
            }
            catch (Exception ex)
            {
                return Error(ex.Message);
            }
        }

        private static string Error(string msg) => new JObject { ["status"] = "Error", ["message"] = msg }.ToString();
    }
}
