using System.Collections.Generic;
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
        public void SpareCount_Zero_Disables_NoPrespawn()
        {
            var pool = new WorkerPool(Cfg());
            var result = pool.ConfigureWarmSpares(0, new List<KbHandle>());
            Assert.Equal(0, result.Configured);
            Assert.Equal(0, result.Requested);
            Assert.False(result.Capped);
            Assert.Empty(result.Prespawned);
            Assert.Equal(0, pool.WarmSpareCount);
        }

        [Fact]
        public void SpareCount_AboveCap_Clamps_And_FlagsCapped()
        {
            var pool = new WorkerPool(Cfg());
            // Empty declared list so AcquireAsync isn't called even after the clamp.
            var result = pool.ConfigureWarmSpares(99, new List<KbHandle>());
            Assert.Equal(99, result.Requested);
            Assert.Equal(WorkerPool.MaxWarmSpareCount, result.Configured);
            Assert.True(result.Capped);
            Assert.Empty(result.Prespawned);
            Assert.Equal(WorkerPool.MaxWarmSpareCount, pool.WarmSpareCount);
        }

        [Fact]
        public void SpareCount_NegativeTreatedAsZero()
        {
            var pool = new WorkerPool(Cfg());
            var result = pool.ConfigureWarmSpares(-3, new List<KbHandle>());
            Assert.Equal(-3, result.Requested);
            Assert.Equal(0, result.Configured);
            Assert.False(result.Capped);
        }

        [Fact]
        public void SpareCount_ConfiguredButNoDeclaredKbs_PrespawnEmpty()
        {
            var pool = new WorkerPool(Cfg());
            var result = pool.ConfigureWarmSpares(2, new List<KbHandle>());
            // Budget=2 but no KBs to spawn against: nothing happens, nothing skipped.
            Assert.Equal(2, result.Configured);
            Assert.Empty(result.Prespawned);
            Assert.Empty(result.Skipped);
        }
    }
}
