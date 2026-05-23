using System;
using GxMcp.Worker.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GxMcp.Worker.Tests
{
    // Wave-3 item 42: SampleDataService — exercises input-validation and the
    // typed-value formatter without bringing up the SDK. End-to-end attribute
    // discovery + INSERT emission is covered by the live worker integration
    // suite.
    public class SampleDataServiceTests
    {
        [Fact]
        public void MissingTransactionName_ReturnsError()
        {
            var svc = new SampleDataService(objectService: null);
            var j = JObject.Parse(svc.Generate("", 5));
            Assert.NotNull(j["error"]);
        }

        [Fact]
        public void NullObjectService_ReturnsTransactionNotFound()
        {
            var svc = new SampleDataService(objectService: null);
            var j = JObject.Parse(svc.Generate("Aluno", 5));
            // With no ObjectService FindObject returns null → "Transaction not found"
            Assert.NotNull(j["error"]);
            Assert.Contains("not found", (string)j["error"], StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void FakeValueSql_NumericNoDecimals_EmitsInteger()
        {
            var attr = new SampleDataService.AttrInfo { Name = "X", Type = "NUMERIC", Length = 4, Decimals = 0 };
            string v = SampleDataService.FakeValueSql(attr, 1, new Random(1));
            // Integer literal — no quotes, no dot.
            Assert.DoesNotContain("'", v);
            Assert.DoesNotContain(".", v);
            Assert.True(int.TryParse(v, out _));
        }

        [Fact]
        public void FakeValueSql_Character_EmitsQuotedAndLengthCapped()
        {
            var attr = new SampleDataService.AttrInfo { Name = "X", Type = "CHARACTER", Length = 10, Decimals = 0 };
            string v = SampleDataService.FakeValueSql(attr, 0, new Random(0));
            Assert.StartsWith("'", v);
            Assert.EndsWith("'", v);
            // 10 chars + 2 quote chars.
            Assert.True(v.Length <= 12);
        }

        [Fact]
        public void FakeValueSql_Boolean_EmitsTOrF()
        {
            var attr = new SampleDataService.AttrInfo { Name = "X", Type = "BOOLEAN", Length = 0, Decimals = 0 };
            string v = SampleDataService.FakeValueSql(attr, 0, new Random(0));
            Assert.True(v == "'t'" || v == "'f'");
        }

        [Fact]
        public void FakeValueSql_DateTime_EmitsRecentIsoQuoted()
        {
            var attr = new SampleDataService.AttrInfo { Name = "X", Type = "DATETIME", Length = 0, Decimals = 0 };
            string v = SampleDataService.FakeValueSql(attr, 0, new Random(0));
            Assert.StartsWith("'", v);
            Assert.EndsWith("'", v);
            Assert.Equal(21, v.Length); // 'yyyy-MM-dd HH:mm:ss' between quotes
        }
    }
}
