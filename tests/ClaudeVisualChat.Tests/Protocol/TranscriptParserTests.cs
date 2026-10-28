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
        public void All_fixtures_parse_without_throwing()
        {
            var total = 0;
            foreach (var scenario in FixtureFiles.Scenarios)
                foreach (var line in FixtureFiles.TranscriptLines(scenario))
                    total += TranscriptParser.Parse(line).Count;
            Assert.True(total > 20);
        }
    }
}
