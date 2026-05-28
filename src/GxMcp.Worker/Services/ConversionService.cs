using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using Artech.Genexus.Common.Objects;
using Artech.Genexus.Common.Parts;
using GxMcp.Worker.Helpers;
using GxMcp.Worker.Models;
using Newtonsoft.Json.Linq;

namespace GxMcp.Worker.Services
{
    public class ConversionService
    {
        private readonly ObjectService _objectService;

        public ConversionService(ObjectService objectService)
        {
            _objectService = objectService;
        }

        public string TranslateTo(string targetName, string targetLanguage)
        {
            try {
                if (string.IsNullOrWhiteSpace(targetName))
                    return McpResponse.Err(
                        code: "ObjectNameRequired",
                        message: "Object name is required.",
                        hint: "Provide the GeneXus object name to translate.",
                        // no-nextStep: the caller must supply the object name; no tool can infer it
                        target: targetName);

                if (string.IsNullOrWhiteSpace(targetLanguage))
                    return McpResponse.Err(
                        code: "TargetLanguageRequired",
                        message: "Target language is required.",
                        hint: "Provide the destination language, for example 'csharp' or 'typescript'.",
                        nextSteps: new JArray(
                            McpResponse.NextStep(
                                tool: "genexus_inspect",
                                args: new JObject { ["name"] = targetName },
                                why: "Inspect the object first to confirm it exists, then retry with a language.")),
                        target: targetName);

                var obj = _objectService.FindObject(targetName);
                if (obj == null)
                    return McpResponse.Err(
                        code: "ObjectNotFound",
                        message: "Object not found.",
                        hint: "The requested object is not available in the active Knowledge Base.",
                        nextSteps: new JArray(
                            McpResponse.NextStep(
                                tool: "genexus_search",
                                args: new JObject { ["query"] = targetName },
                                why: "Search for similarly named objects to find the correct name.")),
                        target: targetName);

                string code = "";
                if (targetLanguage.Equals("csharp", StringComparison.OrdinalIgnoreCase) || targetLanguage.Equals("cs", StringComparison.OrdinalIgnoreCase))
                {
                    code = TranslateToCSharp(obj);
                }
                else if (targetLanguage.Equals("typescript", StringComparison.OrdinalIgnoreCase) || targetLanguage.Equals("ts", StringComparison.OrdinalIgnoreCase))
                {
                    code = TranslateToTypeScript(obj);
                }
                else
                {
                    return McpResponse.Err(
                        code: "TargetLanguageNotSupported",
                        message: "Target language not supported.",
                        hint: "Supported languages are 'csharp' and 'typescript'.",
                        // no-nextStep: the caller must choose a supported language; no tool can select it for them
                        target: targetName);
                }

                return McpResponse.Ok(
                    target: targetName,
                    code: "TranslationOk",
                    result: new JObject
                    {
                        ["language"] = targetLanguage,
                        ["code"] = code
                    });
            } catch (Exception ex) {
                return McpResponse.Err(
                    code: "TranslationFailed",
                    message: ex.Message,
                    hint: "Verify the object exists and has a supported structure for conversion.",
                    nextSteps: new JArray(
                        McpResponse.NextStep(
                            tool: "genexus_inspect",
                            args: new JObject { ["name"] = targetName },
                            why: "Inspect the object to confirm its type and parts before retrying.")),
                    target: targetName);
            }
        }

        private string TranslateToCSharp(global::Artech.Architecture.Common.Objects.KBObject obj)
        {
            var sb = new StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine();
            sb.AppendLine($"namespace GeneXus.Generated.{obj.Module?.Name ?? "Root"}");
            sb.AppendLine("{");
            if (obj is Transaction trn) {
                sb.AppendLine($"    public class {obj.Name}");
                sb.AppendLine("    {");
                foreach (var attr in trn.Structure.Root.Attributes) {
                    string type = MapGxTypeToCSharp(attr.Attribute.Type.ToString());
                    sb.AppendLine($"        public {type} {attr.Name} {{ get; set; }}");
                }
                sb.AppendLine("    }");
            }
            else if (obj is SDT sdt) {
                sb.AppendLine($"    public class {obj.Name}");
                sb.AppendLine("    {");
                // Recursive SDT mapping simplified
                sb.AppendLine("        // SDT structure conversion...");
                sb.AppendLine("    }");
            }
            else if (obj is Procedure prc) {
                sb.AppendLine($"    public class {obj.Name}Handler");
                sb.AppendLine("    {");
                sb.AppendLine("        public void Execute()");
                sb.AppendLine("        {");
                sb.AppendLine("            // Original Source:");
                var procPart = prc.Parts.Get<ProcedurePart>();
                if (procPart != null) {
                    foreach (var line in procPart.Source.Split('\n')) {
                        sb.AppendLine($"            // {line.Trim()}");
                    }
                }
                sb.AppendLine("        }");
                sb.AppendLine("    }");
            }

            sb.AppendLine("}");
            return sb.ToString();
        }

        private string TranslateToTypeScript(global::Artech.Architecture.Common.Objects.KBObject obj)
        {
            var sb = new StringBuilder();
            if (obj is Transaction || obj is SDT) {
                sb.AppendLine($"export interface {obj.Name} {{");
                sb.AppendLine("}");
            }
            return sb.ToString();
        }

        private string MapGxTypeToCSharp(string gxType)
        {
            if (gxType.Contains("Numeric")) return "decimal";
            if (gxType.Contains("Character") || gxType.Contains("VarChar")) return "string";
            if (gxType.Contains("Date") || gxType.Contains("DateTime")) return "DateTime";
            if (gxType.Contains("Boolean")) return "bool";
            return "object";
        }
    }
}
