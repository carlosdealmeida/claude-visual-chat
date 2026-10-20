using System.Linq;
using ClaudeVisualChat.Protocol;
using Xunit;

namespace ClaudeVisualChat.Tests.Protocol
{
    public class StreamJsonParserTests
    {
        [Fact]
        public void Init_gives_session_started()
        {
            var parser = new StreamJsonParser();
            var line = FixtureFiles.StreamLines("edit").First(l => l.Contains("\"subtype\": \"init\""));

            var started = Assert.IsType<SessionStarted>(Assert.Single(parser.Parse(line)));

            Assert.Equal(@"C:\demo\app", started.WorkingDirectory);
            Assert.False(string.IsNullOrEmpty(started.Model));
            Assert.False(string.IsNullOrEmpty(started.SessionId));
            Assert.Equal("default", started.PermissionMode);
        }

        [Fact]
        public void Assistant_line_gives_one_block_event()
        {
            var parser = new StreamJsonParser();
            var line = FixtureFiles.StreamLines("edit")
                .First(l => l.StartsWith("{\"type\": \"assistant\"") && l.Contains("\"name\": \"Edit\""));

            var block = Assert.IsType<AssistantBlock>(Assert.Single(parser.Parse(line)));

            var tool = Assert.IsType<ToolUseBlock>(block.Block);
            Assert.Equal("Edit", tool.Name);
            Assert.EndsWith("notes.md", tool.Input.GetProperty("file_path").GetString());
            Assert.StartsWith("msg_", block.MessageId);
            Assert.Null(block.ParentToolUseId);
        }

        [Fact]
        public void Deltas_carry_message_id_from_message_start()
        {
            var parser = new StreamJsonParser();
            Assert.Empty(parser.Parse("{\"type\":\"stream_event\",\"parent_tool_use_id\":null,\"event\":{\"type\":\"message_start\",\"message\":{\"id\":\"msg_1\"}}}"));

            var started = Assert.IsType<BlockStarted>(Assert.Single(parser.Parse(
                "{\"type\":\"stream_event\",\"parent_tool_use_id\":null,\"event\":{\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"text\",\"text\":\"\"}}}")));
            var d1 = Assert.IsType<BlockDelta>(Assert.Single(parser.Parse(
                "{\"type\":\"stream_event\",\"parent_tool_use_id\":null,\"event\":{\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"Hel\"}}}")));
            var d2 = Assert.IsType<BlockDelta>(Assert.Single(parser.Parse(
                "{\"type\":\"stream_event\",\"parent_tool_use_id\":null,\"event\":{\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"lo\"}}}")));
            var stopped = Assert.IsType<BlockStopped>(Assert.Single(parser.Parse(
                "{\"type\":\"stream_event\",\"parent_tool_use_id\":null,\"event\":{\"type\":\"content_block_stop\",\"index\":0}}")));

            Assert.Equal("msg_1", started.MessageId);
            Assert.Equal(0, started.Index);
            Assert.IsType<TextContentBlock>(started.Block);
            Assert.Equal("msg_1", d1.MessageId);
            Assert.Equal(DeltaKind.Text, d1.Kind);
            Assert.Equal("Hel", d1.Text);
            Assert.Equal("msg_1", d2.MessageId);
            Assert.Equal("lo", d2.Text);
            Assert.Equal("msg_1", stopped.MessageId);
        }

        [Fact]
        public void Message_ids_are_kept_per_parent_tool_use()
        {
            var parser = new StreamJsonParser();
            parser.Parse("{\"type\":\"stream_event\",\"parent_tool_use_id\":null,\"event\":{\"type\":\"message_start\",\"message\":{\"id\":\"msg_main\"}}}");
            parser.Parse("{\"type\":\"stream_event\",\"parent_tool_use_id\":\"toolu_p\",\"event\":{\"type\":\"message_start\",\"message\":{\"id\":\"msg_sub\"}}}");

            var main = Assert.IsType<BlockDelta>(Assert.Single(parser.Parse(
                "{\"type\":\"stream_event\",\"parent_tool_use_id\":null,\"event\":{\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"a\"}}}")));
            var sub = Assert.IsType<BlockDelta>(Assert.Single(parser.Parse(
                "{\"type\":\"stream_event\",\"parent_tool_use_id\":\"toolu_p\",\"event\":{\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"thinking_delta\",\"thinking\":\"b\"}}}")));

            Assert.Equal("msg_main", main.MessageId);
            Assert.Equal("msg_sub", sub.MessageId);
            Assert.Equal("toolu_p", sub.ParentToolUseId);
            Assert.Equal(DeltaKind.Thinking, sub.Kind);
        }

