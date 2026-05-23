using System;
using Newtonsoft.Json.Linq;

namespace GxMcp.Worker.Services
{
    /// <summary>
    /// Wave-3 items 5 + 37: post-edit hook that merges a <c>visualVerify</c>
    /// envelope into the JSON response of <c>genexus_edit</c> /
    /// <c>genexus_edit_and_build</c> when the caller opts in with
    /// <c>visualVerify=true</c>.
    ///
    /// Factored out of <see cref="CommandDispatcher"/> so the wiring is unit
    /// testable without spinning the whole singleton. NEVER throws — on any
    /// failure the original response string is returned verbatim so the edit
    /// result is never lost.
    /// </summary>
    public static class VisualVerifyResponseHook
    {
        /// <summary>
        /// Returns the (possibly modified) response JSON string. When
        /// <paramref name="args"/> has <c>visualVerify=true</c>, runs
        /// <paramref name="verify"/> for the (name, part) pair and stitches
        /// the JObject envelope into the response under a top-level
        /// <c>visualVerify</c> key. When <paramref name="args"/> opts out
        /// (or the response isn't JSON-parseable) the original string is
        /// returned unchanged.
        /// </summary>
        public static string MaybeAttach(JObject args, string responseJson, VisualVerifyService verify)
        {
            try
            {
                if (args == null || verify == null) return responseJson;
                bool requested = args["visualVerify"]?.ToObject<bool?>() ?? false;
                if (!requested) return responseJson;
                if (string.IsNullOrEmpty(responseJson)) return responseJson;

                JObject parsed;
                try { parsed = JObject.Parse(responseJson); }
                catch { return responseJson; }

                string name = args["name"]?.ToString() ?? args["target"]?.ToString();
                string part = args["part"]?.ToString();

                JObject envelope;
                try
                {
                    envelope = verify.VerifyAsJObject(name, part);
                }
                catch (Exception ex)
                {
                    envelope = new JObject
                    {
                        ["skipped"] = true,
                        ["reason"] = "VerifyError",
                        ["error"] = ex.Message
                    };
                }

                parsed["visualVerify"] = envelope ?? new JObject
                {
                    ["skipped"] = true,
                    ["reason"] = "NullEnvelope"
                };
                return parsed.ToString(Newtonsoft.Json.Formatting.None);
            }
            catch
            {
                return responseJson;
            }
        }
    }
}
