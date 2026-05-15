using Xunit;
using GxMcp.Worker.Services;
using GxMcp.Worker.Models;

namespace GxMcp.Worker.Tests
{
    public class IndexStateTests
    {
        [Fact]
        public void GetState_BeforeIndex_ReturnsCold()
        {
            var svc = new IndexCacheService();
            var s = svc.GetState();
            Assert.Equal("Cold", s.Status);
            Assert.Null(s.LastIndexedAt);
            Assert.Equal(0, s.TotalObjects);
        }

        [Fact]
        public void GetState_AfterIndex_ReturnsReadyWithCount()
        {
            var svc = new IndexCacheService();
            svc.MarkIndexComplete(totalObjects: 42);
            var s = svc.GetState();
            Assert.Equal("Ready", s.Status);
            Assert.NotNull(s.LastIndexedAt);
            Assert.Equal(42, s.TotalObjects);
        }

        [Fact]
        public void MarkReindexStarted_SetsStatusToReindexing_Progress0()
        {
            var svc = new IndexCacheService();
            svc.MarkReindexStarted(100);
            var s = svc.GetState();
            Assert.Equal("Reindexing", s.Status);
            Assert.Equal(0, s.Progress);
            Assert.Equal(100, s.TotalObjects);
        }

        [Fact]
        public void MarkReindexProgress_UpdatesProgressAndEta()
        {
            var svc = new IndexCacheService();
            svc.MarkReindexStarted(100);
            svc.MarkReindexProgress(0.5, 8000);
            var s = svc.GetState();
            Assert.Equal(0.5, s.Progress);
            Assert.Equal(8000, s.EtaMs);
        }

        [Fact]
        public void MarkIndexComplete_ClearsProgressAndEta()
        {
            var svc = new IndexCacheService();
            svc.MarkReindexStarted(100);
            svc.MarkReindexProgress(0.5, 8000);
            svc.MarkIndexComplete(42);
            var s = svc.GetState();
            Assert.Equal("Ready", s.Status);
            Assert.Null(s.Progress);
            Assert.Null(s.EtaMs);
            Assert.Equal(42, s.TotalObjects);
        }

        [Fact]
        public void MarkIndexFailed_AfterReindexStarted_ResetsToCold()
        {
            var svc = new IndexCacheService();
            svc.MarkReindexStarted(100);
            svc.MarkIndexFailed();
            var s = svc.GetState();
            Assert.Equal("Cold", s.Status);
            Assert.Null(s.Progress);
            Assert.Null(s.EtaMs);
        }
    }
}