        [Fact]
        public void Tool_use_start_has_empty_input_and_json_deltas_are_raw_text()
        {
            var parser = new StreamJsonParser();
            parser.Parse("{\"type\":\"stream_event\",\"parent_tool_use_id\":null,\"event\":{\"type\":\"message_start\",\"message\":{\"id\":\"msg_2\"}}}");
            var started = Assert.IsType<BlockStarted>(Assert.Single(parser.Parse(
                "{\"type\":\"stream_event\",\"parent_tool_use_id\":null,\"event\":{\"type\":\"content_block_start\",\"index\":1,\"content_block\":{\"type\":\"tool_use\",\"id\":\"toolu_1\",\"name\":\"Bash\",\"input\":{}}}}")));
            var delta = Assert.IsType<BlockDelta>(Assert.Single(parser.Parse(
                "{\"type\":\"stream_event\",\"parent_tool_use_id\":null,\"event\":{\"type\":\"content_block_delta\",\"index\":1,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"{\\\"com\"}}}")));

            var tool = Assert.IsType<ToolUseBlock>(started.Block);
            Assert.Equal("toolu_1", tool.Id);
            Assert.Equal("Bash", tool.Name);
            Assert.Equal(System.Text.Json.JsonValueKind.Object, tool.Input.ValueKind);
            Assert.Equal(DeltaKind.InputJson, delta.Kind);
            Assert.Equal("{\"com", delta.Text);
        }

        [Fact]
        public void Can_use_tool_gives_permission_requested()
        {
            var parser = new StreamJsonParser();
            var line = FixtureFiles.StreamLines("edit").First(l => l.Contains("can_use_tool"));

            var ev = Assert.IsType<PermissionRequested>(Assert.Single(parser.Parse(line)));

            Assert.False(string.IsNullOrEmpty(ev.RequestId));
            Assert.Equal("Edit", ev.ToolName);
            Assert.StartsWith("toolu_", ev.ToolUseId);
            Assert.NotNull(ev.Suggestions);
            Assert.Equal("setMode", ev.Suggestions!.Value[0].GetProperty("type").GetString());
            Assert.Equal("line three", ev.Input.GetProperty("old_string").GetString());
        }

        [Fact]
        public void Cancel_and_result()
        {
            var parser = new StreamJsonParser();

            var cancelled = Assert.IsType<PermissionCancelled>(Assert.Single(parser.Parse(
                "{\"type\":\"control_cancel_request\",\"request_id\":\"r1\"}")));
            Assert.Equal("r1", cancelled.RequestId);

            var line = FixtureFiles.StreamLines("interrupted").First(l => l.Contains("\"type\": \"result\""));
            var done = Assert.IsType<TurnCompleted>(Assert.Single(parser.Parse(line)));
            Assert.True(done.IsError || done.Subtype == "error_during_execution");
        }

        [Fact]
        public void Result_sums_input_tokens()
        {
            var parser = new StreamJsonParser();
            var done = Assert.IsType<TurnCompleted>(Assert.Single(parser.Parse(
                "{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"duration_ms\":1200,\"total_cost_usd\":0.5,\"usage\":{\"input_tokens\":10,\"cache_creation_input_tokens\":100,\"cache_read_input_tokens\":1000,\"output_tokens\":7}}")));

            Assert.Equal("success", done.Subtype);
            Assert.False(done.IsError);
            Assert.Equal(1200, done.DurationMs);
            Assert.Equal(0.5, done.CostUsd);
            Assert.Equal(1110, done.InputTokens);
            Assert.Equal(7, done.OutputTokens);
        }

        [Fact]
        public void User_tool_result_keeps_structured_result()
        {
            var parser = new StreamJsonParser();
            var ev = Assert.IsType<UserBlock>(Assert.Single(parser.Parse(
                "{\"type\":\"user\",\"parent_tool_use_id\":null,\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"toolu_9\",\"content\":[{\"type\":\"text\",\"text\":\"a\"},{\"type\":\"text\",\"text\":\"b\"}],\"is_error\":true}]},\"tool_use_result\":\"User rejected tool use\"}")));

            var result = Assert.IsType<ToolResultBlock>(ev.Block);
            Assert.Equal("toolu_9", result.ToolUseId);
            Assert.Equal("a\nb", result.Text);
            Assert.True(result.IsError);
            Assert.Equal("User rejected tool use", ev.ToolUseResult!.Value.GetString());
        }

        [Theory]
        [InlineData("denied", "permission-rule")]
        [InlineData("interrupted", "user-rejected")]
        [InlineData("error", null)]
        public void Tool_result_meta_marks_results_of_tools_that_did_not_run(string scenario, string? expected)
        {
            var parser = new StreamJsonParser();
            var result = FixtureFiles.StreamLines(scenario)
                .SelectMany(parser.Parse)
                .OfType<UserBlock>()
                .Select(u => u.Block)
                .OfType<ToolResultBlock>()
                .First();

            Assert.Equal(expected, result.NonExecutionKind);
        }

        [Fact]
        public void Tool_result_meta_is_matched_by_tool_use_id()
        {
            var events = new StreamJsonParser().Parse(
                "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":[" +
                "{\"type\":\"tool_result\",\"tool_use_id\":\"t1\",\"content\":\"ran\"}," +
                "{\"type\":\"tool_result\",\"tool_use_id\":\"t2\",\"content\":\"no\",\"is_error\":true}]}," +
                "\"tool_result_meta\":[{\"id\":\"t2\",\"non_execution_kind\":\"permission-rule\"},{\"id\":5},7]}");

            var results = events.OfType<UserBlock>().Select(u => u.Block).OfType<ToolResultBlock>().ToList();
            Assert.Equal(2, results.Count);
            Assert.Null(results[0].NonExecutionKind);
            Assert.Equal("permission-rule", results[1].NonExecutionKind);
        }

        [Fact]
        public void Control_response_and_status()
        {
            var parser = new StreamJsonParser();
            var ack = Assert.IsType<ControlAcknowledged>(Assert.Single(parser.Parse(
                "{\"type\":\"control_response\",\"response\":{\"subtype\":\"success\",\"request_id\":\"q1\",\"response\":{\"mode\":\"acceptEdits\"}}}")));
            Assert.Equal("q1", ack.RequestId);
            Assert.False(ack.IsError);
            Assert.Equal("acceptEdits", ack.Response!.Value.GetProperty("mode").GetString());

            var status = Assert.IsType<StatusChanged>(Assert.Single(parser.Parse(
                "{\"type\":\"system\",\"subtype\":\"status\",\"status\":\"requesting\",\"permissionMode\":\"plan\"}")));
            Assert.Equal("requesting", status.Status);
            Assert.Equal("plan", status.PermissionMode);

            var notice = Assert.IsType<SystemNotice>(Assert.Single(parser.Parse(
                "{\"type\":\"system\",\"subtype\":\"api_retry\"}")));
            Assert.Equal("api_retry", notice.Subtype);
        }

        [Fact]
        public void Control_responses_in_recording_are_parsed()
        {
            var parser = new StreamJsonParser();
            var acks = FixtureFiles.StreamLines("mode-and-model")
                .Where(l => l.StartsWith("{\"type\": \"control_response\""))
                .SelectMany(parser.Parse)
                .OfType<ControlAcknowledged>()
                .ToList();

            Assert.NotEmpty(acks);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not json")]
        [InlineData("{\"type\":\"brand_new\"}")]
        [InlineData("{\"type\":\"assistant\"}")]
        [InlineData("{\"type\":\"assistant\",\"message\":{\"content\":\"x\"}}")]
        [InlineData("[1,2]")]
        [InlineData("{\"type\":\"stream_event\"}")]
        [InlineData("{\"type\":\"stream_event\",\"event\":{\"type\":\"content_block_delta\"}}")]
        [InlineData("{\"type\":\"control_request\",\"request\":{\"subtype\":\"can_use_tool\"}}")]
        [InlineData("{\"type\":\"user\",\"message\":5}")]
        public void Tolerates_bad_lines(string line)
        {
            var parser = new StreamJsonParser();
            Assert.Empty(parser.Parse(line));
        }

        [Fact]
        public void Strips_leading_ansi_escape()
        {
            var parser = new StreamJsonParser();
            var events = parser.Parse("\u001b[2K{\"type\":\"result\",\"subtype\":\"success\"}");
            Assert.IsType<TurnCompleted>(Assert.Single(events));
        }

        [Fact]
        public void Null_line_does_not_throw()
        {
            Assert.Empty(new StreamJsonParser().Parse(null!));
        }

        [Fact]
        public void All_fixtures_parse_without_throwing()
        {
            var total = 0;
            foreach (var scenario in FixtureFiles.Scenarios)
            {
                var parser = new StreamJsonParser();
                foreach (var line in FixtureFiles.StreamLines(scenario))
                    total += parser.Parse(line).Count;
            }
            Assert.True(total > 100);
        }

        [Fact]
        public void Assistant_and_user_lines_carry_their_time()
        {
            var parser = new StreamJsonParser();

            var text = Assert.IsType<AssistantBlock>(Assert.Single(parser.Parse(
                "{\"type\":\"assistant\",\"timestamp\":\"2026-10-19T21:31:35.778Z\",\"message\":{\"id\":\"m1\",\"content\":[{\"type\":\"text\",\"text\":\"hi\"}]}}")));
            var user = Assert.IsType<UserBlock>(Assert.Single(parser.Parse(
                "{\"type\":\"user\",\"timestamp\":\"2026-10-19T21:31:36.123+00:00\",\"message\":{\"role\":\"user\",\"content\":\"oi\"}}")));

            Assert.Equal(new System.DateTimeOffset(2026, 10, 19, 21, 31, 35, 778, System.TimeSpan.Zero), text.Timestamp);
            Assert.Equal(new System.DateTimeOffset(2026, 10, 19, 21, 31, 36, 123, System.TimeSpan.Zero), user.Timestamp);
        }

        [Theory]
        [InlineData("")]
        [InlineData(",\"timestamp\":null")]
        [InlineData(",\"timestamp\":1760909495")]
        [InlineData(",\"timestamp\":\"yesterday\"")]
        public void A_missing_or_odd_time_is_null(string timestamp)
        {
            var parser = new StreamJsonParser();
            var block = Assert.IsType<AssistantBlock>(Assert.Single(parser.Parse(
                "{\"type\":\"assistant\"" + timestamp + ",\"message\":{\"id\":\"m1\",\"content\":[{\"type\":\"text\",\"text\":\"hi\"}]}}")));
            Assert.Null(block.Timestamp);
        }

        [Fact]
        public void Recorded_assistant_lines_have_a_time()
        {
            var parser = new StreamJsonParser();
            var blocks = FixtureFiles.StreamLines("edit").SelectMany(parser.Parse).OfType<AssistantBlock>().ToList();
            Assert.NotEmpty(blocks);
            Assert.All(blocks, b => Assert.NotNull(b.Timestamp));
        }

        [Fact]
        public void Subagent_stream_carries_parent_tool_use_id()
        {
            var parser = new StreamJsonParser();
            var events = FixtureFiles.StreamLines("subagent").SelectMany(parser.Parse).ToList();
            Assert.Contains(events, e => e is AssistantBlock a && a.ParentToolUseId != null);
        }
    }
}
