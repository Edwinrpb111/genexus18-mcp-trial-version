using GxMcp.Worker.Services;
using Xunit;

namespace GxMcp.Worker.Tests
{
    // BUG-06 regression: AcquirePerTargetLock serializes concurrent writes to the
    // same target. A blank/whitespace target previously returned a fresh unshared
    // lock per call, silently disabling that serialization. Blank callers must now
    // share one sentinel lock.
    public class WriteServicePerTargetLockTests
    {
        [Fact]
        public void SameTarget_ReturnsSameLockInstance()
        {
            var a = WriteService.AcquirePerTargetLock("MyPanel");
            var b = WriteService.AcquirePerTargetLock("mypanel"); // OrdinalIgnoreCase
            Assert.Same(a, b);
        }

        [Fact]
        public void BlankTargets_ShareOneSentinelLock()
        {
            var a = WriteService.AcquirePerTargetLock("");
            var b = WriteService.AcquirePerTargetLock("   ");
            var c = WriteService.AcquirePerTargetLock(null);
            Assert.Same(a, b);
            Assert.Same(b, c);
        }

        [Fact]
        public void Blank_And_NonBlank_DoNotShareLock()
        {
            var blank = WriteService.AcquirePerTargetLock("");
            var named = WriteService.AcquirePerTargetLock("SomeObject");
            Assert.NotSame(blank, named);
        }
    }
}
