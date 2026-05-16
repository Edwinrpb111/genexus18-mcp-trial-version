using GxMcp.Worker.Helpers;
using System.Collections.Generic;
using Xunit;

namespace GxMcp.Worker.Tests
{
    public class DslParserUtilsTests
    {
        [Fact]
        public void KeyMarkerAdjacentToColon_StripsFromNameAndSetsIsKey()
        {
            // Regression: prior to v2.4.1 the parser only stripped `*` when it ended the entire
            // trimmed line, so "TrnId* : Numeric(4)" left "TrnId*" as the Name and IsKey=false.
            // That made the existingItems lookup miss and forced unintended create-new paths.
            var nodes = DslParserUtils.ParseLinesIntoNodes(new List<string> { "TrnId* : Numeric(4)" });

            Assert.Single(nodes);
            Assert.Equal("TrnId", nodes[0].Name);
            Assert.True(nodes[0].IsKey);
            Assert.Equal("Numeric(4)", nodes[0].TypeStr);
            Assert.False(nodes[0].IsCompound);
        }

        [Fact]
        public void KeyMarkerAtEndOfBareName_StillRecognized()
        {
            var nodes = DslParserUtils.ParseLinesIntoNodes(new List<string> { "TrnId*" });

            Assert.Single(nodes);
            Assert.Equal("TrnId", nodes[0].Name);
            Assert.True(nodes[0].IsKey);
        }

        [Fact]
        public void NoKeyMarker_LeavesIsKeyFalse()
        {
            var nodes = DslParserUtils.ParseLinesIntoNodes(new List<string> { "TrnDesc : Character(60)" });

            Assert.Single(nodes);
            Assert.Equal("TrnDesc", nodes[0].Name);
            Assert.False(nodes[0].IsKey);
            Assert.Equal("Character(60)", nodes[0].TypeStr);
        }
    }
}
