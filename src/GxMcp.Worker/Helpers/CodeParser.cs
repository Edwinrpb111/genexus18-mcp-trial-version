using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace GxMcp.Worker.Helpers
{
    public static class CodeParser
    {
        private static readonly Regex SectionRegex = new Regex(@"(?i)^\s*(?:Sub|Event)\s+(?:['""]?([\w\.\-]+)['""]?|'([^']+)'|""([^""]+)"")", RegexOptions.Multiline | RegexOptions.Compiled);

        // PERFORMANCE (W-B2): pre-compiled regex for Validate's block-matching loop.
        // Previously each Regex.IsMatch built a fresh interpreted matcher per line.
        private const RegexOptions BlockOptions = RegexOptions.IgnoreCase | RegexOptions.Compiled;
        private static readonly Regex IfStart       = new Regex(@"^\s*If\b",       BlockOptions);
        private static readonly Regex EndifInline   = new Regex(@"\bEndif\b",      BlockOptions);
        private static readonly Regex DoWhile       = new Regex(@"^\s*Do\s+while\b", BlockOptions);
        private static readonly Regex DoCase        = new Regex(@"^\s*Do\s+case\b",  BlockOptions);
        private static readonly Regex ForEach       = new Regex(@"^\s*For\s+each\b", BlockOptions);
        private static readonly Regex ForGeneric    = new Regex(@"^\s*For\b",        BlockOptions);
        private static readonly Regex SubStart      = new Regex(@"^\s*Sub\b",        BlockOptions);
        private static readonly Regex EventStart    = new Regex(@"^\s*Event\b",      BlockOptions);
        private static readonly Regex EndifLine     = new Regex(@"^\s*Endif\b",      BlockOptions);
        private static readonly Regex EnddoLine     = new Regex(@"^\s*Enddo\b",      BlockOptions);
        private static readonly Regex EndcaseLine   = new Regex(@"^\s*Endcase\b",    BlockOptions);
        private static readonly Regex EndforLine    = new Regex(@"^\s*Endfor\b",     BlockOptions);
        private static readonly Regex EndsubLine    = new Regex(@"^\s*Endsub\b",     BlockOptions);
        private static readonly Regex EndeventLine  = new Regex(@"^\s*Endevent\b",   BlockOptions);

        public static List<string> GetSections(string code)
        {
            var sections = new List<string>();
            var subMatches = SectionRegex.Matches(code);
            foreach (Match m in subMatches)
            {
                if (m.Groups[1].Success) sections.Add(m.Groups[1].Value);
                else if (m.Groups[2].Success) sections.Add(m.Groups[2].Value);
                else if (m.Groups[3].Success) sections.Add(m.Groups[3].Value);
            }
            return sections;
        }

        public static List<string> Validate(string code)
        {
            var errors = new List<string>();
            var stack = new Stack<(string name, int line)>();
            
            string[] lines = code.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("//") || line.StartsWith("/*")) continue;

                // Handle single-line If (If ... EndIf)
                bool isSingleLineIf = IfStart.IsMatch(line) && EndifInline.IsMatch(line);
                if (isSingleLineIf) continue;

                // Simple Block Matching
                if (IfStart.IsMatch(line) && !line.Contains(";")) stack.Push(("If", i + 1));
                else if (DoWhile.IsMatch(line)) stack.Push(("Do While", i + 1));
                else if (DoCase.IsMatch(line)) stack.Push(("Do Case", i + 1));
                else if (ForEach.IsMatch(line)) stack.Push(("For Each", i + 1));
                else if (ForGeneric.IsMatch(line)) stack.Push(("For Each", i + 1)); // Handle generic For as For Each for simpler stack
                else if (SubStart.IsMatch(line)) stack.Push(("Sub", i + 1));
                else if (EventStart.IsMatch(line)) stack.Push(("Event", i + 1));

                else if (EndifLine.IsMatch(line))
                {
                    if (stack.Count == 0 || stack.Peek().name != "If") errors.Add($"Line {i + 1}: 'Endif' without matching 'If'");
                    else stack.Pop();
                }
                else if (EnddoLine.IsMatch(line))
                {
                    if (stack.Count == 0 || stack.Peek().name != "Do While") errors.Add($"Line {i + 1}: 'Enddo' without matching 'Do While'");
                    else stack.Pop();
                }
                else if (EndcaseLine.IsMatch(line))
                {
                    if (stack.Count == 0 || stack.Peek().name != "Do Case") errors.Add($"Line {i + 1}: 'Endcase' without matching 'Do Case'");
                    else stack.Pop();
                }
                else if (EndforLine.IsMatch(line))
                {
                    if (stack.Count == 0 || stack.Peek().name != "For Each") errors.Add($"Line {i + 1}: 'Endfor' without matching 'For Each'");
                    else stack.Pop();
                }
                else if (EndsubLine.IsMatch(line))
                {
                    if (stack.Count == 0 || stack.Peek().name != "Sub") errors.Add($"Line {i + 1}: 'Endsub' without matching 'Sub'");
                    else stack.Pop();
                }
                else if (EndeventLine.IsMatch(line))
                {
                    if (stack.Count == 0 || stack.Peek().name != "Event") errors.Add($"Line {i + 1}: 'Endevent' without matching 'Event'");
                    else stack.Pop();
                }
            }

            while (stack.Count > 0)
            {
                var top = stack.Pop();
                errors.Add($"Line {top.line}: '{top.name}' without matching End");
            }

            return errors;
        }

        public static (int start, int end) GetSectionRange(string code, string sectionName)
        {
            string escaped = Regex.Escape(sectionName);
            var pattern = @"(?i)^\s*(?:Sub|Event)\s+(?:['""]?" + escaped + @"['""]?|'" + escaped + @"'|""" + escaped + @""")";
            var match = Regex.Match(code, pattern, RegexOptions.Multiline | RegexOptions.Compiled);
            
            if (!match.Success) return (-1, -1);

            int start = match.Index;
            string endPattern = "";
            
            string line = match.Value.Trim();
            if (line.StartsWith("Sub", StringComparison.OrdinalIgnoreCase))
                endPattern = @"(?i)^\s*EndSub\b";
            else
                endPattern = @"(?i)^\s*EndEvent\b";

            var endMatch = Regex.Match(code.Substring(start), endPattern, RegexOptions.Multiline | RegexOptions.Compiled);
            if (!endMatch.Success) return (start, code.Length);

            return (start, start + endMatch.Index + endMatch.Length);
        }
    }
}
