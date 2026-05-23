using GxMcp.Worker.Helpers;
using Xunit;

namespace GxMcp.Worker.Tests
{
    public class MasterPageCompatScannerTests
    {
        [Fact]
        public void GxMessages_WithoutGamMaster_Flagged()
        {
            string xml = @"<Form type=""html""><gxMessages id=""msg1"" /></Form>";
            var hits = MasterPageCompatScanner.Scan(xml, "Application");
            Assert.Single(hits);
            Assert.Contains("gxMessages", hits[0].Control);
            Assert.Equal("Application", hits[0].MasterPage);
            Assert.Contains("GAM", hits[0].Hint);
        }

        [Fact]
        public void GxMessages_WithGamMaster_NotFlagged()
        {
            string xml = @"<Form type=""html""><gxMessages id=""msg1"" /></Form>";
            var hits = MasterPageCompatScanner.Scan(xml, "Application.gam");
            Assert.Empty(hits);
        }

        [Fact]
        public void ProgressIndicator_UnderCustomMaster_Flagged()
        {
            string xml = @"<Form type=""html""><gxAttribute id=""prog1"" ControlType=""ProgressIndicator"" /></Form>";
            var hits = MasterPageCompatScanner.Scan(xml, "MyCustomMaster");
            Assert.Single(hits);
            Assert.Contains("ProgressIndicator", hits[0].Control);
        }

        [Fact]
        public void NoMasterPage_ControlsRequiringOne_Flagged()
        {
            string xml = @"<Form type=""html""><gxMessages id=""m"" /><gxMenu id=""nav"" /></Form>";
            var hits = MasterPageCompatScanner.Scan(xml, null);
            Assert.Equal(2, hits.Count);
            foreach (var h in hits) Assert.Equal("(none)", h.MasterPage);
        }

        [Fact]
        public void RegularControls_NotFlagged()
        {
            string xml = @"<Form type=""html""><gxButton id=""btn1"" /><gxTextBlock id=""t1"" /></Form>";
            var hits = MasterPageCompatScanner.Scan(xml, "AnyMaster");
            Assert.Empty(hits);
        }

        [Fact]
        public void InvalidXml_ReturnsEmpty()
        {
            var hits = MasterPageCompatScanner.Scan("<not-xml", "Application");
            Assert.Empty(hits);
        }

        [Fact]
        public void EmptyInput_ReturnsEmpty()
        {
            Assert.Empty(MasterPageCompatScanner.Scan(null, "Application"));
            Assert.Empty(MasterPageCompatScanner.Scan("", "Application"));
        }
    }
}
