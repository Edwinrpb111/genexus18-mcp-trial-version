using GxMcp.Worker.Services;
using Xunit;

namespace GxMcp.Worker.Tests
{
    /// <summary>
    /// Friction 2026-05-26 — the worker stamped the .gxw with the GeneXus.exe
    /// FileVersion (e.g. 18.0.7.48055) instead of its ProductVersion
    /// (18.0.7.179127). On installs where the two differ, the IDE re-showed the
    /// "different GeneXus installation than last time" dialog every time the MCP
    /// touched the KB. These tests pin the ProductVersion-based IDE-canonical
    /// formatting.
    /// </summary>
    public class GxwVersionStampTests
    {
        [Fact]
        public void BuildStampFromParts_ProducesIdeCanonicalFormats()
        {
            // ProductVersion 18.0.7.179127 → Major=18 Minor=0 Build=7 Private=179127.
            var stamp = KbService.BuildStampFromParts(18, 0, 7, 179127);

            Assert.Equal("18", stamp.ProductVersionShort);
            Assert.Equal("18.0.179127 U7", stamp.FriendlyVersion);
            Assert.Equal("18.0.7.179127", stamp.VersionNumber);
        }

        [Fact]
        public void BuildStampFromParts_DoesNotEmitFileVersionBuild()
        {
            // Regression guard: the OLD code fed FileVersion parts (build 48055)
            // into the same format, yielding "18.0.48055 U7" — the exact "Before"
            // value in the dialog. Ensure the ProductVersion private part (179127)
            // is what lands in FriendlyVersion, never a FileVersion build number.
            var stamp = KbService.BuildStampFromParts(18, 0, 7, 179127);
            Assert.DoesNotContain("48055", stamp.FriendlyVersion);
            Assert.Contains("179127", stamp.FriendlyVersion);
        }

        [Theory]
        [InlineData(18, 0, 14, 187794, "18.0.187794 U14", "18.0.14.187794")]
        [InlineData(18, 0, 0, 100000, "18.0.100000 U0", "18.0.0.100000")]
        public void BuildStampFromParts_FormatsVariousUpdateLevels(
            int maj, int min, int build, int priv, string expectedFriendly, string expectedVersionNumber)
        {
            var stamp = KbService.BuildStampFromParts(maj, min, build, priv);
            Assert.Equal(expectedFriendly, stamp.FriendlyVersion);
            Assert.Equal(expectedVersionNumber, stamp.VersionNumber);
        }

        [Theory]
        [InlineData("18.0.7.179127", 18, 0, 7, 179127)]
        [InlineData("18.0.7.179127+a1b2c3", 18, 0, 7, 179127)]   // strip git-sha suffix
        [InlineData("18.0.14.187794 ", 18, 0, 14, 187794)]        // strip trailing space
        public void ParseProductVersionString_ExtractsFourNumericParts(
            string raw, int maj, int min, int build, int priv)
        {
            var parts = KbService.ParseProductVersionString(raw);
            Assert.NotNull(parts);
            Assert.Equal(new[] { maj, min, build, priv }, parts);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("18.0.7")]        // fewer than 4 parts
        [InlineData("not.a.version.x")] // non-numeric
        public void ParseProductVersionString_ReturnsNullOnUnparseable(string raw)
        {
            Assert.Null(KbService.ParseProductVersionString(raw));
        }

        [Fact]
        public void ParseThenBuild_ProducesIdeStamp_FromProductVersionString()
        {
            // End-to-end of the fix: the ProductVersion string (not the numeric
            // FileVersion parts) is the source of truth. "18.0.7.179127" must
            // yield the IDE's "18.0.179127 U7", never the FileVersion's 48055.
            var parts = KbService.ParseProductVersionString("18.0.7.179127");
            var stamp = KbService.BuildStampFromParts(parts[0], parts[1], parts[2], parts[3]);
            Assert.Equal("18.0.179127 U7", stamp.FriendlyVersion);
            Assert.Equal("18.0.7.179127", stamp.VersionNumber);
            Assert.Equal("18", stamp.ProductVersionShort);
        }
    }
}
