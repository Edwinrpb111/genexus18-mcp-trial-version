using System;
using Newtonsoft.Json.Linq;

namespace GxMcp.Worker.Helpers
{
    // Wave-3 item 34: derive an EXPLAIN-style preview for navigation SQL queries.
    //
    // Live DB connections are out of scope for the Worker (it runs STA-bound and
    // hosts only the GeneXus SDK; no JDBC/ADO connection is brokered here). To
    // keep the contract honest we *never* execute against a database — instead we
    // surface the canonical EXPLAIN syntax for the target DBMS so downstream
    // tooling can lift the string into a real client, and we annotate every query
    // with `planUnavailable: true`.
    public static class ExecutionPlanFetcher
    {
        public static string ResolveDbmsFamily(int dbmsType)
        {
            // GeneXus eDBMS values (subset): 0=None, 1=SqlServer, 2=Db2, 3=Informix,
            // 4=Oracle (legacy), 5=MySQL, 6=PostgreSQL, 7=Oracle, 8=Db2/AS400,
            // 9=Db2Universal, 10=SAPHana, 11=DynamoDB.
            switch (dbmsType)
            {
                case 1: return "sqlserver";
                case 2: case 8: case 9: return "db2";
                case 3: return "informix";
                case 4: case 7: return "oracle";
                case 5: return "mysql";
                case 6: return "postgres";
                case 10: return "saphana";
                default: return "unknown";
            }
        }

        public static string BuildExplainSyntax(string family, string sql)
        {
            if (string.IsNullOrWhiteSpace(sql)) return null;
            switch (family)
            {
                case "oracle":
                    return "EXPLAIN PLAN FOR " + sql;
                case "sqlserver":
                    // SQL Server uses SET SHOWPLAN_XML / SET STATISTICS PROFILE; the
                    // SET form is connection-scoped, so we keep it for parity with
                    // what an operator would paste into SSMS.
                    return "SET SHOWPLAN_XML ON; " + sql;
                case "postgres":
                case "mysql":
                case "saphana":
                case "db2":
                case "informix":
                    return "EXPLAIN " + sql;
                default:
                    return "EXPLAIN " + sql;
            }
        }

        public static JObject BuildUnavailablePlan(string family, string sql, string reason)
        {
            var plan = new JObject();
            plan["planUnavailable"] = true;
            plan["reason"] = reason ?? "no-live-db-connection";
            plan["dbmsFamily"] = family ?? "unknown";
            var explainSyntax = BuildExplainSyntax(family, sql);
            if (!string.IsNullOrEmpty(explainSyntax)) plan["explainSyntax"] = explainSyntax;
            return plan;
        }

        // Attach a per-query `executionPlan` object onto the queries array shape
        // emitted by NavigationSqlService.Generate (queries[].sql). Idempotent.
        public static void AttachExecutionPlans(JArray queries, int dbmsType)
        {
            if (queries == null) return;
            string family = ResolveDbmsFamily(dbmsType);
            foreach (var q in queries)
            {
                if (!(q is JObject qo)) continue;
                if (qo["executionPlan"] != null) continue;
                string sql = (string)qo["sql"];
                qo["executionPlan"] = BuildUnavailablePlan(family, sql, "no-live-db-connection");
            }
        }
    }
}
