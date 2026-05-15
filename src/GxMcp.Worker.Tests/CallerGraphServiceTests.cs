using System.Linq;
using GxMcp.Worker.Services;
using Xunit;

namespace GxMcp.Worker.Tests
{
    // v2.3.8 (Task 1.3): unit tests for the unified caller/callee graph
    // service. Drives an in-memory IndexCacheService via the LoadFromEntries
    // test seam so the tests don't need a real KB.
    public class CallerGraphServiceTests
    {
        [Fact]
        public void GetCallers_ReturnsDirectCallers()
        {
            var fx = TestFixtures.SmallCallGraph();
            var svc = new CallerGraphService(fx.Index);

            var callersOfB = svc.GetCallers("B");
            Assert.Contains("A", callersOfB);
            // A is the only direct caller of B in the small chain
            Assert.Single(callersOfB.Distinct(System.StringComparer.OrdinalIgnoreCase));

            var callersOfC = svc.GetCallers("C");
            Assert.Contains("B", callersOfC);
            Assert.Single(callersOfC.Distinct(System.StringComparer.OrdinalIgnoreCase));
        }

        [Fact]
        public void GetCallees_ReturnsDirectCallees()
        {
            var fx = TestFixtures.SmallCallGraph();
            var svc = new CallerGraphService(fx.Index);

            var calleesOfA = svc.GetCallees("A");
            Assert.Contains("B", calleesOfA);
            Assert.Single(calleesOfA);

            var calleesOfB = svc.GetCallees("B");
            Assert.Contains("C", calleesOfB);
        }

        [Fact]
        public void GetCalleesTransitive_BfsRespectsCap()
        {
            var fx = TestFixtures.LargeCallChain(depth: 250);
            var svc = new CallerGraphService(fx.Index);

            var result = svc.GetCalleesTransitive("N0", maxNodes: 200);

            Assert.True(result.Truncated, "Expected Truncated=true when the chain exceeds maxNodes");
            Assert.Equal(200, result.Nodes.Count);
        }

        [Fact]
        public void GetCalleesTransitive_SmallChain_NotTruncated()
        {
            var fx = TestFixtures.SmallCallGraph(); // A -> B -> C
            var svc = new CallerGraphService(fx.Index);

            var result = svc.GetCalleesTransitive("A", maxNodes: 200);

            Assert.False(result.Truncated);
            Assert.Equal(2, result.Nodes.Count); // B and C, not the root A
            Assert.Contains("B", result.Nodes);
            Assert.Contains("C", result.Nodes);
        }

        [Fact]
        public void GetCallers_MatchesInternalConsistency_WithGetCallees()
        {
            // For each (caller, callee) edge expressed via GetCallees, the
            // inverse GetCallers(callee) must include caller. This is the
            // internal-consistency check called out in the task spec; Task 1.4
            // will extend it to assert parity with AnalyzeService.ImpactAnalysis.
            var fx = TestFixtures.SmallCallGraph();
            var svc = new CallerGraphService(fx.Index);

            foreach (var caller in new[] { "A", "B", "C" })
            {
                foreach (var callee in svc.GetCallees(caller))
                {
                    var callers = svc.GetCallers(callee);
                    Assert.Contains(caller, callers);
                }
            }
        }
    }
}
