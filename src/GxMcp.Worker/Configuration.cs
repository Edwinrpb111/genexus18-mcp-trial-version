using System;
using System.Configuration;

namespace GxMcp.Worker
{
    /// <summary>
    /// Centralised typed accessors over App.config &lt;appSettings&gt;. Keep this small —
    /// only host flags that gate worker behaviour at runtime (perf knobs, feature flags).
    /// </summary>
    public static class Configuration
    {
        // SP6.T6 — gate the new lite-pass + lazy-enrichment indexing pipeline.
        // Defaults to true (fast path on). Set Indexing.UseLitePass=false in App.config
        // to fall back to the legacy monolithic BulkIndex path for one release.
        public static bool UseLitePass
        {
            get
            {
                try
                {
                    var raw = ConfigurationManager.AppSettings["Indexing.UseLitePass"];
                    return string.IsNullOrWhiteSpace(raw)
                        || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return true;
                }
            }
        }
    }
}
