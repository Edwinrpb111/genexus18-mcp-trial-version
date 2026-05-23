using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace GxMcp.Worker.Helpers
{
    /// <summary>
    /// Friction-report 2026-05-22 item 49 — flag SQL-injection-shaped patterns in
    /// WebPanel Events / Procedure Source so the agent learns at write time, not
    /// after a security review.
    ///
    /// Heuristic: a <c>For each</c> with a <c>Where</c> clause that builds its
    /// expression via string concatenation against a variable
    /// (<c>&amp;var.Concat(...)</c>, <c>"foo" + &amp;var</c>, <c>&amp;v + "'"</c>)
    /// is the classic anti-pattern — GeneXus emits the value verbatim into the
    /// SQL string instead of binding it. Direct parameterised use
    /// (<c>Where AluCod = &amp;AluCod</c>) is safe and not flagged.
    ///
    /// Findings are non-blocking warnings; callers attach them to the
    /// <c>warnings[]</c> array on the write response.
    /// </summary>
    public static class SqlInjectionScanner
    {
        public sealed class Finding
        {
            public int Line;            // 1-based line number inside the source
            public string Text;         // the trimmed source line that matched
            public string Suggestion;   // remediation hint
        }

        // Detect a Where clause that contains either:
        //   • a method call on a variable (&v.Concat / &v.ToString / &v.Trim, etc.)
        //   • a string-concat operator: " + &var, &var + ", "'" + &v
        // Anchored to lines that begin a Where clause (leading whitespace allowed).
        // Case-insensitive.
        private static readonly Regex _rxWhereConcat = new Regex(
            @"^\s*Where\b.*(?:&\w+\s*\.\s*Concat\b|""\s*\+\s*&|&\w+\s*\+\s*[""']|\+\s*&\w+\s*\+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Also detect dynamic SQL string-building: a variable being built with
        // concatenated literals plus an attribute / inbound var, used later as
        // a Where filter. Conservative — requires both concat AND the word "where".
        private static readonly Regex _rxDynamicSqlBuild = new Regex(
            @"^\s*&\w+\s*=\s*[""'].*\bwhere\b.*[""']\s*\+\s*&",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Matches a clean parametrised Where — used to suppress false positives
        // when the same line also contains a safe pattern.
        private static readonly Regex _rxSafeWhere = new Regex(
            @"^\s*Where\s+\w+\s*(?:=|>|<|>=|<=|<>|like)\s*&\w+\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Scans GeneXus source (Events or Procedure Source) line-by-line and
        /// returns each line that matches an injection-shaped pattern. Empty
        /// list when the input is null/empty or nothing matches.
        /// </summary>
        public static List<Finding> Scan(string source)
        {
            var findings = new List<Finding>();
            if (string.IsNullOrEmpty(source)) return findings;

            int lineNum = 0;
            foreach (var rawLine in source.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None))
            {
                lineNum++;
                if (string.IsNullOrWhiteSpace(rawLine)) continue;
                // Skip comments. GeneXus uses // for line comments.
                var trimmed = rawLine.TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal)) continue;

                if (_rxSafeWhere.IsMatch(rawLine)) continue;

                if (_rxWhereConcat.IsMatch(rawLine))
                {
                    findings.Add(new Finding
                    {
                        Line = lineNum,
                        Text = rawLine.Trim(),
                        Suggestion = "use parameterized Where with &var directly"
                    });
                    continue;
                }
                if (_rxDynamicSqlBuild.IsMatch(rawLine))
                {
                    findings.Add(new Finding
                    {
                        Line = lineNum,
                        Text = rawLine.Trim(),
                        Suggestion = "use parameterized Where with &var directly"
                    });
                }
            }
            return findings;
        }
    }
}
