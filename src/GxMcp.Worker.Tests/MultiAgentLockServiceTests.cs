using System;
using System.IO;
using GxMcp.Worker.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GxMcp.Worker.Tests
{
    public class MultiAgentLockServiceTests
    {
        private static string NewKb()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "gxmcp_lock_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            return tmp;
        }

        [Fact]
        public void Acquire_FreshLock_Succeeds()
        {
            string kb = NewKb();
            try
            {
                var json = JObject.Parse(MultiAgentLockService.DispatchCore(kb, "acquire", "Invoice", "Events", "agent-A", 300));
                Assert.Equal("ok", (string)json["status"]);
                Assert.Equal("LockAcquired", (string)json["code"]);
                Assert.True((bool)json["result"]["held"]);
                Assert.Equal("agent-A", (string)json["result"]["holder"]["ownerId"]);
                Assert.True(File.Exists((string)json["result"]["path"]));
            }
            finally { try { Directory.Delete(kb, recursive: true); } catch { } }
        }

        [Fact]
        public void Acquire_WhenHeldByOther_ReturnsConflict()
        {
            string kb = NewKb();
            try
            {
                MultiAgentLockService.DispatchCore(kb, "acquire", "Invoice", "Events", "agent-A", 300);
                var json = JObject.Parse(MultiAgentLockService.DispatchCore(kb, "acquire", "Invoice", "Events", "agent-B", 300));
                Assert.Equal("error", (string)json["status"]);
                Assert.Equal("AlreadyHeld", (string)json["error"]["code"]);
                Assert.Equal("agent-A", (string)json["holder"]["ownerId"]);
            }
            finally { try { Directory.Delete(kb, recursive: true); } catch { } }
        }

        [Fact]
        public void Release_WrongOwner_RefusesAndKeepsLock()
        {
            string kb = NewKb();
            try
            {
                MultiAgentLockService.DispatchCore(kb, "acquire", "Invoice", "Events", "agent-A", 300);
                var json = JObject.Parse(MultiAgentLockService.DispatchCore(kb, "release", "Invoice", "Events", "agent-B", 300));
                Assert.Equal("error", (string)json["status"]);
                Assert.Equal("WrongOwner", (string)json["error"]["code"]);
                // Lock file still exists.
                var status = JObject.Parse(MultiAgentLockService.DispatchCore(kb, "status", "Invoice", "Events", null, 300));
                Assert.True((bool)status["result"]["held"]);
                Assert.Equal("agent-A", (string)status["result"]["holder"]["ownerId"]);
            }
            finally { try { Directory.Delete(kb, recursive: true); } catch { } }
        }

        [Fact]
        public void Acquire_AfterExpiry_TakesOver()
        {
            string kb = NewKb();
            try
            {
                // Manually plant an expired lock file (atUtc far in the past, ttl=1).
                string locksDir = Path.Combine(kb, ".gx", "locks");
                Directory.CreateDirectory(locksDir);
                string lockFile = Path.Combine(locksDir, "Invoice__Events.lock");
                var expired = new JObject
                {
                    ["ownerId"] = "agent-A",
                    ["atUtc"] = DateTime.UtcNow.AddHours(-1).ToString("o"),
                    ["ttlSec"] = 1,
                    ["target"] = "Invoice",
                    ["part"] = "Events"
                };
                File.WriteAllText(lockFile, expired.ToString(Newtonsoft.Json.Formatting.None));

                var json = JObject.Parse(MultiAgentLockService.DispatchCore(kb, "acquire", "Invoice", "Events", "agent-B", 300));
                Assert.Equal("ok", (string)json["status"]);
                Assert.Equal("LockAcquired", (string)json["code"]);
                Assert.True((bool)json["result"]["held"]);
                Assert.Equal("agent-B", (string)json["result"]["holder"]["ownerId"]);
                Assert.True((bool)json["result"]["takeover"]);
            }
            finally { try { Directory.Delete(kb, recursive: true); } catch { } }
        }

        [Fact]
        public void Release_Owner_RemovesLock()
        {
            string kb = NewKb();
            try
            {
                MultiAgentLockService.DispatchCore(kb, "acquire", "Invoice", "Events", "agent-A", 300);
                var rel = JObject.Parse(MultiAgentLockService.DispatchCore(kb, "release", "Invoice", "Events", "agent-A", 300));
                Assert.Equal("ok", (string)rel["status"]);
                Assert.Equal("LockReleased", (string)rel["code"]);
                Assert.False((bool)rel["result"]["held"]);
                var status = JObject.Parse(MultiAgentLockService.DispatchCore(kb, "status", "Invoice", "Events", null, 300));
                Assert.False((bool)status["result"]["held"]);
            }
            finally { try { Directory.Delete(kb, recursive: true); } catch { } }
        }
    }
}
