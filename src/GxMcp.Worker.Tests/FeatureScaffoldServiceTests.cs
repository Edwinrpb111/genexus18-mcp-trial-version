using System;
using System.Collections.Generic;
using GxMcp.Worker.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GxMcp.Worker.Tests
{
    // Item #100 (Tier-S, doc 2026-05-22) — feature_scaffold orchestrator.
    // Coverage:
    //  1) Validation rejects malformed specs WITHOUT invoking the dispatcher.
    //  2) dryRun returns the plan, still WITHOUT invoking the dispatcher.
    //  3) Happy path orchestration: every step is invoked in order, statuses
    //     are bubbled into completedSteps, final envelope is Ok.
    //  4) PartialFailure path: a mid-plan failure short-circuits and surfaces
    //     completedSteps + failedStep.
    public class FeatureScaffoldServiceTests
    {
        private sealed class RecordingDispatcher : IToolDispatcher
        {
            public readonly List<(string tool, JObject args)> Calls = new List<(string, JObject)>();
            private readonly Func<int, string, JObject, JObject> _handler;

            public RecordingDispatcher(Func<int, string, JObject, JObject> handler = null)
            {
                _handler = handler ?? ((_, __, ___) => new JObject { ["status"] = "Ok" });
            }

            public JObject Invoke(string tool, JObject args)
            {
                int idx = Calls.Count;
                Calls.Add((tool, args));
                return _handler(idx, tool, args);
            }
        }

        private static JObject ValidSpec(bool tests = false)
        {
            return new JObject
            {
                ["name"] = "CourseEnrollment",
                ["entity"] = new JObject
                {
                    ["type"] = "Transaction",
                    ["name"] = "Enrollment",
                    ["attributes"] = new JArray(
                        new JObject { ["name"] = "EnrId", ["type"] = "Numeric(8)", ["isKey"] = true },
                        new JObject { ["name"] = "EnrStudent", ["type"] = "Character(60)" }
                    )
                },
                ["ui"] = new JObject { ["list"] = true, ["edit"] = true, ["summary"] = true },
                ["procedures"] = new JArray(
                    new JObject { ["name"] = "GetEnrollmentsByCourse", ["parms"] = new JArray("in:Course:Character(40)") }
                ),
                ["tests"] = tests
            };
        }

        [Fact]
        public void Scaffold_InvalidSpec_ReturnsValidationErrors_DoesNotInvokeDispatcher()
        {
            // Bad: missing entity.attributes; missing entity.name; no isKey;
            // a procedure with a malformed parm string.
            var bad = new JObject
            {
                ["name"] = "Broken",
                ["entity"] = new JObject
                {
                    ["type"] = "Transaction"
                    // no name, no attributes
                },
                ["procedures"] = new JArray(
                    new JObject { ["name"] = "BadProc", ["parms"] = new JArray("notAValidParm") }
                )
            };

            var dispatcher = new RecordingDispatcher();
            var svc = new FeatureScaffoldService(dispatcher);

            var result = svc.Scaffold(bad, dryRun: false);

            Assert.Equal("error", result["status"]?.ToString());
            Assert.Equal("ValidationError", result["error"]?["code"]?.ToString());
            var errs = (JArray)result["validation"];
            Assert.NotNull(errs);
            Assert.True(errs.Count >= 3, $"expected ≥3 validation errors, got {errs.Count}: {errs}");
            // Every error must point at a JSON path.
            foreach (var e in errs)
            {
                Assert.False(string.IsNullOrWhiteSpace(e["path"]?.ToString()), "validation entry missing 'path'");
                Assert.False(string.IsNullOrWhiteSpace(e["message"]?.ToString()), "validation entry missing 'message'");
            }
            // Crucial: no mutations happened.
            Assert.Empty(dispatcher.Calls);
        }

        [Fact]
        public void Scaffold_DryRun_ReturnsPlan_DoesNotInvokeDispatcher()
        {
            var dispatcher = new RecordingDispatcher();
            var svc = new FeatureScaffoldService(dispatcher);

            var result = svc.Scaffold(ValidSpec(), dryRun: true);

            Assert.Equal("ok", result["status"]?.ToString());
            Assert.Equal("DryRun", result["code"]?.ToString());
            var plan = (JArray)result["result"]?["plan"];
            Assert.NotNull(plan);
            // create_object(Transaction) + apply_pattern(WWP) + create_object(Proc) = 3
            Assert.Equal(3, plan.Count);
            Assert.Equal("genexus_create_object", plan[0]["tool"]?.ToString());
            Assert.Equal("Transaction", plan[0]["args"]?["type"]?.ToString());
            Assert.Equal("genexus_apply_pattern", plan[1]["tool"]?.ToString());
            Assert.Equal("WorkWithPlus", plan[1]["args"]?["pattern"]?.ToString());
            Assert.Equal("genexus_create_object", plan[2]["tool"]?.ToString());
            Assert.Equal("Procedure", plan[2]["args"]?["type"]?.ToString());

            // No dispatcher calls in dryRun mode.
            Assert.Empty(dispatcher.Calls);
        }

        [Fact]
        public void Scaffold_HappyPath_InvokesEveryStepInOrder_ReturnsOk()
        {
            var dispatcher = new RecordingDispatcher((idx, tool, args) =>
                new JObject { ["status"] = "Ok", ["echo"] = tool });
            var svc = new FeatureScaffoldService(dispatcher);

            var result = svc.Scaffold(ValidSpec(tests: true), dryRun: false);

            Assert.Equal("ok", result["status"]?.ToString());
            // Transaction + apply_pattern + Procedure + ProcedureTest = 4
            Assert.Equal(4, dispatcher.Calls.Count);
            Assert.Equal("genexus_create_object", dispatcher.Calls[0].tool);
            Assert.Equal("Transaction", dispatcher.Calls[0].args["type"]?.ToString());
            Assert.Equal("Enrollment", dispatcher.Calls[0].args["name"]?.ToString());

            Assert.Equal("genexus_apply_pattern", dispatcher.Calls[1].tool);
            Assert.Equal("WorkWithPlus", dispatcher.Calls[1].args["pattern"]?.ToString());

            Assert.Equal("genexus_create_object", dispatcher.Calls[2].tool);
            Assert.Equal("GetEnrollmentsByCourse", dispatcher.Calls[2].args["name"]?.ToString());
            Assert.Equal("parm(in:&Course);", dispatcher.Calls[2].args["rules"]?.ToString());

            Assert.Equal("genexus_create_object", dispatcher.Calls[3].tool);
            Assert.Equal("GetEnrollmentsByCourseTest", dispatcher.Calls[3].args["name"]?.ToString());

            var completed = (JArray)result["result"]?["completedSteps"];
            Assert.Equal(4, completed.Count);
        }

        [Fact]
        public void Scaffold_PartialFailure_ShortCircuits_ReturnsCompletedAndFailedStep()
        {
            // Fail the apply_pattern call (step index 1) to verify the
            // orchestrator stops there and reports completedSteps=[step 0].
            var dispatcher = new RecordingDispatcher((idx, tool, args) =>
            {
                if (idx == 1)
                    return new JObject { ["status"] = "Error", ["error"] = "WWP template not found" };
                return new JObject { ["status"] = "Ok" };
            });
            var svc = new FeatureScaffoldService(dispatcher);

            var result = svc.Scaffold(ValidSpec(), dryRun: false);

            Assert.Equal("error", result["status"]?.ToString());
            Assert.Equal("ScaffoldPartialFailure", result["error"]?["code"]?.ToString());
            var completed = (JArray)result["completedSteps"];
            Assert.Single(completed);
            Assert.Equal(0, (int)completed[0]["index"]);

            var failed = (JObject)result["failedStep"];
            Assert.Equal(1, (int)failed["index"]);
            Assert.Equal("genexus_apply_pattern", failed["tool"]?.ToString());

            // Dispatcher should NOT have been called for step 2.
            Assert.Equal(2, dispatcher.Calls.Count);
        }
    }
}
