using System;
using System.Net;
using System.Threading;
using GxMcp.Worker.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GxMcp.Worker.Tests
{
    /// <summary>
    /// Item 72 (friction 2026-05-22) — Slack/Discord webhook on build failure.
    /// Covers the payload shape (pure function) and the end-to-end POST against
    /// a localhost HttpListener so the actual networking code is exercised.
    /// </summary>
    public class BuildNotifyOnFailureTests
    {
        [Fact]
        public void BuildNotificationPayload_HasRequiredFields()
        {
            var status = new BuildService.BuildTaskStatus
            {
                TaskId = "abc12345",
                Target = "MyProc",
                Status = "Failed",
                ElapsedSeconds = 12.5,
            };
            status.Errors.Add("spc0022: missing var");
            status.Errors.Add("CS0246: unknown type");

            string json = BuildService.BuildNotificationPayload(status);
            var jo = JObject.Parse(json);
            Assert.Equal("MyProc", jo["target"]?.ToString());
            Assert.Equal("abc12345", jo["jobId"]?.ToString());
            Assert.Equal(12.5, jo["durationSec"]?.ToObject<double>());
            var errors = jo["errors"] as JArray;
            Assert.NotNull(errors);
            Assert.Equal(2, errors!.Count);
            Assert.NotNull(jo["errorsDetailedHead"]);
            Assert.NotNull(jo["kb"]);
        }

        [Fact]
        public void MaybeNotifyOnFailure_SkipsWhenStatusIsSuccess()
        {
            // Should NOT POST anything; we simulate this by setting an
            // unreachable URL — if MaybeNotifyOnFailure tried to POST, the
            // 5s timeout would surface. We bound the test to 1s.
            var status = new BuildService.BuildTaskStatus
            {
                TaskId = "x", Status = "Succeeded",
                NotifyOnFailureUrl = "http://127.0.0.1:1/never"
            };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            BuildService.MaybeNotifyOnFailure(status);
            sw.Stop();
            Assert.True(sw.ElapsedMilliseconds < 1000, $"MaybeNotifyOnFailure should skip Success path quickly; took {sw.ElapsedMilliseconds}ms");
        }

        [Fact]
        public void MaybeNotifyOnFailure_SkipsPartialSuccess()
        {
            var status = new BuildService.BuildTaskStatus
            {
                TaskId = "x", Status = "Failed", PartialSuccess = true,
                NotifyOnFailureUrl = "http://127.0.0.1:1/never"
            };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            BuildService.MaybeNotifyOnFailure(status);
            sw.Stop();
            Assert.True(sw.ElapsedMilliseconds < 1000, "PartialSuccess should not trigger the webhook.");
        }

        [Fact]
        public void MaybeNotifyOnFailure_PostsToWebhookOnFailed()
        {
            // Spin up a localhost listener, capture the body, assert the payload shape.
            // Pick an ephemeral port by trying a few.
            HttpListener listener = null;
            string url = null;
            for (int port = 18900; port < 19000; port++)
            {
                try
                {
                    var l = new HttpListener();
                    string u = $"http://127.0.0.1:{port}/webhook/";
                    l.Prefixes.Add(u);
                    l.Start();
                    listener = l;
                    url = u;
                    break;
                }
                catch { /* port taken; try next */ }
            }
            if (listener == null)
            {
                // CI environments may forbid HttpListener; treat as skipped.
                return;
            }

            try
            {
                string capturedBody = null;
                using (var done = new ManualResetEventSlim(false))
                {
                    listener.BeginGetContext(ar =>
                    {
                        try
                        {
                            var ctx = listener.EndGetContext(ar);
                            using (var sr = new System.IO.StreamReader(ctx.Request.InputStream, System.Text.Encoding.UTF8))
                                capturedBody = sr.ReadToEnd();
                            ctx.Response.StatusCode = 200;
                            ctx.Response.Close();
                        }
                        catch { }
                        finally { done.Set(); }
                    }, null);

                    var status = new BuildService.BuildTaskStatus
                    {
                        TaskId = "job123",
                        Target = "ProcX",
                        Status = "Failed",
                        ElapsedSeconds = 8.0,
                        NotifyOnFailureUrl = url
                    };
                    status.Errors.Add("spc0022: oh no");

                    BuildService.MaybeNotifyOnFailure(status);
                    Assert.True(done.Wait(TimeSpan.FromSeconds(5)), "Webhook listener never received a request.");
                }

                Assert.False(string.IsNullOrEmpty(capturedBody));
                var jo = JObject.Parse(capturedBody);
                Assert.Equal("ProcX", jo["target"]?.ToString());
                Assert.Equal("job123", jo["jobId"]?.ToString());
                Assert.Contains("spc0022", ((JArray)jo["errors"])![0]!.ToString());
            }
            finally
            {
                try { listener.Stop(); } catch { }
                try { listener.Close(); } catch { }
            }
        }
    }
}
