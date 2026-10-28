using System.Linq;
using ClaudeVisualChat.Protocol;
using Xunit;

namespace ClaudeVisualChat.Tests.Protocol
{
    public class TranscriptParserTests
    {
        [Fact]
        public void Image_message_from_the_session_file()
        {
            var events = FixtureFiles.TranscriptLines("image").SelectMany(TranscriptParser.Parse).ToList();

            var user = events.OfType<UserBlock>().First(u => u.Images.Count > 0);
            Assert.Equal("What two colors do you see? One line.", Assert.IsType<TextContentBlock>(user.Block).Text);
            var image = Assert.Single(user.Images);
            Assert.Equal("image/png", image.MediaType);
            Assert.StartsWith("iVBOR", image.Data);
            var meta = events.OfType<UserBlock>().First(u => u.IsMeta);
            Assert.StartsWith("[Image: source:", Assert.IsType<TextContentBlock>(meta.Block).Text);
        }

        [Fact]
        public void Command_lines_from_the_session_file()
        {
            var events = FixtureFiles.TranscriptLines("commands").SelectMany(TranscriptParser.Parse).ToList();

            Assert.Contains(new CommandEcho("hello", "Ana"), events);
            Assert.Contains(new CommandEcho("compact", ""), events);
            Assert.Contains(new CommandOutput("Compacted "), events);
            var caveat = events.OfType<UserBlock>().Single(
                u => u.Block is TextContentBlock t && t.Text.StartsWith("<local-command-caveat>"));
            Assert.True(caveat.IsMeta);
            Assert.DoesNotContain(events.OfType<UserBlock>(), u => u.Block is TextContentBlock t && t.Text.Contains("<command-name>"));

            var local = TranscriptParser.Parse(
                "{\"type\":\"system\",\"subtype\":\"local_command\",\"content\":\"<local-command-stdout>X</local-command-stdout>\"}");
            Assert.Equal(new CommandOutput("X"), Assert.Single(local));
            var user = TranscriptParser.Parse(
                "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":\"<local-command-stdout>X</local-command-stdout>\"}}");
            Assert.Equal(new CommandOutput("X"), Assert.Single(user));
        }

        [Fact]
        public void Transcript_user_text_and_tool_result()
        {
            var events = FixtureFiles.TranscriptLines("edit").SelectMany(TranscriptParser.Parse).ToList();

            var first = events.OfType<UserBlock>().First();
            Assert.IsType<TextContentBlock>(first.Block);

            var toolUseIds = events.OfType<AssistantBlock>()
                .Select(a => a.Block).OfType<ToolUseBlock>().Select(t => t.Id).ToList();
            var results = events.OfType<UserBlock>().Select(u => u.Block).OfType<ToolResultBlock>().ToList();
            Assert.NotEmpty(results);
            Assert.All(results, r => Assert.Contains(r.ToolUseId, toolUseIds));
        }

        [Fact]
        public void Transcript_lines_carry_their_time()
        {
            var events = FixtureFiles.TranscriptLines("edit").SelectMany(TranscriptParser.Parse).ToList();

            var user = events.OfType<UserBlock>().First(u => u.Block is TextContentBlock);
            Assert.NotNull(user.Timestamp);
            Assert.All(events.OfType<AssistantBlock>(), a => Assert.NotNull(a.Timestamp));
            Assert.Equal(System.TimeSpan.Zero, user.Timestamp!.Value.Offset); // the file writes UTC
        }

        [Fact]
        public void A_thought_carries_how_long_it_took()
        {
            var block = Assert.IsType<AssistantBlock>(Assert.Single(TranscriptParser.Parse(
                "{\"type\":\"assistant\",\"thinkingDurationMs\":4321,\"message\":{\"id\":\"m1\",\"content\":[{\"type\":\"thinking\",\"thinking\":\"hm\"}]}}")));
            Assert.Equal(4321, block.ThinkingDurationMs);

            var odd = Assert.IsType<AssistantBlock>(Assert.Single(TranscriptParser.Parse(
                "{\"type\":\"assistant\",\"thinkingDurationMs\":\"soon\",\"message\":{\"id\":\"m1\",\"content\":[{\"type\":\"thinking\",\"thinking\":\"hm\"}]}}")));
            Assert.Null(odd.ThinkingDurationMs);
        }

