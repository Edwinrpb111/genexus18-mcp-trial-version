using GxMcp.Worker.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace GxMcp.Worker.Tests
{
    public class PlaybookServiceTests
    {
        [Fact]
        public void Read_List_ReturnsAllEmbeddedTopics()
        {
            var svc = new PlaybookService();
            var j = JObject.Parse(svc.Read(topic: null, listOnly: true));

            Assert.Equal("ok", (string)j["status"]);
            var topics = (JArray)j["result"]["topics"];
            Assert.NotNull(topics);
            Assert.Contains("popup_layout", topics.ToObject<string[]>());
            Assert.Contains("wwp_dual_form", topics.ToObject<string[]>());
            Assert.Contains("pattern_reapply", topics.ToObject<string[]>());
        }

        [Fact]
        public void Read_KnownTopic_ReturnsMarkdownBody()
        {
            var svc = new PlaybookService();
            var j = JObject.Parse(svc.Read(topic: "popup_layout", listOnly: false));

            Assert.Equal("ok", (string)j["status"]);
            Assert.Equal("popup_layout", (string)j["result"]["topic"]);
            string body = (string)j["result"]["content"];
            Assert.False(string.IsNullOrWhiteSpace(body));
            Assert.Contains("PatternInstance", body);
            Assert.True((int)j["result"]["bytes"] > 0);
        }

        [Fact]
        public void Read_UnknownTopic_ReturnsErrorWithAvailableList()
        {
            var svc = new PlaybookService();
            var j = JObject.Parse(svc.Read(topic: "does_not_exist", listOnly: false));

            Assert.Equal("error", (string)j["status"]);
            Assert.Equal("PlaybookTopicNotFound", (string)j["error"]["code"]);
            Assert.NotNull(j["error"]["hint"]);
        }

        [Fact]
        public void Read_EmptyTopic_TreatedAsList()
        {
            var svc = new PlaybookService();
            var j = JObject.Parse(svc.Read(topic: "", listOnly: false));

            Assert.Equal("ok", (string)j["status"]);
            Assert.NotNull(j["result"]["topics"]);
        }
    }
}
