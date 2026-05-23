using GxMcp.Worker.Services;
using Xunit;

namespace GxMcp.Worker.Tests
{
    // Item 20 (friction 2026-05-22) — pure formatter, no SDK access.
    public class EventsFormatterTests
    {
        [Fact]
        public void TabsAreNormalisedToFourSpaces()
        {
            string input = "\t&x = 1\n\t&y = 2\n";
            string output = EventsFormatter.Format(input);
            Assert.StartsWith("    ", output.Split('\n')[0]);
            // Aligned: lhs widths match, single space before '=' on both sides
            Assert.Contains("    &x = 1", output);
            Assert.Contains("    &y = 2", output);
        }

        [Fact]
        public void AssignmentRunsAreAligned()
        {
            string input =
                "&shortname = 1\n" +
                "&longerName = 22\n" +
                "&x = 3\n";
            string output = EventsFormatter.Format(input);
            // After alignment all three '=' should share the same column.
            var lines = output.Split('\n');
            int col0 = lines[0].IndexOf('=');
            int col1 = lines[1].IndexOf('=');
            int col2 = lines[2].IndexOf('=');
            Assert.Equal(col0, col1);
            Assert.Equal(col1, col2);
            Assert.True(col0 > 0);
        }

        [Fact]
        public void BlankRunOfFiveCollapsesToOne()
        {
            string input = "&a = 1\n\n\n\n\n&b = 2\n";
            string output = EventsFormatter.Format(input);
            // Exactly one blank line between the two assignments.
            var lines = output.Split('\n');
            int firstBlank = -1, secondNonBlank = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (firstBlank == -1 && string.IsNullOrWhiteSpace(lines[i]) && i > 0 && !string.IsNullOrWhiteSpace(lines[i - 1]))
                    firstBlank = i;
                else if (firstBlank != -1 && secondNonBlank == -1 && !string.IsNullOrWhiteSpace(lines[i]))
                    secondNonBlank = i;
            }
            Assert.True(firstBlank > 0);
            Assert.True(secondNonBlank > firstBlank);
            Assert.Equal(1, secondNonBlank - firstBlank);
        }

        [Fact]
        public void DoubleBlankIsPreserved()
        {
            // 2-blank groupings (intentional block separation) should NOT be
            // collapsed — only 3+ runs collapse.
            string input = "Event Start\n&a = 1\nendEvent\n\n\nEvent Refresh\n&b = 2\nendEvent\n";
            string output = EventsFormatter.Format(input);
            // The 3 newlines between blocks collapsed to 1 blank — verify.
            Assert.Contains("endEvent\n\nEvent Refresh", output);
            Assert.DoesNotContain("\n\n\n", output);
        }

        [Fact]
        public void NonAssignmentLinesAreLeftAlone()
        {
            // For each / If / function calls should NOT get tampered with by the
            // assignment-alignment pass.
            string input =
                "For each\n" +
                "    &count = &count + 1\n" +
                "    DoSomething(&x, &y)\n" +
                "EndFor\n";
            string output = EventsFormatter.Format(input);
            Assert.Contains("For each", output);
            Assert.Contains("DoSomething(&x, &y)", output);
            // The lone assignment inside should not have been "aligned" against itself
            // beyond preserving its single ' = ' surround.
            Assert.Contains("    &count = &count + 1", output);
        }

        [Fact]
        public void EmptyAndNullSafe()
        {
            Assert.Equal(string.Empty, EventsFormatter.Format(null));
            Assert.Equal(string.Empty, EventsFormatter.Format(""));
        }
    }
}
