using System;
using GxMcp.Worker.Models;
using Newtonsoft.Json.Linq;

namespace GxMcp.Worker.Services
{
    /// <summary>
    /// Item 78 — genexus_sd_panel. Thin proxy that resolves the target as an
    /// SDPanel via ObjectService.FindObject(name, typeFilter:"SDPanel") and
    /// forwards to existing tools (inspect/create/edit), tagging the response
    /// with <c>kind:"SDPanel"</c>. NOT a full SDPanel feature surface — just a
    /// type-locked entry-point for mobile-first agents.
    /// </summary>
    public class SdPanelService
    {
        private readonly ObjectService _objectService;
        private readonly WriteService _writeService;

        public SdPanelService(ObjectService objectService, WriteService writeService)
        {
            _objectService = objectService;
            _writeService = writeService;
        }

        public string Dispatch(string action, string target, JObject args)
        {
            try
            {
                action = (action ?? "inspect").ToLowerInvariant();
                switch (action)
                {
                    case "inspect": return Inspect(target);
                    case "create": return Create(target, args);
                    case "edit": return Edit(target, args);
                    default:
                        return Error("UnknownAction", "SDPanel action must be inspect|create|edit; got '" + action + "'.");
                }
            }
            catch (Exception ex)
            {
                return Error("SdPanelDispatchFailed", ex.Message);
            }
        }

        public string Inspect(string target)
        {
            if (string.IsNullOrWhiteSpace(target))
                return Error("MissingTarget", "target is required for inspect.");
            var obj = _objectService?.FindObject(target, "SDPanel");
            if (obj == null)
                return Error("NotFound", "SDPanel '" + target + "' not found.");
            return McpResponse.Ok(
                target: target,
                code: "SdPanelInspected",
                result: new JObject
                {
                    ["kind"] = "SDPanel",
                    ["name"] = obj.Name,
                    ["type"] = obj.TypeDescriptor?.Name ?? "SDPanel",
                    ["guid"] = obj.Guid.ToString(),
                    ["description"] = obj.Description ?? string.Empty
                });
        }

        public string Create(string target, JObject args)
        {
            if (string.IsNullOrWhiteSpace(target))
                return Error("MissingTarget", "target/name is required for create.");
            try
            {
                var opts = args != null ? (JObject)args.DeepClone() : new JObject();
                string raw = _objectService.CreateObject("SDPanel", target, opts);
                // Tag the underlying envelope as SDPanel for the agent.
                JObject parsed;
                try { parsed = JObject.Parse(raw); }
                catch { parsed = JObject.Parse(Models.McpResponse.Ok(code: "SdPanelRawFallback", result: new JObject { ["raw"] = raw })); }
                parsed["kind"] = "SDPanel";
                return parsed.ToString(Newtonsoft.Json.Formatting.None);
            }
            catch (Exception ex)
            {
                return Error("CreateFailed", ex.Message);
            }
        }

        public string Edit(string target, JObject args)
        {
            if (string.IsNullOrWhiteSpace(target))
                return Error("MissingTarget", "target is required for edit.");
            // Verify it's actually an SDPanel before delegating.
            var obj = _objectService?.FindObject(target, "SDPanel");
            if (obj == null)
                return Error("NotFound", "SDPanel '" + target + "' not found.");
            try
            {
                string part = args?["part"]?.ToString() ?? "Events";
                string content = args?["content"]?.ToString() ?? string.Empty;
                string result = _writeService.WriteObject(target, part, content, "SDPanel");
                // Tag the response so the agent sees this routed through the SDPanel path.
                JObject parsed;
                try { parsed = JObject.Parse(result); }
                catch { parsed = JObject.Parse(Models.McpResponse.Ok(code: "SdPanelRawFallback", result: new JObject { ["raw"] = result })); }
                parsed["kind"] = "SDPanel";
                return parsed.ToString(Newtonsoft.Json.Formatting.None);
            }
            catch (Exception ex)
            {
                return Error("EditFailed", ex.Message);
            }
        }

        private static string Error(string code, string message) =>
            Models.McpResponse.Err(
                code: code,
                message: message,
                extra: new JObject { ["kind"] = "SDPanel" });
    }
}
