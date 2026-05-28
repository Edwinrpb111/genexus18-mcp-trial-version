using System.Collections.Generic;
using GxMcp.Worker.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GxMcp.Worker.Tests
{
    /// <summary>
    /// Item 15 (mcp-improvements-2026-05-22) — multi-object atomic edits.
    /// Covers the rollback-replay helper directly so the SDK-less unit tests
    /// can drive happy / failure / partial-rollback-failure paths.
    /// </summary>
    public class BulkTransactionalRollbackTests
    {
        [Fact]
        public void HappyPath_AllRestoresSucceed_ReportRestored()
        {
            var plan = new List<WriteService.BulkRollbackItem>
            {
                new WriteService.BulkRollbackItem { Name = "A", Part = "Source", SnapshotPath = "/snap-A" },
                new WriteService.BulkRollbackItem { Name = "B", Part = "Source", SnapshotPath = "/snap-B" }
            };

            var writeCalls = new List<string>();
            JArray results = WriteService.BulkRollbackReplay(
                plan,
                path => path == "/snap-A" ? "prior-A" : "prior-B",
                (name, part, content, type) =>
                {
                    writeCalls.Add(name + ":" + content);
                    return "{\"status\":\"Success\"}";
                });

            // Reverse order: B then A.
            Assert.Equal("B:prior-B", writeCalls[0]);
            Assert.Equal("A:prior-A", writeCalls[1]);
            Assert.Equal(2, results.Count);
            Assert.Equal("Restored", results[0]["itemStatus"]?.ToString());
            Assert.Equal("Restored", results[1]["itemStatus"]?.ToString());
        }

        [Fact]
        public void PartialFailure_OneRollbackFails_OthersStillReported()
        {
            var plan = new List<WriteService.BulkRollbackItem>
            {
                new WriteService.BulkRollbackItem { Name = "A", SnapshotPath = "/snap-A" },
                new WriteService.BulkRollbackItem { Name = "B", SnapshotPath = "/snap-B" },
                new WriteService.BulkRollbackItem { Name = "C", SnapshotPath = "/snap-C" }
            };

            JArray results = WriteService.BulkRollbackReplay(
                plan,
                path => "prior",
                (name, part, content, type) =>
                {
                    // Replay order is C, B, A. Fail B; A and C must still be reported.
                    if (name == "B") return "{\"status\":\"Error\",\"error\":\"validation failed\"}";
                    return "{\"status\":\"Success\"}";
                });

            Assert.Equal(3, results.Count);
            // Reverse order: index 0 → C, 1 → B, 2 → A
            Assert.Equal("C", results[0]["target"]?.ToString());
            Assert.Equal("Restored", results[0]["itemStatus"]?.ToString());
            Assert.Equal("B", results[1]["target"]?.ToString());
            Assert.Equal("Error", results[1]["itemStatus"]?.ToString());
            Assert.Equal("A", results[2]["target"]?.ToString());
            Assert.Equal("Restored", results[2]["itemStatus"]?.ToString());
        }

        [Fact]
        public void UnreadableSnapshot_SurfacesAsErrorNotException()
        {
            var plan = new List<WriteService.BulkRollbackItem>
            {
                new WriteService.BulkRollbackItem { Name = "A", SnapshotPath = "/snap-A" }
            };

            JArray results = WriteService.BulkRollbackReplay(
                plan,
                path => null, // snapshot unreadable
                (name, part, content, type) => "{\"status\":\"Success\"}");

            Assert.Single(results);
            Assert.Equal("Error", results[0]["itemStatus"]?.ToString());
            Assert.Contains("Snapshot bytes unreadable", results[0]["message"]?.ToString() ?? "");
        }

        [Fact]
        public void WriterThrows_SurfacesAsErrorWithMessage()
        {
            var plan = new List<WriteService.BulkRollbackItem>
            {
                new WriteService.BulkRollbackItem { Name = "A", SnapshotPath = "/snap-A" }
            };

            JArray results = WriteService.BulkRollbackReplay(
                plan,
                path => "prior",
                (name, part, content, type) => { throw new System.Exception("boom"); });

            Assert.Single(results);
            Assert.Equal("Error", results[0]["itemStatus"]?.ToString());
            Assert.Contains("boom", results[0]["message"]?.ToString() ?? "");
        }

        [Fact]
        public void BulkWrite_NoTargets_ReturnsErrorEnvelope()
        {
            // Smoke: the BulkWrite entry point itself short-circuits on empty input.
            var ws = BuildIsolatedWriteService();
            string raw = ws.BulkWrite(new JObject { ["targets"] = new JArray() });
            var jo = JObject.Parse(raw);
            Assert.Equal("error", jo["status"]?.ToString());
        }

        [Fact]
        public void BulkWrite_Transactional_MissingContent_FailsAndDoesNotCrashWithoutKb()
        {
            // Without a KB open, snapshot capture returns null and the write returns
            // an error envelope. The transactional path must not throw — it should
            // produce a RolledBack envelope with empty rollback plan since no
            // prior write succeeded.
            var ws = BuildIsolatedWriteService();
            var args = new JObject
            {
                ["transactional"] = true,
                ["targets"] = new JArray
                {
                    new JObject { ["name"] = "Foo" /* no content */ }
                }
            };
            string raw = ws.BulkWrite(args);
            var jo = JObject.Parse(raw);
            Assert.Equal("error", jo["status"]?.ToString());
            Assert.True(jo["error"]?["code"]?.ToString()?.Contains("Rolled") == true
                        || jo["error"]?["code"]?.ToString()?.Contains("Bulk") == true,
                        "Expected error.code containing 'Rolled' or 'Bulk'. Got: " + jo.ToString());
            Assert.Equal("Foo", jo["failedAt"]?.ToString());
            Assert.NotNull(jo["rollbackResults"]);
        }

        private static WriteService BuildIsolatedWriteService()
        {
            var indexCache = new IndexCacheService();
            var build = new BuildService();
            var kb = new KbService(indexCache);
            kb.SetBuildService(build);
            build.SetKbService(kb);
            indexCache.SetBuildService(build);
            var obj = new ObjectService(kb, build);
            return new WriteService(obj);
        }
    }
}
