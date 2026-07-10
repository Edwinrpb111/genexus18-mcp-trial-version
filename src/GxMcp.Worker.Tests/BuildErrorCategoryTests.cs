using System.Linq;
using GxMcp.Worker.Services;
using Xunit;

namespace GxMcp.Worker.Tests
{
    /// <summary>
    /// issue #28 item 13 — build output splits environment/infra errors from the
    /// authored object's spec/code errors so an agent can tell "my code is wrong"
    /// from "the KB can't compile in this environment".
    /// </summary>
    public class BuildErrorCategoryTests
    {
        [Theory]
        [InlineData("error CS2001: Origem 'GxWebServicesConfig.cs' não pôde ser encontrada", "environment")]
        [InlineData("error MSB3245: Não foi possível resolver a referência \"GeneXus.Security.API.Common.dll\"", "environment")]
        [InlineData("error CS0006: Metadata file 'Foo.dll' could not be found", "environment")]
        [InlineData("error NU1101: Unable to find package", "environment")]
        [InlineData("error spc0031: attribute not found", "spec")]
        [InlineData("error gen0022: generation issue", "spec")]
        [InlineData("error CS0246: type 'Foo' could not be found", "code")]
        [InlineData("error CS1002: ; expected", "code")]
        public void ClassifyErrorCategory_BucketsCorrectly(string line, string expected)
        {
            Assert.Equal(expected, BuildService.ClassifyErrorCategory(line));
        }

        [Fact]
        public void Status_SplitsEnvVsCode_AndHintsWhenOnlyEnvironmental()
        {
            var status = new BuildService.BuildTaskStatus { TaskId = "t1", Status = "Failed" };
            status.ErrorsDetailed.Add(new BuildService.ErrorDetail { raw = "error CS2001: 'GxWebServicesConfig.cs' não pôde ser encontrada", category = "environment" });
            status.ErrorsDetailed.Add(new BuildService.ErrorDetail { raw = "error MSB3245: unresolved GeneXus.Security.API.Common.dll", category = "environment" });

            Assert.Equal(2, status.EnvErrorCount);
            Assert.Equal(0, status.CodeErrorCount);
            Assert.NotNull(status.EnvErrorsHint);
            Assert.Contains("environment", status.EnvErrorsHint);
        }

        [Fact]
        public void Status_NoHint_WhenAuthoredCodeAlsoFailed()
        {
            var status = new BuildService.BuildTaskStatus { TaskId = "t2", Status = "Failed" };
            status.ErrorsDetailed.Add(new BuildService.ErrorDetail { raw = "error MSB3245: unresolved dll", category = "environment" });
            status.ErrorsDetailed.Add(new BuildService.ErrorDetail { raw = "error spc0031: attribute not found", category = "spec" });

            Assert.Equal(1, status.EnvErrorCount);
            Assert.Equal(1, status.CodeErrorCount);
            Assert.Null(status.EnvErrorsHint);
            Assert.Single(status.EnvErrors);
            Assert.Single(status.CodeErrors);
        }
    }
}
