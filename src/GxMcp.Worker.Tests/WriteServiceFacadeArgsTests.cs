using GxMcp.Worker.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GxMcp.Worker.Tests
{
    public class WriteServiceFacadeArgsTests
    {
        [Fact]
        public void NormalizeFacadeArgs_FullMode_UsesRealPartAndContent()
        {
            var normalized = WriteService.NormalizeFacadeArgs(new JObject
            {
                ["part"] = "Source",
                ["content"] = "parm(out:&Ok);",
                ["dryRun"] = true
            });

            Assert.Null(normalized.Mode);
            Assert.Equal("Source", normalized.PartName);
            Assert.Equal("parm(out:&Ok);", normalized.Content);
            Assert.True(normalized.DryRun);
        }

        [Fact]
        public void NormalizeFacadeArgs_PatchMode_UnwrapsFindReplaceShape()
        {
            var normalized = WriteService.NormalizeFacadeArgs(new JObject
            {
                ["mode"] = "patch",
                ["part"] = "Source",
                ["content"] = new JObject
                {
                    ["find"] = "old line",
                    ["replace"] = "new line"
                },
                ["expectedCount"] = 2,
                ["replaceAll"] = true
            });

            Assert.Equal("patch", normalized.Mode);
            Assert.Equal("Source", normalized.PartName);
            Assert.Equal("Replace", normalized.Operation);
            Assert.Equal("old line", normalized.Context);
            Assert.Equal("new line", normalized.Content);
            Assert.Equal(2, normalized.ExpectedCount);
            Assert.True(normalized.ReplaceAll);
        }
    }
}
