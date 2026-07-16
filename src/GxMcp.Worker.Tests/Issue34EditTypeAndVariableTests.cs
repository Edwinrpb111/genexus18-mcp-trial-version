using System.Collections.Generic;
using System.Linq;
using GxMcp.Worker.Helpers;
using GxMcp.Worker.Models;
using GxMcp.Worker.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GxMcp.Worker.Tests
{
    public class Issue34EditTypeAndVariableTests
    {
        // Fake attribute mirroring the surface AttributeTypeApplier writes to (Type is string
        // in the fake path, so we can assert the eDBType member name it resolves to).
        private class FakeAttribute
        {
            public string Type { get; set; }
            public int Length { get; set; }
            public int Decimals { get; set; }
        }

        // ── Bug 3: blob/image map to the real eDBType members (BINARY / BITMAP) ──

        [Theory]
        [InlineData("Blob", "BINARY")]
        [InlineData("Binary", "BINARY")]
        [InlineData("Image", "BITMAP")]
        [InlineData("Bitmap", "BITMAP")]
        public void ApplyPrimitive_BlobAndImage_MapToRealEdbMembers(string canonical, string expectedEdb)
        {
            var fake = new FakeAttribute();
            bool applied = AttributeTypeApplier.ApplyPrimitive(fake, canonical, 4, null);
            Assert.True(applied);
            Assert.Equal(expectedEdb, fake.Type);
        }

        // ── Bug 2: SemanticOp.From accepts BOTH the flat and the documented nested-args shape ──

        [Fact]
        public void SemanticOpFrom_FlatShape_PopulatesArgs()
        {
            var op = SemanticOp.From(JObject.Parse(
                "{\"op\":\"add_attribute\",\"name\":\"A\",\"type\":\"Numeric(4.0)\"}"));
            Assert.Equal("add_attribute", op.Op);
            Assert.Equal("A", op.Args["name"]?.ToString());
            Assert.Equal("Numeric(4.0)", op.Args["type"]?.ToString());
        }

        [Fact]
        public void SemanticOpFrom_NestedArgsShape_HoistsToTopLevel()
        {
            var op = SemanticOp.From(JObject.Parse(
                "{\"op\":\"add_attribute\",\"args\":{\"name\":\"A\",\"type\":\"Numeric(4.0)\",\"length\":1}}"));
            Assert.Equal("add_attribute", op.Op);
            Assert.Equal("A", op.Args["name"]?.ToString());
            Assert.Equal("Numeric(4.0)", op.Args["type"]?.ToString());
            Assert.Equal(1, op.Args["length"]?.ToObject<int>());
            Assert.Null(op.Args["args"]);
        }

        [Fact]
        public void SemanticOpFrom_FlatWins_OnClashWithNestedArgs()
        {
            var op = SemanticOp.From(JObject.Parse(
                "{\"op\":\"add_attribute\",\"name\":\"Flat\",\"args\":{\"name\":\"Nested\"}}"));
            Assert.Equal("Flat", op.Args["name"]?.ToString());
        }

        // ── Bug 2: Transaction Structure attribute ops applied to the DSL text ──

        private static IList<SemanticOp> Ops(params string[] json)
            => json.Select(j => SemanticOp.From(JObject.Parse(j))).ToList();

        private const string BaseDsl =
            "AcaoCod* : NUMERIC(4) // \"Codigo\"\nAcaoDes : VARCHAR(40) // \"Descricao\"";

        [Fact]
        public void Dsl_AddAttribute_AppendsLine()
        {
            var outcome = new SemanticOpsService().ApplyTransactionStructureDsl(
                BaseDsl, Ops("{\"op\":\"add_attribute\",\"name\":\"AcaoVlr\",\"type\":\"Numeric(12.2)\"}"), "strict");
            Assert.False(outcome.Aborted);
            Assert.Contains("AcaoVlr : Numeric(12.2)", outcome.Text);
            Assert.Contains("AcaoCod* : NUMERIC(4)", outcome.Text);
        }

        [Fact]
        public void Dsl_AddAttribute_DuplicateName_Fails()
        {
            var outcome = new SemanticOpsService().ApplyTransactionStructureDsl(
                BaseDsl, Ops("{\"op\":\"add_attribute\",\"name\":\"AcaoDes\",\"type\":\"VarChar(10)\"}"), "strict");
            Assert.True(outcome.Aborted);
            Assert.Contains("already exists", outcome.Results.Single().Reason);
        }

        [Fact]
        public void Dsl_SetAttribute_ReplacesTypePreservingComment()
        {
            var outcome = new SemanticOpsService().ApplyTransactionStructureDsl(
                BaseDsl, Ops("{\"op\":\"set_attribute\",\"name\":\"AcaoDes\",\"type\":\"VarChar(80)\"}"), "strict");
            Assert.False(outcome.Aborted);
            Assert.Contains("AcaoDes : VarChar(80)", outcome.Text);
            Assert.Contains("\"Descricao\"", outcome.Text);
        }

        [Fact]
        public void Dsl_RemoveAttribute_DropsLine()
        {
            var outcome = new SemanticOpsService().ApplyTransactionStructureDsl(
                BaseDsl, Ops("{\"op\":\"remove_attribute\",\"name\":\"AcaoDes\"}"), "strict");
            Assert.False(outcome.Aborted);
            Assert.DoesNotContain("AcaoDes", outcome.Text);
            Assert.Contains("AcaoCod*", outcome.Text);
        }

        [Fact]
        public void Dsl_SetAttribute_NotFound_Fails()
        {
            var outcome = new SemanticOpsService().ApplyTransactionStructureDsl(
                BaseDsl, Ops("{\"op\":\"set_attribute\",\"name\":\"Ghost\",\"type\":\"Numeric(2.0)\"}"), "strict");
            Assert.True(outcome.Aborted);
            Assert.Contains("not found", outcome.Results.Single().Reason);
        }

        [Fact]
        public void Dsl_NestedArgsShape_Works_EndToEnd()
        {
            // The documented { op, args:{...} } shape must reach the DSL applier intact.
            var outcome = new SemanticOpsService().ApplyTransactionStructureDsl(
                BaseDsl, Ops("{\"op\":\"add_attribute\",\"args\":{\"name\":\"AcaoFlag\",\"type\":\"Boolean\"}}"), "strict");
            Assert.False(outcome.Aborted);
            Assert.Contains("AcaoFlag : Boolean", outcome.Text);
        }
    }
}
