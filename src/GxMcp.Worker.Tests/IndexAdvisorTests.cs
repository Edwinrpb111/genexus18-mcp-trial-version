using System.Collections.Generic;
using GxMcp.Worker.Helpers;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GxMcp.Worker.Tests
{
    // Wave-3 item 44: IndexAdvisor heuristic — given the navigation query array
    // shape emitted by NavigationSqlService, ensure we suggest exactly when the
    // WHERE-attribute set isn't covered by a leading-column prefix of any
    // existing index.
    public class IndexAdvisorTests
    {
        [Fact]
        public void Suggests_WhenNoExistingIndexes()
        {
            var queries = new JArray {
                new JObject {
                    ["baseTable"] = "Aluno",
                    ["sql"] = "SELECT * FROM Aluno WHERE AluNome = :p1",
                    ["filters"] = new JArray { new JObject { ["attribute"] = "AluNome", ["op"] = "=" } }
                }
            };
            var existing = new Dictionary<string, JArray>();
            var advisor = IndexAdvisor.BuildAdvisor(queries, existing);
            var tables = (JArray)advisor["tables"];
            Assert.Single(tables);
            var entry = (JObject)tables[0];
            Assert.Equal("Aluno", (string)entry["table"]);
            var cols = (JArray)entry["suggestedIndex"]["columns"];
            Assert.Single(cols);
            Assert.Equal("AluNome", (string)cols[0]);
        }

        [Fact]
        public void NoSuggestion_WhenLeadingPrefixCoversWhere()
        {
            var queries = new JArray {
                new JObject {
                    ["baseTable"] = "Aluno",
                    ["sql"] = "SELECT * FROM Aluno WHERE AluCod = :p1",
                    ["filters"] = new JArray { new JObject { ["attribute"] = "AluCod", ["op"] = "=" } }
                }
            };
            var existing = new Dictionary<string, JArray> {
                ["Aluno"] = new JArray { new JObject {
                    ["name"] = "IALUNO",
                    ["columns"] = new JArray { "AluCod" }
                }}
            };
            var advisor = IndexAdvisor.BuildAdvisor(queries, existing);
            Assert.Empty((JArray)advisor["tables"]);
        }

        [Fact]
        public void ExtractsAttributesFromSqlWhenFiltersMissing()
        {
            var queries = new JArray {
                new JObject {
                    ["baseTable"] = "Aluno",
                    ["sql"] = "SELECT * FROM Aluno WHERE AluCidade = ? AND AluActive = ?"
                }
            };
            var advisor = IndexAdvisor.BuildAdvisor(queries, new Dictionary<string, JArray>());
            var tables = (JArray)advisor["tables"];
            Assert.Single(tables);
            var cols = (JArray)tables[0]["suggestedIndex"]["columns"];
            Assert.Equal(2, cols.Count);
            Assert.Equal("AluCidade", (string)cols[0]);
            Assert.Equal("AluActive", (string)cols[1]);
        }

        [Fact]
        public void EmptyQueries_EmptyTables()
        {
            var advisor = IndexAdvisor.BuildAdvisor(new JArray(), new Dictionary<string, JArray>());
            Assert.Empty((JArray)advisor["tables"]);
        }
    }
}
