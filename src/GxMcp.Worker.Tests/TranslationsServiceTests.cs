using System.IO;
using GxMcp.Worker.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GxMcp.Worker.Tests
{
    // Wave-3 item 92: TranslationsService — CSV parser + the deferred-write
    // contract. The SDK CaptionExpression write path is not yet wired, so the
    // service must still return a structured ItemDeferred shape (status, code,
    // hint) for callers to detect.
    public class TranslationsServiceTests
    {
        [Fact]
        public void MissingInputPath_ReturnsError()
        {
            var svc = new TranslationsService(null);
            var j = JObject.Parse(svc.Import(""));
            Assert.NotNull(j["message"]);
        }

        [Fact]
        public void NonexistentFile_ReturnsError()
        {
            var svc = new TranslationsService(null);
            var j = JObject.Parse(svc.Import(@"Z:\does\not\exist.csv"));
            Assert.NotNull(j["message"]);
        }

        [Fact]
        public void ValidCsv_ReportsItemDeferredAndSkipsRows()
        {
            string path = Path.Combine(Path.GetTempPath(), "gxmcp-trn-" + System.Guid.NewGuid().ToString("N") + ".csv");
            File.WriteAllText(path,
                "objectName,attribute,language,value\n" +
                "Aluno,Caption,en,Student\n" +
                "Aluno,Caption,pt,Aluno\n");
            try
            {
                var svc = new TranslationsService(null);
                var j = JObject.Parse(svc.Import(path));
                Assert.Equal("Unwired", (string)j["status"]);
                Assert.Equal("ItemDeferred", (string)j["code"]);
                Assert.Equal(2, (int)j["rowsParsed"]);
                Assert.Equal(0, (int)j["updated"]);
                var skipped = (JArray)j["skipped"];
                Assert.Equal(2, skipped.Count);
                Assert.Equal("write-path-deferred", (string)skipped[0]["reason"]);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void MalformedRow_RecordedAsParseError()
        {
            string path = Path.Combine(Path.GetTempPath(), "gxmcp-trn-" + System.Guid.NewGuid().ToString("N") + ".csv");
            File.WriteAllText(path,
                "objectName,attribute,language,value\n" +
                "Aluno,OnlyTwoFields\n" +
                "Aluno,Caption,en,Student\n");
            try
            {
                var svc = new TranslationsService(null);
                var j = JObject.Parse(svc.Import(path));
                var errors = (JArray)j["errors"];
                Assert.True(errors.Count >= 1);
                Assert.Equal(1, (int)j["rowsParsed"]); // only the valid row
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void CsvParser_HandlesQuotedFieldsWithCommas()
        {
            var fields = TranslationsService.SplitCsvLine("a,b,\"c,d\",e");
            Assert.Equal(4, fields.Count);
            Assert.Equal("c,d", fields[2]);
        }
    }
}
