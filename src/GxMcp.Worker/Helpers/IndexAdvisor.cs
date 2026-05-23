using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace GxMcp.Worker.Helpers
{
    // Wave-3 item 44: read-only index advisor for navigation SQL output.
    //
    // Heuristic: for each emitted query, collect the attributes that appear on the
    // left side of WHERE clauses. Compare against the set of leading-column lists
    // of existing indexes on the same base table. If a query's where-attributes
    // are NOT a prefix of any existing index, suggest a new composite index on
    // those columns in order.
    //
    // No DDL is emitted; the advisor is purely informational. The caller decides
    // whether to surface or ignore the suggestion.
    public static class IndexAdvisor
    {
        // existingIndexes shape: { tableName: [ { name, columns: [..] } ] }
        // queries shape: navigation queries array (queries[].baseTable + .sql + .filters?)
        // Returns the `indexAdvisor` envelope: { tables: [{ table, suggestedIndex, currentIndexes }] }
        public static JObject BuildAdvisor(JArray queries, IDictionary<string, JArray> existingIndexes)
        {
            var result = new JObject();
            var tables = new JArray();
            if (queries == null) { result["tables"] = tables; return result; }
            existingIndexes = existingIndexes ?? new Dictionary<string, JArray>(StringComparer.OrdinalIgnoreCase);

            // group queries by base table; collect distinct where-attribute lists per table
            var byTable = new Dictionary<string, List<List<string>>>(StringComparer.OrdinalIgnoreCase);
            foreach (var q in queries)
            {
                if (!(q is JObject qo)) continue;
                string baseTable = (string)qo["baseTable"];
                if (string.IsNullOrEmpty(baseTable)) continue;
                var attrs = ExtractWhereAttributes(qo);
                if (attrs.Count == 0) continue;
                if (!byTable.ContainsKey(baseTable)) byTable[baseTable] = new List<List<string>>();
                if (!byTable[baseTable].Any(existing => existing.SequenceEqual(attrs, StringComparer.OrdinalIgnoreCase)))
                    byTable[baseTable].Add(attrs);
            }

            foreach (var kv in byTable)
            {
                JArray current = existingIndexes.TryGetValue(kv.Key, out var ix) ? ix : new JArray();
                foreach (var whereAttrs in kv.Value)
                {
                    if (IsCoveredByExistingIndex(whereAttrs, current)) continue;
                    var entry = new JObject();
                    entry["table"] = kv.Key;
                    var suggested = new JObject();
                    suggested["columns"] = new JArray(whereAttrs.ToArray());
                    suggested["reason"] = "WHERE-attribute combo not covered by a leading-column prefix of any existing index.";
                    entry["suggestedIndex"] = suggested;
                    entry["currentIndexes"] = current;
                    tables.Add(entry);
                }
            }

            result["tables"] = tables;
            return result;
        }

        private static List<string> ExtractWhereAttributes(JObject query)
        {
            var attrs = new List<string>();
            // Preferred: structured `filters` array if surfaced.
            if (query["filters"] is JArray filters)
            {
                foreach (var f in filters)
                {
                    string a = (string)f["attribute"];
                    if (!string.IsNullOrWhiteSpace(a) && !attrs.Contains(a, StringComparer.OrdinalIgnoreCase))
                        attrs.Add(a);
                }
                if (attrs.Count > 0) return attrs;
            }
            // Fallback: parse the SQL string after the first WHERE.
            string sql = (string)query["sql"];
            if (string.IsNullOrWhiteSpace(sql)) return attrs;
            int idx = sql.IndexOf(" WHERE ", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return attrs;
            string where = sql.Substring(idx + 7);
            int orderIdx = where.IndexOf(" ORDER BY ", StringComparison.OrdinalIgnoreCase);
            if (orderIdx >= 0) where = where.Substring(0, orderIdx);
            // tokens of shape `Attr op …` separated by AND
            foreach (var clause in where.Split(new[] { " AND " }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = clause.Trim().TrimStart('(').TrimEnd(')');
                var parts = trimmed.Split(new[] { ' ', '=', '<', '>', '!' }, 2);
                if (parts.Length > 0)
                {
                    string a = parts[0].Trim();
                    if (a.Length > 0 && !attrs.Contains(a, StringComparer.OrdinalIgnoreCase))
                        attrs.Add(a);
                }
            }
            return attrs;
        }

        private static bool IsCoveredByExistingIndex(List<string> whereAttrs, JArray currentIndexes)
        {
            if (currentIndexes == null || currentIndexes.Count == 0) return false;
            foreach (var ix in currentIndexes)
            {
                var cols = ix["columns"] as JArray;
                if (cols == null || cols.Count < whereAttrs.Count) continue;
                bool prefixMatch = true;
                for (int i = 0; i < whereAttrs.Count; i++)
                {
                    string c = (string)cols[i];
                    if (!string.Equals(c, whereAttrs[i], StringComparison.OrdinalIgnoreCase))
                    {
                        prefixMatch = false; break;
                    }
                }
                if (prefixMatch) return true;
            }
            return false;
        }
    }
}
