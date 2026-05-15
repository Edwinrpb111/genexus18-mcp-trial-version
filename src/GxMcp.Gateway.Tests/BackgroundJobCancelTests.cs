using System.Threading;
using GxMcp.Gateway;
using Xunit;

namespace GxMcp.Gateway.Tests
{
    // v2.3.8 (Task 7.2) — friction report 2026-05-15 #16: lifecycle action=cancel
    // with a job_id had no effect on the async build pollers. Cancellation now
    // flows through JobRegistry.Cancel, which signals the registered CTS and
    // flips status to "cancelled" so the polling loop exits within ~one tick.
    public class BackgroundJobCancelTests
    {
        [Fact]
        public void Cancel_FlipsStatusToCancelled()
        {
            var reg = new BackgroundJobRegistry(600);
            var j = reg.Start("s1", "build", 60);
            Assert.True(reg.Cancel(j.Id, "user clicked stop"));
            var after = reg.Get(j.Id);
            Assert.NotNull(after);
            Assert.Equal("cancelled", after!.Status);
            Assert.Equal("user clicked stop", after.Summary);
            Assert.NotNull(after.CompletedAt);
        }

        [Fact]
        public void Cancel_UnknownJob_ReturnsFalse()
        {
            var reg = new BackgroundJobRegistry(600);
            Assert.False(reg.Cancel("nope"));
        }

        [Fact]
        public void RegisterCancellation_SignalsTokenOnCancel()
        {
            var reg = new BackgroundJobRegistry(600);
            var j = reg.Start("s1", "build", 60);
            var ct = reg.RegisterCancellation(j.Id);
            Assert.False(ct.IsCancellationRequested);
            reg.Cancel(j.Id);
            Assert.True(ct.IsCancellationRequested);
        }

        [Fact]
        public void Complete_AfterCancel_DoesNotOverwriteStatus()
        {
            // Race scenario: poller terminates and calls Complete after the cancel
            // already landed. Cancelled status must persist.
            var reg = new BackgroundJobRegistry(600);
            var j = reg.Start("s1", "build", 60);
            reg.Cancel(j.Id, "cancelled by client");
            reg.Complete(j.Id, success: true, summary: "Build succeeded");
            Assert.Equal("cancelled", reg.Get(j.Id)!.Status);
        }

        [Fact]
        public void RegisterCancellation_IsIdempotent()
        {
            var reg = new BackgroundJobRegistry(600);
            var j = reg.Start("s1", "build", 60);
            var ct1 = reg.RegisterCancellation(j.Id);
            var ct2 = reg.RegisterCancellation(j.Id);
            // Same CTS → calling Cancel once trips both tokens.
            reg.Cancel(j.Id);
            Assert.True(ct1.IsCancellationRequested);
            Assert.True(ct2.IsCancellationRequested);
        }
    }
}
