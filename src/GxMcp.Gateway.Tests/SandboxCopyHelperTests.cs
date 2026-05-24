using System;
using System.IO;
using Xunit;

namespace GxMcp.Gateway.Tests
{
    // Item 54: SandboxCopyHelper.CopyDirectory — recursive file copy with
    // build/cache-dir filter. Tests cover (a) baseline copy, (b) skipped
    // subdirs, (c) missing-source error.
    public class SandboxCopyHelperTests : IDisposable
    {
        private readonly string _root;

        public SandboxCopyHelperTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "gxmcp-sandbox-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        [Fact]
        public void Copies_All_Files_And_Subdirs()
        {
            string src = Path.Combine(_root, "src");
            string dst = Path.Combine(_root, "dst");
            Directory.CreateDirectory(Path.Combine(src, "Objects", "WebPanel", "Home"));
            File.WriteAllText(Path.Combine(src, "kb.gxw"), "hello");
            File.WriteAllText(Path.Combine(src, "Objects", "WebPanel", "Home", "Layout.xml"), "<root/>");

            var result = SandboxCopyHelper.CopyDirectory(src, dst);

            Assert.True(File.Exists(Path.Combine(dst, "kb.gxw")));
            Assert.True(File.Exists(Path.Combine(dst, "Objects", "WebPanel", "Home", "Layout.xml")));
            Assert.True(result.Files >= 2);
            Assert.True(result.Bytes > 0);
        }

        [Fact]
        public void Skips_Build_And_Cache_Dirs()
        {
            string src = Path.Combine(_root, "src");
            string dst = Path.Combine(_root, "dst");
            Directory.CreateDirectory(Path.Combine(src, "bin", "Debug"));
            Directory.CreateDirectory(Path.Combine(src, "obj"));
            Directory.CreateDirectory(Path.Combine(src, ".gx-cache"));
            Directory.CreateDirectory(Path.Combine(src, "Objects"));
            File.WriteAllText(Path.Combine(src, "bin", "Debug", "skip.dll"), "x");
            File.WriteAllText(Path.Combine(src, "obj", "skip.obj"), "x");
            File.WriteAllText(Path.Combine(src, ".gx-cache", "skip.bin"), "x");
            File.WriteAllText(Path.Combine(src, "Objects", "keep.txt"), "x");

            SandboxCopyHelper.CopyDirectory(src, dst);

            Assert.False(Directory.Exists(Path.Combine(dst, "bin")));
            Assert.False(Directory.Exists(Path.Combine(dst, "obj")));
            Assert.False(Directory.Exists(Path.Combine(dst, ".gx-cache")));
            Assert.True(File.Exists(Path.Combine(dst, "Objects", "keep.txt")));
        }

        [Fact]
        public void Missing_Source_Throws()
        {
            string src = Path.Combine(_root, "does-not-exist");
            string dst = Path.Combine(_root, "dst");
            Assert.Throws<DirectoryNotFoundException>(() => SandboxCopyHelper.CopyDirectory(src, dst));
        }
    }
}
