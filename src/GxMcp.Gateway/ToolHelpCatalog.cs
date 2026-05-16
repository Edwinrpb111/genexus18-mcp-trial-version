using System.Collections.Generic;

namespace GxMcp.Gateway
{
    internal static class ToolHelpCatalog
    {
        private static readonly Dictionary<string, string> _helpTexts = new(System.StringComparer.OrdinalIgnoreCase)
        {
            ["genexus_query"] =
                "# genexus_query\n\n" +
                "Search objects in the active Knowledge Base.\n\n" +
                "## Query prefixes\n" +
                "- `usedby:<name>` — objects that reference <name>\n" +
                "- `type:<ObjectType>` — filter by Transaction, Procedure, WebPanel, etc.\n" +
                "- `description:<text>` — search inside object descriptions\n" +
                "- `parent:<folder>` — filter by direct parent folder\n" +
                "- `parentPath:<a/b/c>` — filter by full folder path\n\n" +
                "## Index behaviour\n" +
                "- The first call on a fresh install triggers the KB index build.\n" +
                "- `_meta.partial=true` means more results are still being indexed.\n" +
                "- Literal-name queries (no prefix) skip the index entirely.\n" +
                "- `genexus_read`, `genexus_edit`, `genexus_list_objects`, and `genexus_lifecycle` are index-independent.\n\n" +
                "## Defaults\n" +
                "- `axiCompact: true` — pass `false` to get the full payload.\n" +
                "- `limit: 50`, `offset: 0`.\n\n" +
                "## Examples\n" +
                "- `{ query: 'type:Procedure', limit: 20 }`\n" +
                "- `{ query: 'usedby:InvoiceProc' }`\n" +
                "- `{ query: 'OrderTrn', fields: 'name,type,path,description' }`\n",

            ["genexus_lifecycle"] =
                "# genexus_lifecycle\n\n" +
                "Build, validate, index, or poll the active Knowledge Base.\n\n" +
                "## Actions\n" +
                "- `build` — non-blocking when `estimated_seconds >= 20`; returns `{ job_id, status: 'running' }` and surfaces `_meta.background_jobs` on the next call.\n" +
                "- `validate` — runs the GeneXus specifier; same async pattern as build.\n" +
                "- `index` — rebuilds the search index. Pass `force=true` to ignore the on-disk cache.\n" +
                "- `status` — accepts either a `taskId` or `job_id` via `target`; pass `wait_seconds > 0` to long-poll up to 25s.\n" +
                "- `result` — fetch the completion payload of a finished operation.\n" +
                "- `stop-worker` — gracefully recycle the worker process for the active KB.\n\n" +
                "## target format\n" +
                "- Build/validate: object name(s), comma- or semicolon-separated.\n" +
                "- Status/result on a background op: `op:<operationId>` or just `<job_id>`.\n\n" +
                "## Examples\n" +
                "- `{ action: 'build', target: 'InvoiceProc' }`\n" +
                "- `{ action: 'status', target: 'op:abc123', wait_seconds: 25 }`\n" +
                "- `{ action: 'index', force: true }`\n",

            ["genexus_edit"] =
                "# genexus_edit\n\n" +
                "Edit the source or metadata of a GeneXus object.\n\n" +
                "## Required\n" +
                "- Either `name` (single object) **or** `targets` (array) — never both.\n" +
                "- `mode`: `full` (replace whole part) or `patch` (apply unified diff).\n" +
                "- `dryRun: true` with `mode: 'patch'` first to preview without persisting.\n\n" +
                "## Output\n" +
                "- Returns `post_state.diff` (unified diff) by default.\n" +
                "- `verbose: true` adds slices with ±15 lines of context.\n" +
                "- `return_post_state: false` opts out of the post-state block to save tokens.\n\n" +
                "## Disambiguation\n" +
                "If `name` matches multiple objects, the error includes `suggestion` and `availableTypes`. Pass `type=<ObjectType>` or use `parentPath` to disambiguate.\n\n" +
                "## Examples\n" +
                "- `{ name: 'InvoiceProc', part: 'Source', mode: 'patch', content: '<diff>', dryRun: true }`\n" +
                "- `{ name: 'OrderTrn', part: 'Rules', mode: 'full', content: '<rules text>' }`\n",

            ["genexus_analyze"] =
                "# genexus_analyze\n\n" +
                "Semantic analysis across one or more objects.\n\n" +
                "## Modes\n" +
                "- `impact` — callers, callees, blast radius, risk level, affected entry points.\n" +
                "- `dependencies` — typed dependency graph.\n" +
                "- `complexity` — line/cyclomatic counts.\n" +
                "- `naming` — naming-convention audit.\n" +
                "- `summary` — LLM-oriented summary of the object.\n" +
                "- `explain` — natural-language explanation of a slice of source.\n\n" +
                "## When to use what\n" +
                "- Raw source: `genexus_read`.\n" +
                "- Single-object metadata: `genexus_inspect`.\n" +
                "- Cross-object reasoning: `genexus_analyze`.\n\n" +
                "## Notes\n" +
                "- `impact` waits up to 30s for the index to be ready unless `waitForIndex: false`.\n" +
                "- Returns `callersTruncated: true` and `_meta.partial` when the graph is incomplete.\n\n" +
                "## Examples\n" +
                "- `{ mode: 'impact', target: 'InvoiceProc' }`\n" +
                "- `{ mode: 'summary', target: 'OrderTrn' }`\n",

            ["genexus_read"] =
                "# genexus_read\n\n" +
                "Read source or metadata parts of one or more GeneXus objects.\n\n" +
                "## Required\n" +
                "- Either `name` (single) **or** `targets` (array). Never both.\n" +
                "- `parts`: array of part names. Common: `Source`, `Variables`, `Rules`, `Events`, `Structure`, `Layout`. Omitting `parts` returns the canonical default set for the object type.\n\n" +
                "## Pagination\n" +
                "- `offset` and `limit` apply to the **source** part for large objects.\n" +
                "- `_meta.partial: true` and `_meta.nextOffset` signal more content available.\n\n" +
                "## Examples\n" +
                "- `{ name: 'InvoiceProc', parts: ['Source', 'Variables'] }`\n" +
                "- `{ name: 'OrderTrn', parts: ['Rules'], offset: 0, limit: 200 }`\n" +
                "- `{ targets: [{ name: 'A' }, { name: 'B' }], parts: ['Source'] }`\n"
        };

        internal static string? Get(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName)) return null;
            return _helpTexts.TryGetValue(toolName, out var text) ? text : null;
        }

        internal static System.Collections.Generic.IReadOnlyCollection<string> KnownTools => _helpTexts.Keys;
    }
}
