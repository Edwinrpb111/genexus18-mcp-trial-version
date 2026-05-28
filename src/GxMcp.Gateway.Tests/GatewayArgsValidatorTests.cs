using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GxMcp.Gateway.Tests
{
    /// <summary>
    /// Unit tests for GatewayArgsValidator: required-field, type, and enum checks
    /// plus an end-to-end test confirming InvalidArgs is returned without hitting the worker.
    /// </summary>
    public class GatewayArgsValidatorTests
    {
        // ── Helper: build a minimal inputSchema ──────────────────────────────

        private static JObject MakeSchema(
            IEnumerable<(string name, string type, string[]? enumValues)>? props = null,
            string[]? required = null,
            bool? additionalPropertiesFalse = null)
        {
            var schema = new JObject { ["type"] = "object" };
            var propertiesObj = new JObject();
            foreach (var (name, type, enumValues) in props ?? System.Array.Empty<(string, string, string[]?)>())
            {
                var p = new JObject { ["type"] = type };
                if (enumValues != null)
                    p["enum"] = new JArray(enumValues);
                propertiesObj[name] = p;
            }
            schema["properties"] = propertiesObj;

            if (required != null)
                schema["required"] = new JArray(required);
            if (additionalPropertiesFalse == true)
                schema["additionalProperties"] = false;

            return schema;
        }

        // ── 1. Pass: well-formed call ─────────────────────────────────────────

        [Fact]
        public void Validate_WellFormedCall_ReturnsOk()
        {
            // genexus_read schema: name is a string (not required in schema but type-declared)
            GatewayArgsValidator.ClearCache();
            GatewayArgsValidator.PrimeCache("genexus_read", MakeSchema(
                props: new[] { ("name", "string", (string[]?)null) }
            ));

            var result = GatewayArgsValidator.Validate("genexus_read", new JObject { ["name"] = "MyProcedure" });

            Assert.True(result.Ok);
            Assert.Empty(result.Violations);
        }

        // ── 2. Fail: missing required field ──────────────────────────────────

        [Fact]
        public void Validate_MissingRequiredField_ReturnsViolation()
        {
            GatewayArgsValidator.ClearCache();
            GatewayArgsValidator.PrimeCache("genexus_edit", MakeSchema(
                props: new[] { ("name", "string", (string[]?)null), ("part", "string", (string[]?)null) },
                required: new[] { "name" }
            ));

            // Call without the required "name" field
            var result = GatewayArgsValidator.Validate("genexus_edit", new JObject { ["part"] = "Source" });

            Assert.False(result.Ok);
            Assert.Contains(result.Violations, v => v.Path == "name" && v.Actual == "missing");
        }

        [Fact]
        public void Validate_NullArgs_WithRequiredField_ReturnsViolation()
        {
            GatewayArgsValidator.ClearCache();
            GatewayArgsValidator.PrimeCache("genexus_inspect", MakeSchema(
                props: new[] { ("name", "string", (string[]?)null) },
                required: new[] { "name" }
            ));

            var result = GatewayArgsValidator.Validate("genexus_inspect", null);

            Assert.False(result.Ok);
            Assert.Contains(result.Violations, v => v.Path == "name" && v.Actual == "missing");
        }

        // ── 3. Fail: wrong type ───────────────────────────────────────────────

        [Fact]
        public void Validate_WrongTypeValue_ReturnsViolation()
        {
            GatewayArgsValidator.ClearCache();
            GatewayArgsValidator.PrimeCache("genexus_read", MakeSchema(
                props: new[] { ("limit", "integer", (string[]?)null) }
            ));

            // Pass a string where an integer is expected
            var result = GatewayArgsValidator.Validate("genexus_read", new JObject { ["limit"] = "not-a-number" });

            Assert.False(result.Ok);
            Assert.Contains(result.Violations, v => v.Path == "limit" && v.Expected == "integer");
        }

        [Fact]
        public void Validate_WrongTypeBoolean_ReturnsViolation()
        {
            GatewayArgsValidator.ClearCache();
            GatewayArgsValidator.PrimeCache("genexus_edit", MakeSchema(
                props: new[] { ("dryRun", "boolean", (string[]?)null) }
            ));

            // Pass a string instead of boolean
            var result = GatewayArgsValidator.Validate("genexus_edit", new JObject { ["dryRun"] = "yes" });

            Assert.False(result.Ok);
            Assert.Contains(result.Violations, v => v.Path == "dryRun" && v.Expected == "boolean");
        }

        // ── 4. Fail: out-of-enum value ────────────────────────────────────────

        [Fact]
        public void Validate_OutOfEnumValue_ReturnsViolation()
        {
            GatewayArgsValidator.ClearCache();
            // genexus_edit mode: enum [full, patch, ops]
            GatewayArgsValidator.PrimeCache("genexus_edit", MakeSchema(
                props: new[] { ("mode", "string", new[] { "full", "patch", "ops" }) }
            ));

            var result = GatewayArgsValidator.Validate("genexus_edit", new JObject { ["mode"] = "invalid_mode" });

            Assert.False(result.Ok);
            Assert.Contains(result.Violations, v => v.Path == "mode" && v.Actual == "invalid_mode");
        }

        [Fact]
        public void Validate_ValidEnumValue_ReturnsOk()
        {
            GatewayArgsValidator.ClearCache();
            GatewayArgsValidator.PrimeCache("genexus_edit", MakeSchema(
                props: new[] { ("mode", "string", new[] { "full", "patch", "ops" }) }
            ));

            var result = GatewayArgsValidator.Validate("genexus_edit", new JObject { ["mode"] = "patch" });

            Assert.True(result.Ok);
            Assert.Empty(result.Violations);
        }

        // ── 5. Pass: tool with no declared schema ─────────────────────────────

        [Fact]
        public void Validate_NoSchema_AlwaysReturnsOk()
        {
            GatewayArgsValidator.ClearCache();
            GatewayArgsValidator.PrimeCache("genexus_no_schema_tool", null);

            var result = GatewayArgsValidator.Validate("genexus_no_schema_tool", new JObject { ["anything"] = "goes" });

            Assert.True(result.Ok);
        }

        // ── 6. Pass: no required fields + empty args ──────────────────────────

        [Fact]
        public void Validate_NoRequiredFields_NullArgs_ReturnsOk()
        {
            GatewayArgsValidator.ClearCache();
            GatewayArgsValidator.PrimeCache("genexus_whoami", MakeSchema()); // no required

            var result = GatewayArgsValidator.Validate("genexus_whoami", null);

            Assert.True(result.Ok);
        }

        // ── 7. Fail: additionalProperties: false with unknown field ───────────

        [Fact]
        public void Validate_AdditionalPropertiesFalse_UnknownField_ReturnsViolation()
        {
            GatewayArgsValidator.ClearCache();
            GatewayArgsValidator.PrimeCache("genexus_doctor", MakeSchema(additionalPropertiesFalse: true));

            var result = GatewayArgsValidator.Validate("genexus_doctor", new JObject { ["unknown"] = "x" });

            Assert.False(result.Ok);
            Assert.Contains(result.Violations, v => v.Path == "unknown");
        }

        // ── 8. End-to-end: bad args via ProcessMcpRequest → InvalidArgs envelope ──

        [Fact]
        public async Task ProcessMcpRequest_MissingRequiredArg_ReturnsInvalidArgsEnvelope_WithoutWorker()
        {
            // Prime cache with a schema that requires "name" for genexus_inspect.
            // This bypasses file-loading and ensures isolation from other tests.
            GatewayArgsValidator.ClearCache();
            GatewayArgsValidator.PrimeCache("genexus_inspect", MakeSchema(
                props: new[] { ("name", "string", (string[]?)null) },
                required: new[] { "name" }
            ));

            var request = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = "e2e-test-1",
                ["method"] = "tools/call",
                ["params"] = new JObject
                {
                    ["name"] = "genexus_inspect",
                    // "name" arg intentionally omitted — required by schema
                    ["arguments"] = new JObject { ["include"] = new JArray("metadata") }
                }
            };

            var response = await Program.ProcessMcpRequest(request);

            Assert.NotNull(response);
            // Should be a result (tool envelope), not a JSON-RPC error
            var result = response!["result"] as JObject;
            Assert.NotNull(result);
            var content = result!["content"] as JArray;
            Assert.NotNull(content);
            string text = content![0]?["text"]?.ToString() ?? "";
            var payload = JObject.Parse(text);

            Assert.Equal("error", payload["status"]?.ToString());
            var error = payload["error"] as JObject;
            Assert.NotNull(error);
            Assert.Equal("InvalidArgs", error!["code"]?.ToString());
            Assert.Contains("genexus_inspect", error["message"]?.ToString() ?? "");

            var violations = error["violations"] as JArray;
            Assert.NotNull(violations);
            Assert.Contains(violations!.OfType<JObject>(), v => v["path"]?.ToString() == "name" && v["actual"]?.ToString() == "missing");

            var nextSteps = error["nextSteps"] as JArray;
            Assert.NotNull(nextSteps);
            Assert.Contains(nextSteps!.OfType<JObject>(), s => s["tool"]?.ToString() == "genexus_orient");
        }
    }
}