        [Fact]
        public void Transcript_tool_use_result_is_kept()
        {
            var events = FixtureFiles.TranscriptLines("edit").SelectMany(TranscriptParser.Parse).ToList();
            Assert.Contains(events, e => e is UserBlock u && u.ToolUseResult != null);
        }

        [Theory]
        [InlineData("denied", "permission-rule")]
        [InlineData("interrupted", "user-rejected")]
        [InlineData("error", null)]
        public void Tool_denial_kind_marks_results_of_tools_that_did_not_run(string scenario, string? expected)
        {
            var result = FixtureFiles.TranscriptLines(scenario)
                .SelectMany(TranscriptParser.Parse)
                .OfType<UserBlock>()
                .Select(u => u.Block)
                .OfType<ToolResultBlock>()
                .First();

            Assert.Equal(expected, result.NonExecutionKind);
        }

        [Fact]
        public void Tool_denial_kind_applies_to_every_result_of_the_line()
        {
            var results = TranscriptParser.Parse(
                    "{\"type\":\"user\",\"toolDenialKind\":\"permission-rule\",\"message\":{\"role\":\"user\",\"content\":[" +
                    "{\"type\":\"tool_result\",\"tool_use_id\":\"t1\",\"content\":\"no\",\"is_error\":true}," +
                    "{\"type\":\"text\",\"text\":\"note\"}]}}")
                .OfType<UserBlock>().ToList();

            Assert.Equal("permission-rule", Assert.IsType<ToolResultBlock>(results[0].Block).NonExecutionKind);
            Assert.IsType<TextContentBlock>(results[1].Block);
            Assert.Null(TranscriptParser.Parse(
                    "{\"type\":\"user\",\"toolDenialKind\":5,\"message\":{\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"t1\"}]}}")
                .OfType<UserBlock>().Select(u => u.Block).OfType<ToolResultBlock>().Single().NonExecutionKind);
        }

        [Fact]
        public void Meta_and_compact_flags()
        {
            var meta = Assert.IsType<UserBlock>(Assert.Single(TranscriptParser.Parse(
                "{\"type\":\"user\",\"isMeta\":true,\"message\":{\"role\":\"user\",\"content\":\"caveat\"}}")));
            Assert.True(meta.IsMeta);
            Assert.False(meta.IsCompactSummary);

            var compact = Assert.IsType<UserBlock>(Assert.Single(TranscriptParser.Parse(
                "{\"type\":\"user\",\"isCompactSummary\":true,\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"text\",\"text\":\"summary\"}]}}")));
            Assert.True(compact.IsCompactSummary);
            Assert.False(compact.IsMeta);
        }

        [Theory]
        [InlineData("{\"type\":\"attachment\"}")]
        [InlineData("{\"type\":\"mode\",\"mode\":\"x\"}")]
        [InlineData("{\"type\":\"api-request\"}")]
        [InlineData("{\"type\":\"file-history-snapshot\"}")]
        [InlineData("{\"type\":\"ai-title\",\"aiTitle\":\"t\"}")]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("[1]")]
        public void Ignores_other_types(string line)
        {
            Assert.Empty(TranscriptParser.Parse(line));
        }

        [Fact]
        public void Compact_boundary_gives_notice()
        {
            var notice = Assert.IsType<SystemNotice>(Assert.Single(TranscriptParser.Parse(
                "{\"type\":\"system\",\"subtype\":\"compact_boundary\",\"content\":\"Conversation compacted\"}")));
            Assert.Equal("compact_boundary", notice.Subtype);
        }

        [Fact]
        public void Compact_boundary_brings_the_metadata_of_either_spelling()
        {
            var stream = Assert.IsType<SystemNotice>(Assert.Single(TranscriptParser.Parse(
                "{\"type\":\"system\",\"subtype\":\"compact_boundary\",\"compact_metadata\":{\"trigger\":\"auto\",\"pre_tokens\":160000,\"post_tokens\":3000}}")));
            Assert.Equal("auto", stream.Trigger);
            Assert.Equal(160000, stream.PreTokens);
            Assert.Equal(3000, stream.PostTokens);
            var file = Assert.IsType<SystemNotice>(Assert.Single(TranscriptParser.Parse(
                "{\"type\":\"system\",\"subtype\":\"compact_boundary\",\"compactMetadata\":{\"trigger\":\"manual\",\"preTokens\":36326}}")));
            Assert.Equal("manual", file.Trigger);
            Assert.Equal(36326, file.PreTokens);
            Assert.Null(file.PostTokens);
        }

