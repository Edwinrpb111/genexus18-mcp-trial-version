using Newtonsoft.Json.Linq;
using GxMcp.Worker.Models;

namespace GxMcp.Worker.Services
{
    public class ApplyTemplateService
    {
        public const string KpiHeader = "kpi_header";
        public const string EmptyState = "empty_state";
        public const string ConfirmDialog = "confirm_dialog";
        private const string AvailableList = KpiHeader + " | " + EmptyState + " | " + ConfirmDialog;

        private static readonly string _listJson = new JObject
        {
            ["templates"] = new JArray
            {
                Describe(KpiHeader,     "Card-style header row with a title and three KPI slots.", "title, kpi1, kpi2, kpi3"),
                Describe(EmptyState,    "Bitmap + caption pair for an empty list/grid.",            "caption, image"),
                Describe(ConfirmDialog, "Confirm/Cancel button pair wired to events.",              "confirmEvent, cancelEvent"),
            }
        }.ToString();

        private readonly WriteService _writeService;

        public ApplyTemplateService(WriteService writeService)
        {
            _writeService = writeService;
        }

        public string Apply(string template, string target, JObject args, bool dryRun)
        {
            if (string.IsNullOrEmpty(template))
                return Err("template is required (" + AvailableList + ")");
            if (string.IsNullOrEmpty(target))
                return Err("target object is required");

            args = args ?? new JObject();
            var partName = args["part"]?.ToString();

            switch (template.ToLowerInvariant())
            {
                case KpiHeader:     return ApplyKpiHeader(target, partName, args, dryRun);
                case EmptyState:    return ApplyEmptyState(target, partName, args, dryRun);
                case ConfirmDialog: return ApplyConfirmDialog(target, partName, args, dryRun);
                default:
                    return Err("Unknown template '" + template + "'. Available: " + AvailableList + ".");
            }
        }

        public string ListTemplates() => _listJson;

        private static JObject Describe(string name, string desc, string args)
            => new JObject { ["name"] = name, ["description"] = desc, ["args"] = args };

        private string ApplyKpiHeader(string target, string partName, JObject args, bool dryRun)
        {
            string title = args["title"]?.ToString() ?? "KPI Overview";
            string k1 = args["kpi1"]?.ToString() ?? "&Kpi1";
            string k2 = args["kpi2"]?.ToString() ?? "&Kpi2";
            string k3 = args["kpi3"]?.ToString() ?? "&Kpi3";

            var xml = "<table id=\"TblKpiHeader\" cellSpacing=\"4\">"
                    + "<row><cell ColSpan=\"3\"><gxTextBlock id=\"LblKpiTitle\" CaptionExpression=\"" + Esc(title) + "\"/></cell></row>"
                    + "<row>"
                    + "<cell><gxAttribute id=\"AttKpi1\" AttID=\"" + Esc(k1) + "\"/></cell>"
                    + "<cell><gxAttribute id=\"AttKpi2\" AttID=\"" + Esc(k2) + "\"/></cell>"
                    + "<cell><gxAttribute id=\"AttKpi3\" AttID=\"" + Esc(k3) + "\"/></cell>"
                    + "</row></table>";
            return _writeService.WriteObject(target, partName ?? "WebForm", xml, null, true, false, true, dryRun);
        }

        private string ApplyEmptyState(string target, string partName, JObject args, bool dryRun)
        {
            string caption = args["caption"]?.ToString() ?? "No data available.";
            string image = args["image"]?.ToString() ?? "EmptyState.png";

            var xml = "<table id=\"TblEmptyState\" cellSpacing=\"8\">"
                    + "<row><cell HAlign=\"center\"><gxBitmap id=\"BmpEmpty\" ImageData=\"" + Esc(image) + "\"/></cell></row>"
                    + "<row><cell HAlign=\"center\"><gxTextBlock id=\"LblEmpty\" CaptionExpression=\"" + Esc(caption) + "\"/></cell></row>"
                    + "</table>";
            return _writeService.WriteObject(target, partName ?? "WebForm", xml, null, true, false, true, dryRun);
        }

        private string ApplyConfirmDialog(string target, string partName, JObject args, bool dryRun)
        {
            string confirmEvent = args["confirmEvent"]?.ToString() ?? "Confirm";
            string cancelEvent = args["cancelEvent"]?.ToString() ?? "Cancel";

            var xml = "<table id=\"TblConfirm\" cellSpacing=\"4\">"
                    + "<row>"
                    + "<cell><gxButton id=\"BtnConfirm\" CaptionExpression=\"Confirm\" OnClickEvent=\"" + Esc(confirmEvent) + "\"/></cell>"
                    + "<cell><gxButton id=\"BtnCancel\" CaptionExpression=\"Cancel\" OnClickEvent=\"" + Esc(cancelEvent) + "\"/></cell>"
                    + "</row></table>";
            return _writeService.WriteObject(target, partName ?? "WebForm", xml, null, true, false, true, dryRun);
        }

        private static string Esc(string s) => System.Security.SecurityElement.Escape(s ?? "");

        private static string Err(string m) => McpResponse.Err(code: "ApplyTemplateFailed", message: m);
    }
}
