using System;
using System.Linq;
using GxMcp.Worker.Services;
using GxMcp.Worker.Models;
using Xunit;

namespace GxMcp.Worker.Tests
{
    // Fase 2 — the Guid→storage-key map (SearchIndex.GuidToKey) underpins rename collapse
    // and Guid-based deletion. A rename keeps the Guid stable but changes the Type:Name key,
    // so the index must be able to find an object by Guid regardless of its current name.
    public class GuidKeyMapTests
    {
        private static string UniqueKbPath() =>
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gxmcp-guidtest-" + Guid.NewGuid().ToString("N"));

        [Fact]
        public void ReplaceAll_builds_GuidToKey_for_every_entry()
        {
            var cache = new IndexCacheService();
            cache.Initialize(UniqueKbPath());
            try
            {
                var g1 = Guid.NewGuid().ToString();
                var g2 = Guid.NewGuid().ToString();
                cache.ReplaceAll(new[]
                {
                    new SearchIndex.IndexEntry { Name = "Proc1", Type = "Procedure", Guid = g1 },
                    new SearchIndex.IndexEntry { Name = "Trn1",  Type = "Transaction", Guid = g2 }
                });

                var idx = cache.GetIndex();
                Assert.NotNull(idx.GuidToKey);
                Assert.Equal("Procedure:Proc1", idx.GuidToKey[g1]);
                Assert.Equal("Transaction:Trn1", idx.GuidToKey[g2]);
            }
            finally { cache.DeleteOnDiskSnapshot(); }
        }

        [Fact]
        public void RemoveEntryByGuid_drops_object_and_mapping()
        {
            var cache = new IndexCacheService();
            cache.Initialize(UniqueKbPath());
            try
            {
                var g1 = Guid.NewGuid().ToString();
                cache.ReplaceAll(new[]
                {
                    new SearchIndex.IndexEntry { Name = "Gone", Type = "Procedure", Guid = g1 },
                    new SearchIndex.IndexEntry { Name = "Stay", Type = "Procedure", Guid = Guid.NewGuid().ToString() }
                });

                cache.RemoveEntryByGuid(g1);

                var idx = cache.GetIndex();
                Assert.False(idx.Objects.ContainsKey("Procedure:Gone"));
                Assert.True(idx.Objects.ContainsKey("Procedure:Stay"));
                Assert.False(idx.GuidToKey.ContainsKey(g1));
            }
            finally { cache.DeleteOnDiskSnapshot(); }
        }

        [Fact]
        public void RemoveEntryByGuid_is_noop_for_unknown_guid()
        {
            var cache = new IndexCacheService();
            cache.Initialize(UniqueKbPath());
            try
            {
                cache.ReplaceAll(new[]
                {
                    new SearchIndex.IndexEntry { Name = "Stay", Type = "Procedure", Guid = Guid.NewGuid().ToString() }
                });

                cache.RemoveEntryByGuid(Guid.NewGuid().ToString()); // unknown — must not throw or remove

                Assert.True(cache.GetIndex().Objects.ContainsKey("Procedure:Stay"));
            }
            finally { cache.DeleteOnDiskSnapshot(); }
        }
    }
}
