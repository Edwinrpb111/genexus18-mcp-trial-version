using Newtonsoft.Json.Linq;
using Xunit;
using GxMcp.Gateway;
using GxMcp.Gateway.Routers;

namespace GxMcp.Gateway.Tests
{
    public class EditValidationContractTests
    {
        [Fact]
        public void Edit_InvalidMode_ThrowsUsageWithSuggestion()
        {
            var router = new ObjectRouter();
            var args = JObject.Parse("{\"name\":\"Customer\",\"mode\":\"patche\",\"patch\":[]}");
            var ex = Assert.Throws<UsageException>(() => router.ConvertToolCall("genexus_edit", args));
            Assert.Equal("usage_error", ex.Code);
            Assert.Contains("'patch'", ex.Message);
            Assert.Contains("Did you mean", ex.Message);
        }

        [Fact]
        public void Edit_InvalidMode_ListsAllowedSet()
        {
            var router = new ObjectRouter();
            var args = JObject.Parse("{\"name\":\"Customer\",\"mode\":\"banana\"}");
            var ex = Assert.Throws<UsageException>(() => router.ConvertToolCall("genexus_edit", args));
            Assert.Contains("xml", ex.Message);
            Assert.Contains("ops", ex.Message);
            Assert.Contains("patch", ex.Message);
            Assert.Contains("full", ex.Message);
        }

        [Fact]
        public void Edit_OpsMode_InvalidOpName_ThrowsWithSuggestion()
        {
            var router = new ObjectRouter();
            var args = JObject.Parse(
                "{\"name\":\"Customer\",\"mode\":\"ops\",\"ops\":[{\"op\":\"set_atribute\",\"name\":\"X\"}]}");
            var ex = Assert.Throws<UsageException>(() => router.ConvertToolCall("genexus_edit", args));
            Assert.Contains("set_attribute", ex.Message);
            Assert.Contains("ops[0]", ex.Message);
        }

        [Fact]
        public void Edit_OpsMode_MissingOpField_ThrowsClear()
        {
            var router = new ObjectRouter();
            var args = JObject.Parse(
                "{\"name\":\"Customer\",\"mode\":\"ops\",\"ops\":[{\"name\":\"X\"}]}");
            var ex = Assert.Throws<UsageException>(() => router.ConvertToolCall("genexus_edit", args));
            Assert.Contains("'op' field is required", ex.Message);
        }

        [Fact]
        public void Edit_OpsMode_AllValidOps_DoNotThrow()
        {
            var router = new ObjectRouter();
            string[] validOps = {
                "set_attribute", "add_attribute", "remove_attribute",
                "add_rule", "remove_rule", "set_property"
            };
            foreach (var op in validOps)
            {
                var args = JObject.Parse(
                    "{\"name\":\"Customer\",\"mode\":\"ops\",\"ops\":[{\"op\":\"" + op + "\",\"name\":\"X\"}]}");
                var result = router.ConvertToolCall("genexus_edit", args);
                Assert.NotNull(result);
            }
        }

        [Fact]
        public void Edit_NoMode_DoesNotValidate()
        {
            var router = new ObjectRouter();
            var args = JObject.Parse("{\"name\":\"Customer\",\"content\":\"<x/>\"}");
            var result = router.ConvertToolCall("genexus_edit", args);
            Assert.NotNull(result);
        }

        [Fact]
        public void Edit_XmlMode_Accepted()
        {
            var router = new ObjectRouter();
            var args = JObject.Parse("{\"name\":\"Customer\",\"mode\":\"xml\",\"content\":\"<x/>\"}");
            var result = router.ConvertToolCall("genexus_edit", args);
            Assert.NotNull(result);
        }

        [Fact]
        public void Edit_FullMode_Accepted()
        {
            var router = new ObjectRouter();
            var args = JObject.Parse("{\"name\":\"Customer\",\"mode\":\"full\",\"content\":\"<x/>\"}");
            var result = router.ConvertToolCall("genexus_edit", args);
            Assert.NotNull(result);
        }
    }
}
