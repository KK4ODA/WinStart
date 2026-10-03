using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WinStart.Core;
using Xunit;

namespace WinStart.Tests
{
    public class DelayFormatTests
    {
        [Theory]
        [InlineData(0, "No delay")]
        [InlineData(-5, "No delay")]
        [InlineData(10, "10 sec")]
        [InlineData(60, "1 min")]
        [InlineData(90, "1 min 30 sec")]
        [InlineData(300, "5 min")]
        [InlineData(3600, "1 hr")]
        [InlineData(3725, "1 hr 2 min 5 sec")]
        public void Describes_delays(int seconds, string expected)
        {
            Assert.Equal(expected, DelayFormat.Describe(seconds));
        }

        [Theory]
        [InlineData(0, "0:00")]
        [InlineData(5, "0:05")]
        [InlineData(65, "1:05")]
        [InlineData(600, "10:00")]
        [InlineData(3723, "1:02:03")]
        [InlineData(-1, "0:00")]
        public void Formats_countdowns(int seconds, string expected)
        {
            Assert.Equal(expected, DelayFormat.Countdown(seconds));
        }
    }

    public class JsonTests
    {
        [Fact]
        public void Round_trips_values_through_text()
        {
            var original = new Dictionary<string, object?>
            {
                ["text"] = "quote \" backslash \\ slash / tab \t newline \n unicode é ✓ control \u0001",
                ["number"] = 42,
                ["big"] = 5000000000L,
                ["fraction"] = 1.5,
                ["yes"] = true,
                ["no"] = false,
                ["nothing"] = null,
                ["list"] = new List<object?> { 1, "two", null },
                ["nested"] = new Dictionary<string, object?> { ["empty"] = new List<object?>() },
            };

            foreach (bool indented in new[] { true, false })
            {
                IDictionary<string, object?> parsed = Json.ParseObject(Json.Write(original, indented));
                Assert.Equal(original["text"], parsed.GetString("text"));
                Assert.Equal(42, parsed.GetInt("number", 0));
                Assert.Equal(5000000000L, parsed.GetLong("big", 0));
                Assert.True(parsed.GetBool("yes", false));
                Assert.False(parsed.GetBool("no", true));
                Assert.Null(parsed["nothing"]);
                Assert.Equal(3, parsed.GetArray("list").Count());
                Assert.Empty(parsed.GetObject("nested")!.GetArray("empty"));
            }
        }

        [Fact]
        public void Compact_output_is_a_single_line()
        {
            string json = Json.Write(new Dictionary<string, object?> { ["a"] = 1, ["b"] = "x" }, indented: false);
            Assert.Equal("{\"a\":1,\"b\":\"x\"}", json);
        }

        [Fact]
        public void Accessors_fall_back_instead_of_throwing()
        {
            IDictionary<string, object?> obj = Json.ParseObject("{ \"s\": 5, \"n\": \"text\", \"b\": 1 }");
            Assert.Equal("fallback", obj.GetString("s", "fallback"));
            Assert.Equal(7, obj.GetInt("n", 7));
            Assert.True(obj.GetBool("b", true));
            Assert.Equal("", obj.GetString("missing"));
            Assert.Null(obj.GetObject("missing"));
            Assert.Empty(obj.GetArray("missing"));
        }

        [Fact]
        public void A_root_that_is_not_an_object_is_rejected()
        {
            Assert.Throws<FormatException>(() => Json.ParseObject("[1, 2, 3]"));
        }
    }

    public class HistoryTests
    {
        [Fact]
        public void Records_are_read_back_in_order()
        {
            var history = new History(Path.Combine(TestEnvironment.NewFolder(), "history.jsonl"));
            DateTime before = DateTime.Now.AddSeconds(-2);

            history.Add("WSJT-X", History.Launched, "After 30 sec");
            history.Add("Chrome", History.Failed, "File not found: C:\\x \"y\"");

            List<HistoryRecord> records = history.Read();
            Assert.Equal(2, records.Count);
            Assert.Equal("WSJT-X", records[0].App);
            Assert.Equal(History.Launched, records[0].Result);
            Assert.Equal("After 30 sec", records[0].Detail);
            Assert.Equal("File not found: C:\\x \"y\"", records[1].Detail);
            Assert.InRange(records[0].Time, before, DateTime.Now.AddSeconds(2));
        }

        [Fact]
        public void Damaged_lines_are_skipped()
        {
            string path = Path.Combine(TestEnvironment.NewFolder(), "history.jsonl");
            var history = new History(path);
            history.Add("One", History.Launched);
            File.AppendAllText(path, "this line is not json\r\n\r\n{\"app\":\"no time\"}\r\n");
            history.Add("Two", History.Skipped, "Already running");

            Assert.Equal(new[] { "One", "Two" }, history.Read().Select(r => r.App).ToArray());
        }

        [Fact]
        public void Trim_keeps_the_newest_records()
        {
            var history = new History(Path.Combine(TestEnvironment.NewFolder(), "history.jsonl"));
            for (int i = 1; i <= 10; i++) history.Add("App " + i, History.Launched);

            history.Trim(keep: 3);

            Assert.Equal(new[] { "App 8", "App 9", "App 10" }, history.Read().Select(r => r.App).ToArray());
        }

        [Fact]
        public void Clear_removes_everything_and_missing_files_are_fine()
        {
            var history = new History(Path.Combine(TestEnvironment.NewFolder(), "history.jsonl"));
            Assert.Empty(history.Read());
            history.Add("App", History.Launched);
            history.Clear();
            Assert.Empty(history.Read());
            history.Trim();
            history.Clear();
        }
    }
}
