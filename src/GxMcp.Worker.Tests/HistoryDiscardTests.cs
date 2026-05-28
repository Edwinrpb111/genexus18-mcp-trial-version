using System;
using System.IO;
using GxMcp.Worker.Helpers;
using GxMcp.Worker.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GxMcp.Worker.Tests
{
    /// <summary>
    /// v2.6.6 Stream H (FR#28) — IDE "Discard changes" parity. Validates
    /// HistoryService.DiscardLatestEditSnapshotCore behaviour with a real
    /// EditSnapshotStore round-trip and a recording writer in place of
    /// WriteService.WriteObject.
    /// </summary>
    public class HistoryDiscardTests : IDisposable
    {
        private readonly string _kbPath;

        public HistoryDiscardTests()
        {
            _kbPath = Path.Combine(Path.GetTempPath(), "GxMcpHistoryDiscardTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_kbPath);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_kbPath)) Directory.Delete(_kbPath, true); } catch { }
        }

        [Fact]
        public void Discard_NoSnapshot_ReturnsHintEnvelope()
        {
            string result = HistoryService.DiscardLatestEditSnapshotCore(
                target: "MyPanel",
                partName: "Source",
                objectGuid: "11111111-1111-1111-1111-111111111111",
                kbPath: _kbPath,
                writer: (t, p, c) => "{\"status\":\"Success\"}");

            var json = JObject.Parse(result);
            Assert.Equal("ok", json["status"]?.ToString());
            Assert.Equal("NoSnapshot", json["code"]?.ToString());
            Assert.Equal("MyPanel", json["target"]?.ToString());
            Assert.Equal("Source", json["result"]?["part"]?.ToString());
            Assert.Contains("Edit this object first", json["result"]?["hint"]?.ToString() ?? "");
        }

        [Fact]
        public void Discard_NoSnapshot_DefaultsPartToSource()
        {
            string result = HistoryService.DiscardLatestEditSnapshotCore(
                target: "MyPanel",
                partName: null,
                objectGuid: "22222222-2222-2222-2222-222222222222",
                kbPath: _kbPath,
                writer: (t, p, c) => "{\"status\":\"Success\"}");

            var json = JObject.Parse(result);
            Assert.Equal("ok", json["status"]?.ToString());
            Assert.Equal("NoSnapshot", json["code"]?.ToString());
            Assert.Equal("Source", json["result"]?["part"]?.ToString());
        }

        [Fact]
        public void Discard_WithSnapshot_RestoresLatestThroughWriter()
        {
            // Seed two snapshots; expect the newer one to be restored.
            string root = EditSnapshotStore.ResolveRoot(_kbPath);
            Directory.CreateDirectory(root);

            string guid = "33333333-3333-3333-3333-333333333333";
            var older = EditSnapshotStore.SaveSnapshot(root, guid, "Source", "OLD CONTENT");
            Assert.NotNull(older);
            // Force a timestamp gap so the second filename sorts newer.
            System.Threading.Thread.Sleep(20);
            var newer = EditSnapshotStore.SaveSnapshot(root, guid, "Source", "NEW CONTENT");
            Assert.NotNull(newer);

            string writtenContent = null;
            string writtenTarget = null;
            string writtenPart = null;
            string result = HistoryService.DiscardLatestEditSnapshotCore(
                target: "MyPanel",
                partName: "Source",
                objectGuid: guid,
                kbPath: _kbPath,
                writer: (t, p, c) =>
                {
                    writtenTarget = t; writtenPart = p; writtenContent = c;
                    return "{\"status\":\"ok\",\"code\":\"WriteApplied\"}";
                });

            Assert.Equal("MyPanel", writtenTarget);
            Assert.Equal("Source", writtenPart);
            Assert.Equal("NEW CONTENT", writtenContent);

            var json = JObject.Parse(result);
            Assert.Equal("ok", json["status"]?.ToString());
            Assert.True(json["discarded"]?.ToObject<bool>() ?? false);
            Assert.NotNull(json["restoredFrom"]);
            Assert.NotNull(json["restoredSnapshot"]);
        }

        [Fact]
        public void Discard_NonJsonWriterResult_ReturnsRawWriteResult()
        {
            string root = EditSnapshotStore.ResolveRoot(_kbPath);
            Directory.CreateDirectory(root);
            string guid = "44444444-4444-4444-4444-444444444444";
            EditSnapshotStore.SaveSnapshot(root, guid, "Source", "snapshot bytes");

            string result = HistoryService.DiscardLatestEditSnapshotCore(
                target: "MyPanel",
                partName: "Source",
                objectGuid: guid,
                kbPath: _kbPath,
                writer: (t, p, c) => "not-json-output");

            Assert.Equal("not-json-output", result);
        }
    }
}
