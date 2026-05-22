using System;
using GxMcp.Worker.Services;
using Xunit;

namespace GxMcp.Worker.Tests
{
    // Friction 2026-05-22: "Visual write failed" used to surface only ex.Message,
    // which was often a generic wrapper. FormatExceptionChain walks
    // InnerException so the real SDK diagnostic makes it to the response.
    public class ExceptionChainFormatterTests
    {
        [Fact]
        public void FormatExceptionChain_NullException_ReturnsNull()
        {
            Assert.Null(WriteService.FormatExceptionChain(null));
        }

        [Fact]
        public void FormatExceptionChain_NoInner_ReturnsTypeAndMessage()
        {
            var ex = new InvalidOperationException("Top message");
            var result = WriteService.FormatExceptionChain(ex);
            Assert.Contains("InvalidOperationException", result);
            Assert.Contains("Top message", result);
        }

        [Fact]
        public void FormatExceptionChain_NestedInner_IncludesAllUniqueMessages()
        {
            var inner = new ArgumentException("Variable not declared");
            var middle = new InvalidOperationException("Save rejected", inner);
            var outer = new Exception("Visual save failed", middle);

            var result = WriteService.FormatExceptionChain(outer);

            Assert.Contains("Visual save failed", result);
            Assert.Contains("Save rejected", result);
            Assert.Contains("Variable not declared", result);
            Assert.Contains(" -> ", result);
        }

        [Fact]
        public void FormatExceptionChain_RepeatedMessages_DedupesAfterFirstOccurrence()
        {
            var inner = new Exception("same");
            var outer = new Exception("same", inner);

            var result = WriteService.FormatExceptionChain(outer);
            int firstIdx = result.IndexOf("same", StringComparison.Ordinal);
            int secondIdx = result.IndexOf("same", firstIdx + 1, StringComparison.Ordinal);
            Assert.True(firstIdx >= 0);
            Assert.True(secondIdx < 0, "Duplicate message should be omitted.");
        }
    }
}
