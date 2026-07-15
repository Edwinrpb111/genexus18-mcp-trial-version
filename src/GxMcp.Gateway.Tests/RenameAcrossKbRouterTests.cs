using GxMcp.Gateway.Routers;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GxMcp.Gateway.Tests
{
    // Item 91: genexus_rename_across_kb is claimed by OperationsRouter and routes
    // to the existing RefactorService.Refactor(action=RenameObject|RenameAttribute)
    // path. These tests pin the action selection (Attribute vs default) and the
    // payload shape (oldName/newName) that the worker dispatcher expects.
    public class RenameAcrossKbRouterTests
    {
        [Fact]
        public void NonAttribute_Type_Routes_As_RenameObject()
        {
            var router = new OperationsRouter();
            var args = new JObject
            {
                ["from"] = "ProcOld",
                ["to"] = "ProcNew",
                ["type"] = "Procedure"
            };
            var routed = router.ConvertToolCall("genexus_rename_across_kb", args);
            Assert.NotNull(routed);
            var jo = JObject.FromObject(routed!);
            Assert.Equal("Refactor", jo["module"]!.ToString());
            Assert.Equal("RenameObject", jo["action"]!.ToString());
            Assert.Equal("ProcOld", jo["target"]!.ToString());
            var payload = JObject.Parse(jo["payload"]!.ToString());
            Assert.Equal("ProcOld", payload["oldName"]!.ToString());
            Assert.Equal("ProcNew", payload["newName"]!.ToString());
            // Bug fix: type must reach the worker payload so RenameObject can
            // disambiguate same-named objects across types (e.g. Table vs WebPanel).
            Assert.Equal("Procedure", payload["type"]!.ToString());
        }

        [Fact]
        public void Attribute_Type_Routes_As_RenameAttribute()
        {
            var router = new OperationsRouter();
            var args = new JObject
            {
                ["from"] = "AttrOld",
                ["to"] = "AttrNew",
                ["type"] = "Attribute"
            };
            var routed = router.ConvertToolCall("genexus_rename_across_kb", args);
            var jo = JObject.FromObject(routed!);
            Assert.Equal("RenameAttribute", jo["action"]!.ToString());
        }

        [Fact]
        public void Missing_Type_Defaults_To_RenameObject()
        {
            var router = new OperationsRouter();
            var args = new JObject
            {
                ["from"] = "X",
                ["to"] = "Y"
            };
            var routed = router.ConvertToolCall("genexus_rename_across_kb", args);
            var jo = JObject.FromObject(routed!);
            Assert.Equal("RenameObject", jo["action"]!.ToString());
        }
    }
}
