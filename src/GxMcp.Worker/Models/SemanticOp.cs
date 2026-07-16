using System;
using Newtonsoft.Json.Linq;

namespace GxMcp.Worker.Models
{
    public sealed class SemanticOp
    {
        public string Op { get; set; } = "";
        public JObject Args { get; set; } = new JObject();

        public static SemanticOp From(JObject raw)
        {
            string op = raw["op"]?.ToString();
            if (string.IsNullOrEmpty(op))
                throw new ArgumentException("op required");
            JObject args = (JObject)raw.DeepClone();
            args.Remove("op");
            // issue #34: the tool schema documents each op as { op, args: { name, type, ... } },
            // but historically the handlers read the arg fields flat off the op object. Accept
            // BOTH: if a nested "args" object is present, hoist its members up to the top level
            // (flat fields still win on a key clash, preserving the legacy shape's behavior).
            if (args["args"] is JObject nested)
            {
                args.Remove("args");
                foreach (var prop in nested.Properties())
                {
                    if (args[prop.Name] == null)
                        args[prop.Name] = prop.Value;
                }
            }
            return new SemanticOp { Op = op, Args = args };
        }
    }
}
