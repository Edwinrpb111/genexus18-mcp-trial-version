using System;
using GxMcp.Worker.Helpers;
using GxMcp.Worker.Models;
using GxMcp.Worker.Structure;
using Newtonsoft.Json.Linq;

namespace GxMcp.Worker.Services
{
    public class DiffService
    {
        private readonly ObjectService _objectService;

        public DiffService(ObjectService objectService)
        {
            _objectService = objectService;
        }

        public const string ModeTextVsText = "textVsText";
        public const string ModeCurrentVsText = "currentVsText";

        public string Diff(string mode, string target, string partName, string left, string right, int context)
        {
            try
            {
                if (string.IsNullOrEmpty(mode)) mode = ModeTextVsText;
                string before, after;

                switch (mode)
                {
                    case ModeTextVsText:
                        before = left ?? "";
                        after = right ?? "";
                        break;
                    case ModeCurrentVsText:
                        before = ReadCurrentPart(target, partName);
                        after = right ?? "";
                        break;
                    default:
                        return Err("Unknown diff mode: " + mode + ". Use " + ModeTextVsText + " | " + ModeCurrentVsText + ".");
                }

                if (context <= 0) context = 3;
                var diff = DiffBuilder.UnifiedDiff(before, after, context);
                return McpResponse.Ok(
                    target: target,
                    code: "DiffComputed",
                    result: new JObject
                    {
                        ["mode"] = mode,
                        ["part"] = partName,
                        ["diff"] = diff,
                        ["beforeLines"] = before.Split('\n').Length,
                        ["afterLines"] = after.Split('\n').Length,
                    });
            }
            catch (Exception ex) { return Err(ex.Message); }
        }

        private string ReadCurrentPart(string target, string partName)
        {
            if (string.IsNullOrEmpty(target)) return "";
            var obj = _objectService.FindObject(target);
            if (obj == null) return "";
            if (WebFormXmlHelper.IsVisualPart(partName))
                return WebFormXmlHelper.ReadEditableXml(obj) ?? "";
            return PartAccessor.GetFirstSourceText(obj);
        }

        private static string Err(string m) => McpResponse.Err(code: "DiffFailed", message: m);
    }
}
