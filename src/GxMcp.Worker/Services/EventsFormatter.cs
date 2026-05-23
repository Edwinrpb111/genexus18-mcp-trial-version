using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace GxMcp.Worker.Services
{
    /// <summary>
    /// Item 20 (friction 2026-05-22) — lightweight reformatter for the Events
    /// part of WebPanels / SDPanels / Procedures. Runs AFTER the patch matcher
    /// finds its anchor (so context blocks see raw input) but BEFORE the SDK
    /// save, so the LLM's freshly-inserted source matches the IDE's typical
    /// formatting. Opt-out flag <c>autoFormat=false</c> when verbatim bytes
    /// are required (e.g. round-tripping a hex-sensitive payload).
    ///
    /// Rules (intentionally narrow — we don't want to surprise the LLM with
    /// semantic rewrites):
    ///   (a) 4-space indentation. Tabs → 4 spaces. Mixed indents normalized.
    ///   (b) Align '=' in consecutive assignment runs at the same indent
    ///       (only the simple `&var = expr` pattern, not function calls).
    ///   (c) 3+ consecutive blank lines collapse to 1 blank line.
    ///
    /// Pure helper, no SDK dependency — unit-testable without a KB.
    /// </summary>
    public static class EventsFormatter
    {
        // Matches a simple assignment statement of the form  `&name = expr` or
        // `name = expr` (no comparison `==`, no `<=`/`>=`/`!=`, no procedure
        // calls). The leading whitespace is captured separately so we can
        // group runs at the same indent.
        private static readonly Regex _assignmentRx = new Regex(
            @"^(?<indent>[ \t]*)(?<lhs>[&A-Za-z_][A-Za-z0-9_\.\[\]&]*)\s*=\s*(?<rhs>(?!=).*)$",
            RegexOptions.Compiled);

        public static string Format(string source)
        {
            if (string.IsNullOrEmpty(source)) return source ?? string.Empty;

            // Preserve final newline if the input had one — splitting on \n and
            // re-joining loses it otherwise.
            bool trailingNewline = source.EndsWith("\n", StringComparison.Ordinal)
                                   || source.EndsWith("\r\n", StringComparison.Ordinal);
            string normalised = source.Replace("\r\n", "\n").Replace("\r", "\n");
            var lines = new List<string>(normalised.Split('\n'));

            // (a) Indentation normalisation: tabs → 4 spaces. Leave leading
            // spaces alone (the LLM may want a 6-space hanging indent; we only
            // remove the tab/space mixing problem).
            for (int i = 0; i < lines.Count; i++)
            {
                lines[i] = ExpandLeadingTabs(lines[i]);
            }

            // (c) Collapse 3+ blank lines BEFORE alignment so the assignment
            // run detector doesn't treat 5-blank-line gaps as continuous.
            lines = CollapseBlankRuns(lines);

            // (b) Align '=' inside consecutive assignment runs at the same indent.
            AlignAssignmentRuns(lines);

            string joined = string.Join("\n", lines);
            if (trailingNewline && !joined.EndsWith("\n", StringComparison.Ordinal))
                joined += "\n";
            return joined;
        }

        private static string ExpandLeadingTabs(string line)
        {
            if (string.IsNullOrEmpty(line)) return line;
            int i = 0;
            var sb = new StringBuilder();
            while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
            {
                if (line[i] == '\t') sb.Append("    "); else sb.Append(' ');
                i++;
            }
            sb.Append(line, i, line.Length - i);
            return sb.ToString();
        }

        private static List<string> CollapseBlankRuns(List<string> lines)
        {
            var output = new List<string>(lines.Count);
            int blankRun = 0;
            foreach (var line in lines)
            {
                bool isBlank = string.IsNullOrWhiteSpace(line);
                if (isBlank)
                {
                    blankRun++;
                    // Keep at most ONE blank line in a row of 3+; pass through
                    // 1 or 2 untouched so the LLM's intentional 2-blank breaks
                    // between blocks survive.
                    if (blankRun <= 1) output.Add(string.Empty);
                }
                else
                {
                    if (blankRun >= 3)
                    {
                        // The 2..N blank lines we suppressed earlier come back as 0.
                    }
                    blankRun = 0;
                    output.Add(line);
                }
            }
            // If trailing blanks exceed 1, trim.
            while (output.Count > 0 && string.IsNullOrEmpty(output[output.Count - 1])
                   && output.Count >= 2 && string.IsNullOrEmpty(output[output.Count - 2]))
            {
                output.RemoveAt(output.Count - 1);
            }
            return output;
        }

        private static void AlignAssignmentRuns(List<string> lines)
        {
            int i = 0;
            while (i < lines.Count)
            {
                var match = _assignmentRx.Match(lines[i]);
                if (!match.Success) { i++; continue; }

                string runIndent = match.Groups["indent"].Value;
                int runStart = i;
                int maxLhs = match.Groups["lhs"].Value.Length;

                // Extend the run forward as long as each subsequent line is
                // an assignment at the same indent. Comments / blank lines
                // break the run so groups stay tight.
                int j = i + 1;
                while (j < lines.Count)
                {
                    var m2 = _assignmentRx.Match(lines[j]);
                    if (!m2.Success) break;
                    if (m2.Groups["indent"].Value != runIndent) break;
                    if (m2.Groups["lhs"].Value.Length > maxLhs) maxLhs = m2.Groups["lhs"].Value.Length;
                    j++;
                }

                int runLen = j - runStart;
                if (runLen >= 2)
                {
                    for (int k = runStart; k < j; k++)
                    {
                        var m = _assignmentRx.Match(lines[k]);
                        string lhs = m.Groups["lhs"].Value;
                        string rhs = m.Groups["rhs"].Value;
                        string pad = new string(' ', maxLhs - lhs.Length);
                        lines[k] = runIndent + lhs + pad + " = " + rhs;
                    }
                }
                i = j;
            }
        }
    }
}
