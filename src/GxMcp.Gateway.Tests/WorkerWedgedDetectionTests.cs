using System;
using Xunit;

namespace GxMcp.Gateway.Tests
{
    // BUG-03: a worker wedged mid-command (alive, never responds) used to sit forever —
    // ShouldStopForIdle refuses to reap while _inFlightCommands > 0, and the gateway op
    // timeout only marks the operation timed out without touching the worker. These tests
    // cover the extracted decision logic (HasWedgedCommand) and the bookkeeping cleanup
    // (CompleteInFlight removing the timestamp on normal completion), independent of the
    // timer-driven health-check loop itself.
    public class WorkerWedgedDetectionTests
    {
        private static Configuration CfgWithCeiling(int minutes) =>
            new Configuration { Server = new ServerConfig { WedgedCommandTimeoutMinutes = minutes } };

        [Fact]
        public void HasWedgedCommand_False_WhenNoCommandsInFlight()
        {
            var worker = new WorkerProcess(CfgWithCeiling(15), new KbHandle("test", "C:\\fake"));

            Assert.False(worker.HasWedgedCommand(out var age));
            Assert.Equal(TimeSpan.Zero, age);
        }

        [Fact]
        public void HasWedgedCommand_False_UnderCeiling()
        {
            var worker = new WorkerProcess(CfgWithCeiling(15), new KbHandle("test", "C:\\fake"));
            // A long-running but legitimate build: 5 minutes in, ceiling is 15.
            worker.SeedInFlightForTest("op-1", DateTime.UtcNow.AddMinutes(-5));

            Assert.False(worker.HasWedgedCommand(out var age));
            Assert.True(age.TotalMinutes < 15);
        }

        [Fact]
        public void HasWedgedCommand_True_PastCeiling()
        {
            var worker = new WorkerProcess(CfgWithCeiling(15), new KbHandle("test", "C:\\fake"));
            // Genuinely wedged: unanswered for 20 minutes against a 15-minute ceiling.
            worker.SeedInFlightForTest("op-1", DateTime.UtcNow.AddMinutes(-20));

            Assert.True(worker.HasWedgedCommand(out var age));
            Assert.True(age.TotalMinutes >= 15);
        }

        [Fact]
        public void HasWedgedCommand_UsesOldestEntry_WhenMultipleInFlight()
        {
            var worker = new WorkerProcess(CfgWithCeiling(15), new KbHandle("test", "C:\\fake"));
            worker.SeedInFlightForTest("recent", DateTime.UtcNow.AddMinutes(-1));
            worker.SeedInFlightForTest("stale", DateTime.UtcNow.AddMinutes(-20));

            Assert.True(worker.HasWedgedCommand(out var age));
            Assert.True(age.TotalMinutes >= 15);
        }

        [Fact]
        public void CompleteInFlight_RemovesTimestamp_OnNormalCompletion()
        {
            var worker = new WorkerProcess(CfgWithCeiling(15), new KbHandle("test", "C:\\fake"));
            worker.SeedInFlightForTest("op-1", DateTime.UtcNow.AddMinutes(-20));
            Assert.Equal(1, worker.InFlightStartTimesCountForTest);

            worker.CompleteInFlightForTest("op-1");

            Assert.Equal(0, worker.InFlightStartTimesCountForTest);
            Assert.False(worker.HasWedgedCommand(out _));
        }

        [Fact]
        public void WedgedCommandTimeoutMinutes_DefaultsTo15_WhenUnset()
        {
            // Config with no Server section at all — constructor must clamp/default
            // rather than throw, mirroring the existing WorkerIdleTimeoutMinutes pattern.
            var worker = new WorkerProcess(new Configuration(), new KbHandle("test", "C:\\fake"));
            worker.SeedInFlightForTest("op-1", DateTime.UtcNow.AddMinutes(-14));

            Assert.False(worker.HasWedgedCommand(out _));
        }
    }
}
