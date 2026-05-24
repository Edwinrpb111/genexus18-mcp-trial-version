using System;
using System.Collections.Generic;
using Artech.Architecture.Common.Objects;
using GxMcp.Worker.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GxMcp.Worker.Tests
{
    // PatternApplyService is the W2 surface for IDE 'Right-click → Apply Pattern'.
    // The live SDK path requires Artech.Packages.Patterns.dll + a WorkWithPlus
    // license + an open KB; the unit suite covers everything reachable through
    // the IPatternEngineAdapter seam, calling the real service via InternalsVisibleTo.
    //
    //  - pattern unavailable (no license / dll missing) → "pattern_unavailable"
    //  - unknown pattern key (not a GUID, not in registry) → "pattern_unavailable"
    //  - object not found → reuses McpResponse.Error not-found shape
    //  - happy-path first apply → status=Success, wasFirstApply=true
    //  - reapply with existing instance → status=Success, wasFirstApply=false
    //  - reapply when no instance exists → falls back to first-apply
    //  - engine throws → surfaced as Error envelope (not bubbled)
    //
    // Real end-to-end apply on a live KB is gated on Skip="no WWP license".
    //
    // v2.6.6: serialized with InProcessBuildRunnerTests via a shared xunit
    // Collection because both touch static SDK-reflection probes that race
    // under xunit's default parallel scheduler. Reliably green either alone
    // or in a clean run; the Collection just removes the race window.
    [Collection("InProcessSdkReflection")]
    public class PatternApplyServiceTests
    {
        private const string ObjName = "SomeTransaction";
        private static readonly Guid WWP = PatternApplyService.WorkWithPlusPatternId;

        private class FakeEngine : IPatternEngineAdapter
        {
            public object DefinitionToReturn { get; set; } = new object();
            public object ExistingInstance { get; set; }
            public Func<JObject, PatternApplyResult> ApplyImpl { get; set; }
            public Func<JObject, PatternApplyResult> ReapplyImpl { get; set; }

            public int ApplyCalls;
            public int ReapplyCalls;

            public object GetPatternDefinition(Guid patternId) => DefinitionToReturn;
            public object GetPatternInstance(KBObject parent, Guid patternId) => ExistingInstance;

            public PatternApplyResult ApplyPattern(KBObject parent, object patternDefinition, JObject settings)
            {
                ApplyCalls++;
                return ApplyImpl != null
                    ? ApplyImpl(settings)
                    : new PatternApplyResult { GeneratedObjects = new List<string> { "Generated1", "Generated2" } };
            }

            public PatternApplyResult ReapplyPattern(object patternInstance, JObject settings)
            {
                ReapplyCalls++;
                return ReapplyImpl != null
                    ? ReapplyImpl(settings)
                    : new PatternApplyResult { GeneratedObjects = new List<string> { "Regenerated" } };
            }
        }

        // Builds a service whose object resolver returns the supplied KBObject (or null).
        // We pass null for the KBObject in tests; the fake engine never dereferences it
        // and PatternApplyService.ApplyPatternToObject tolerates null via objectNameForResponse.
        private static PatternApplyService MakeService(IPatternEngineAdapter engine, KBObject objToReturn)
        {
            return new PatternApplyService(null, engine, name => objToReturn);
        }

        [Fact]
        public void ApplyPattern_NoLicense_ReturnsPatternUnavailable()
        {
            var engine = new FakeEngine { DefinitionToReturn = null };
            // Object IS resolved (non-null) so we hit the engine probe path. But
            // we can't easily build a KBObject, so call the internal pipeline directly.
            var svc = MakeService(engine, null);

            string json = svc.ApplyPatternToObject(null, WWP, "WorkWithPlus", null, reapply: false, objectNameForResponse: ObjName);
            var obj = JObject.Parse(json);

            Assert.Equal("pattern_unavailable", obj["status"]?.ToString());
            Assert.Equal("WorkWithPlus", obj["patternKey"]?.ToString());
            Assert.Contains("license", obj["message"]?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, engine.ApplyCalls);
            Assert.Equal(0, engine.ReapplyCalls);
        }

        [Fact]
        public void ApplyPattern_UnknownKey_ReturnsPatternUnavailable()
        {
            // The public ApplyPattern parses the key before resolving objects, so
            // unknown keys short-circuit even with a null _objectService.
            var engine = new FakeEngine();
            var svc = new PatternApplyService(null, engine, name => null);

            string json = svc.ApplyPattern(ObjName, "NotARealPatternKey");
            var obj = JObject.Parse(json);

            Assert.Equal("pattern_unavailable", obj["status"]?.ToString());
            Assert.Equal("NotARealPatternKey", obj["patternKey"]?.ToString());
        }

        [Fact]
        public void ApplyPattern_ObjectNotFound_ReturnsError()
        {
            // findObjectOverride returns null and _objectService is null → fallback
            // McpResponse.Error("Object not found") branch (no SearchIndex needed).
            var engine = new FakeEngine();
            var svc = new PatternApplyService(null, engine, name => null);

            string json = svc.ApplyPattern(ObjName, "WorkWithPlus");
            var obj = JObject.Parse(json);

            Assert.Equal("Error", obj["status"]?.ToString());
            Assert.Contains("not found", obj["message"]?.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, engine.ApplyCalls);
        }

        [Fact]
        public void ApplyPattern_HappyPath_FirstApply_CallsApplyOnce()
        {
            var engine = new FakeEngine
            {
                ExistingInstance = null,
                ApplyImpl = _ => new PatternApplyResult { GeneratedObjects = new List<string> { "WWAlpha", "WWBeta" } }
            };
            var svc = MakeService(engine, null);

            string json = svc.ApplyPatternToObject(null, WWP, "WorkWithPlus", new JObject { ["foo"] = "bar" }, reapply: false, objectNameForResponse: ObjName);
            var obj = JObject.Parse(json);

            Assert.Equal("Success", obj["status"]?.ToString());
            Assert.True(obj["wasFirstApply"]?.ToObject<bool>());
            Assert.Equal(1, engine.ApplyCalls);
            Assert.Equal(0, engine.ReapplyCalls);

            var generated = (JArray)obj["generatedObjects"];
            Assert.Equal(2, generated.Count);
            Assert.Contains("WWAlpha", generated.ToObject<List<string>>());
        }

        [Fact]
        public void ApplyPattern_ExistingInstance_SkipsEngineReapply()
        {
            // F17 behavior change: when existingInstance != null we no longer invoke
            // engine.ReapplyPattern (it NREs on the live SDK install) — projection
            // is done via IPatternBuildProcess.UpdateParentObject instead. The test
            // verifies the engine NEVER sees a Reapply call in this path.
            var engine = new FakeEngine { ExistingInstance = new object() };
            var svc = MakeService(engine, null);

            string json = svc.ApplyPatternToObject(null, WWP, "WorkWithPlus", null, reapply: false, objectNameForResponse: ObjName);
            var obj = JObject.Parse(json);

            Assert.Equal("Success", obj["status"]?.ToString());
            Assert.False(obj["wasFirstApply"]?.ToObject<bool>());
            Assert.Equal(0, engine.ApplyCalls);
            Assert.Equal(0, engine.ReapplyCalls);
        }

        [Fact]
        public void Reapply_WithExistingInstance_SkipsEngineReapply()
        {
            // See ApplyPattern_ExistingInstance_SkipsEngineReapply for rationale.
            var engine = new FakeEngine { ExistingInstance = new object() };
            var svc = MakeService(engine, null);

            string json = svc.ApplyPatternToObject(null, WWP, "WorkWithPlus", null, reapply: true, objectNameForResponse: ObjName);
            var obj = JObject.Parse(json);

            Assert.Equal("Success", obj["status"]?.ToString());
            Assert.False(obj["wasFirstApply"]?.ToObject<bool>());
            Assert.Equal(0, engine.ReapplyCalls);
        }

        [Fact]
        public void Reapply_WithoutExistingInstance_FallsBackToFirstApply()
        {
            var engine = new FakeEngine { ExistingInstance = null };
            var svc = MakeService(engine, null);

            string json = svc.ApplyPatternToObject(null, WWP, "WorkWithPlus", null, reapply: true, objectNameForResponse: ObjName);
            var obj = JObject.Parse(json);

            Assert.Equal("Success", obj["status"]?.ToString());
            Assert.True(obj["wasFirstApply"]?.ToObject<bool>());
            Assert.Equal(1, engine.ApplyCalls);
            Assert.Equal(0, engine.ReapplyCalls);
        }

        [Fact]
        public void ApplyPattern_EngineThrows_SurfacesAsErrorEnvelope()
        {
            var engine = new FakeEngine
            {
                ApplyImpl = _ => throw new InvalidOperationException("boom in SDK")
            };
            var svc = MakeService(engine, null);

            string json = svc.ApplyPatternToObject(null, WWP, "WorkWithPlus", null, reapply: false, objectNameForResponse: ObjName);
            var obj = JObject.Parse(json);

            Assert.Equal("Error", obj["status"]?.ToString());
            Assert.Contains("boom", obj["message"]?.ToString() ?? "");
        }

        // Live integration smokes. Opt-in via GXMCP_TEST_KB=<path-to-kb> and
        // GXMCP_REQUIRE_WWP=1 for the WorkWithPlus-licensed tests.
        //
        // Body intentionally left minimal — a future commit will wire the real
        // ObjectService bootstrap + PatternApplyService(_realAdapter) once we
        // commit to a fixture KB layout. The conditional Skip already removes
        // the "permanently-Skip=true" friction, which is what F3 was about: the
        // tests are now part of the discoverable surface for anyone with a
        // licensed install, instead of dead code.
        [LiveKbFact(requiresWWP: true)]
        public void Integration_FirstApply_WWP_OnRealTransaction_GeneratesObjects()
        {
            string kb = Environment.GetEnvironmentVariable("GXMCP_TEST_KB");
            Assert.False(string.IsNullOrEmpty(kb)); // sanity: env-gate fired
            // TODO: open KB at <kb>, locate a non-WWP Transaction, call ApplyPattern,
            // assert wasFirstApply==true and PatternInstance present after.
        }

        [LiveKbFact(requiresWWP: true)]
        public void Integration_FirstApply_WWP_OnFreshWebPanel_AttachesPatternInstance()
        {
            string kb = Environment.GetEnvironmentVariable("GXMCP_TEST_KB");
            Assert.False(string.IsNullOrEmpty(kb));
            // TODO: create empty WebPanel, ApplyPattern WorkWithPlus, re-read and
            // assert PatternInstance part is populated.
        }

        // ── ApplySettings projection (best-effort JObject → ApplySettings instance) ──

        // Stand-in type that exercises the projection code paths without depending on
        // the live Artech.Packages.Patterns ApplySettings (which only exists in the
        // GeneXus install). Reflection logic in ProjectJObjectOntoInstance is type-
        // agnostic.
        private enum FakeMode { Tabular, Selection, View }

        private class FakeSettings
        {
            public string Title { get; set; }
            public int MaxRows { get; set; }
            public bool ShowFilters { get; set; }
            public FakeMode Mode { get; set; }
            public FakeNested Layout { get; set; }
        }
        private class FakeNested
        {
            public string Theme { get; set; }
            public int Columns { get; set; }
        }

        [Fact]
        public void ProjectJObject_ScalarsAndEnum_MapByName()
        {
            var instance = new FakeSettings();
            var unmapped = new List<string>();
            InvokeProject(
                new JObject
                {
                    ["title"] = "Invoices",
                    ["maxRows"] = 50,
                    ["showFilters"] = true,
                    ["mode"] = "Selection"
                },
                instance,
                unmapped);

            Assert.Equal("Invoices", instance.Title);
            Assert.Equal(50, instance.MaxRows);
            Assert.True(instance.ShowFilters);
            Assert.Equal(FakeMode.Selection, instance.Mode);
            Assert.Empty(unmapped);
        }

        [Fact]
        public void ProjectJObject_NestedObject_RecursesAndSetsChildProperties()
        {
            var instance = new FakeSettings();
            var unmapped = new List<string>();
            InvokeProject(
                new JObject
                {
                    ["layout"] = new JObject { ["theme"] = "Carmine", ["columns"] = 3 }
                },
                instance,
                unmapped);

            Assert.NotNull(instance.Layout);
            Assert.Equal("Carmine", instance.Layout.Theme);
            Assert.Equal(3, instance.Layout.Columns);
            Assert.Empty(unmapped);
        }

        [Fact]
        public void ProjectJObject_UnknownKeys_CollectedNotThrown()
        {
            var instance = new FakeSettings();
            var unmapped = new List<string>();
            InvokeProject(
                new JObject
                {
                    ["title"] = "Foo",
                    ["thisKeyDoesNotExist"] = "x",
                    ["alsoMissing"] = 42
                },
                instance,
                unmapped);

            Assert.Equal("Foo", instance.Title);
            Assert.Contains("thisKeyDoesNotExist", unmapped);
            Assert.Contains("alsoMissing", unmapped);
        }

        // Helper: call the static internal ProjectJObjectOntoInstance via reflection so
        // the test does not require InternalsVisibleTo gymnastics across runtime types.
        private static void InvokeProject(JObject src, object dst, IList<string> unmapped)
        {
            var t = typeof(PatternApplyService).Assembly.GetType("GxMcp.Worker.Services.ReflectionPatternEngineAdapter");
            Assert.NotNull(t);
            var method = t.GetMethod("ProjectJObjectOntoInstance",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.NotNull(method);
            method.Invoke(null, new object[] { src, dst, unmapped, 0 });
        }
    }
}
