using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace GxMcp.Worker.Services
{
    /// <summary>
    /// Item 84 — genexus_multi_agent_lock. File-based advisory lock per
    /// (kbPath, target, part) stored as JSON under
    /// <c>&lt;kbPath&gt;/.gx/locks/&lt;sanitized&gt;.lock</c>.
    /// Actions: acquire / release / status. Auto-expires entries whose
    /// <c>atUtc + ttlSec</c> is in the past (treated as released so a fresh
    /// owner can take over).
    /// </summary>
    public class MultiAgentLockService
    {
        private readonly KbService _kbService;

        public MultiAgentLockService(KbService kbService)
        {
            _kbService = kbService;
        }

        public string Dispatch(string action, string target, string part, string ownerId, int ttlSec, string kbPathOverride = null)
        {
            string kbPath = ResolveKbPath(kbPathOverride);
            if (string.IsNullOrEmpty(kbPath))
                return Error("NoKbOpen", "No KB is currently open.");
            return DispatchCore(kbPath, action, target, part, ownerId, ttlSec);
        }

        public static string DispatchCore(string kbPath, string action, string target, string part, string ownerId, int ttlSec)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(target))
                    return Error("MissingTarget", "target is required.");
                if (ttlSec <= 0) ttlSec = 300;
                if (ttlSec > 86400) ttlSec = 86400;

                string locksDir = Path.Combine(kbPath, ".gx", "locks");
                Directory.CreateDirectory(locksDir);
                string lockPath = Path.Combine(locksDir, Sanitize(target, part) + ".lock");

                action = (action ?? "status").ToLowerInvariant();

                switch (action)
                {
                    case "status":
                    {
                        var existing = TryReadLock(lockPath, out bool expired);
                        if (existing == null || expired)
                        {
                            return new JObject
                            {
                                ["status"] = "Success",
                                ["action"] = "status",
                                ["held"] = false,
                                ["holder"] = JValue.CreateNull(),
                                ["path"] = lockPath
                            }.ToString(Newtonsoft.Json.Formatting.None);
                        }
                        return new JObject
                        {
                            ["status"] = "Success",
                            ["action"] = "status",
                            ["held"] = true,
                            ["holder"] = existing,
                            ["path"] = lockPath
                        }.ToString(Newtonsoft.Json.Formatting.None);
                    }

                    case "acquire":
                    {
                        if (string.IsNullOrWhiteSpace(ownerId))
                            return Error("MissingOwnerId", "ownerId is required for acquire.");
                        var existing = TryReadLock(lockPath, out bool expired);
                        if (existing != null && !expired)
                        {
                            string existingOwner = existing["ownerId"]?.ToString();
                            if (!string.Equals(existingOwner, ownerId, StringComparison.Ordinal))
                            {
                                return new JObject
                                {
                                    ["status"] = "Conflict",
                                    ["code"] = "AlreadyHeld",
                                    ["action"] = "acquire",
                                    ["holder"] = existing
                                }.ToString(Newtonsoft.Json.Formatting.None);
                            }
                            // Same owner re-acquiring — refresh TTL.
                        }
                        var entry = new JObject
                        {
                            ["ownerId"] = ownerId,
                            ["atUtc"] = DateTime.UtcNow.ToString("o"),
                            ["ttlSec"] = ttlSec,
                            ["target"] = target,
                            ["part"] = part ?? string.Empty
                        };
                        File.WriteAllText(lockPath, entry.ToString(Newtonsoft.Json.Formatting.None));
                        return new JObject
                        {
                            ["status"] = "Success",
                            ["action"] = "acquire",
                            ["held"] = true,
                            ["holder"] = entry,
                            ["path"] = lockPath,
                            ["takeover"] = expired
                        }.ToString(Newtonsoft.Json.Formatting.None);
                    }

                    case "release":
                    {
                        if (!File.Exists(lockPath))
                        {
                            return new JObject
                            {
                                ["status"] = "Success",
                                ["action"] = "release",
                                ["held"] = false,
                                ["note"] = "no lock file"
                            }.ToString(Newtonsoft.Json.Formatting.None);
                        }
                        var existing = TryReadLock(lockPath, out bool expired);
                        if (existing != null && !expired)
                        {
                            string existingOwner = existing["ownerId"]?.ToString();
                            if (!string.Equals(existingOwner, ownerId, StringComparison.Ordinal))
                            {
                                return new JObject
                                {
                                    ["status"] = "Error",
                                    ["code"] = "WrongOwner",
                                    ["action"] = "release",
                                    ["holder"] = existing,
                                    ["message"] = "ownerId mismatch; refusing to release."
                                }.ToString(Newtonsoft.Json.Formatting.None);
                            }
                        }
                        File.Delete(lockPath);
                        return new JObject
                        {
                            ["status"] = "Success",
                            ["action"] = "release",
                            ["held"] = false,
                            ["takeover"] = expired
                        }.ToString(Newtonsoft.Json.Formatting.None);
                    }

                    default:
                        return Error("UnknownAction", "action must be acquire|release|status; got '" + action + "'.");
                }
            }
            catch (Exception ex)
            {
                return Error("LockOperationFailed", ex.Message);
            }
        }

        private static JObject TryReadLock(string path, out bool expired)
        {
            expired = false;
            if (!File.Exists(path)) return null;
            try
            {
                string raw = File.ReadAllText(path);
                var entry = JObject.Parse(raw);
                int ttl = entry["ttlSec"]?.ToObject<int?>() ?? 300;
                string atStr = entry["atUtc"]?.ToString();
                if (DateTime.TryParse(atStr, null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var at))
                {
                    if (at.AddSeconds(ttl) < DateTime.UtcNow) expired = true;
                }
                return entry;
            }
            catch
            {
                // Corrupted lock file — treat as expired so a fresh acquire can replace it.
                expired = true;
                return null;
            }
        }

        private static string Sanitize(string target, string part)
        {
            string combined = (target ?? "_") + "__" + (part ?? "_");
            var sb = new System.Text.StringBuilder(combined.Length);
            foreach (char c in combined)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.')
                    sb.Append(c);
                else
                    sb.Append('_');
            }
            return sb.ToString();
        }

        private string ResolveKbPath(string kbPathOverride)
        {
            if (!string.IsNullOrEmpty(kbPathOverride)) return kbPathOverride;
            try { return _kbService?.GetKbPath(); } catch { return null; }
        }

        private static string Error(string code, string message) =>
            new JObject
            {
                ["status"] = "Error",
                ["code"] = code,
                ["message"] = message
            }.ToString(Newtonsoft.Json.Formatting.None);
    }
}
