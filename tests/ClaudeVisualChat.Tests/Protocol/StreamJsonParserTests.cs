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
        public void Command_lifecycle_gives_what_became_of_a_sent_message()
        {
            var parser = new StreamJsonParser();
            var started = Assert.IsType<CommandLifecycle>(Assert.Single(parser.Parse(
                "{\"type\":\"command_lifecycle\",\"command_uuid\":\"c-1\",\"state\":\"started\",\"uuid\":\"u\",\"session_id\":\"s\"}")));
            Assert.Equal(new CommandLifecycle("c-1", "started", false), started);

            var ranAhead = Assert.IsType<CommandLifecycle>(Assert.Single(parser.Parse(
                "{\"type\":\"command_lifecycle\",\"command_uuid\":\"c-2\",\"state\":\"queued\",\"ran_ahead\":true}")));
            Assert.Equal(new CommandLifecycle("c-2", "queued", true), ranAhead);

            // without the message's uuid or a state it says nothing
            Assert.Empty(parser.Parse("{\"type\":\"command_lifecycle\",\"state\":\"started\"}"));
            Assert.Empty(parser.Parse("{\"type\":\"command_lifecycle\",\"command_uuid\":\"c-3\"}"));
        }

        [Fact]
        public void Assistant_line_gives_one_block_event()
        {
            var parser = new StreamJsonParser();
            var line = FixtureFiles.StreamLines("edit")
                .First(l => l.StartsWith("{\"type\": \"assistant\"") && l.Contains("\"name\": \"Edit\""));

            // The line also reports its cache usage (PromptCacheUsed); the block is the one AssistantBlock.
            var block = Assert.Single(parser.Parse(line).OfType<AssistantBlock>());

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
        public void Can_use_tool_reads_agent_id_default_to_no_and_suppress_always_allow_rule()
        {
            var parser = new StreamJsonParser();
            var line = "{\"type\":\"control_request\",\"request_id\":\"r9\",\"request\":{\"subtype\":\"can_use_tool\",\"tool_name\":\"Bash\","
                       + "\"input\":{\"command\":\"ls\"},\"tool_use_id\":\"toolu_9\",\"agent_id\":\"adb1f0b70b0f95ff6\","
                       + "\"default_to_no\":true,\"suppress_always_allow_rule\":true}}";

            var ev = Assert.IsType<PermissionRequested>(Assert.Single(parser.Parse(line)));

            Assert.Equal("adb1f0b70b0f95ff6", ev.AgentId);
            Assert.True(ev.DefaultToNo);
            Assert.True(ev.SuppressAlwaysAllowRule);

            // the recordings of 2.1.310 carry none of them: the defaults
            var recorded = Assert.IsType<PermissionRequested>(Assert.Single(parser.Parse(FixtureFiles.StreamLines("edit").First(l => l.Contains("can_use_tool")))));
            Assert.Null(recorded.AgentId);
            Assert.False(recorded.DefaultToNo);
            Assert.False(recorded.SuppressAlwaysAllowRule);
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

        [Fact]
        public void A_tool_result_keeps_the_base64_images_of_its_content()
        {
            var parser = new StreamJsonParser();
            var ev = Assert.IsType<UserBlock>(Assert.Single(parser.Parse(
                "{\"type\":\"user\",\"parent_tool_use_id\":null,\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"toolu_1\",\"content\":["
                + "{\"type\":\"text\",\"text\":\"done\"},"
                + "{\"type\":\"image\",\"source\":{\"type\":\"base64\",\"media_type\":\"image/png\",\"data\":\"AAAA\"}},"
                + "{\"type\":\"image\",\"source\":{\"type\":\"url\",\"url\":\"https://x/y.png\"}}]}]}}")));

            var result = Assert.IsType<ToolResultBlock>(ev.Block);
            Assert.Equal("done", result.Text);
            var image = Assert.Single(result.Images);   // the one that is not base64 has nothing to draw
            Assert.Equal(("image/png", "AAAA"), (image.MediaType, image.Data));
            Assert.Empty(ev.Images);                    // they are the result's, not the message's
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

        private static string Line(string scenario, string contains) =>
            FixtureFiles.StreamLines(scenario).First(l => l.Contains(contains));

        [Fact]
        public void Initialize_response_lists_commands_and_models()
        {
            var events = new StreamJsonParser().Parse(Line("commands", "\"request_id\": \"init-1\""));

            Assert.IsType<ControlAcknowledged>(events[0]);
            var commands = events.OfType<CommandsAvailable>().Single().Commands;
            var hello = commands.Single(c => c.Name == "hello");
            Assert.Equal("name", hello.ArgumentHint);
            Assert.Equal("Say hello (project)", hello.Description);
            var models = events.OfType<ModelsAvailable>().Single().Models;
            var haiku = models.Single(m => m.Value == "haiku");
            Assert.Empty(haiku.EffortLevels);
            Assert.Equal("claude-haiku-4-5-20251001", haiku.ResolvedModel);
            Assert.Equal("Haiku 4.5", haiku.DisplayName);
            Assert.Equal(new[] { "low", "medium", "high", "xhigh", "max" }, models.Single(m => m.Value == "opus").EffortLevels);
        }

        [Fact]
        public void Commands_changed_lists_commands()
        {
            var events = new StreamJsonParser().Parse(
                "{\"type\":\"system\",\"subtype\":\"commands_changed\",\"commands\":[{\"name\":\"/deploy\",\"description\":\"Ship\",\"argumentHint\":\"env\"}]}");

            var command = Assert.Single(Assert.IsType<CommandsAvailable>(Assert.Single(events)).Commands);
            Assert.Equal(new ChatCommand("deploy", "Ship", "env"), command);
        }

        private static string Control(string body) =>
            "{\"type\":\"control_response\",\"response\":{\"subtype\":\"success\",\"request_id\":\"q\",\"response\":" + body + "}}";

        [Fact]
        public void Get_settings_reports_applied()
        {
            var parser = new StreamJsonParser();
            var high = parser.Parse(Control("{\"applied\":{\"model\":\"claude-sonnet-5-5\",\"effort\":\"high\"}}"));
            Assert.Equal(new SettingsApplied("claude-sonnet-5-5", "high", true), high.OfType<SettingsApplied>().Single());
            Assert.IsType<ControlAcknowledged>(high[0]);

            var recorded = parser.Parse(Line("effort", "\"request_id\": \"settings-1\""));
            Assert.Equal(new SettingsApplied("claude-haiku-4-5-20251001", null, true), recorded.OfType<SettingsApplied>().Single());

            var noKey = parser.Parse(Control("{\"applied\":{\"model\":\"m\"}}"));
            Assert.False(noKey.OfType<SettingsApplied>().Single().HasEffort);

            var none = parser.Parse(Control("{}"));
            Assert.IsType<ControlAcknowledged>(Assert.Single(none));
        }

        private static string AssistantLine(string usage, string model = "claude-haiku-4-5", string parent = "null") =>
            "{\"type\":\"assistant\",\"message\":{\"model\":\"" + model + "\",\"id\":\"m1\",\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"x\"}]"
            + usage + "},\"parent_tool_use_id\":" + parent + ",\"timestamp\":\"2026-10-22T16:40:36.545Z\"}";

        [Fact]
        public void Assistant_usage_reports_the_cache()
        {
            var parser = new StreamJsonParser();
            var at = System.DateTimeOffset.Parse("2026-10-22T16:40:36.545Z");

            var h1 = parser.Parse(AssistantLine(",\"usage\":{\"cache_read_input_tokens\":5,\"cache_creation\":{\"ephemeral_5m_input_tokens\":0,\"ephemeral_1h_input_tokens\":9}}"));
            Assert.Equal(new PromptCacheUsed(System.TimeSpan.FromHours(1), at), h1.OfType<PromptCacheUsed>().Single());
            Assert.IsType<AssistantBlock>(h1[0]);

            var m5 = parser.Parse(AssistantLine(",\"usage\":{\"cache_creation\":{\"ephemeral_5m_input_tokens\":7,\"ephemeral_1h_input_tokens\":0}}"));
            Assert.Equal(System.TimeSpan.FromMinutes(5), m5.OfType<PromptCacheUsed>().Single().Ttl);

            var read = parser.Parse(AssistantLine(",\"usage\":{\"cache_read_input_tokens\":5,\"cache_creation\":{\"ephemeral_5m_input_tokens\":0,\"ephemeral_1h_input_tokens\":0}}"));
            Assert.Null(read.OfType<PromptCacheUsed>().Single().Ttl);

            Assert.Empty(parser.Parse(AssistantLine("")).OfType<PromptCacheUsed>());
            Assert.Empty(parser.Parse(AssistantLine(",\"usage\":{\"cache_read_input_tokens\":0}")).OfType<PromptCacheUsed>());
            Assert.Empty(parser.Parse(AssistantLine(",\"usage\":{\"cache_read_input_tokens\":5}", parent: "\"toolu_1\"")).OfType<PromptCacheUsed>());
            Assert.Empty(parser.Parse(AssistantLine(",\"usage\":{\"cache_read_input_tokens\":5}", model: "<synthetic>")).OfType<PromptCacheUsed>());
        }

        [Fact]
        public void Context_usage_response()
        {
            var events = new StreamJsonParser().Parse(Line("commands", "\"request_id\": \"context-2\""));

            var usage = events.OfType<ContextUsageReported>().Single().Usage;
            Assert.Equal(18, usage.Percentage);
            Assert.Equal(200000, usage.MaxTokens);
            Assert.Equal(36174, usage.TotalTokens);
            Assert.Equal(7, usage.Categories.Count);
            Assert.Equal(new ContextCategory("System prompt", 6579, "used"), usage.Categories[0]);
            Assert.Equal("deferred", usage.Categories[2].Kind);
            Assert.Equal(new ContextCategory("Free space", 163826, "free"), usage.Categories[6]);
        }

        [Fact]
        public void Rate_limit_event_reports_the_windows()
        {
            const string line = "{\"type\":\"rate_limit_event\",\"rate_limit_info\":{\"status\":\"allowed\",\"resetsAt\":1792698600,\"rateLimitType\":\"five_hour\",\"unifiedWindows\":{\"five_hour\":{\"utilization\":0.01,\"resetsAt\":1792698600},\"seven_day\":{\"utilization\":0.12,\"resetsAt\":1793016000}}},\"uuid\":\"u1\",\"session_id\":\"s1\"}";

            var limits = Assert.IsType<UsageLimitsReported>(Assert.Single(new StreamJsonParser().Parse(line))).Limits;

            Assert.Equal(2, limits.Windows.Count);
            Assert.Equal("five_hour", limits.Windows[0].Name);
            Assert.Equal(0.01, limits.Windows[0].Utilization);
            Assert.Equal(System.DateTimeOffset.FromUnixTimeSeconds(1792698600), limits.Windows[0].ResetsAt);
            Assert.Equal("seven_day", limits.Windows[1].Name);
            Assert.Equal(0.12, limits.Windows[1].Utilization);
        }

        [Fact]
        public void Bridge_state_and_remote_control_response()
        {
            var parser = new StreamJsonParser();

            var state = parser.Parse("{\"type\":\"system\",\"subtype\":\"bridge_state\",\"state\":\"connected\",\"detail\":\"ok\"}");
            Assert.Equal(new RemoteControlChanged("connected", null, "ok"), Assert.Single(state));

            var response = parser.Parse(Control("{\"session_url\":\"https://claude.ai/code/x\"}"));
            Assert.Equal(new RemoteControlChanged(null, "https://claude.ai/code/x", null), response.OfType<RemoteControlChanged>().Single());
        }

        [Fact]
        public void Init_lists_terminal_commands()
        {
            var events = new StreamJsonParser().Parse(
                "{\"type\":\"system\",\"subtype\":\"init\",\"session_id\":\"s\",\"terminal_slash_commands\":[\"clear\",\"exit\"]}");

            var started = Assert.IsType<SessionStarted>(Assert.Single(events));
            Assert.Equal(new[] { "clear", "exit" }, started.TerminalCommands);

            var plain = Assert.IsType<SessionStarted>(Assert.Single(new StreamJsonParser().Parse(Line("edit", "\"subtype\": \"init\""))));
            Assert.Empty(plain.TerminalCommands);
        }

        [Fact]
        public void User_line_with_images_groups_them_with_the_text()
        {
            var parser = new StreamJsonParser();
            const string img = "{\"type\":\"image\",\"source\":{\"type\":\"base64\",\"media_type\":\"image/png\",\"data\":\"QUJD\"}}";

            var both = parser.Parse("{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":[" + img + "," + img + ",{\"type\":\"text\",\"text\":\"look\"}]}}");
            var user = Assert.IsType<UserBlock>(Assert.Single(both));
            Assert.Equal("look", Assert.IsType<TextContentBlock>(user.Block).Text);
            Assert.Equal(2, user.Images.Count);
            Assert.Equal(new ImageBlock("image/png", "QUJD"), user.Images[0]);

            var alone = parser.Parse("{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":[" + img + "]}}");
            var only = Assert.IsType<UserBlock>(Assert.Single(alone));
            Assert.Equal("", Assert.IsType<TextContentBlock>(only.Block).Text);
            Assert.Single(only.Images);

            var url = parser.Parse("{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"image\",\"source\":{\"type\":\"url\",\"url\":\"http://x\"}}]}}");
            Assert.Equal(new ImageBlock("", ""), Assert.IsType<UserBlock>(Assert.Single(url)).Images[0]);
        }

        [Fact]
        public void Stream_replay_of_a_local_command()
        {
            var events = new StreamJsonParser().Parse(Line("commands", "<local-command-stdout>Compacted"));

            Assert.Equal(new CommandOutput("Compacted "), Assert.Single(events));
        }
    }
}
