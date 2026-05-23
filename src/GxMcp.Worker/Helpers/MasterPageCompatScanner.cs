using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace GxMcp.Worker.Helpers
{
    /// <summary>
    /// Friction-report 2026-05-22 item 80 — master-page compatibility lint.
    ///
    /// When a WebPanel's Form XML references controls that depend on scripts
    /// or runtime services injected by a specific master page, but the object
    /// is bound to a different (or missing) master page, the build succeeds
    /// but the runtime widget either doesn't render, renders unstyled, or
    /// silently fails to wire its events. The scanner emits non-blocking
    /// warnings so the caller knows before they get a "white screen" bug
    /// report from the browser side.
    ///
    /// Conservative heuristic — known-incompatible control names only.
    /// Extend as new SDK-silent quirks are confirmed.
    /// </summary>
    public static class MasterPageCompatScanner
    {
        public sealed class Finding
        {
            public string Control;       // control element name / type
            public string MasterPage;    // master page actually set on the object (may be null)
            public string Hint;          // what to do
        }

        // Each rule: a predicate evaluated against the XML element + a list of
        // master pages that satisfy the dependency. If the object's master page
        // is not in the satisfying list (or is null/empty), we emit a warning.
        private sealed class Rule
        {
            public string Code;
            public Func<XElement, bool> Match;
            public string ControlDescription;
            public HashSet<string> RequiredMasters; // master-page names that satisfy
            public string Hint;
        }

        private static readonly Rule[] _rules = new[]
        {
            // gxMessages — the SDK control that renders GAM error / success messages.
            // It only renders if the application has GAM (Application.gam) wired in
            // and the master page references the gam.js runtime.
            new Rule
            {
                Code = "gxMessages",
                Match = el => string.Equals(el.Name.LocalName, "gxMessages", StringComparison.OrdinalIgnoreCase),
                ControlDescription = "gxMessages",
                RequiredMasters = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Application.gam", "ApplicationGAM" },
                Hint = "gxMessages requires the GAM master page (Application.gam / ApplicationGAM) to load gam.js — under any other master the widget renders empty."
            },

            // gxAttribute ControlType="ProgressIndicator" — depends on a progress
            // <script src> the GeneXus default master injects via a layout block.
            new Rule
            {
                Code = "ProgressIndicator",
                Match = el =>
                    string.Equals(el.Name.LocalName, "gxAttribute", StringComparison.OrdinalIgnoreCase)
                    && string.Equals((string)el.Attribute("ControlType"), "ProgressIndicator", StringComparison.OrdinalIgnoreCase),
                ControlDescription = "gxAttribute ControlType=\"ProgressIndicator\"",
                RequiredMasters = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Default", "ApplicationDefault" },
                Hint = "ProgressIndicator depends on the progress.js script injected by the Default master page; under a stripped / custom master it renders as a static label."
            },

            // gxMenu — the navigation widget. Most custom masters override the menu
            // chrome; using the SDK gxMenu under those typically yields a hidden
            // <div> because the CSS rules expect a specific class hierarchy from
            // ApplicationMenu / Application.
            new Rule
            {
                Code = "gxMenu",
                Match = el => string.Equals(el.Name.LocalName, "gxMenu", StringComparison.OrdinalIgnoreCase),
                ControlDescription = "gxMenu",
                RequiredMasters = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Application", "ApplicationMenu", "ApplicationDefault" },
                Hint = "gxMenu requires the Application / ApplicationMenu master page's CSS+JS — under custom masters the widget renders as a hidden <div>."
            },

            // gxBreadcrumb — same family.
            new Rule
            {
                Code = "gxBreadcrumb",
                Match = el => string.Equals(el.Name.LocalName, "gxBreadcrumb", StringComparison.OrdinalIgnoreCase),
                ControlDescription = "gxBreadcrumb",
                RequiredMasters = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Application", "ApplicationDefault" },
                Hint = "gxBreadcrumb expects the breadcrumb container the Application master defines; without it the control renders empty."
            },

            // gxNotification — toast widget; needs notifications.js from the GAM master.
            new Rule
            {
                Code = "gxNotification",
                Match = el => string.Equals(el.Name.LocalName, "gxNotification", StringComparison.OrdinalIgnoreCase),
                ControlDescription = "gxNotification",
                RequiredMasters = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Application.gam", "ApplicationGAM" },
                Hint = "gxNotification depends on the notifications.js bundle in the GAM master; toasts won't appear under a plain master."
            }
        };

        /// <summary>
        /// Scans a WebForm XML against the object's master page and returns any
        /// control that depends on a different master. Empty list when nothing
        /// is detected or input is null/invalid.
        /// </summary>
        public static List<Finding> Scan(string layoutXml, string masterPageName)
        {
            var findings = new List<Finding>();
            if (string.IsNullOrWhiteSpace(layoutXml)) return findings;

            XDocument doc;
            try { doc = XDocument.Parse(layoutXml); }
            catch { return findings; }

            foreach (var el in doc.Descendants())
            {
                foreach (var rule in _rules)
                {
                    if (!rule.Match(el)) continue;
                    if (!string.IsNullOrWhiteSpace(masterPageName)
                        && rule.RequiredMasters.Contains(masterPageName))
                    {
                        continue;
                    }
                    findings.Add(new Finding
                    {
                        Control = rule.ControlDescription,
                        MasterPage = string.IsNullOrWhiteSpace(masterPageName) ? "(none)" : masterPageName,
                        Hint = rule.Hint
                    });
                    break; // one finding per element is enough
                }
            }
            return findings;
        }
    }
}
