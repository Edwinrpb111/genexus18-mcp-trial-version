using GxMcp.Worker.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GxMcp.Worker.Tests
{
    // Friction 2026-05-22: KBs default to WIN1252 on Windows. When the agent
    // writes content with characters outside that codepage the SDK accepts the
    // write, generation succeeds, runtime shows '?'. The CollectNonWin1252Glyphs
    // helper surfaces the bad glyphs so the edit response can carry a warning.
    public class Win1252CharsetWarnTests
    {
        [Fact]
        public void CollectNonWin1252Glyphs_PureAscii_ReturnsEmpty()
        {
            var args = JObject.Parse("{\"content\": \"Hello world\", \"name\":\"Foo\"}");
            var glyphs = WriteService.CollectNonWin1252Glyphs(args);
            Assert.Empty(glyphs);
        }

        [Fact]
        public void CollectNonWin1252Glyphs_LatinAccents_ReturnsEmpty()
        {
            // çñãéüö are all representable in codepage 1252.
            var args = JObject.Parse("{\"caption\": \"São Paulo — ação rápida\"}");
            var glyphs = WriteService.CollectNonWin1252Glyphs(args);
            Assert.Empty(glyphs);
        }

        [Fact]
        public void CollectNonWin1252Glyphs_CheckmarkAndHourglass_AreFlagged()
        {
            var args = new JObject
            {
                ["content"] = new JObject { ["text"] = "Status: ✓ done, ⧖ pending" }
            };
            var glyphs = WriteService.CollectNonWin1252Glyphs(args);
            Assert.Contains("✓", glyphs);
            Assert.Contains("⧖", glyphs);
        }

        [Fact]
        public void CollectNonWin1252Glyphs_DedupesRepeatedGlyphs()
        {
            var args = new JObject
            {
                ["a"] = "✓✓✓",
                ["b"] = "value=✓ status=✓"
            };
            var glyphs = WriteService.CollectNonWin1252Glyphs(args);
            Assert.Single(glyphs);
            Assert.Equal("✓", glyphs[0]);
        }

        [Fact]
        public void CollectNonWin1252Glyphs_NullArgs_ReturnsEmpty()
        {
            Assert.Empty(WriteService.CollectNonWin1252Glyphs(null));
        }

        [Fact]
        public void CollectNonWin1252Glyphs_GlyphInsidePatchFind_NotFlagged()
        {
            // Caller is REMOVING the lossy glyph — the persisted result has no ✓,
            // so the warning would be spurious.
            var args = new JObject
            {
                ["mode"] = "patch",
                ["patch"] = new JObject
                {
                    ["find"] = "Status: ✓ Done",
                    ["replace"] = "Status: OK"
                }
            };
            Assert.Empty(WriteService.CollectNonWin1252Glyphs(args));
        }

        [Fact]
        public void CollectNonWin1252Glyphs_GlyphInsideReplace_StillFlagged()
        {
            // Replace is the write-side; lossy glyphs there DO land on disk.
            var args = new JObject
            {
                ["mode"] = "patch",
                ["patch"] = new JObject
                {
                    ["find"] = "Status: OK",
                    ["replace"] = "Status: ✓ Done"
                }
            };
            var glyphs = WriteService.CollectNonWin1252Glyphs(args);
            Assert.Contains("✓", glyphs);
        }

        [Fact]
        public void CollectNonWin1252Glyphs_LegacyContextKey_NotFlagged()
        {
            var args = new JObject
            {
                ["context"] = "old ✓ value",
                ["content"] = "new value"
            };
            Assert.Empty(WriteService.CollectNonWin1252Glyphs(args));
        }
    }
}
