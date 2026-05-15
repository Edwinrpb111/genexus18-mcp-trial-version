using System.Linq;
using GxMcp.Worker.Helpers;
using Xunit;

namespace GxMcp.Worker.Tests
{
    // v2.3.8 Task 4.6 — patch-window-only rollback verification.
    //
    // The integration story is documented on PatchService.TryClassifyOutOfWindowOnly:
    //   - input edit window comes from HunkDiff(workSource, updatedSource)
    //   - post-save divergence comes from HunkDiff(finalCode, persistedSource)
    //   - all divergence hunks out-of-window → success + _meta.sideEffectNormalizations
    //   - any hunk in-window → rollback (existing behavior preserved)
    //
    // The full SDK flow needs an open KB and is exercised by the higher-level smoke
    // tests; here we lock the line-level helpers + the classification math down so the
    // friction-report #13 / #6 regression cannot reappear silently.
    public class PatchWindowRollbackTests
    {
        // Source with `DATETIME(10,5)` on line 30 (1-based). The agent edits line 10.
        // We model "what the agent asked for" as workSource with line 10 modified.
        // We model "what landed on disk" as updatedSource-but-with-line-30-renormalized.
        // The classifier must accept this as out-of-window only → success.
        [Fact]
        public void HunkDiff_ProducesOutOfWindowHunk_WhenSdkNormalizesUntouchedLine()
        {
            // 50-line synthetic body: line 10 is the edit target, line 30 carries the
            // datetime literal that the SDK rewrites independently of the edit.
            var beforeLines = new string[50];
            for (int i = 0; i < beforeLines.Length; i++) beforeLines[i] = "line" + (i + 1);
            beforeLines[9] = "&total = 0";                      // line 10
            beforeLines[29] = "// DATETIME(10,5) sample row";    // line 30

            // Agent edits line 10 → "&total = 1"
            var requestedLines = (string[])beforeLines.Clone();
            requestedLines[9] = "&total = 1";

            // SDK persists with line 30 normalized to DATETIME(8,5)
            var persistedLines = (string[])requestedLines.Clone();
            persistedLines[29] = "// DATETIME(8,5) sample row";

            string before = string.Join("\n", beforeLines);
            string requested = string.Join("\n", requestedLines);
            string persisted = string.Join("\n", persistedLines);

            // 1. Edit window = the line(s) the patch actually touched (line 10).
            var editHunks = XmlEquivalence.HunkDiff(before, requested);
            Assert.Single(editHunks);
            Assert.Equal(10, editHunks[0].Line);

            // 2. Divergence between requested and persisted = the SDK's normalization
            //    on line 30 only.
            var divergeHunks = XmlEquivalence.HunkDiff(requested, persisted);
            Assert.Single(divergeHunks);
            Assert.Equal(30, divergeHunks[0].Line);

            // 3. Classification: that divergence is OUT of [10, 10] → no rollback.
            Assert.False(
                XmlEquivalence.HunkOverlapsWindow(divergeHunks[0], 10, 10),
                "line 30 must not overlap a [10,10] edit window — would trigger spurious rollback");
        }

        // Counterpart: when the SDK rewrites the very line we edited (e.g. it rejects
        // a literal we wrote and emits something else), the hunk overlaps the window
        // and rollback must fire.
        [Fact]
        public void HunkDiff_ProducesInWindowHunk_WhenSdkRewritesEditedLine()
        {
            var beforeLines = new string[20];
            for (int i = 0; i < beforeLines.Length; i++) beforeLines[i] = "line" + (i + 1);
            beforeLines[9] = "&total = 0";

            var requestedLines = (string[])beforeLines.Clone();
            requestedLines[9] = "&total : NUMERIC(4,0)";

            var persistedLines = (string[])requestedLines.Clone();
            // SDK sanitises the edited line away.
            persistedLines[9] = "&total : NUMERIC(4)";

            string before = string.Join("\n", beforeLines);
            string requested = string.Join("\n", requestedLines);
            string persisted = string.Join("\n", persistedLines);

            var editHunks = XmlEquivalence.HunkDiff(before, requested);
            Assert.Single(editHunks);
            int wStart = editHunks[0].Line;
            int wEnd = editHunks[0].Line + System.Math.Max(0, editHunks[0].BeforeLineCount - 1);

            var divergeHunks = XmlEquivalence.HunkDiff(requested, persisted);
            Assert.Single(divergeHunks);
            Assert.True(
                XmlEquivalence.HunkOverlapsWindow(divergeHunks[0], wStart, wEnd),
                "in-window divergence must be flagged so the caller rolls back");
        }

        [Fact]
        public void HunkDiff_IdenticalInputs_ReturnsEmpty()
        {
            var hunks = XmlEquivalence.HunkDiff("a\nb\nc", "a\nb\nc");
            Assert.Empty(hunks);
        }

        [Fact]
        public void HunkDiff_PureInsertion_ReportsInsertedLines()
        {
            // before: a, b, c   after: a, b, X, c
            var hunks = XmlEquivalence.HunkDiff("a\nb\nc", "a\nb\nX\nc");
            Assert.Single(hunks);
            Assert.Equal(3, hunks[0].Line);
            Assert.Equal("", hunks[0].Before);
            Assert.Equal("X", hunks[0].After);
            Assert.Equal(0, hunks[0].BeforeLineCount);
            Assert.Equal(1, hunks[0].AfterLineCount);
        }

        [Fact]
        public void HunkDiff_PureDeletion_ReportsRemovedLines()
        {
            // before: a, b, c, d   after: a, c, d
            var hunks = XmlEquivalence.HunkDiff("a\nb\nc\nd", "a\nc\nd");
            Assert.Single(hunks);
            Assert.Equal(2, hunks[0].Line);
            Assert.Equal("b", hunks[0].Before);
            Assert.Equal("", hunks[0].After);
        }

        [Fact]
        public void HunkOverlapsWindow_PureInsertionAtWindowEdge_Overlaps()
        {
            // Insertion at line 10 must count as touching a [10,10] window.
            var h = new XmlEquivalence.LineHunk
            {
                Line = 10,
                Before = "",
                After = "X",
                BeforeLineCount = 0,
                AfterLineCount = 1
            };
            Assert.True(XmlEquivalence.HunkOverlapsWindow(h, 10, 10));
            Assert.False(XmlEquivalence.HunkOverlapsWindow(h, 11, 20));
            Assert.False(XmlEquivalence.HunkOverlapsWindow(h, 1, 9));
        }

        [Fact]
        public void HunkOverlapsWindow_MultiLineHunk_OverlapsWhenAnyLineInWindow()
        {
            // Hunk covers lines 8..12; window 10..15 → overlap.
            var h = new XmlEquivalence.LineHunk
            {
                Line = 8,
                BeforeLineCount = 5,
                AfterLineCount = 5,
                Before = "a\nb\nc\nd\ne",
                After = "A\nB\nC\nD\nE"
            };
            Assert.True(XmlEquivalence.HunkOverlapsWindow(h, 10, 15));
            // window entirely past hunk → no overlap
            Assert.False(XmlEquivalence.HunkOverlapsWindow(h, 13, 20));
            // window entirely before hunk → no overlap
            Assert.False(XmlEquivalence.HunkOverlapsWindow(h, 1, 7));
        }
    }
}
