using System;
using System.Diagnostics;
using System.IO;

namespace GxMcp.Gateway
{
    // Item 54: sandbox clone — recursive filesystem copy of a KB directory.
    // Skips a small set of build/cache subdirs that are big, regeneratable,
    // and frequently locked by the live IDE / worker (bin/, obj/, .gx-cache/,
    // and the cached `.gx/index-snapshot.bin` which is per-KB-instance).
    // Returns aggregate file count / byte count / wall-clock duration so the
    // tool envelope can report a useful summary.
    internal static class SandboxCopyHelper
    {
        public sealed record CopyResult(int Files, long Bytes, long DurationMs);

        private static readonly string[] _skipDirs = new[]
        {
            "bin", "obj", ".gx-cache", ".gx\\generated", ".vs"
        };

        public static CopyResult CopyDirectory(string source, string target)
        {
            if (!Directory.Exists(source))
                throw new DirectoryNotFoundException($"Source KB directory not found: {source}");
            var sw = Stopwatch.StartNew();
            int files = 0;
            long bytes = 0;
            CopyRecursive(new DirectoryInfo(source), new DirectoryInfo(target), ref files, ref bytes, source);
            sw.Stop();
            return new CopyResult(files, bytes, sw.ElapsedMilliseconds);
        }

        private static void CopyRecursive(DirectoryInfo src, DirectoryInfo dst, ref int files, ref long bytes, string root)
        {
            // Skip blacklisted subdirs by relative path segment.
            string rel = src.FullName.Length > root.Length
                ? src.FullName.Substring(root.Length).TrimStart('\\', '/')
                : string.Empty;
            foreach (var skip in _skipDirs)
            {
                if (rel.Equals(skip, StringComparison.OrdinalIgnoreCase) ||
                    rel.StartsWith(skip + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    rel.StartsWith(skip + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            if (!dst.Exists) dst.Create();
            foreach (var f in src.GetFiles())
            {
                try
                {
                    string targetFile = Path.Combine(dst.FullName, f.Name);
                    f.CopyTo(targetFile, overwrite: true);
                    files++;
                    bytes += f.Length;
                }
                catch (IOException)
                {
                    // Best-effort: skip locked files (e.g. live IDE write-lock).
                }
            }
            foreach (var d in src.GetDirectories())
            {
                CopyRecursive(d, new DirectoryInfo(Path.Combine(dst.FullName, d.Name)), ref files, ref bytes, root);
            }
        }
    }
}
