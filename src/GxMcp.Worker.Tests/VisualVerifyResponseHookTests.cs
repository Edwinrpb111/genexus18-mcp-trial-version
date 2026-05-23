using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using GxMcp.Worker.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GxMcp.Worker.Tests
{
    /// <summary>
    /// Wave-3 items 5 + 37: verifies the dispatcher hook that bolts a
    /// <c>visualVerify</c> envelope onto edit responses when callers opt in.
    /// The hook is a pure JSON-string transformer; we don't need a full
    /// dispatcher to exercise it.
    /// </summary>
    public class VisualVerifyResponseHookTests
    {
        private class FakeRunner : VisualVerifyService.ICliRunner
        {
            public string WhichResult = "C:/fake/chrome-devtools-axi.cmd";
            public VisualVerifyService.CliResult Run(string fileName, string arguments, int timeoutMs)
            {
                if (arguments != null && arguments.StartsWith("screenshot "))
                {
                    string outPath = arguments.Substring("screenshot ".Length).Trim('"');
                    try { Directory.CreateDirectory(Path.GetDirectoryName(outPath) ?? "."); } catch { }
                    using (var bmp = new Bitmap(4, 4, PixelFormat.Format32bppArgb))
                    {
                        for (int y = 0; y < 4; y++)
                            for (int x = 0; x < 4; x++)
                                bmp.SetPixel(x, y, Color.Blue);
                        bmp.Save(outPath, ImageFormat.Png);
                    }
                }
                return new VisualVerifyService.CliResult { ExitCode = 0 };
            }
            public string Which(string command) => WhichResult;
        }

        private static string FreshKb()
        {
            string p = Path.Combine(Path.GetTempPath(), "VVHook_" + System.Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(p);
            return p;
        }

        private static VisualVerifyService BuildSvc(FakeRunner runner, string kbDir)
        {
            return new VisualVerifyService(
                runner,
                () => "MyPanel",
                () => kbDir,
                name => (name ?? "obj").ToLowerInvariant() + ".aspx",
                "http://localhost/fake");
        }

        [Fact]
        public void OffByDefault_ResponseUnchanged_NoVisualVerifyField()
        {
            // Contract: omitting visualVerify (or setting it to false) MUST
            // not touch the response — no extra latency, no spurious envelope.
            var svc = BuildSvc(new FakeRunner(), FreshKb());
            string originalResponse = "{\"status\":\"Success\",\"target\":\"MyPanel\"}";
            var args = new JObject { ["name"] = "MyPanel", ["part"] = "WebForm" };

            string outResp = VisualVerifyResponseHook.MaybeAttach(args, originalResponse, svc);

            Assert.Equal(originalResponse, outResp);
            var parsed = JObject.Parse(outResp);
            Assert.Null(parsed["visualVerify"]);
        }

        [Fact]
        public void On_WithDriverAvailable_AttachesPathAndBase64Truncated()
        {
            // Contract: visualVerify=true + a working driver mock yields a
            // visualVerify envelope with the screenshot path and a non-empty
            // base64Truncated, matching the schema documented for the field.
            string kb = FreshKb();
            var svc = BuildSvc(new FakeRunner(), kb);
            string originalResponse = "{\"status\":\"Success\",\"target\":\"MyPanel\"}";
            var args = new JObject
            {
                ["name"] = "MyPanel",
                ["part"] = "WebForm",
                ["visualVerify"] = true
            };

            string outResp = VisualVerifyResponseHook.MaybeAttach(args, originalResponse, svc);

            var parsed = JObject.Parse(outResp);
            Assert.Equal("Success", parsed["status"]?.ToString());
            var vv = parsed["visualVerify"] as JObject;
            Assert.NotNull(vv);
            Assert.NotNull(vv!["path"]);
            Assert.False(string.IsNullOrEmpty(vv["path"]?.ToString()));
            Assert.NotNull(vv["base64Truncated"]);
            Assert.False(string.IsNullOrEmpty(vv["base64Truncated"]?.ToString()));
            Assert.Null(vv["skipped"]);
        }

        [Fact]
        public void On_WithDriverUnavailable_AttachesSkippedWithReason()
        {
            // Contract: when no browser CLI is on PATH the hook MUST NOT
            // swallow the edit response — it attaches a structured skipped
            // envelope so the LLM can degrade gracefully.
            var runner = new FakeRunner { WhichResult = null };
            var svc = BuildSvc(runner, FreshKb());
            string originalResponse = "{\"status\":\"Success\",\"target\":\"MyPanel\"}";
            var args = new JObject
            {
                ["name"] = "MyPanel",
                ["part"] = "WebForm",
                ["visualVerify"] = true
            };

            string outResp = VisualVerifyResponseHook.MaybeAttach(args, originalResponse, svc);

            var parsed = JObject.Parse(outResp);
            Assert.Equal("Success", parsed["status"]?.ToString());
            var vv = parsed["visualVerify"] as JObject;
            Assert.NotNull(vv);
            Assert.True(vv!["skipped"]?.ToObject<bool>() ?? false);
            Assert.Equal("BrowserDriverUnavailable", vv["reason"]?.ToString());
        }

        [Fact]
        public void NonJsonResponse_ReturnedVerbatim_NoThrow()
        {
            // Defensive: if the upstream service emits non-JSON (it shouldn't,
            // but the hook is on a hot path), the hook degrades to a no-op.
            var svc = BuildSvc(new FakeRunner(), FreshKb());
            var args = new JObject { ["visualVerify"] = true, ["name"] = "MyPanel" };

            string outResp = VisualVerifyResponseHook.MaybeAttach(args, "not-json", svc);

            Assert.Equal("not-json", outResp);
        }
    }
}
