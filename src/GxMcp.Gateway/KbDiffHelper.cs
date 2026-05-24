using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace GxMcp.Gateway
{
    // Item 55: KB diff at the OBJECT-INDEX level. Walks each KB's filesystem
    // Objects/<Type>/<Name>/ tree (the canonical on-disk layout written by
    // the GeneXus IDE / SDK) and emits {onlyInA, onlyInB, modified[]}. Stays
    // out of the SDK so the diff is cheap and doesn't require either KB to be
    // open in a worker. Modification detection uses the max LastWriteTime
    // across the object's part files; equal mtimes within ±1s are treated
    // as identical (NTFS resolution noise).
    internal static class KbDiffHelper
    {
        public static JObject Diff(string pathA, string pathB)
        {
            var objsA = EnumerateObjects(pathA);
            var objsB = EnumerateObjects(pathB);

            var keysA = new HashSet<string>(objsA.Keys, StringComparer.OrdinalIgnoreCase);
            var keysB = new HashSet<string>(objsB.Keys, StringComparer.OrdinalIgnoreCase);

            var onlyInA = keysA.Except(keysB, StringComparer.OrdinalIgnoreCase).ToList();
            var onlyInB = keysB.Except(keysA, StringComparer.OrdinalIgnoreCase).ToList();
            var modified = new List<JObject>();

            foreach (var key in keysA.Intersect(keysB, StringComparer.OrdinalIgnoreCase))
            {
                var a = objsA[key];
                var b = objsB[key];
                if (Math.Abs((a.LastWriteUtc - b.LastWriteUtc).TotalSeconds) > 1.0 || a.PartCount != b.PartCount)
                {
                    modified.Add(new JObject
                    {
                        ["name"] = a.Name,
                        ["type"] = a.Type,
                        ["lastUpdateA"] = a.LastWriteUtc.ToString("o"),
                        ["lastUpdateB"] = b.LastWriteUtc.ToString("o"),
                        ["partsA"] = a.PartCount,
                        ["partsB"] = b.PartCount
                    });
                }
            }

            return new JObject
            {
                ["status"] = "Success",
                ["kbA"] = pathA,
                ["kbB"] = pathB,
                ["countA"] = keysA.Count,
                ["countB"] = keysB.Count,
                ["onlyInA"] = JArray.FromObject(onlyInA.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)),
                ["onlyInB"] = JArray.FromObject(onlyInB.OrderBy(s => s, StringComparer.OrdinalIgnoreCase)),
                ["modified"] = new JArray(modified.OrderBy(o => o["name"]?.ToString(), StringComparer.OrdinalIgnoreCase))
            };
        }

        internal sealed record ObjEntry(string Name, string Type, DateTime LastWriteUtc, int PartCount);

        // Walks <kbRoot>/Objects/<Type>/<Name>/ — the on-disk layout used by the
        // GeneXus IDE. Tolerant of missing Objects/ (returns empty so disjoint
        // KBs still diff cleanly).
        internal static Dictionary<string, ObjEntry> EnumerateObjects(string kbPath)
        {
            var dict = new Dictionary<string, ObjEntry>(StringComparer.OrdinalIgnoreCase);
            string objectsDir = Path.Combine(kbPath, "Objects");
            if (!Directory.Exists(objectsDir)) return dict;
            foreach (var typeDir in new DirectoryInfo(objectsDir).GetDirectories())
            {
                string type = typeDir.Name;
                foreach (var objDir in typeDir.GetDirectories())
                {
                    string name = objDir.Name;
                    var parts = SafeGetFiles(objDir);
                    if (parts.Length == 0) continue;
                    DateTime maxMtime = DateTime.MinValue;
                    foreach (var p in parts)
                    {
                        var t = p.LastWriteTimeUtc;
                        if (t > maxMtime) maxMtime = t;
                    }
                    dict[$"{type}:{name}"] = new ObjEntry(name, type, maxMtime, parts.Length);
                }
            }
            return dict;
        }

        private static FileInfo[] SafeGetFiles(DirectoryInfo d)
        {
            try { return d.GetFiles("*", SearchOption.TopDirectoryOnly); }
            catch { return Array.Empty<FileInfo>(); }
        }
    }
}
