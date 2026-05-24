using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GxMcp.Gateway.Tests
{
    // Item 55: KbDiffHelper.Diff walks <kb>/Objects/<Type>/<Name>/ on disk.
    // Tests cover: identical KBs (no diff), disjoint KBs (everything in
    // onlyInA / onlyInB), and partial overlap with one modified object.
    public class KbDiffHelperTests : IDisposable
    {
        private readonly string _root;

        public KbDiffHelperTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "gxmcp-kbdiff-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        private string CreateKb(string name)
        {
            string p = Path.Combine(_root, name);
            Directory.CreateDirectory(Path.Combine(p, "Objects"));
            return p;
        }

        private void AddObject(string kb, string type, string objName, string content = "x")
        {
            string dir = Path.Combine(kb, "Objects", type, objName);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "part.xml"), content);
        }

        [Fact]
        public void Identical_Kbs_Produce_Empty_Diff()
        {
            string a = CreateKb("a");
            string b = CreateKb("b");
            AddObject(a, "WebPanel", "Home");
            AddObject(b, "WebPanel", "Home");
            // Set identical mtimes so the modified detector doesn't fire on tiny mtime jitter.
            var t = DateTime.UtcNow;
            File.SetLastWriteTimeUtc(Path.Combine(a, "Objects", "WebPanel", "Home", "part.xml"), t);
            File.SetLastWriteTimeUtc(Path.Combine(b, "Objects", "WebPanel", "Home", "part.xml"), t);

            var diff = KbDiffHelper.Diff(a, b);
            Assert.Empty((JArray)diff["onlyInA"]!);
            Assert.Empty((JArray)diff["onlyInB"]!);
            Assert.Empty((JArray)diff["modified"]!);
            Assert.Equal(1, (int)diff["countA"]!);
            Assert.Equal(1, (int)diff["countB"]!);
        }

        [Fact]
        public void Disjoint_Kbs_Everything_Is_OnlyIn()
        {
            string a = CreateKb("a");
            string b = CreateKb("b");
            AddObject(a, "Procedure", "ProcA");
            AddObject(a, "WebPanel", "Home");
            AddObject(b, "Transaction", "TrnB");

            var diff = KbDiffHelper.Diff(a, b);
            var onlyA = (JArray)diff["onlyInA"]!;
            var onlyB = (JArray)diff["onlyInB"]!;
            Assert.Equal(2, onlyA.Count);
            Assert.Single(onlyB);
            Assert.Empty((JArray)diff["modified"]!);
            Assert.Contains(onlyA, t => t.ToString() == "Procedure:ProcA");
            Assert.Contains(onlyA, t => t.ToString() == "WebPanel:Home");
            Assert.Contains(onlyB, t => t.ToString() == "Transaction:TrnB");
        }

        [Fact]
        public void Partial_Overlap_With_Modified_Object()
        {
            string a = CreateKb("a");
            string b = CreateKb("b");
            AddObject(a, "WebPanel", "Shared");
            AddObject(b, "WebPanel", "Shared");
            AddObject(a, "WebPanel", "OnlyInA");
            AddObject(b, "Procedure", "OnlyInB");
            // Spread mtimes by >1s so Shared shows as modified.
            File.SetLastWriteTimeUtc(Path.Combine(a, "Objects", "WebPanel", "Shared", "part.xml"),
                DateTime.UtcNow.AddSeconds(-60));
            File.SetLastWriteTimeUtc(Path.Combine(b, "Objects", "WebPanel", "Shared", "part.xml"),
                DateTime.UtcNow);

            var diff = KbDiffHelper.Diff(a, b);
            Assert.Single((JArray)diff["onlyInA"]!);
            Assert.Single((JArray)diff["onlyInB"]!);
            var mod = (JArray)diff["modified"]!;
            Assert.Single(mod);
            Assert.Equal("Shared", mod[0]!["name"]!.ToString());
            Assert.Equal("WebPanel", mod[0]!["type"]!.ToString());
        }
    }
}
