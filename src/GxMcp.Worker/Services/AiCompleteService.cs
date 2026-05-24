using System;
using System.IO;
using System.Net;
using System.Text;
using Newtonsoft.Json.Linq;

namespace GxMcp.Worker.Services
{
    /// <summary>
    /// Item 81 — AI-prompted code completion. Forwards the supplied <c>context</c> to a
    /// configurable OpenAI-compatible chat completion endpoint and returns the model's
    /// reply text. Endpoint + key live in env vars (<c>GXMCP_AI_COMPLETE_URL</c>,
    /// <c>GXMCP_AI_COMPLETE_KEY</c>); when unset we return a typed
    /// <c>{ code:"AiEndpointNotConfigured" }</c> envelope rather than a Future stub so the
    /// agent gets actionable wiring guidance.
    /// </summary>
    public class AiCompleteService
    {
        /// <summary>Test seam. The default implementation uses WebRequest (net48-friendly).</summary>
        public interface IAiHttpClient
        {
            /// <summary>POST <paramref name="bodyJson"/> as application/json to <paramref name="url"/>
            /// with bearer <paramref name="apiKey"/>. Returns the raw response body. Throws on
            /// transport error; non-2xx responses are returned with their body via the
            /// out parameter <paramref name="statusCode"/> rather than throwing.</summary>
            string Post(string url, string apiKey, string bodyJson, out int statusCode);
        }

        private readonly IAiHttpClient _http;
        private readonly Func<string, string> _envLookup;

        public AiCompleteService(IAiHttpClient http = null, Func<string, string> envLookup = null)
        {
            _http = http ?? new DefaultHttpClient();
            _envLookup = envLookup ?? Environment.GetEnvironmentVariable;
        }

        public JObject Complete(string name, string part, string context, int maxTokens)
        {
            if (maxTokens <= 0) maxTokens = 200;
            if (maxTokens > 4000) maxTokens = 4000;

            string url = _envLookup("GXMCP_AI_COMPLETE_URL");
            string key = _envLookup("GXMCP_AI_COMPLETE_KEY");
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
            {
                return new JObject
                {
                    ["code"] = "AiEndpointNotConfigured",
                    ["hint"] = "Set GXMCP_AI_COMPLETE_URL and GXMCP_AI_COMPLETE_KEY env vars (OpenAI-compatible endpoint)."
                };
            }

            if (string.IsNullOrWhiteSpace(context))
            {
                return new JObject
                {
                    ["code"] = "InvalidRequest",
                    ["hint"] = "context is required (the prompt/code snippet to complete)."
                };
            }

            string model = _envLookup("GXMCP_AI_COMPLETE_MODEL");
            if (string.IsNullOrWhiteSpace(model)) model = "gpt-4o-mini";

            var body = new JObject
            {
                ["model"] = model,
                ["max_tokens"] = maxTokens,
                ["messages"] = new JArray(
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = context
                    })
            };

            int statusCode;
            string respText;
            try
            {
                respText = _http.Post(url, key, body.ToString(Newtonsoft.Json.Formatting.None), out statusCode);
            }
            catch (Exception ex)
            {
                return new JObject
                {
                    ["code"] = "AiEndpointUnreachable",
                    ["error"] = ex.Message,
                    ["hint"] = "Endpoint configured but request failed at the transport layer."
                };
            }

            if (statusCode < 200 || statusCode >= 300)
            {
                return new JObject
                {
                    ["code"] = "AiEndpointError",
                    ["statusCode"] = statusCode,
                    ["body"] = TruncateForEnvelope(respText),
                    ["hint"] = "Endpoint returned a non-2xx response."
                };
            }

            // Parse OpenAI-compatible response: choices[0].message.content + usage.{prompt_tokens, completion_tokens}.
            try
            {
                var parsed = JObject.Parse(respText ?? "{}");
                string completion = parsed["choices"]?[0]?["message"]?["content"]?.ToString() ?? string.Empty;
                int tokensIn = parsed["usage"]?["prompt_tokens"]?.ToObject<int?>() ?? 0;
                int tokensOut = parsed["usage"]?["completion_tokens"]?.ToObject<int?>() ?? 0;
                string respModel = parsed["model"]?.ToString() ?? model;
                return new JObject
                {
                    ["status"] = "Success",
                    ["name"] = name ?? string.Empty,
                    ["part"] = part ?? string.Empty,
                    ["completion"] = completion,
                    ["tokensIn"] = tokensIn,
                    ["tokensOut"] = tokensOut,
                    ["model"] = respModel
                };
            }
            catch (Exception ex)
            {
                return new JObject
                {
                    ["code"] = "AiResponseUnparseable",
                    ["error"] = ex.Message,
                    ["body"] = TruncateForEnvelope(respText)
                };
            }
        }

        private static string TruncateForEnvelope(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Length <= 512 ? s : s.Substring(0, 512) + "...[truncated]";
        }

        /// <summary>net48-safe HTTP POST using HttpWebRequest. Production seam — tests
        /// inject an in-memory fake via <see cref="IAiHttpClient"/>.</summary>
        private class DefaultHttpClient : IAiHttpClient
        {
            public string Post(string url, string apiKey, string bodyJson, out int statusCode)
            {
                statusCode = 0;
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "POST";
                req.ContentType = "application/json";
                req.Headers["Authorization"] = "Bearer " + apiKey;
                req.Timeout = 60000;
                req.ReadWriteTimeout = 60000;
                byte[] bytes = Encoding.UTF8.GetBytes(bodyJson ?? "{}");
                req.ContentLength = bytes.Length;
                using (var rs = req.GetRequestStream())
                {
                    rs.Write(bytes, 0, bytes.Length);
                }

                try
                {
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    {
                        statusCode = (int)resp.StatusCode;
                        using (var sr = new StreamReader(resp.GetResponseStream() ?? new MemoryStream(), Encoding.UTF8))
                        {
                            return sr.ReadToEnd();
                        }
                    }
                }
                catch (WebException wex)
                {
                    var resp = wex.Response as HttpWebResponse;
                    if (resp != null)
                    {
                        statusCode = (int)resp.StatusCode;
                        using (var sr = new StreamReader(resp.GetResponseStream() ?? new MemoryStream(), Encoding.UTF8))
                        {
                            return sr.ReadToEnd();
                        }
                    }
                    throw;
                }
            }
        }
    }
}
