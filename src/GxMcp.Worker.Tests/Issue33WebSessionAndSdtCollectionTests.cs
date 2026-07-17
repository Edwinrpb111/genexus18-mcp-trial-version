using GxMcp.Worker.Helpers;
using Xunit;

namespace GxMcp.Worker.Tests
{
    // issue #33: WebSession variable typing (problem B) and SDT-typed collection members
    // (problem A). The SDK-bound persistence (AttCustomType construction, GX_SDT item binding)
    // is exercised by the live KB verification recorded in the PR; these cover the
    // KB-independent resolution surface that decides whether those paths are reached at all.
    public class Issue33WebSessionAndSdtCollectionTests
    {
        // ── Problem B: WebSession is recognized as a built-in user-defined type ──

        [Theory]
        [InlineData("WebSession")]
        [InlineData("websession")]
        [InlineData("  WebSession  ")]
        public void IsBuiltinUserDefinedType_WebSession_IsRecognized(string typeName)
        {
            Assert.True(VariableInjector.IsBuiltinUserDefinedType(typeName));
        }

        [Theory]
        [InlineData("Character")]
        [InlineData("SdtFoo")]
        [InlineData("")]
        [InlineData(null)]
        public void IsBuiltinUserDefinedType_NonBuiltins_AreNot(string typeName)
        {
            Assert.False(VariableInjector.IsBuiltinUserDefinedType(typeName));
        }

        [Theory]
        [InlineData("websession", "WebSession")]
        [InlineData("WEBSESSION", "WebSession")]
        public void CanonicalUserDefinedTypeName_NormalisesCasing(string input, string expected)
        {
            Assert.Equal(expected, VariableInjector.CanonicalUserDefinedTypeName(input));
        }

        [Fact]
        public void CanonicalUserDefinedTypeName_Unknown_ReturnsNull()
        {
            Assert.Null(VariableInjector.CanonicalUserDefinedTypeName("HttpRequest"));
        }

        // The resolver must accept a bare "WebSession" (Recognized, as a DomainReference) so the
        // add/modify paths reach BuildResolvedVariableInto where the external-type bind happens —
        // rather than rejecting it up front with UnknownType.
        [Fact]
        public void VariableTypeResolver_WebSession_ResolvesAsRecognizedReference()
        {
            var res = VariableTypeResolver.Resolve("WebSession");
            Assert.True(res.Recognized);
            Assert.Equal("DomainReference", res.CanonicalType);
            Assert.Equal("WebSession", res.DomainName);
        }
    }
}
