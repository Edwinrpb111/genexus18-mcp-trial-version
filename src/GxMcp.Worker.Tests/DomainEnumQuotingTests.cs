using GxMcp.Worker.Services;
using Xunit;

namespace GxMcp.Worker.Tests
{
    // User report 2026-07-17: "can't create combobox domain with options". Root cause:
    // character-family enum values were stored raw ("A") but GeneXus needs quoted literals
    // ("A"), so the IDE rendered an empty combobox. ObjectService now auto-quotes them.
    public class DomainEnumQuotingTests
    {
        [Theory]
        [InlineData("Character", true)]
        [InlineData("char", true)]
        [InlineData("VarChar", true)]
        [InlineData("LongVarChar", true)]
        [InlineData("Numeric", false)]
        [InlineData("Date", false)]
        [InlineData("Boolean", false)]
        [InlineData(null, false)]
        public void IsStringDataType_ClassifiesCharacterFamily(string dt, bool expected)
        {
            Assert.Equal(expected, ObjectService.IsStringDataType(dt));
        }

        [Fact]
        public void QuoteCharEnumValue_WrapsBareValue()
        {
            Assert.Equal("\"A\"", ObjectService.QuoteCharEnumValue("A"));
            Assert.Equal("\"Ativo\"", ObjectService.QuoteCharEnumValue("Ativo"));
        }

        [Fact]
        public void QuoteCharEnumValue_LeavesAlreadyQuotedUntouched()
        {
            Assert.Equal("\"A\"", ObjectService.QuoteCharEnumValue("\"A\""));
            Assert.Equal("'A'", ObjectService.QuoteCharEnumValue("'A'"));
        }

        [Fact]
        public void QuoteCharEnumValue_PassesEmptyThrough()
        {
            Assert.Equal("", ObjectService.QuoteCharEnumValue(""));
            Assert.Null(ObjectService.QuoteCharEnumValue(null));
        }
    }
}
