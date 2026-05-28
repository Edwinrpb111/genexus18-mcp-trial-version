using GxMcp.Worker.Helpers;
using GxMcp.Worker.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GxMcp.Worker.Tests
{
    public class EditPostStateTests
    {
        [Fact]
        public void UnifiedDiff_DetectsChangedLine()
        {
            var diff = DiffBuilder.UnifiedDiff("a\nb\nc", "a\nB!\nc", 3);
            Assert.Contains("-b", diff);
            Assert.Contains("+B!", diff);
            Assert.Contains("@@", diff);
        }

        [Fact]
        public void UnifiedDiff_NoChange_Empty()
        {
            var diff = DiffBuilder.UnifiedDiff("a\nb", "a\nb", 3);
            Assert.True(string.IsNullOrEmpty(diff));
        }

        [Fact]
        public void UnifiedDiff_AddedLine_ShowsPlus()
        {
            var diff = DiffBuilder.UnifiedDiff("a\nb", "a\nb\nc", 3);
            Assert.Contains("+c", diff);
            Assert.Contains("@@", diff);
        }

        [Fact]
        public void UnifiedDiff_RemovedLine_ShowsMinus()
        {
            var diff = DiffBuilder.UnifiedDiff("a\nb\nc", "a\nc", 3);
            Assert.Contains("-b", diff);
            Assert.Contains("@@", diff);
        }

        [Fact]
        public void UnifiedDiff_ContextLines_IncludesUnchangedNeighbors()
        {
            // 10-line file; change line 5 only; context=2 should show lines 3-7
            string before = "1\n2\n3\n4\n5\n6\n7\n8\n9\n10";
            string after  = "1\n2\n3\n4\nX\n6\n7\n8\n9\n10";
            var diff = DiffBuilder.UnifiedDiff(before, after, context: 2);
            Assert.Contains("-5", diff);
            Assert.Contains("+X", diff);
            // context lines 3,4,6,7 should appear
            Assert.Contains(" 3", diff);
            Assert.Contains(" 7", diff);
            // lines 1,2 and 8,9,10 should NOT appear (outside context)
            Assert.DoesNotContain(" 1\n", diff);
            Assert.DoesNotContain(" 10\n", diff);
        }

        [Fact]
        public void BuildPostState_DefaultHasDiffNoSlices()
        {
            var ps = JsonPatchService.BuildPostState("a", "b", verbose: false);
            Assert.NotNull(ps["diff"]);
            Assert.Null(ps["slices"]);
        }

        [Fact]
        public void BuildPostState_VerboseHasSlices()
        {
            var ps = JsonPatchService.BuildPostState("a", "b", verbose: true);
            Assert.NotNull(ps["slices"]);
            Assert.True(ps["slices"] is JArray);
        }

        [Fact]
        public void BuildPostState_NoDiff_WhenIdentical()
        {
            var ps = JsonPatchService.BuildPostState("same\ncontent", "same\ncontent", verbose: false);
            Assert.Equal("", ps["diff"]?.ToString());
        }

        [Fact]
        public void BuildPostState_DiffContainsChangedLines()
        {
            var ps = JsonPatchService.BuildPostState("hello\nworld", "hello\nearth", verbose: false);
            string diff = ps["diff"]?.ToString() ?? "";
            Assert.Contains("-world", diff);
            Assert.Contains("+earth", diff);
        }

        // ---- v2.3.8 Task 3.4: persistedHash + persistedSnippet helpers --------

        [Fact]
        public void ComputeSha256_EmptyAndKnownValue_HasSha256Prefix()
        {
            string h1 = WriteService.ComputeSha256("");
            string h2 = WriteService.ComputeSha256("abc");
            Assert.StartsWith("sha256:", h1);
            Assert.StartsWith("sha256:", h2);
            Assert.NotEqual(h1, h2);
            // Known SHA256("abc")
            Assert.Equal("sha256:ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", h2);
        }

        [Fact]
        public void ComputeSha256_NullInput_TreatedAsEmpty()
        {
            string hNull = WriteService.ComputeSha256(null);
            string hEmpty = WriteService.ComputeSha256("");
            Assert.Equal(hEmpty, hNull);
        }

        [Fact]
        public void ExtractSnippet_DefaultLineHint_ReturnsLeadingLines()
        {
            string src = "l1\nl2\nl3\nl4\nl5";
            string snip = WriteService.ExtractSnippet(src, 0, 10);
            Assert.Contains("l1", snip);
            Assert.Contains("l5", snip);
        }

        [Fact]
        public void ExtractSnippet_AroundLine_RespectsContextWindow()
        {
            // 21-line source; lineHint=10 with contextLines=2 → lines 8..12 (5 lines)
            var lines = new System.Text.StringBuilder();
            for (int i = 1; i <= 21; i++) { if (i > 1) lines.Append('\n'); lines.Append("L" + i); }
            string snip = WriteService.ExtractSnippet(lines.ToString(), lineHint: 10, contextLines: 2);
            // lineHint=10 maps to lines[10] which is "L11" (0-indexed). Window: [8..12] → L9..L13
            Assert.Contains("L9", snip);
            Assert.Contains("L13", snip);
            Assert.DoesNotContain("L7", snip);
            Assert.DoesNotContain("L15", snip);
        }

        [Fact]
        public void ExtractSnippet_EmptyOrNullSource_ReturnsEmpty()
        {
            Assert.Equal("", WriteService.ExtractSnippet(null, 0, 10));
            Assert.Equal("", WriteService.ExtractSnippet("", 5, 10));
        }

        [Fact]
        public void AppendPersistedState_AlwaysAddsBothKeys()
        {
            // Canonical envelope status — AppendPersistedState should not mutate it.
            var resp = new JObject { ["status"] = "ok" };
            var decorated = WriteService.AppendPersistedState(resp, "line1\nline2\nline3", null);
            Assert.NotNull(decorated["persistedHash"]);
            Assert.StartsWith("sha256:", decorated["persistedHash"].ToString());
            Assert.NotNull(decorated["persistedSnippet"]);
            Assert.False(string.IsNullOrEmpty(decorated["persistedSnippet"].ToString()));
            // Status survives the decoration
            Assert.Equal("ok", decorated["status"].ToString());
        }

        [Fact]
        public void AppendPersistedState_NullResponse_StillReturnsDecoratedJObject()
        {
            var decorated = WriteService.AppendPersistedState(null, "x", 0);
            Assert.NotNull(decorated);
            Assert.StartsWith("sha256:", decorated["persistedHash"].ToString());
        }

        [Fact]
        public void AppendPersistedState_EmptyFinalSource_PersistedHashStillPresent()
        {
            // Simulates rollback / not-found path where re-read produced no source.
            var resp = new JObject { ["status"] = "Error", ["error"] = "rollback" };
            var decorated = WriteService.AppendPersistedState(resp, "", null);
            Assert.NotNull(decorated["persistedHash"]);
            Assert.StartsWith("sha256:", decorated["persistedHash"].ToString());
            Assert.NotNull(decorated["persistedSnippet"]);
            Assert.Equal("", decorated["persistedSnippet"].ToString());
        }
    }
}
