using System.Threading;
using Xunit;

namespace GxMcp.Gateway.Tests
{
    public class WorkerProcessLatencyTests
    {
        [Fact]
        public void SpawnMs_DefaultsToNull_BeforeStart()
        {
            var kb = new KbHandle("test", "C:\\fake\\path");
            var config = new Configuration();
            var worker = new WorkerProcess(config, kb);

            Assert.Null(worker.SpawnMs);
            Assert.Null(worker.SdkInitMs);
        }
    }

    // Regression: StopProcess disposes the OS Process right after Kill, which suppresses
    // the async Process.Exited event that fires OnWorkerExited. On an idle-shutdown that
    // left the pool entry undropped, so the next AcquireAsync handed back the dead worker
    // and the command failed with WorkerCrashed. StopProcess must now signal the exit
    // deterministically — and exactly once, so the async Exited event can't double-fire it.
    public class WorkerProcessExitNotificationTests
    {
        private static WorkerProcess NewWorker() =>
            new WorkerProcess(new Configuration(), new KbHandle("test", "C:\\fake\\path"));

        [Fact]
        public void StopWithReason_FiresOnWorkerExited_Once_WithReason()
        {
            var worker = NewWorker();
            int calls = 0;
            WorkerStopReason observed = WorkerStopReason.None;
            worker.OnWorkerExited += r => { Interlocked.Increment(ref calls); observed = r; };

            worker.StopWithReason(WorkerStopReason.IdleTimeout);

            Assert.Equal(1, calls);
            Assert.Equal(WorkerStopReason.IdleTimeout, observed);
        }

        [Fact]
        public void StopWithReason_CalledTwice_FiresOnWorkerExited_OnlyOnce()
        {
            var worker = NewWorker();
            int calls = 0;
            worker.OnWorkerExited += _ => Interlocked.Increment(ref calls);

            worker.StopWithReason(WorkerStopReason.IdleTimeout);
            worker.StopWithReason(WorkerStopReason.GatewayShutdown);

            Assert.Equal(1, calls);
        }
    }
}
