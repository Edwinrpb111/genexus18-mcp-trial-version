using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GxMcp.Gateway.Tests
{
    // Item 56: KbImportHelper.ImportObject — filesystem-level part-copy
    // between two KB directories. Tests cover: unknown source object,
    // success path, and the overwrite branch.
    public class KbImportHelperTests : IDisposable
    {
        private readonly string _root;

        public KbImportHelperTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "gxmcp-kbimport-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        private string MakeKb(string name)
        {
            string p = Path.Combine(_root, name);
            Directory.CreateDirectory(Path.Combine(p, "Objects"));
            return p;
        }

        [Fact]
        public void Unknown_Source_Object_Returns_ObjectNotFound()
        {
            string a = MakeKb("a");
            string b = MakeKb("b");
            var result = KbImportHelper.ImportObject(a, b, "Ghost", "WebPanel");
            Assert.Equal("Error", result["status"]!.ToString());
            Assert.Equal("ObjectNotFound", result["code"]!.ToString());
        }

        [Fact]
        public void Success_Copies_All_Part_Files()
        {
            string a = MakeKb("a");
            string b = MakeKb("b");
            string objDir = Path.Combine(a, "Objects", "WebPanel", "Home");
            Directory.CreateDirectory(objDir);
            File.WriteAllText(Path.Combine(objDir, "Layout.xml"), "<root/>");
            File.WriteAllText(Path.Combine(objDir, "Events.txt"), "Event Start\nendevent\n");

            var result = KbImportHelper.ImportObject(a, b, "Home", "WebPanel");
            Assert.Equal("Success", result["status"]!.ToString());
            Assert.Equal(2, (int)result["filesCopied"]!);
            Assert.True(File.Exists(Path.Combine(b, "Objects", "WebPanel", "Home", "Layout.xml")));
            Assert.True(File.Exists(Path.Combine(b, "Objects", "WebPanel", "Home", "Events.txt")));
            Assert.False((bool)result["overwroteExisting"]!);
        }

        [Fact]
        public void Overwrites_Existing_Target_And_Flags_It()
        {
            string a = MakeKb("a");
            string b = MakeKb("b");
            // Source has new file; target has stale file that should be wiped.
            Directory.CreateDirectory(Path.Combine(a, "Objects", "Procedure", "P"));
            File.WriteAllText(Path.Combine(a, "Objects", "Procedure", "P", "Source.txt"), "new");
            Directory.CreateDirectory(Path.Combine(b, "Objects", "Procedure", "P"));
            File.WriteAllText(Path.Combine(b, "Objects", "Procedure", "P", "Stale.txt"), "old");

            var result = KbImportHelper.ImportObject(a, b, "P", "Procedure");
            Assert.Equal("Success", result["status"]!.ToString());
            Assert.True((bool)result["overwroteExisting"]!);
            Assert.True(File.Exists(Path.Combine(b, "Objects", "Procedure", "P", "Source.txt")));
            Assert.False(File.Exists(Path.Combine(b, "Objects", "Procedure", "P", "Stale.txt")));
        }
    }
}
