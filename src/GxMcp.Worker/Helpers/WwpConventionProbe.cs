using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Artech.Architecture.Common.Objects;
using GxMcp.Worker.Services;

namespace GxMcp.Worker.Helpers
{
    /// <summary>
    /// Detects whether the active KB follows the WorkWithPlus dual-form layout-form
    /// convention (where the SDK's WebLayoutHandler.LoadPanelElement requires
    /// <c>&lt;detail&gt;&lt;layout id="GUID"&gt;&lt;table controlName tableType="Responsive"
    /// class="GUID-N"&gt;</c> at the root of a Form type="layout" body, and rejects the
    /// flat-table schema with "Elemento não pode ser desserializado do nó XML porque sua
    /// marca (table) não corresponde ao nome do elemento (detail)").
    ///
    /// Strategy:
    ///   1. Cheap check — the WorkWithPlus assembly (DVelop.Patterns.WorkWithPlus) is loaded.
    ///      Necessary signal but not sufficient (some panels in a WWP KB may still tolerate
    ///      the flat schema), so we still try to harvest a theme GUID prefix.
    ///   2. Sample a handful of WebPanel WebForm parts looking for the
    ///      <c>&lt;detail&gt;&lt;layout id="GUID"&gt;&lt;table ... class="GUID-N"&gt;</c>
    ///      pattern. First match wins; we extract the theme-class GUID prefix
    ///      (e.g. "d4876646-98dd-419b-8c1c-896f83c48368") for emit time.
    /// </summary>
    public static class WwpConventionProbe
    {
        public sealed class Result
        {
            public bool IsWwp { get; set; }
            public string ThemeClassPrefix { get; set; } // GUID without the "-NN" suffix
            public string SampleFromObject { get; set; }
            public string Reason { get; set; }
        }

        // class="<36-char guid>-<int>"
        private static readonly Regex ClassGuidRx = new Regex(
            @"class\s*=\s*""([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})-\d+""",
            RegexOptions.Compiled);

        private static readonly Regex DetailLayoutRx = new Regex(
            @"<Form\b[^>]*type\s*=\s*""layout""[^>]*>\s*<detail\b[^>]*>\s*<layout\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static Result Probe(KbService kbService)
        {
            var result = new Result { IsWwp = false };
            if (kbService == null) { result.Reason = "no KbService"; return result; }

            // (1) WWP assembly loaded?
            bool wwpAsmLoaded = false;
            try
            {
                wwpAsmLoaded = AppDomain.CurrentDomain.GetAssemblies()
                    .Any(a => string.Equals(a.GetName().Name, "DVelop.Patterns.WorkWithPlus",
                        StringComparison.OrdinalIgnoreCase));
            }
            catch { }

            // (2) Sample WebPanels for the <detail><layout> pattern + theme GUID prefix.
            try
            {
                dynamic kb = kbService.GetKB();
                if (kb == null) { result.Reason = "no KB"; return result; }
                int scanned = 0;
                const int MaxScan = 40; // bounded — first hit usually within 5-10 WebPanels
                foreach (KBObject obj in (System.Collections.IEnumerable)kb.DesignModel.Objects.GetAll())
                {
                    string typeName = null;
                    try { typeName = obj.TypeDescriptor?.Name; } catch { }
                    if (!string.Equals(typeName, "WebPanel", StringComparison.OrdinalIgnoreCase))
                        continue;
                    string source = null;
                    try
                    {
                        var part = obj.Parts.Cast<KBObjectPart>()
                            .FirstOrDefault(p => string.Equals(p.TypeDescriptor?.Name, "WebForm", StringComparison.OrdinalIgnoreCase));
                        if (part is Artech.Architecture.Common.Objects.ISource src) source = src.Source;
                    }
                    catch { }
                    if (string.IsNullOrEmpty(source)) continue;

                    if (DetailLayoutRx.IsMatch(source))
                    {
                        result.IsWwp = true;
                        result.SampleFromObject = obj.Name;
                        var m = ClassGuidRx.Match(source);
                        if (m.Success)
                        {
                            result.ThemeClassPrefix = m.Groups[1].Value;
                        }
                        result.Reason = "Found <detail><layout> WebForm in " + obj.Name;
                        return result;
                    }

                    if (++scanned >= MaxScan) break;
                }

                if (wwpAsmLoaded)
                {
                    // WWP package is loaded but we didn't find a dual-form sample in the
                    // scanned window. Treat as WWP-aware; emit without a theme prefix
                    // (the SDK accepts the unrooted "Attribute" / "Button" theme names too).
                    result.IsWwp = true;
                    result.Reason = "DVelop.Patterns.WorkWithPlus loaded; no dual-form sample harvested";
                    return result;
                }

                result.Reason = "Scanned " + scanned + " WebPanels; no <detail><layout> pattern; WWP asm not loaded";
                return result;
            }
            catch (Exception ex)
            {
                result.Reason = "Probe exception: " + ex.Message;
                return result;
            }
        }
    }
}