        [Fact]
        public void Hook_lines_of_a_file_give_the_id_and_the_event()
        {
            Assert.Equal(new HookStarted("h1", "Setup"), Assert.Single(TranscriptParser.Parse(
                "{\"type\":\"system\",\"subtype\":\"hook_started\",\"hook_id\":\"h1\",\"hook_event\":\"Setup\"}")));
            Assert.Equal(new HookFinished("h1"), Assert.Single(TranscriptParser.Parse(
                "{\"type\":\"system\",\"subtype\":\"hook_response\",\"hook_id\":\"h1\"}")));
        }

        [Fact]
        public void A_thinking_tokens_line_of_a_file_gives_the_count_too()
        {
            Assert.Equal(new ThinkingTokens(50), Assert.Single(TranscriptParser.Parse(
                "{\"type\":\"system\",\"subtype\":\"thinking_tokens\",\"estimated_tokens\":50}")));
        }

        [Fact]
        public void The_synthetic_flag_of_a_file_line_is_read()
        {
            var line = Assert.IsType<UserBlock>(Assert.Single(TranscriptParser.Parse(
                "{\"type\":\"user\",\"isSynthetic\":true,\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"text\",\"text\":\"x\"}]}}")));
            Assert.True(line.IsSynthetic);
        }

        [Fact]
        public void The_origin_of_a_user_line_is_read_when_the_line_has_one()
        {
            var line = Assert.IsType<UserBlock>(Assert.Single(TranscriptParser.Parse(
                "{\"type\":\"user\",\"origin\":{\"kind\":\"peer\",\"name\":\"Maria\",\"senderTaskId\":\"a1\",\"subkind\":\"x\",\"producer\":\"p\"}," +
                "\"message\":{\"role\":\"user\",\"content\":\"hi\"}}")));
            Assert.Equal("peer", line.Origin!.Kind);
            Assert.Equal("Maria", line.Origin.Name);
            Assert.Equal("a1", line.Origin.SenderTaskId);
            Assert.Equal("x", line.Origin.Subkind);

            var channel = Assert.IsType<UserBlock>(Assert.Single(TranscriptParser.Parse(
                "{\"type\":\"user\",\"origin\":{\"kind\":\"channel\"},\"message\":{\"role\":\"user\",\"content\":\"hi\"}}")));
            Assert.Equal("channel", channel.Origin!.Kind);
            Assert.Null(channel.Origin.Name);

            // no origin, an origin that is no object, or one without a kind: none
            foreach (var origin in new[] { "", "\"origin\":null,", "\"origin\":\"peer\",", "\"origin\":{\"name\":\"x\"}," })
            {
                var plain = Assert.IsType<UserBlock>(Assert.Single(TranscriptParser.Parse(
                    "{\"type\":\"user\"," + origin + "\"message\":{\"role\":\"user\",\"content\":\"hi\"}}")));
                Assert.Null(plain.Origin);
            }
        }

        [Fact]
        public void All_fixtures_parse_without_throwing()
        {
            var total = 0;
            foreach (var scenario in FixtureFiles.Scenarios)
                foreach (var line in FixtureFiles.TranscriptLines(scenario))
                    total += TranscriptParser.Parse(line).Count;
            Assert.True(total > 20);
        }

        private static string UserLine(object content) =>
            System.Text.Json.JsonSerializer.Serialize(new { type = "user", message = new { role = "user", content } });

        private static string TextOf(string line)
        {
            var user = TranscriptParser.Parse(line).OfType<UserBlock>().Single();
            return Assert.IsType<TextContentBlock>(user.Block).Text;
        }

