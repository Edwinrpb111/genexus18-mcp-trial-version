using System;
using System.IO;
using GxMcp.Worker.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GxMcp.Worker.Tests
{
    // issue #38 defect #1: OpenKB fails fast with a clear KbInvalidPath envelope for a
    // path that is structurally not a KB root, before paying the heavy SDK open. This is
    // the defense-in-depth mirror of the gateway pre-check and keeps the auto-open
    // give-up counter converging quickly.
    public class KbOpenValidationTests
    {
        private static KbService NewKb() => new KbService(new IndexCacheService());

        private static string NewTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "gxmcp-kbopen-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        [Fact]
        public void OpenKB_EnvironmentSubfolder_ReturnsKbInvalidPath()
        {
            string kbDir = NewTempDir();
            try
            {
                File.WriteAllText(Path.Combine(kbDir, "Demo.gxw"), "");
                string envDir = Path.Combine(kbDir, "Desenv");
                Directory.CreateDirectory(envDir);

                string raw = NewKb().OpenKB(envDir);
                var env = JObject.Parse(raw);

                Assert.Equal("error", env["status"]?.ToString());
                Assert.Equal("KbInvalidPath", env["error"]?["code"]?.ToString() ?? env["code"]?.ToString());
            }
            finally { Directory.Delete(kbDir, true); }
        }

        [Fact]
        public void OpenKB_MissingPath_ReturnsKbInvalidPath()
        {
            string missing = Path.Combine(Path.GetTempPath(), "gxmcp-missing-" + Guid.NewGuid().ToString("N"));
            var env = JObject.Parse(NewKb().OpenKB(missing));
            Assert.Equal("error", env["status"]?.ToString());
            Assert.Equal("KbInvalidPath", env["error"]?["code"]?.ToString() ?? env["code"]?.ToString());
        }
    }
}
