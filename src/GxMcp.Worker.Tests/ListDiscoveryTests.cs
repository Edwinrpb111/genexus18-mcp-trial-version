using GxMcp.Worker.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GxMcp.Worker.Tests
{
    // v2.3.8 (Task 2.2): exercises the new ListCriteria + ListService.List
    // overload with the LoadFromEntries fixture seam (no live KB required).
    public class ListDiscoveryTests
    {
        private static JArray ResultsOf(string json)
        {
            return (JArray)JObject.Parse(json)["results"];
        }

        [Fact]
        public void NameFilter_MatchesName_NotDescription()
        {
            var fixture = TestFixtures.IndexWithFolders();
            var svc = new ListService(fixture.Index);
            var json = svc.List(new ListCriteria { NameFilter = "Libera" });
            var hits = ResultsOf(json);
            Assert.Contains(hits, h => h["name"].ToString() == "ComissaoLiberaPareceres");
            Assert.DoesNotContain(hits, h => h["name"].ToString() == "PSPContParecer");
        }

        [Fact]
        public void DescriptionFilter_MatchesDescription_NotName()
        {
            var fixture = TestFixtures.IndexWithFolders();
            var svc = new ListService(fixture.Index);
            var json = svc.List(new ListCriteria { DescriptionFilter = "pareceres" });
            var hits = ResultsOf(json);
            Assert.Contains(hits, h => h["name"].ToString() == "PSPContParecer");
            // "ComissaoLiberaPareceres" has description "Liberar comissões" — no "pareceres" — so it must be excluded.
            Assert.DoesNotContain(hits, h => h["name"].ToString() == "ComissaoLiberaPareceres");
        }

        [Fact]
        public void PathPrefix_ListsFolderChildren()
        {
            var fixture = TestFixtures.IndexWithFolders();
            var svc = new ListService(fixture.Index);
            var json = svc.List(new ListCriteria { PathPrefix = "Root Module/ClickSign/" });
            var hits = ResultsOf(json);
            Assert.NotEmpty(hits);
            Assert.All(hits, h => Assert.StartsWith("Root Module/ClickSign/", h["parentFolderPath"].ToString()));
        }
    }
}