        [Theory]
        [InlineData("look at this:\n\n<pasted_content id=\"ab12\">\nfirst\nsecond\n</pasted_content id=\"ab12\">\n\nwhat is wrong?",
            "look at this:\nfirst\nsecond\nwhat is wrong?")]
        [InlineData("see <pasted_content id=\"09af\">\nONE LINE\n</pasted_content id=\"09af\"> now", "see ONE LINE now")]
        [InlineData("see<pasted_content id=\"09af\">\nONE LINE\n</pasted_content id=\"09af\">now", "see\nONE LINE\nnow")]
        [InlineData("<pasted_content id=\"0f3a\">\nonly the paste\n</pasted_content id=\"0f3a\">", "only the paste")]
        [InlineData("a\n\n<pasted_content id=\"0001\">\nx\ny\n</pasted_content id=\"0001\">\n\nb\n\n<pasted_content id=\"0002\">\nz\nw\n</pasted_content id=\"0002\">",
            "a\nx\ny\nb\nz\nw")]
        [InlineData("<pasted_content id=\"0001\">\none\n</pasted_content id=\"0001\"><pasted_content id=\"0002\">\ntwo\n</pasted_content id=\"0002\">", "one\ntwo")]
        [InlineData("a <pasted_content id=\"abcd\">\n\n</pasted_content id=\"abcd\"> b", "a  b")]
        [InlineData("a paste that stays: <pasted_content id=\"zzzz\">\nx\n</pasted_content id=\"zzzz\">",
            "a paste that stays: <pasted_content id=\"zzzz\">\nx\n</pasted_content id=\"zzzz\">")]
        [InlineData("<pasted_content id=\"abc\">\nx\n</pasted_content id=\"abc\">", "<pasted_content id=\"abc\">\nx\n</pasted_content id=\"abc\">")]
        [InlineData("<pasted_content id=\"abcd\">\nnever closed", "<pasted_content id=\"abcd\">\nnever closed")]
        [InlineData("<pasted_content id=\"abcd\">\nbody\n</pasted_content id=\"ffff\">", "<pasted_content id=\"abcd\">\nbody\n</pasted_content id=\"ffff\">")]
        [InlineData("no tag at all", "no tag at all")]
        // the four real shapes of the session file
        [InlineData("\n\n<pasted_content id=\"04d0\">\nfirst line, with a long text\n</pasted_content id=\"04d0\">\n", "first line, with a long text")]
        [InlineData("\n\n<pasted_content id=\"af7f\">\nPS C:\\> run it\n</pasted_content id=\"af7f\">\n\n<pasted_content id=\"af7f\">\nPS C:\\> run it\n</pasted_content id=\"af7f\">\n",
            "PS C:\\> run it\nPS C:\\> run it")]
        [InlineData("\n\n<pasted_content id=\"af7f\">\nPS C:\\> ssh host\nConnection closed.\nPS C:\\>\n</pasted_content id=\"af7f\">\n\nalready ran it on the server",
            "PS C:\\> ssh host\nConnection closed.\nPS C:\\>\nalready ran it on the server")]
        [InlineData("rebase the branch\nsee the last comment\n\n<pasted_content id=\"2b03\">\n[18:57] Ana: I opened a ticket\n\nhttps://example.com/plans/\n</pasted_content id=\"2b03\">\n",
            "rebase the branch\nsee the last comment\n[18:57] Ana: I opened a ticket\n\nhttps://example.com/plans/")]
        // a closing line in the middle of a line is part of the paste
        [InlineData("<pasted_content id=\"ab12\">\nthe tag </pasted_content id=\"ab12\"> inside\n</pasted_content id=\"ab12\">\nafter",
            "the tag </pasted_content id=\"ab12\"> inside\nafter")]
        [InlineData("<pasted_content id=\"ab12\">\nthe </pasted_content tag\n</pasted_content id=\"ab12\">", "the </pasted_content tag")]
        // a wrapper that is not closed ends the reading
        [InlineData("<pasted_content id=\"ab12\">\nx\n</pasted_content id=\"ab12\">\n\nthen <pasted_content id=\"cd34\">\nopen\nand done",
            "x\nthen <pasted_content id=\"cd34\">\nopen\nand done")]
        public void The_history_drops_the_pasted_content_wrapper(string said, string shown)
        {
            Assert.Equal(shown, TextOf(UserLine(said)));
            // the same in a list of blocks, and live
            var blocks = new object[] { new { type = "text", text = said } };
            Assert.Equal(shown, TextOf(UserLine(blocks)));
            var live = new StreamJsonParser().Parse(UserLine(said)).OfType<UserBlock>().Single();
            Assert.Equal(shown, Assert.IsType<TextContentBlock>(live.Block).Text);
        }

        [Theory]
        [InlineData("no tag at all")]
        [InlineData("")]
        [InlineData("<pasted_content id=\"abcd\">\nnever closed")]
        [InlineData("<pasted_content id=\"abcd\">\nbody\n</pasted_content id=\"ffff\">")]
        [InlineData("<pasted_content id=\"abcd\">no line break\n</pasted_content id=\"abcd\">")]
        public void A_message_without_a_well_formed_wrapper_comes_back_as_the_same_string(string said)
        {
            Assert.Same(said, PastedContent.Strip(said));
        }
    }
}
