using GxMcp.Worker.Helpers;
using Xunit;

namespace GxMcp.Worker.Tests
{
    public class WebFormTypedPropertyAutoRouteTests
    {
        [Fact]
        public void OnClickEvent_OnGxButton_RoutesToEvent()
        {
            Assert.Equal("Event", WebFormTypedPropertyWriter.ResolveCanonicalAttr("OnClickEvent", "gxButton"));
        }

        [Fact]
        public void OnClickEvent_OnGxAttribute_RoutesToEventGX()
        {
            Assert.Equal("eventGX", WebFormTypedPropertyWriter.ResolveCanonicalAttr("OnClickEvent", "gxAttribute"));
            Assert.Equal("eventGX", WebFormTypedPropertyWriter.ResolveCanonicalAttr("OnClickEvent", "gxImage"));
            Assert.Equal("eventGX", WebFormTypedPropertyWriter.ResolveCanonicalAttr("OnClickEvent", "gxBitmap"));
        }

        [Fact]
        public void OnClickEvent_UnknownElement_FallsBackToEvent()
        {
            Assert.Equal("Event", WebFormTypedPropertyWriter.ResolveCanonicalAttr("OnClickEvent", "gxFoo"));
        }

        [Fact]
        public void CaptionExpression_RoutesToCaption()
        {
            Assert.Equal("Caption", WebFormTypedPropertyWriter.ResolveCanonicalAttr("CaptionExpression", "gxButton"));
            Assert.Equal("Caption", WebFormTypedPropertyWriter.ResolveCanonicalAttr("CaptionExpression", "gxTextBlock"));
        }

        [Fact]
        public void OnEnterEvent_RoutesToEventGX()
        {
            Assert.Equal("eventGX", WebFormTypedPropertyWriter.ResolveCanonicalAttr("OnEnterEvent", "gxAttribute"));
        }

        [Fact]
        public void UnknownDescriptor_ReturnedUnchanged()
        {
            Assert.Equal("Class", WebFormTypedPropertyWriter.ResolveCanonicalAttr("Class", "gxButton"));
            Assert.Equal("Visible", WebFormTypedPropertyWriter.ResolveCanonicalAttr("Visible", "gxButton"));
        }

        [Fact]
        public void DrainAutoRoutes_ReturnsEmptyByDefault()
        {
            // Clear any prior thread-local state first.
            WebFormTypedPropertyWriter.DrainAutoRoutes();
            var routes = WebFormTypedPropertyWriter.DrainAutoRoutes();
            Assert.NotNull(routes);
            Assert.Empty(routes);
        }
    }
}
