using System;
using System.Collections.Generic;
using GxMcp.Worker.Helpers;
using GxMcp.Worker.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GxMcp.Worker.Tests
{
    // v2.3.8 Task 4.5 — Ghost-binding diagnostics + [var:N] resolver.
    //
    // Helper-only tests. The KBObject overload requires Artech.Genexus.Common,
    // which the test project deliberately does not reference (only
    // Artech.Architecture.Common is wired up via HintPath). The
    // ResolveVarBindings(message, Func<int,string>) overload exists precisely
    // so the resolver logic can be unit-tested without an SDK install; the
    // production KBObject overload is a thin wrapper that builds the same
    // lookup from VariablesPart and forwards.
    //
    // The end-to-end DeleteVariable bound-to-control behaviour is covered by
    // the helper TryBuildBoundToControlsError contract via FindVarBindings,
    // and a guarded WriteService invocation that follows the SDK-skip pattern
    // used by DeleteVariableSymmetryTests.
    public class VarBindingResolverTests
    {
        [Fact]
        public void ResolveVarBindings_KnownId_SubstitutesName()
        {
            Func<int, string> lookup = id => id == 64 ? "PareceresStatusLabel" : null;
            var msg = "Invalid control reference: '[var:64]'";
            var resolved = WebFormSchemaHints.ResolveVarBindings(msg, lookup);
            Assert.Contains("&PareceresStatusLabel", resolved);
            Assert.DoesNotContain("[var:64]", resolved);
        }

        [Fact]
        public void ResolveVarBindings_UnknownId_AppendsUnresolvedMarker()
        {
            Func<int, string> lookup = _ => null;
            var msg = "Invalid control reference: '[var:999]'";
            var resolved = WebFormSchemaHints.ResolveVarBindings(msg, lookup);
            Assert.Contains("[var:999 (unresolved)]", resolved);
        }

        [Fact]
        public void ResolveVarBindings_MultipleIds_MixedResolution()
        {
            // Both substitutions happen in a single pass; unresolved ids retain
            // their marker without poisoning the resolved ones.
            Func<int, string> lookup = id => id switch
            {
                1 => "Alpha",
                2 => "Beta",
                _ => null,
            };
            var msg = "binds [var:1] and [var:2] and [var:99]";
            var resolved = WebFormSchemaHints.ResolveVarBindings(msg, lookup);
            Assert.Contains("&Alpha", resolved);
            Assert.Contains("&Beta", resolved);
            Assert.Contains("[var:99 (unresolved)]", resolved);
        }

        [Fact]
        public void ResolveVarBindings_NullMessage_ReturnsNull()
        {
            var resolved = WebFormSchemaHints.ResolveVarBindings((string)null, id => "X");
            Assert.Null(resolved);
        }

        [Fact]
        public void ResolveVarBindings_NullLookup_LeavesAllUnresolved()
        {
            var resolved = WebFormSchemaHints.ResolveVarBindings("ref [var:7]", (Func<int, string>)null);
            Assert.Contains("[var:7 (unresolved)]", resolved);
        }

        [Fact]
        public void ResolveVarBindings_NoVarTokens_Passthrough()
        {
            var msg = "Some unrelated error message";
            var resolved = WebFormSchemaHints.ResolveVarBindings(msg, id => "X");
            Assert.Equal(msg, resolved);
        }

        [Fact]
        public void FindVarBindings_MatchesExactToken_NotPrefix()
        {
            // Guard against var:6 spuriously matching inside var:64.
            var xml = @"<root>
                <gxAttribute id='ctl1' AttID='var:6' />
                <gxAttribute id='ctl2' AttID='var:64' />
                <gxAttribute id='ctl3' AttID='var:600' />
            </root>";
            var hits = WebFormSchemaHints.FindVarBindings(xml, 6);
            Assert.Single(hits);
            Assert.Equal("ctl1", hits[0].ControlId);
            Assert.Equal("gxAttribute", hits[0].Element);
            Assert.Equal("AttID", hits[0].Attribute);
        }

        [Fact]
        public void FindVarBindings_NoMatches_ReturnsEmpty()
        {
            var xml = "<root><gxAttribute id='ctl1' AttID='attr:Customer' /></root>";
            var hits = WebFormSchemaHints.FindVarBindings(xml, 42);
            Assert.Empty(hits);
        }

        [Fact]
        public void FindVarBindings_MalformedXml_ReturnsEmptyNotThrow()
        {
            var hits = WebFormSchemaHints.FindVarBindings("<not-xml<>", 1);
            Assert.Empty(hits);
        }

        [Fact]
        public void FindVarBindings_EmptyXml_ReturnsEmpty()
        {
            Assert.Empty(WebFormSchemaHints.FindVarBindings("", 1));
            Assert.Empty(WebFormSchemaHints.FindVarBindings(null, 1));
        }

        [Fact]
        public void FindVarBindings_MultipleControlsSameVar_ReturnsAll()
        {
            var xml = @"<root>
                <gxAttribute id='a' AttID='var:5' />
                <gxTextBlock id='b' AttID='var:5' />
            </root>";
            var hits = WebFormSchemaHints.FindVarBindings(xml, 5);
            Assert.Equal(2, hits.Count);
        }

        // ── End-to-end DeleteVariable surface check ──────────────────────────
        //
        // No KB is loaded in this test host, so DeleteVariable returns the
        // "Object not found" envelope rather than a BoundToControls one. The
        // invariant we assert here is that the structured-error code path
        // doesn't crash, and that the legacy raw-error shape still parses as
        // JSON. The actual BoundToControls assertion requires a fixture KB —
        // see DeleteVariableSymmetryTests for the same SDK-skip pattern.
        [Fact]
        public void DeleteVariable_NoKbLoaded_DoesNotEmitBoundToControlsSpuriously()
        {
            WriteService ws;
            try
            {
                var indexCache = new IndexCacheService();
                var build = new BuildService();
                var kb = new KbService(indexCache);
                kb.SetBuildService(build);
                build.SetKbService(kb);
                indexCache.SetBuildService(build);
                var obj = new ObjectService(kb, build);
                ws = new WriteService(obj);
            }
            catch (System.IO.FileNotFoundException) { return; }
            catch (System.TypeLoadException) { return; }

            string json;
            try { json = ws.DeleteVariable("Fixture_NoSuchObject", "X"); }
            catch (System.IO.FileNotFoundException) { return; }
            catch (System.TypeLoadException) { return; }

            var parsed = JObject.Parse(json);
            Assert.NotNull(parsed);
            // Without a real bound-to-control rejection, code should not be
            // BoundToControls — confirms the heuristic doesn't fire on the
            // generic "Object not found" path.
            Assert.NotEqual("BoundToControls", parsed["code"]?.ToString());
        }
    }
}
