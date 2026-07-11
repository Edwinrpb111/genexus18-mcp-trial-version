using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace GxMcp.Gateway.Tests
{
    // Item 53: genexus_worker_pool action=warm_spares ships configured spare-count
    // tracking + pre-spawn against declared KBs. We can't exercise actual spawning
    // here (would require the GeneXus SDK + a real KB), so these tests validate the
    // clamp/cap/disable contract via the WorkerPool API directly. The
    // declaredKbs=empty branch returns without touching AcquireAsync, so no SDK
    // dependency is dragged in.
    public class WarmSpareConfigurationTests
    {
        private static Configuration Cfg() =>
            new Configuration { Server = new ServerConfig { MaxOpenKbs = 5 } };

        [Fact]
        public async Task SpareCount_Zero_Disables_NoPrespawn()
        {
            var pool = new WorkerPool(Cfg());
            var result = await pool.ConfigureWarmSpares(0, new List<KbHandle>());
            Assert.Equal(0, result.Configured);
            Assert.Equal(0, result.Requested);
            Assert.False(result.Capped);
            Assert.Empty(result.Prespawned);
            Assert.Equal(0, pool.WarmSpareCount);
        }

        [Fact]
        public async Task SpareCount_AboveCap_Clamps_And_FlagsCapped()
        {
            var pool = new WorkerPool(Cfg());
            // Empty declared list so AcquireAsync isn't called even after the clamp.
            var result = await pool.ConfigureWarmSpares(99, new List<KbHandle>());
            Assert.Equal(99, result.Requested);
            Assert.Equal(WorkerPool.MaxWarmSpareCount, result.Configured);
            Assert.True(result.Capped);
            Assert.Empty(result.Prespawned);
            Assert.Equal(WorkerPool.MaxWarmSpareCount, pool.WarmSpareCount);
        }

        [Fact]
        public async Task SpareCount_NegativeTreatedAsZero()
        {
            var pool = new WorkerPool(Cfg());
            var result = await pool.ConfigureWarmSpares(-3, new List<KbHandle>());
            Assert.Equal(-3, result.Requested);
            Assert.Equal(0, result.Configured);
            Assert.False(result.Capped);
        }

        [Fact]
        public async Task SpareCount_ConfiguredButNoDeclaredKbs_PrespawnEmpty()
        {
            var pool = new WorkerPool(Cfg());
            var result = await pool.ConfigureWarmSpares(2, new List<KbHandle>());
            // Budget=2 but no KBs to spawn against: nothing happens, nothing skipped.
            Assert.Equal(2, result.Configured);
            Assert.Empty(result.Prespawned);
            Assert.Empty(result.Skipped);
        }

        // BUG-04 regression: ConfigureWarmSpares used to return synchronously while
        // pre-spawns ran fire-and-forget on the thread pool, so Prespawned/Skipped were
        // always built from an empty bag. The already-open KB path was always
        // synchronous (no AcquireAsync call needed), so this test pins that the
        // returned Task, once awaited, reflects it correctly — and guards against a
        // future regression that makes the whole method fire-and-forget again.
        [Fact]
        public async Task SpareCount_AlreadyOpenKb_ReportedAsSkipped_AfterAwait()
        {
            var pool = new WorkerPool(Cfg());
            var handle = new KbHandle("already-open", "C:/AlreadyOpen");
            var worker = new WorkerProcess(Cfg(), handle);
            try
            {
                pool.RegisterForTest(handle, worker: worker);

                var result = await pool.ConfigureWarmSpares(1, new List<KbHandle> { handle });

                Assert.Empty(result.Prespawned);
                Assert.Contains("already-open", result.Skipped);
            }
            finally
            {
                worker.StopWithReason(WorkerStopReason.ExplicitClose);
            }
        }
    }
}
