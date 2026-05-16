using GxMcp.Worker.Models;
using GxMcp.Worker.Services;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace GxMcp.Worker.Tests
{
    public class LiteIndexBuilderTests
    {
        [Fact]
        public void Build_ProducesLiteEntries_ForEachKbObject()
        {
            var objects = new List<IKbObjectInfo>
            {
                new KbObjectStub { Guid = System.Guid.NewGuid().ToString(), Name = "InvoiceProc", Type = "Procedure", ParentPath = "Main/Procs" },
                new KbObjectStub { Guid = System.Guid.NewGuid().ToString(), Name = "OrderTrn",    Type = "Transaction", ParentPath = "Main/Trns" }
            };

            var builder = new LiteIndexBuilder();
            var entries = builder.Build(objects).ToList();

            Assert.Equal(2, (int)entries.Count);
            Assert.All(entries, e =>
            {
                Assert.False(e.IsEnriched);
                Assert.Null(e.SourceSnippet);
                Assert.True(e.Calls == null || e.Calls.Count == 0);
                Assert.True(e.CalledBy == null || e.CalledBy.Count == 0);
            });
            Assert.Contains(entries, e => e.Name == "InvoiceProc" && e.Type == "Procedure");
        }

        [Fact]
        public void Build_TimingTarget_CompletesUnder1Second_For1000Stubs()
        {
            var objects = Enumerable.Range(0, 1000).Select(i => (IKbObjectInfo)new KbObjectStub
            {
                Guid = System.Guid.NewGuid().ToString(),
                Name = "Obj" + i,
                Type = "Procedure",
                ParentPath = "Main"
            }).ToList();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var entries = new LiteIndexBuilder().Build(objects).ToList();
            sw.Stop();

            Assert.Equal(1000, (int)entries.Count);
            Assert.True(sw.ElapsedMilliseconds < 1000,
                "LiteIndexBuilder should process 1000 stubs in <1s; took " + sw.ElapsedMilliseconds + "ms");
        }
    }
}
