using System;
using System.IO;
using GxMcp.Worker.Models;
using Newtonsoft.Json.Linq;

namespace GxMcp.Worker.Services
{
    /// <summary>
    /// Item 89 — genexus_screenshot_publish. Local-only: copies a screenshot
    /// PNG to <c>&lt;kbPath&gt;/.gx/published-screenshots/&lt;UTC&gt;-&lt;basename&gt;</c>
    /// and returns the destination path. No remote upload.
    /// </summary>
    public class ScreenshotPublishService
    {
        private readonly KbService _kbService;

        public ScreenshotPublishService(KbService kbService)
        {
            _kbService = kbService;
        }

        public string Publish(string sourcePath, string kbPathOverride = null)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return Error("MissingPath", "sourcePath is required.");
            }

            string kbPath = kbPathOverride;
            if (string.IsNullOrEmpty(kbPath))
            {
                try { kbPath = _kbService?.GetKbPath(); } catch { }
            }
            if (string.IsNullOrEmpty(kbPath))
            {
                return Error("NoKbOpen", "No KB is currently open; pass kbPathOverride or open a KB first.");
            }
            if (!File.Exists(sourcePath))
            {
                return Error("SourceNotFound", "Screenshot file does not exist: " + sourcePath);
            }

            return PublishCore(sourcePath, kbPath);
        }

        // Exposed for tests so they don't need a live KbService.
        public static string PublishCore(string sourcePath, string kbPath)
        {
            try
            {
                string destDir = Path.Combine(kbPath, ".gx", "published-screenshots");
                Directory.CreateDirectory(destDir);

                string basename = Path.GetFileName(sourcePath);
                string stamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
                string destName = stamp + "-" + basename;
                string destPath = Path.Combine(destDir, destName);
                File.Copy(sourcePath, destPath, overwrite: false);

                long size = 0;
                try { size = new FileInfo(destPath).Length; } catch { }

                return McpResponse.Ok(
                    code: "ScreenshotPublished",
                    result: new JObject
                    {
                        ["sourcePath"] = sourcePath,
                        ["publishedPath"] = destPath,
                        ["basename"] = destName,
                        ["sizeBytes"] = size,
                        ["publishedAtUtc"] = DateTime.UtcNow.ToString("o")
                    });
            }
            catch (Exception ex)
            {
                return McpResponse.Err(
                    code: "PublishFailed",
                    message: ex.Message,
                    hint: "Check that the source file exists and the KB path is writable.",
                    nextSteps: new JArray {
                        McpResponse.NextStep("genexus_screenshot_publish", new JObject { ["sourcePath"] = sourcePath ?? "" }, "Retry after verifying the source path.")
                    });
            }
        }

        private static string Error(string code, string message) =>
            McpResponse.Err(
                code: code,
                message: message,
                nextSteps: new JArray {
                    McpResponse.NextStep("genexus_screenshot_publish", new JObject { ["sourcePath"] = "<path to PNG>" }, "Retry with a valid source path and an open KB.")
                });
    }
}
