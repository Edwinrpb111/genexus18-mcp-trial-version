using GxMcp.Worker.Helpers;
using Xunit;

namespace GxMcp.Worker.Tests
{
    // v2.3.8 (Task 7.1) — friction report 2026-05-15 #15: SDK error messages
    // come back in PT-BR while the assistant's tool output otherwise speaks EN,
    // forcing the LLM to guess at intent. ErrorMessages translates the known
    // canonical patterns and preserves the source string for forensics.
    public class ErrorMessagesTests
    {
        [Theory]
        [InlineData("A validação de Web Panel 'X' falhou.", "Web Panel 'X' validation failed.")]
        [InlineData("Referência de controle inválida: '[var:64]'", "Invalid control reference: '[var:64]'")]
        [InlineData("'Vazio' não é um valor válido", "'Empty' is not a valid value")]
        [InlineData("GAM não será reorganizado", "GAM will not be reorganized")]
        public void Translate_KnownPattern_ReturnsEn(string ptbr, string expected)
        {
            Assert.Equal(expected, ErrorMessages.Translate(ptbr));
        }

        [Fact]
        public void TranslateWithSource_PreservesOriginal()
        {
            var (en, src) = ErrorMessages.TranslateWithSource("A validação de Web Panel 'X' falhou.");
            Assert.Equal("A validação de Web Panel 'X' falhou.", src);
            Assert.StartsWith("Web Panel", en);
        }

        [Fact]
        public void Translate_NormalizesDoubleDot()
        {
            Assert.Equal("Validation failed.", ErrorMessages.Translate("Validation failed.."));
        }

        [Fact]
        public void Translate_NullOrEmpty_IsIdentity()
        {
            Assert.Equal("", ErrorMessages.Translate(""));
            Assert.Null(ErrorMessages.Translate(null));
        }

        [Fact]
        public void Translate_UnknownMessage_PassesThrough()
        {
            Assert.Equal("totally unknown text", ErrorMessages.Translate("totally unknown text"));
        }

        [Fact]
        public void Translate_NormalizesDetailedMessagesHeader()
        {
            Assert.Equal("Detailed messages:", ErrorMessages.Translate("Detailed Messages:"));
        }
    }
}
