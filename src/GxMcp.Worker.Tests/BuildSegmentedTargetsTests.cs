using System.Linq;
using GxMcp.Worker.Services;
using Xunit;

namespace GxMcp.Worker.Tests
{
    // v2.3.8 (Task 5.1): segmented build target expansion. Friction report
    // 2026-05-15 #7: building a WebPanel that calls N procedures fails with
    // CS0246 because each BuildOne generates its own csproj and the callee
    // DLLs aren't on disk yet. Fix: expand the target list via
    // CallerGraphService and emit BuildOne in reverse-topological order
    // (deepest callees first, the originally requested target last) so
    // every csproj sees its dependencies already compiled.
    public class BuildSegmentedTargetsTests
    {
        [Fact]
        public void Expand_Transitive_IncludesAllCalleesAndOrdersLeavesFirst()
        {
            // A -> B -> C. Requesting A with transitive should expand to
            // [C, B, A] so C and B compile before A's csproj is generated.
            var fx = TestFixtures.SmallCallGraph();
            var graph = new CallerGraphService(fx.Index);
            var build = new BuildService();
            build.SetCallerGraphService(graph);

            var plan = build.ExpandTargets(new[] { "A" }, includeCallees: "transitive", cap: 200);

            Assert.False(plan.Truncated);
            Assert.Equal(new[] { "C", "B", "A" }, plan.Expanded.ToArray());
            Assert.Empty(plan.Skipped);
        }

        [Fact]
        public void Expand_Direct_IncludesOnlyFirstHop()
        {
            // A -> B -> C, direct from A includes only B (not C).
            var fx = TestFixtures.SmallCallGraph();
            var graph = new CallerGraphService(fx.Index);
            var build = new BuildService();
            build.SetCallerGraphService(graph);

            var plan = build.ExpandTargets(new[] { "A" }, includeCallees: "direct", cap: 200);

            Assert.False(plan.Truncated);
            Assert.Equal(new[] { "B", "A" }, plan.Expanded.ToArray());
        }

        [Fact]
        public void Expand_None_PreservesOriginalTargetsOnly()
        {
            var fx = TestFixtures.SmallCallGraph();
            var graph = new CallerGraphService(fx.Index);
            var build = new BuildService();
            build.SetCallerGraphService(graph);

            var plan = build.ExpandTargets(new[] { "A" }, includeCallees: "none", cap: 200);

            Assert.False(plan.Truncated);
            Assert.Equal(new[] { "A" }, plan.Expanded.ToArray());
        }

        [Fact]
        public void Expand_OverCap_SetsTruncatedAndRecordsRequested()
        {
            // 250-deep chain, cap 200 → Truncated=true.
            var fx = TestFixtures.LargeCallChain(depth: 250);
            var graph = new CallerGraphService(fx.Index);
            var build = new BuildService();
            build.SetCallerGraphService(graph);

            var plan = build.ExpandTargets(new[] { "N0" }, includeCallees: "transitive", cap: 200);

            Assert.True(plan.Truncated);
            Assert.Equal(200, plan.NodeCap);
        }

        [Fact]
        public void Expand_NullGraph_FallsBackToOriginalTargets()
        {
            // No graph wired (e.g. tests / minimal startup) → expansion is a no-op.
            var build = new BuildService();

            var plan = build.ExpandTargets(new[] { "A", "B" }, includeCallees: "transitive", cap: 200);

            Assert.Equal(new[] { "A", "B" }, plan.Expanded.ToArray());
            Assert.False(plan.Truncated);
        }

        [Fact]
        public void Expand_DedupesAcrossMultipleTargets()
        {
            // A -> B -> C; if we ask for both A and B, B's expansion (C) and
            // A's expansion (C, B) must dedupe to a single occurrence each.
            var fx = TestFixtures.SmallCallGraph();
            var graph = new CallerGraphService(fx.Index);
            var build = new BuildService();
            build.SetCallerGraphService(graph);

            var plan = build.ExpandTargets(new[] { "A", "B" }, includeCallees: "transitive", cap: 200);

            // Originally requested targets always last (in input order),
            // callees first (deepest leaf first).
            Assert.Equal(plan.Expanded.Count, plan.Expanded.Distinct().Count());
            Assert.Contains("C", plan.Expanded);
            Assert.Equal("A", plan.Expanded[plan.Expanded.Count - 2]);
            Assert.Equal("B", plan.Expanded[plan.Expanded.Count - 1]);
        }
    }
}
