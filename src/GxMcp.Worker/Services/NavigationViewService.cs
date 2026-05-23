using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace GxMcp.Worker.Services
{
    /// <summary>
    /// Wave-3 item — "View Navigation / View Last Navigation" parity.
    ///
    /// Wraps <see cref="NavigationService.GetNavigation"/>. When the caller
    /// passes <c>latest=true</c>, returns the most recent cached navigation
    /// report under <c>&lt;kbPath&gt;/.gx/navigation-cache/&lt;objectName&gt;/&lt;UTC&gt;.txt</c>
    /// if one exists; otherwise runs a fresh navigation and persists the
    /// result so subsequent <c>latest=true</c> calls are O(disk read).
    /// </summary>
    public class NavigationViewService
    {
        private readonly NavigationService _navigation;
        private readonly KbService _kbService;

        public NavigationViewService(NavigationService navigation, KbService kbService)
        {
            _navigation = navigation;
            _kbService = kbService;
        }

        public string View(string name, bool latest)
        {
            if (string.IsNullOrWhiteSpace(name))
                return new JObject { ["error"] = "Missing 'name'." }.ToString(Newtonsoft.Json.Formatting.None);

            string kbPath = null;
            try { kbPath = _kbService?.GetKbPath(); } catch { }
            string cacheDir = ResolveCacheDir(kbPath, name);

            if (latest && cacheDir != null && Directory.Exists(cacheDir))
            {
                try
                {
                    var newest = Directory.GetFiles(cacheDir, "*.txt", SearchOption.TopDirectoryOnly)
                        .OrderByDescending(f => f)
                        .FirstOrDefault();
                    if (!string.IsNullOrEmpty(newest))
                    {
                        string cached = File.ReadAllText(newest);
                        return new JObject
                        {
                            ["name"] = name,
                            ["fromCache"] = true,
                            ["cachePath"] = newest,
                            ["cachedAt"] = File.GetLastWriteTimeUtc(newest).ToString("o"),
                            ["navigation"] = TryParseEmbed(cached)
                        }.ToString(Newtonsoft.Json.Formatting.None);
                    }
                }
                catch { /* fall through to live navigation */ }
            }

            string raw = _navigation?.GetNavigation(name);
            if (string.IsNullOrEmpty(raw))
            {
                return new JObject { ["error"] = "Navigation returned no payload.", ["code"] = "NoNavigation" }
                    .ToString(Newtonsoft.Json.Formatting.None);
            }

            string savedPath = null;
            try
            {
                if (cacheDir != null)
                {
                    Directory.CreateDirectory(cacheDir);
                    string stamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
                    savedPath = Path.Combine(cacheDir, stamp + ".txt");
                    File.WriteAllText(savedPath, raw);
                }
            }
            catch { savedPath = null; }

            return new JObject
            {
                ["name"] = name,
                ["fromCache"] = false,
                ["cachePath"] = savedPath ?? string.Empty,
                ["navigation"] = TryParseEmbed(raw)
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string ResolveCacheDir(string kbPath, string objectName)
        {
            if (string.IsNullOrWhiteSpace(kbPath) || string.IsNullOrWhiteSpace(objectName)) return null;
            try
            {
                string root = Directory.Exists(kbPath) ? kbPath : Path.GetDirectoryName(kbPath);
                if (string.IsNullOrEmpty(root)) return null;
                string sanitized = SanitizeName(objectName);
                return Path.Combine(root, ".gx", "navigation-cache", sanitized);
            }
            catch { return null; }
        }

        private static string SanitizeName(string n)
        {
            var bad = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(n.Length);
            foreach (var c in n) sb.Append(Array.IndexOf(bad, c) >= 0 ? '_' : c);
            return sb.ToString();
        }

        private static JToken TryParseEmbed(string raw)
        {
            try { return JToken.Parse(raw); }
            catch { return raw; }
        }
    }
}
