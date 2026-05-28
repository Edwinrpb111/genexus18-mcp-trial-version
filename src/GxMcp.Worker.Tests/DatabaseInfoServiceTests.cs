using GxMcp.Worker.Services;
using Xunit;

namespace GxMcp.Worker.Tests
{
    public class DatabaseInfoServiceTests
    {
        [Theory]
        [InlineData(1, "SqlServer")]
        [InlineData(2, "Db2")]
        [InlineData(3, "Informix")]
        [InlineData(4, "Oracle")]
        [InlineData(5, "MySQL")]
        [InlineData(6, "PostgreSQL")]
        [InlineData(7, "Oracle")]
        [InlineData(8, "Db2/AS400")]
        [InlineData(9, "Db2Universal")]
        [InlineData(10, "SAPHana")]
        [InlineData(11, "DynamoDB")]
        [InlineData(0, "Unknown")]
        [InlineData(99, "Unknown")]
        public void DbmsTypeLabel_MapsKnownCodes(int code, string expected)
        {
            Assert.Equal(expected, DatabaseInfoService.DbmsTypeLabel(code));
        }

        [Fact]
        public void GetInfo_WithoutKb_ReturnsStructuredError()
        {
            var svc = new DatabaseInfoService(null);
            string json = svc.GetInfo();
            var obj = Newtonsoft.Json.Linq.JObject.Parse(json);
            Assert.Equal("error", obj["status"]?.ToString());
            Assert.Equal("KbNotOpen", obj["error"]?["code"]?.ToString());
        }
    }
}
