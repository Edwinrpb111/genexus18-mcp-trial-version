using GxMcp.Worker.Helpers;
using System.IO;
using Xunit;

namespace GxMcp.Worker.Tests
{
    public class ProgressEmitterTests
    {
        [Fact]
        public void Emit_WritesNotificationsProgressLine_ToStdout_WhenTokenIsSet()
        {
            var captured = new StringWriter();
            var originalOut = System.Console.Out;
            System.Console.SetOut(captured);

            try
            {
                using (ProgressContext.Use("op-abc"))
                {
                    ProgressEmitter.Emit(progress: 5, total: 10, message: "halfway");
                }
            }
            finally
            {
                System.Console.SetOut(originalOut);
            }

            string line = captured.ToString().Trim();
            Assert.Contains("\"method\":\"notifications/progress\"", line);
            Assert.Contains("\"progressToken\":\"op-abc\"", line);
            Assert.Contains("\"progress\":5", line);
            Assert.Contains("\"total\":10", line);
            Assert.Contains("halfway", line);
        }

        [Fact]
        public void Emit_IsNoOp_WhenNoTokenInContext()
        {
            var captured = new StringWriter();
            var originalOut = System.Console.Out;
            System.Console.SetOut(captured);

            try
            {
                ProgressEmitter.Emit(progress: 1, total: 2, message: "x");
            }
            finally
            {
                System.Console.SetOut(originalOut);
            }

            Assert.Equal(string.Empty, captured.ToString().Trim());
        }

        [Fact]
        public void Context_IsAsyncLocal_AndDisposesCleanly()
        {
            Assert.Null(ProgressContext.CurrentToken);
            using (ProgressContext.Use("op-1"))
            {
                Assert.Equal("op-1", ProgressContext.CurrentToken);
                using (ProgressContext.Use("op-2"))
                {
                    Assert.Equal("op-2", ProgressContext.CurrentToken);
                }
                Assert.Equal("op-1", ProgressContext.CurrentToken);
            }
            Assert.Null(ProgressContext.CurrentToken);
        }

        [Fact]
        public void Emit_FromMultiplePhases_ProducesMonotonicProgress()
        {
            var captured = new StringWriter();
            var originalOut = System.Console.Out;
            System.Console.SetOut(captured);

            try
            {
                using (GxMcp.Worker.Helpers.ProgressContext.Use("op-build"))
                {
                    GxMcp.Worker.Helpers.ProgressEmitter.Emit(5, 100, "Build phase: Starting");
                    GxMcp.Worker.Helpers.ProgressEmitter.Emit(50, 100, "Build phase: Compiling");
                    GxMcp.Worker.Helpers.ProgressEmitter.Emit(100, 100, "Build phase: Completed");
                }
            }
            finally
            {
                System.Console.SetOut(originalOut);
            }

            var lines = captured.ToString().Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(3, lines.Length);
            Assert.Contains("\"progress\":5", lines[0]);
            Assert.Contains("\"progress\":50", lines[1]);
            Assert.Contains("\"progress\":100", lines[2]);
        }
    }
}
