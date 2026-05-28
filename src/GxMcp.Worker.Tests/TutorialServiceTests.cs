using GxMcp.Worker.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GxMcp.Worker.Tests
{
    public class TutorialServiceTests
    {
        [Fact]
        public void GetStep_One_ReturnsOrientStep()
        {
            var svc = new TutorialService();
            var j = JObject.Parse(svc.GetStep(1));

            Assert.Equal("ok", (string)j["status"]);
            Assert.Equal("TutorialStep", (string)j["code"]);
            Assert.Equal(1, (int)j["result"]["stepNumber"]);
            Assert.Equal(6, (int)j["result"]["totalSteps"]);
            Assert.Equal("Orient", (string)j["result"]["title"]);
            Assert.NotNull(j["result"]["suggestedCall"]);
            Assert.Equal("genexus_whoami", (string)j["result"]["suggestedCall"]["tool"]);
            Assert.Equal(2, (int)j["result"]["next"]);
        }

        [Fact]
        public void GetStep_Six_HasNullNext()
        {
            var svc = new TutorialService();
            var j = JObject.Parse(svc.GetStep(6));

            Assert.Equal("ok", (string)j["status"]);
            Assert.Equal(6, (int)j["result"]["stepNumber"]);
            Assert.Equal(6, (int)j["result"]["totalSteps"]);
            // Final step → next is JSON null (no further step).
            Assert.True(j["result"]["next"] == null || j["result"]["next"].Type == JTokenType.Null);
        }

        [Fact]
        public void GetStep_Zero_ReturnsOutOfRangeError()
        {
            var svc = new TutorialService();
            var j = JObject.Parse(svc.GetStep(0));

            Assert.Equal("error", (string)j["status"]);
            Assert.Equal("StepOutOfRange", (string)j["error"]["code"]);
            Assert.Equal(6, (int)j["totalSteps"]);
        }

        [Fact]
        public void GetStep_Seven_ReturnsOutOfRangeError()
        {
            var svc = new TutorialService();
            var j = JObject.Parse(svc.GetStep(7));

            Assert.Equal("error", (string)j["status"]);
            Assert.Equal("StepOutOfRange", (string)j["error"]["code"]);
        }
    }
}
