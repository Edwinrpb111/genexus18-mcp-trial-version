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
        // Shared accessor: an appSetting bool that defaults to true (on) and is also true on
        // any read/parse failure. All the indexing feature flags share these semantics.
        private static bool BoolSetting(string key, bool defaultValue = true)
        {
            try
            {
                var raw = ConfigurationManager.AppSettings[key];
                if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
                return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return defaultValue;
            }
        }

        // SP6.T6 — gate the new lite-pass + lazy-enrichment indexing pipeline.
        // Defaults to true (fast path on). Set Indexing.UseLitePass=false in App.config
        // to fall back to the legacy monolithic BulkIndex path for one release.
        public static bool UseLitePass => BoolSetting("Indexing.UseLitePass");

        // Fase 1 — gate the persistible delta-on-open path. Defaults to true. Set
        // Indexing.UseDeltaOnOpen=false in App.config to fall back to the previous
        // "load cache + trust forever" warm-start behaviour (no automatic delta).
        public static bool UseDeltaOnOpen => BoolSetting("Indexing.UseDeltaOnOpen");

        // Fase 3 — lazy/on-demand enrichment. Defaults to true. When true, the index build
        // stops after the lite catalogue (LiteReady→Ready) and does NOT eagerly drain all
        // objects through enrichment (measured ~20min on a 38.6k KB, ~91% STA-bound SDK reads,
        // and mostly wasted since most objects are never queried). Edges/snippets/embeddings
        // are filled in on demand via EnrichmentQueue.PromoteAsync when a tool needs a target.
        // Set Indexing.LazyEnrichment=false to restore the eager full-KB enrichment drain.
        public static bool LazyEnrichment => BoolSetting("Indexing.LazyEnrichment");
    }
}
