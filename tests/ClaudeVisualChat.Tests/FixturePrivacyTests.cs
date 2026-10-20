using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace ClaudeVisualChat.Tests
{
    public class FixturePrivacyTests
    {
        /// <summary>
        /// The prefix of the claude.ai connectors (an MCP server of whoever recorded). Each one's own words (user name,
        /// company, clients, project folders) go in <c>tests/private-words.txt</c>, one per line, which git ignores:
        /// they are checked too when the file exists.
        /// </summary>
        private static readonly string[] Forbidden = { "claude_ai_" };

        private const string PrivateWordsFile = "private-words.txt";

        /// <summary>A user folder other than demo's: <c>Users\x</c>, <c>/Users/x</c>, Claude's <c>Users-x</c>, <c>/home/x</c>.</summary>
        private static readonly Regex OtherUserFolder =
            new Regex(@"(users[\\/-]+|home[\\/]+)(?!demo\b)\w", RegexOptions.IgnoreCase);

        private static IEnumerable<string> ForbiddenWords()
        {
            var words = new List<string>(Forbidden);
            var file = Path.Combine(AppContext.BaseDirectory, PrivateWordsFile);
            if (File.Exists(file))
            {
                foreach (var line in File.ReadAllLines(file))
                {
                    var word = line.Trim();
                    if (word.Length > 0 && !word.StartsWith("#", StringComparison.Ordinal)) words.Add(word);
                }
            }
            return words;
        }

        [Fact]
        public void The_user_folder_pattern_finds_other_users_and_spares_demo()
        {
            foreach (var text in new[] { @"C:\\Users\\alice\\x", "C:/Users/alice", "C--Users-alice-AppData", "/home/alice/x" })
                Assert.True(OtherUserFolder.IsMatch(text), text);
            foreach (var text in new[] { @"C:\\Users\\demo\\x", "C--Users-demo-AppData", "/home/demo", "a user, at home" })
                Assert.False(OtherUserFolder.IsMatch(text), text);
        }

        [Fact]
        public void Fixtures_have_no_private_data()
        {
            var files = Directory.GetFiles(FixtureFiles.Directory);
            Assert.NotEmpty(files);
            var forbidden = ForbiddenWords().ToList();
            foreach (var file in files)
            {
                var text = File.ReadAllText(file);
                foreach (var word in forbidden)
                {
                    Assert.False(
                        text.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0,
                        Path.GetFileName(file) + " contains '" + word + "'");
                }

                var match = OtherUserFolder.Match(text);
                Assert.False(match.Success, Path.GetFileName(file) + " has a user folder other than 'demo'");
            }
        }

        [Fact]
        public void FixtureFiles_cover_all_scenarios()
        {
            Assert.Equal(
                new[] { "edit", "bash", "subagent", "error", "denied", "interrupted", "mode-and-model" },
                FixtureFiles.Scenarios);
            foreach (var scenario in FixtureFiles.Scenarios)
            {
                AssertValidJsonLines(scenario + ".stream", FixtureFiles.StreamLines(scenario));
                AssertValidJsonLines(scenario + ".transcript", FixtureFiles.TranscriptLines(scenario));
            }
        }

        /// <summary>Re-assembles the streamed deltas of each content block (parent, message, index).</summary>
        private static List<(string ToolUseId, string Name, string Json)> ReassembleBlocks(
            string[] lines, string scenario)
        {
            var result = new List<(string, string, string)>();
            var open = new Dictionary<string, (string Id, string Name, StringBuilder Json)>();
            var messageSeq = new Dictionary<string, int>();
            foreach (var line in lines)
            {
                using (var doc = JsonDocument.Parse(line))
                {
                    var root = doc.RootElement;
                    if (Str(root, "type") != "stream_event") continue;
                    var ev = root.GetProperty("event");
                    var parent = Str(root, "parent_tool_use_id") ?? "";
                    var type = Str(ev, "type");
                    if (type == "message_start")
                    {
                        messageSeq[parent] = messageSeq.TryGetValue(parent, out var n) ? n + 1 : 1;
                        continue;
                    }

                    if (type != "content_block_start" && type != "content_block_delta"
                        && type != "content_block_stop") continue;
                    var key = parent + "|" + (messageSeq.TryGetValue(parent, out var m) ? m : 0)
                              + "|" + ev.GetProperty("index").GetInt32();
                    if (type == "content_block_start")
                    {
                        var block = ev.GetProperty("content_block");
                        if (Str(block, "type") == "tool_use")
                            open[key] = (Str(block, "id") ?? "", Str(block, "name") ?? "", new StringBuilder());
                    }
                    else if (type == "content_block_delta" && open.ContainsKey(key))
                    {
                        var delta = ev.GetProperty("delta");
                        if (Str(delta, "type") == "input_json_delta")
                            open[key].Json.Append(Str(delta, "partial_json"));
                    }
                    else if (type == "content_block_stop" && open.ContainsKey(key))
                    {
                        result.Add((open[key].Id, open[key].Name, open[key].Json.ToString()));
                        open.Remove(key);
                    }
                }
            }

            return result;
        }

        private static string? Str(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var v)
            && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;

        [Fact]
        public void Reassembled_input_json_deltas_are_valid_and_private_free()
        {
            foreach (var scenario in FixtureFiles.Scenarios)
            {
                var blocks = ReassembleBlocks(FixtureFiles.StreamLines(scenario), scenario);
                foreach (var block in blocks)
                {
                    var where = scenario + " tool_use " + block.ToolUseId;
                    var json = block.Json.Length == 0 ? "{}" : block.Json;
                    using (JsonDocument.Parse(json)) { }
                    Assert.False(json.IndexOf("cvc-demo", StringComparison.OrdinalIgnoreCase) >= 0,
                        where + " has cvc-demo");
                    Assert.False(json.IndexOf("AppData", StringComparison.OrdinalIgnoreCase) >= 0,
                        where + " has AppData");
                    Assert.False(OtherUserFolder.IsMatch(json), where + " has a user folder other than 'demo'");
                }
            }
        }

        [Fact]
        public void Reassembled_deltas_equal_the_tool_use_input_of_the_assistant_line()
        {
            var compared = 0;
            foreach (var scenario in FixtureFiles.Scenarios)
            {
                var streamed = new Dictionary<string, string>();
                foreach (var block in ReassembleBlocks(FixtureFiles.StreamLines(scenario), scenario))
                    streamed[block.ToolUseId] = block.Json.Length == 0 ? "{}" : block.Json;

                foreach (var line in FixtureFiles.StreamLines(scenario))
                {
                    using (var doc = JsonDocument.Parse(line))
                    {
                        var root = doc.RootElement;
                        if (Str(root, "type") != "assistant") continue;
                        var content = root.GetProperty("message").GetProperty("content");
                        foreach (var block in content.EnumerateArray())
                        {
                            if (Str(block, "type") != "tool_use") continue;
                            var id = Str(block, "id")!;
                            // The claude CLI does not stream the messages of a subagent (parent_tool_use_id set).
                            if (Str(root, "parent_tool_use_id") != null && !streamed.ContainsKey(id)) continue;
                            Assert.True(streamed.ContainsKey(id), scenario + ": no streamed input for " + id);
                            // The CLI may add defaults to the final input (Edit gets "replace_all": false),
                            // so every streamed property must be present and equal in the assistant input.
                            var finalInput = block.GetProperty("input");
                            using (var streamedDoc = JsonDocument.Parse(streamed[id]))
                            {
                                foreach (var property in streamedDoc.RootElement.EnumerateObject())
                                {
                                    Assert.True(
                                        finalInput.TryGetProperty(property.Name, out var finalValue)
                                        && JsonEquals(finalValue, property.Value),
                                        scenario + ": streamed '" + property.Name
                                        + "' differs from assistant input for " + id);
                                }
                            }

                            compared++;
                        }
                    }
                }
            }

            Assert.True(compared >= 10, "compared only " + compared + " tool_use blocks");
        }

        private static bool JsonEquals(JsonElement a, JsonElement b)
        {
            if (a.ValueKind != b.ValueKind) return false;
            switch (a.ValueKind)
            {
                case JsonValueKind.Object:
                    var left = new Dictionary<string, JsonElement>();
                    foreach (var p in a.EnumerateObject()) left[p.Name] = p.Value;
                    var count = 0;
                    foreach (var p in b.EnumerateObject())
                    {
                        count++;
                        if (!left.TryGetValue(p.Name, out var other) || !JsonEquals(other, p.Value)) return false;
                    }

                    return count == left.Count;
                case JsonValueKind.Array:
                    var x = a.EnumerateArray().ToArray();
                    var y = b.EnumerateArray().ToArray();
                    if (x.Length != y.Length) return false;
                    for (var i = 0; i < x.Length; i++)
                        if (!JsonEquals(x[i], y[i])) return false;
                    return true;
                default:
                    return a.GetRawText() == b.GetRawText();
            }
        }

        private static void AssertValidJsonLines(string name, string[] lines)
        {
            Assert.True(lines.Length >= 5, name + " has fewer than 5 lines");
            foreach (var line in lines)
            {
                using (var doc = JsonDocument.Parse(line))
                {
                    Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
                }
            }
        }
    }
}
