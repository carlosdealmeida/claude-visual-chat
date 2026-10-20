using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using ClaudeVisualChat.Protocol;
using Xunit;

namespace ClaudeVisualChat.Tests;

public class ChatConversationTests
{
    public static IEnumerable<object[]> Scenarios => FixtureFiles.Scenarios.Select(s => new object[] { s });

    private const string FinalEditText =
        "Done. Updated line 3 of notes.md to 'line 3 (edited)' and created todo.md with two bullet items.";

    // ---- helpers ----

    private static JsonElement J(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static string? LineType(string line)
    {
        using var doc = JsonDocument.Parse(line);
        return doc.RootElement.GetProperty("type").GetString();
    }

    private static ChatConversation Play(string scenario, bool withStreamEvents = true)
    {
        var conversation = new ChatConversation();
        foreach (var line in FixtureFiles.StreamLines(scenario))
        {
            if (withStreamEvents || LineType(line) != "stream_event")
                conversation.AppendLine(line);
        }
        return conversation;
    }

    private static ChatConversation Load(string scenario)
    {
        var conversation = new ChatConversation();
        conversation.LoadTranscript(FixtureFiles.TranscriptLines(scenario));
        return conversation;
    }

    private static IEnumerable<ChatItem> All(IEnumerable<ChatItem> items)
    {
        foreach (var item in items)
        {
            yield return item;
            if (item is ToolCallItem tool)
                foreach (var child in All(tool.Children))
                    yield return child;
        }
    }

    private static bool IsStreaming(ChatItem item) =>
        item is AssistantTextItem { IsStreaming: true } || item is ThinkingItem { IsStreaming: true };

    private static string Describe(ChatItem item) => item switch
    {
        UserMessageItem u => "user:" + u.Text,
        AssistantTextItem a => "text:" + a.Text,
        ThinkingItem t => "thinking:" + t.Text,
        ToolCallItem c => "tool:" + c.Name + ":" + c.Summary + ":" + c.Status + ":" + c.Children.Count,
        TurnFooterItem f => "footer:" + f.IsError,
        NoticeItem n => "notice:" + n.Kind,
        _ => item.GetType().Name,
    };

    private static List<string> TopLevelAssistant(ChatConversation conversation) =>
        conversation.Items
            .Where(i => i is AssistantTextItem || i is ThinkingItem || i is ToolCallItem)
            .Select(i => i.GetType().Name + ":" + (i as ToolCallItem)?.Name)
            .ToList();

    private static UserBlock ToolResult(string toolUseId, string text = "ok", bool isError = false) =>
        new UserBlock(null, new ToolResultBlock(toolUseId, text, isError), null, false, false);

    private static PermissionRequested Request(string requestId, string toolUseId, string tool = "Bash",
        string input = "{\"command\":\"ls\"}", string? suggestions = null) =>
        new PermissionRequested(requestId, tool, null, null, toolUseId, J(input),
            suggestions == null ? null : J(suggestions));

    private const string UserOi =
        "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":\"oi\"},\"parent_tool_use_id\":null,\"session_id\":\"s\"}";

    // ---- recordings ----

    [Fact]
    public void Edit_fixture_builds_tool_items()
    {
        var c = Play("edit");

        var tools = c.Items.OfType<ToolCallItem>().ToList();
        Assert.Equal(new[] { "Read", "Edit", "Write" }, tools.Select(t => t.Name));
        Assert.All(tools, t => Assert.Equal(ToolStatus.Succeeded, t.Status));

        var edit = tools[1];
        Assert.Equal("notes.md", edit.Summary);
        Assert.NotNull(edit.Diff);
        Assert.Contains(edit.Diff!, l => l.Kind == DiffKind.Removed && l.Text == "line three");
        Assert.Contains(edit.Diff!, l => l.Kind == DiffKind.Added && l.Text == "line 3 (edited)");
        Assert.False(edit.Input.GetProperty("replace_all").GetBoolean()); // input from the assistant line

        var write = tools[2];
        Assert.Equal("todo.md", write.Summary);
        Assert.Equal(new[] { "- Item 1", "- Item 2" }, write.Diff!.Select(l => l.Text));

        var read = tools[0];
        Assert.StartsWith("1\tline one", read.ResultText);
        Assert.Equal(0, read.HiddenResultLines);

        Assert.Equal(FinalEditText, Assert.Single(c.Items.OfType<AssistantTextItem>()).Text);
        Assert.Equal(3, c.Items.OfType<ThinkingItem>().Count());

        var footer = Assert.Single(c.Items.OfType<TurnFooterItem>());
        Assert.Same(footer, c.Items.Last());
        Assert.Equal(11611, footer.DurationMs);
        Assert.Equal(571, footer.OutputTokens);
        Assert.Equal(0.08504, footer.CostUsd);
        Assert.False(footer.IsError);

        Assert.False(c.IsBusy);
        Assert.Empty(c.PendingPermissions);
        Assert.Null(c.CurrentPermission);
        Assert.DoesNotContain(All(c.Items), IsStreaming);
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Partial_and_full_streams_agree(string scenario)
    {
        var partial = Play(scenario, withStreamEvents: true);
        var full = Play(scenario, withStreamEvents: false);

        Assert.Equal(All(full.Items).Select(Describe), All(partial.Items).Select(Describe));
        Assert.DoesNotContain(All(partial.Items), IsStreaming);
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Transcript_and_stream_agree(string scenario)
    {
        var stream = Play(scenario);
        var transcript = Load(scenario);

        Assert.NotEmpty(TopLevelAssistant(transcript));
        Assert.Equal(TopLevelAssistant(stream), TopLevelAssistant(transcript));
        Assert.False(transcript.IsBusy);
        Assert.DoesNotContain(All(transcript.Items), IsStreaming);
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Transcript_and_stream_agree_on_tool_status(string scenario)
    {
        static List<string> Statuses(ChatConversation c) =>
            c.Items.OfType<ToolCallItem>().Select(t => t.Name + ":" + t.Status).ToList();

        Assert.Equal(Statuses(Play(scenario)), Statuses(Load(scenario)));
    }

    [Fact]
    public void Denied_from_the_transcript_is_rejected()
    {
        var c = Load("denied");

        var write = Assert.Single(c.Items.OfType<ToolCallItem>());
        Assert.Equal("Write", write.Name);
        Assert.Equal(ToolStatus.Rejected, write.Status);
    }

    [Fact]
    public void Task_notification_is_not_shown()
    {
        var c = Load("subagent");

        var user = Assert.Single(c.Items.OfType<UserMessageItem>());
        Assert.StartsWith("Use the Agent tool", user.Text);
        Assert.DoesNotContain(All(c.Items).OfType<UserMessageItem>(), u => u.Text.Contains("task-notification"));
    }

    [Fact]
    public void Subagent_items_nest()
    {
        var c = Play("subagent");

        var agent = Assert.Single(c.Items.OfType<ToolCallItem>());
        Assert.True(agent.Name == "Agent" || agent.Name == "Task");
        Assert.NotEmpty(agent.Children);

        var parser = new StreamJsonParser();
        var blocks = FixtureFiles.StreamLines("subagent").SelectMany(parser.Parse).OfType<AssistantBlock>().ToList();
        var childBlocks = blocks.Where(b => b.ParentToolUseId != null).ToList();
        Assert.All(childBlocks, b => Assert.Equal(agent.ToolUseId, b.ParentToolUseId));
        Assert.Equal(childBlocks.Count, agent.Children.Count);
        Assert.Equal(blocks.Count(b => b.ParentToolUseId == null),
            c.Items.Count(i => i is AssistantTextItem || i is ThinkingItem || i is ToolCallItem));

        var childTools = agent.Children.OfType<ToolCallItem>().ToList();
        Assert.Equal(new[] { "Read", "Read", "Read" }, childTools.Select(t => t.Name));
        Assert.All(childTools, t => Assert.Equal(ToolStatus.Succeeded, t.Status)); // results found inside Children
        Assert.False(c.IsBusy);
    }

    [Fact]
    public void Error_and_denied_statuses()
    {
        var error = Play("error");
        var read = Assert.Single(error.Items.OfType<ToolCallItem>(), t => t.Name == "Read");
        Assert.Equal(ToolStatus.Failed, read.Status);
        Assert.StartsWith("File does not exist", read.ResultText);
        Assert.Equal(ToolStatus.Succeeded, Assert.Single(error.Items.OfType<ToolCallItem>(), t => t.Name == "Glob").Status);

        var denied = Play("denied");
        var items = denied.Items.ToList();
        var rejected = items.FindIndex(i => i is ToolCallItem { Status: ToolStatus.Rejected });
        Assert.True(rejected >= 0, "no Rejected tool in 'denied'");
        Assert.Equal("Write", ((ToolCallItem)items[rejected]).Name);
        Assert.Contains(items.Skip(rejected + 1), i => i is AssistantTextItem);
    }

    [Fact]
    public void Interrupted_fixture()
    {
        var c = new ChatConversation();
        var sawRequest = false;
        foreach (var line in FixtureFiles.StreamLines("interrupted"))
        {
            c.AppendLine(line);
            var type = LineType(line);
            if (type == "control_request")
            {
                Assert.NotNull(c.CurrentPermission);
                sawRequest = true;
            }
            else if (type == "control_cancel_request")
            {
                Assert.Empty(c.PendingPermissions); // the request leaves as soon as it is cancelled
                Assert.Null(c.CurrentPermission);
            }
        }

        Assert.True(sawRequest);
        Assert.Contains(c.Items, i => i is NoticeItem { Kind: NoticeKind.Interrupted });
        Assert.Empty(c.PendingPermissions);
        Assert.DoesNotContain(c.Items, i => i is NoticeItem { Kind: NoticeKind.TurnFailed });
        Assert.Equal(ToolStatus.Rejected, Assert.Single(c.Items.OfType<ToolCallItem>()).Status);
        Assert.True(Assert.Single(c.Items.OfType<TurnFooterItem>()).IsError);
        Assert.False(c.IsBusy);
    }

    [Fact]
    public void Session_details_follow_claude()
    {
        var c = Play("mode-and-model");

        Assert.Equal("claude-sonnet-5-5", c.Model);
        Assert.Equal(@"C:\demo\app", c.WorkingDirectory);
        Assert.Equal("04c7663a-a689-456f-b56f-3d434babea06", c.SessionId);
        Assert.Equal("acceptEdits", c.PermissionMode); // after "allow always" the claude switched modes
        Assert.Equal("a.txt", Assert.Single(c.Items.OfType<ToolCallItem>()).Summary);
        Assert.Equal(2, c.Items.OfType<TurnFooterItem>().Count());
    }

    // ---- streaming ----

    [Fact]
    public void Streamed_items_grow_and_are_completed_in_place()
    {
        var c = new ChatConversation();
        var lines = FixtureFiles.StreamLines("edit");
        var firstTextDelta = Array.FindIndex(lines, l => l.Contains("\"text_delta\""));
        var editStart = Array.FindIndex(lines, l => l.Contains("\"content_block_start\"") && l.Contains("\"Edit\""));

        for (var i = 0; i <= editStart; i++) c.AppendLine(lines[i]);
        var edit = Assert.IsType<ToolCallItem>(c.Items.Last());
        Assert.Equal("Edit", edit.Name);
        Assert.Equal("", edit.Summary);
        Assert.Equal(ToolStatus.Running, edit.Status);
        Assert.Null(edit.Diff);
        Assert.True(c.IsBusy);

        for (var i = editStart + 1; i <= firstTextDelta; i++) c.AppendLine(lines[i]);
        Assert.Equal("notes.md", edit.Summary);
        Assert.NotNull(edit.Diff);
        var text = Assert.IsType<AssistantTextItem>(c.Items.Last());
        Assert.True(text.IsStreaming);
        Assert.Equal("Done", text.Text);

        var texts = new List<string>();
        text.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(AssistantTextItem.Text)) texts.Add(text.Text); };
        for (var i = firstTextDelta + 1; i < lines.Length; i++) c.AppendLine(lines[i]);

        Assert.True(texts.Count > 5);
        Assert.Equal("Done.", texts[0]);
        Assert.Same(text, Assert.Single(c.Items.OfType<AssistantTextItem>()));
        Assert.Equal(FinalEditText, text.Text);
        Assert.False(text.IsStreaming);
        Assert.Same(edit, Assert.Single(c.Items.OfType<ToolCallItem>(), t => t.Name == "Edit"));
    }

    [Fact]
    public void Input_json_deltas_do_not_show_and_stop_only_ends_streaming()
    {
        var c = new ChatConversation();
        c.Apply(new BlockStarted("m1", 0, null, new ToolUseBlock("toolu_1", "Bash", J("{}"))));
        c.Apply(new BlockDelta("m1", 0, null, DeltaKind.InputJson, "{\"command\":\"ls\"}"));
        c.Apply(new BlockStarted("m1", 1, null, new TextContentBlock("")));
        c.Apply(new BlockDelta("m1", 1, null, DeltaKind.Text, "Hel"));
        c.Apply(new BlockDelta("m1", 1, null, DeltaKind.Text, "lo"));
        c.Apply(new BlockStopped("m1", 0, null));
        c.Apply(new BlockStopped("m1", 1, null));

        var tool = Assert.IsType<ToolCallItem>(c.Items[0]);
        Assert.Equal("", tool.Summary);
        Assert.Equal(ToolStatus.Running, tool.Status);
        var text = Assert.IsType<AssistantTextItem>(c.Items[1]);
        Assert.Equal("Hello", text.Text);
        Assert.False(text.IsStreaming);

        // the assistant line arriving after the stop still completes the same items
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("toolu_1", "Bash", J("{\"command\":\"ls\"}"))));
        c.Apply(new AssistantBlock("m1", null, new TextContentBlock("Hello!")));
        Assert.Equal(2, c.Items.Count);
        Assert.Equal("ls", tool.Summary);
        Assert.Equal("Hello!", text.Text);
    }

    [Fact]
    public void Deltas_after_the_assistant_line_do_not_change_the_final_text()
    {
        var c = new ChatConversation();
        c.Apply(new BlockStarted("m1", 0, null, new TextContentBlock("")));
        c.Apply(new BlockDelta("m1", 0, null, DeltaKind.Text, "Hel"));
        c.Apply(new AssistantBlock("m1", null, new TextContentBlock("Hello")));
        c.Apply(new BlockDelta("m1", 0, null, DeltaKind.Text, "lo"));
        c.Apply(new BlockStopped("m1", 0, null));

        var text = Assert.IsType<AssistantTextItem>(Assert.Single(c.Items));
        Assert.Equal("Hello", text.Text);
        Assert.False(text.IsStreaming);
    }

    [Fact]
    public void A_live_tool_without_id_takes_the_id_of_the_assistant_line()
    {
        var c = new ChatConversation();
        c.Apply(new BlockStarted("m1", 0, null, new ToolUseBlock("", "Bash", J("{}"))));
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("t9", "Bash", J("{\"command\":\"ls\"}"))));
        c.Apply(new AssistantBlock("m2", "t9", new TextContentBlock("from the subagent")));
        c.Apply(ToolResult("t9", "done"));

        var tool = Assert.IsType<ToolCallItem>(Assert.Single(c.Items));
        Assert.Equal("t9", tool.ToolUseId);
        Assert.Equal("ls", tool.Summary);
        Assert.Equal(ToolStatus.Succeeded, tool.Status);
        Assert.Single(tool.Children);
    }

    [Fact]
    public void Turn_end_stops_whatever_is_still_streaming()
    {
        var c = new ChatConversation();
        c.Apply(new BlockStarted("m1", 0, null, new ThinkingBlock("")));
        c.Apply(new BlockDelta("m1", 0, null, DeltaKind.Thinking, "hmm"));
        var thinking = Assert.IsType<ThinkingItem>(Assert.Single(c.Items));
        Assert.True(thinking.IsStreaming);
        Assert.False(thinking.IsExpanded);
        Assert.Equal("hmm", thinking.Text);

        c.Apply(new TurnCompleted("error_during_execution", true, 10, null, null, null));

        Assert.False(thinking.IsStreaming);
        Assert.False(c.IsBusy);
    }

    // ---- results and notices ----

    [Fact]
    public void Tool_results_set_status_and_truncate_output()
    {
        var c = new ChatConversation();
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("t1", "Bash", J("{\"command\":\"seq 30\"}"))));
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("t2", "Bash", J("{\"command\":\"false\"}"))));
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("t3", "Write", J("{\"file_path\":\"a\",\"content\":\"x\"}"))));
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("t4", "Write", J("{\"file_path\":\"b\",\"content\":\"y\"}"))));
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("t5", "Write", J("{\"file_path\":\"c\",\"content\":\"z\"}"))));
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("t6", "Write", J("{\"file_path\":\"d\",\"content\":\"w\"}"))));
        var tools = c.Items.OfType<ToolCallItem>().ToList();
        Assert.All(tools, t => Assert.Equal(ToolStatus.Running, t.Status));

        var thirty = string.Join("\n", Enumerable.Range(1, 30));
        c.Apply(ToolResult("t1", thirty));
        c.Apply(ToolResult("t2", "exit code 1", isError: true));
        c.Apply(new UserBlock(null, new ToolResultBlock("t3", "nope", true), J("\"User rejected tool use\""), false, false));
        c.Apply(ToolResult("t4", "The user doesn't want to proceed with this tool use.", isError: true));
        c.Apply(new UserBlock(null, new ToolResultBlock("t5", "Do not create files.", true) { NonExecutionKind = "permission-rule" },
            J("\"Error: Do not create files.\""), false, false));
        c.Apply(new UserBlock(null, new ToolResultBlock("t6", "boom", true) { NonExecutionKind = "something-new" },
            J("\"Error: boom\""), false, false));

        Assert.Equal(ToolStatus.Succeeded, tools[0].Status);
        Assert.Equal(string.Join("\n", Enumerable.Range(1, ToolPresentation.OutputLines)), tools[0].ResultText);
        Assert.Equal(30 - ToolPresentation.OutputLines, tools[0].HiddenResultLines);
        Assert.Equal(ToolStatus.Failed, tools[1].Status);
        Assert.Equal("exit code 1", tools[1].ResultText);
        Assert.Equal(ToolStatus.Rejected, tools[2].Status);
        Assert.Equal(ToolStatus.Rejected, tools[3].Status);
        Assert.Equal(ToolStatus.Rejected, tools[4].Status); // denied with a message
        Assert.Equal(ToolStatus.Failed, tools[5].Status);   // an unknown kind keeps the error rule
    }

    [Fact]
    public void Unknown_parent_goes_to_the_top_level_and_todos_are_filled()
    {
        var c = new ChatConversation();
        c.Apply(new AssistantBlock("m1", "toolu_missing", new ToolUseBlock("t1", "TodoWrite",
            J("{\"todos\":[{\"content\":\"a\",\"status\":\"completed\"},{\"content\":\"b\",\"status\":\"in_progress\"}]}"))));

        var tool = Assert.IsType<ToolCallItem>(Assert.Single(c.Items));
        Assert.Equal(new[] { TodoStatus.Completed, TodoStatus.InProgress }, tool.Todos!.Select(t => t.Status));
        Assert.Null(tool.Diff);
        Assert.False(tool.IsExpanded);

        c.Apply(new AssistantBlock("m2", "t1", new TextContentBlock("inside")));
        Assert.Equal("inside", Assert.IsType<AssistantTextItem>(Assert.Single(tool.Children)).Text);
        Assert.Single(c.Items);
    }

    [Fact]
    public void User_text_rules()
    {
        var c = new ChatConversation();
        c.Apply(new UserBlock(null, new TextContentBlock("hidden"), null, true, false));
        Assert.Empty(c.Items);

        c.Apply(new UserBlock(null, new TextContentBlock("hello"), null, false, false));
        Assert.Equal("hello", Assert.IsType<UserMessageItem>(Assert.Single(c.Items)).Text);

        c.Apply(new UserBlock(null, new TextContentBlock("[Request interrupted by user]"), null, false, false));
        Assert.Equal(NoticeKind.Interrupted, Assert.IsType<NoticeItem>(c.Items.Last()).Kind);

        c.Apply(new UserBlock(null, new TextContentBlock("This session is being continued..."), null, false, true));
        Assert.Equal(NoticeKind.Compacted, Assert.IsType<NoticeItem>(c.Items.Last()).Kind);
        Assert.Equal(3, c.Items.Count);
    }

    [Fact]
    public void System_notices_become_notice_items()
    {
        var c = new ChatConversation();
        c.Apply(new SystemNotice("compact_boundary"));
        c.Apply(new UserBlock(null, new TextContentBlock("summary of the earlier conversation"), null, false, true));
        c.Apply(new SystemNotice("api_retry"));
        c.Apply(new SystemNotice("api_retry"));

        // the transcript marks one compaction twice (boundary and summary); a repeated notice shows once
        Assert.Equal(new[] { NoticeKind.Compacted, NoticeKind.ApiRetry },
            c.Items.Cast<NoticeItem>().Select(n => n.Kind));
    }

    [Fact]
    public void Failed_turn_adds_notice_unless_interrupted_and_the_flag_resets_each_turn()
    {
        var c = new ChatConversation();
        c.Apply(new UserBlock(null, new TextContentBlock("[Request interrupted by user]"), null, false, false));
        c.Apply(new TurnCompleted("error_during_execution", true, 1, null, null, null));
        Assert.DoesNotContain(c.Items, i => i is NoticeItem { Kind: NoticeKind.TurnFailed });

        c.Apply(new TurnCompleted("error_during_execution", true, 2, 0.5, 10, 20));
        var last = c.Items.Count - 1;
        Assert.Equal(NoticeKind.TurnFailed, Assert.IsType<NoticeItem>(c.Items[last - 1]).Kind);
        var footer = Assert.IsType<TurnFooterItem>(c.Items[last]);
        Assert.Equal(2, footer.DurationMs);
        Assert.Equal(10, footer.InputTokens);
        Assert.Equal(20, footer.OutputTokens);
        Assert.Equal(0.5, footer.CostUsd);
        Assert.True(footer.IsError);

        c.Apply(new TurnCompleted("success", false, 3, null, null, null));
        Assert.Single(c.Items, i => i is NoticeItem { Kind: NoticeKind.TurnFailed });
    }

    [Fact]
    public void Tools_left_running_by_an_interrupted_turn_are_rejected()
    {
        var c = new ChatConversation();
        // an earlier turn that ended well leaves its running tool alone
        c.Apply(new AssistantBlock("m0", null, new ToolUseBlock("t0", "Bash", J("{\"command\":\"sleep 9\"}"))));
        c.Apply(new TurnCompleted("success", false, null, null, null, null));

        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("t1", "Read", J("{\"file_path\":\"a\"}"))));
        c.Apply(ToolResult("t1"));
        c.Apply(new BlockStarted("m1", 1, null, new ToolUseBlock("t2", "Write", J("{}")))); // input still arriving
        c.Apply(new UserBlock(null, new TextContentBlock("[Request interrupted by user]"), null, false, false));
        c.Apply(new TurnCompleted("error_during_execution", true, null, null, null, null));

        var tools = c.Items.OfType<ToolCallItem>().ToDictionary(t => t.ToolUseId);
        Assert.Equal(ToolStatus.Running, tools["t0"].Status);
        Assert.Equal(ToolStatus.Succeeded, tools["t1"].Status);
        Assert.Equal(ToolStatus.Rejected, tools["t2"].Status);
    }

    [Fact]
    public void A_failed_turn_rejects_running_tools_but_not_a_subagent_still_working()
    {
        var c = new ChatConversation();
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("ta", "Agent", J("{\"description\":\"busy\"}"))));
        c.Apply(new AssistantBlock("s1", "ta", new ToolUseBlock("tb", "Read", J("{\"file_path\":\"x\"}"))));
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("tc", "Agent", J("{\"description\":\"done\"}"))));
        c.Apply(new AssistantBlock("s2", "tc", new ToolUseBlock("td", "Read", J("{\"file_path\":\"y\"}"))));
        c.Apply(new UserBlock("tc", new ToolResultBlock("td", "ok", false), null, false, false));
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("te", "Bash", J("{\"command\":\"ls\"}"))));
        c.Apply(new TurnCompleted("error_during_execution", true, null, null, null, null));

        var agents = c.Items.OfType<ToolCallItem>().ToDictionary(t => t.ToolUseId);
        Assert.Equal(ToolStatus.Running, agents["ta"].Status);                                 // its subagent still runs
        Assert.Equal(ToolStatus.Running, ((ToolCallItem)agents["ta"].Children[0]).Status);
        Assert.Equal(ToolStatus.Rejected, agents["tc"].Status);
        Assert.Equal(ToolStatus.Succeeded, ((ToolCallItem)agents["tc"].Children[0]).Status);
        Assert.Equal(ToolStatus.Rejected, agents["te"].Status);

        // a successful turn changes nothing
        c.Apply(new AssistantBlock("m3", null, new ToolUseBlock("tf", "Bash", J("{\"command\":\"ls\"}"))));
        c.Apply(new TurnCompleted("success", false, null, null, null, null));
        Assert.Equal(ToolStatus.Running, c.Items.OfType<ToolCallItem>().Single(t => t.ToolUseId == "tf").Status);
    }

    [Fact]
    public void Live_turn_after_an_interrupted_transcript_reports_failure()
    {
        var c = new ChatConversation();
        const string unanswered =
            "{\"type\":\"assistant\",\"message\":{\"id\":\"msg_h\",\"role\":\"assistant\",\"content\":[" +
            "{\"type\":\"tool_use\",\"id\":\"toolu_hist\",\"name\":\"Bash\",\"input\":{\"command\":\"ls\"}}]}}";
        c.LoadTranscript(FixtureFiles.TranscriptLines("interrupted").Append(unanswered));
        Assert.Contains(c.Items, i => i is NoticeItem { Kind: NoticeKind.Interrupted });

        c.Apply(new TurnCompleted("error_during_execution", true, null, null, null, null));

        Assert.Contains(c.Items, i => i is NoticeItem { Kind: NoticeKind.TurnFailed });
        // a call of the history without a result belongs to a past turn: the live turn's end leaves it alone
        Assert.Equal(ToolStatus.Running, c.Items.OfType<ToolCallItem>().Single(t => t.ToolUseId == "toolu_hist").Status);
    }

    // ---- permissions ----

    [Fact]
    public void Turn_end_keeps_the_requests_of_a_subagent()
    {
        var c = new ChatConversation();
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("ta", "Agent", J("{\"description\":\"bg\"}"))));
        c.Apply(new AssistantBlock("s1", "ta", new ToolUseBlock("tb", "Bash", J("{\"command\":\"ls\"}"))));
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("tc", "Bash", J("{\"command\":\"pwd\"}"))));
        c.Apply(Request("r-sub", "tb"));
        c.Apply(Request("r-main", "tc"));
        c.Apply(new PermissionRequested("r-none", "Bash", null, null, null, J("{}"), null));

        c.Apply(new TurnCompleted("success", false, null, null, null, null));

        Assert.Equal("r-sub", Assert.Single(c.PendingPermissions).RequestId);
        Assert.Equal("r-sub", c.CurrentPermission!.RequestId);

        c.Apply(new UserBlock("ta", new ToolResultBlock("tb", "ok", false), null, false, false));
        Assert.Empty(c.PendingPermissions);
    }

    [Fact]
    public void Subagent_request_leaves_by_cancel_or_dismiss()
    {
        var c = new ChatConversation();
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("ta", "Agent", J("{\"description\":\"bg\"}"))));
        c.Apply(new AssistantBlock("s1", "ta", new ToolUseBlock("tb", "Bash", J("{\"command\":\"ls\"}"))));
        c.Apply(new AssistantBlock("s1", "ta", new ToolUseBlock("tc", "Bash", J("{\"command\":\"pwd\"}"))));
        c.Apply(Request("r1", "tb"));
        c.Apply(Request("r2", "tc"));
        c.Apply(new TurnCompleted("success", false, null, null, null, null));
        Assert.Equal(2, c.PendingPermissions.Count);

        c.Apply(new PermissionCancelled("r1"));
        c.DismissPermission("r2");

        Assert.Empty(c.PendingPermissions);
    }

    [Fact]
    public void Permission_queue()
    {
        var c = new ChatConversation();
        c.Apply(new SessionStarted("s", "m", @"C:\demo\app", "default", "2.1.286"));
        var currentChanges = 0;
        c.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ChatConversation.CurrentPermission)) currentChanges++; };

        c.Apply(new PermissionRequested("r1", "Bash", "Run shell", "List files", "toolu_1", J("{\"command\":\"ls\"}"), null));
        c.Apply(Request("r2", "toolu_2", "Write", "{\"file_path\":\"C:\\\\demo\\\\app\\\\a.txt\",\"content\":\"x\"}",
            "[{\"type\":\"setMode\",\"mode\":\"acceptEdits\",\"destination\":\"session\"}]"));

        var first = c.CurrentPermission!;
        Assert.Equal("r1", first.RequestId);
        Assert.Equal("Bash", first.ToolName);
        Assert.Equal("Run shell", first.DisplayName);
        Assert.Equal("List files", first.Description);
        Assert.Equal("toolu_1", first.ToolUseId);
        Assert.Equal("ls", first.Summary);
        Assert.False(first.CanAllowAlways);
        Assert.Equal(2, c.PendingPermissions.Count);
        Assert.Equal(1, currentChanges);

        PermissionAnsweredEventArgs? answered = null;
        c.PermissionAnswered += (_, e) => answered = e;
        c.AnswerPermission(first, PermissionDecision.Allow);

        Assert.NotNull(answered);
        Assert.Same(first, answered!.Request);
        Assert.Equal(PermissionDecision.Allow, answered.Decision);
        Assert.Null(answered.Message);
        var second = c.CurrentPermission!;
        Assert.Equal("r2", second.RequestId);
        Assert.Equal("Write", second.DisplayName); // falls back to the tool name
        Assert.Equal("a.txt", second.Summary);
        Assert.True(second.CanAllowAlways);
        Assert.Equal(2, currentChanges);

        c.Apply(ToolResult("toolu_2"));
        Assert.Empty(c.PendingPermissions);
        Assert.Null(c.CurrentPermission);
        Assert.Equal(3, currentChanges);

        c.Apply(Request("r3", "toolu_3"));
        c.DismissPermission("unknown");
        Assert.Equal("r3", Assert.Single(c.PendingPermissions).RequestId);
        c.DismissPermission("r3");
        Assert.Empty(c.PendingPermissions);
    }

    [Fact]
    public void Vanishing_permission_requests()
    {
        var c = new ChatConversation();
        var answers = 0;
        c.PermissionAnswered += (_, _) => answers++;

        c.Apply(Request("r1", "toolu_1"));
        c.Apply(Request("r1", "toolu_1")); // the same request twice is kept once
        Assert.Single(c.PendingPermissions);
        var stale = c.CurrentPermission!;
        c.Apply(new PermissionCancelled("r1"));
        Assert.Empty(c.PendingPermissions);

        c.AnswerPermission(stale, PermissionDecision.Allow); // answered in a card that already left
        Assert.Equal(0, answers);

        c.Apply(Request("r2", "toolu_2"));
        c.Apply(Request("r3", "toolu_3"));
        c.Apply(new TurnCompleted("success", false, null, null, null, null));
        Assert.Empty(c.PendingPermissions);
        Assert.Null(c.CurrentPermission);
    }

    [Fact]
    public void Deny_and_allow_always_carry_message_and_decision()
    {
        var c = new ChatConversation();
        var answers = new List<PermissionAnsweredEventArgs>();
        c.PermissionAnswered += (_, e) => answers.Add(e);
        c.Apply(Request("r1", "toolu_1"));
        c.Apply(Request("r2", "toolu_2", "Write", "{}", "[{\"type\":\"setMode\",\"mode\":\"acceptEdits\",\"destination\":\"session\"}]"));

        c.AnswerPermission(c.CurrentPermission!, PermissionDecision.Deny, "not now");
        c.AnswerPermission(c.CurrentPermission!, PermissionDecision.AllowAlways);

        Assert.Equal(PermissionDecision.Deny, answers[0].Decision);
        Assert.Equal("not now", answers[0].Message);
        Assert.Equal("r2", answers[1].Request.RequestId);
        Assert.Equal(PermissionDecision.AllowAlways, answers[1].Decision);
        Assert.Empty(c.PendingPermissions);
    }

    // ---- user actions ----

    [Fact]
    public void Submit_rules()
    {
        var c = new ChatConversation();
        var submitted = new List<string>();
        c.MessageSubmitted += (_, e) => submitted.Add(e.Text);

        Assert.False(c.Submit("  "));
        Assert.False(c.Submit(""));
        Assert.Empty(submitted);
        Assert.Empty(c.Items);
        Assert.False(c.IsBusy);

        Assert.True(c.Submit("oi"));
        Assert.Equal("oi", Assert.IsType<UserMessageItem>(Assert.Single(c.Items)).Text);
        Assert.True(c.IsBusy);
        Assert.Equal(new[] { "oi" }, submitted);

        c.AppendLine(UserOi); // the claude repeating the message does not duplicate it
        Assert.Single(c.Items);

        Assert.True(c.Submit("again")); // works while busy: the claude queues it
        Assert.Equal(new[] { "oi", "again" }, submitted);
        Assert.Equal(2, c.Items.Count);

        var twice = new ChatConversation();
        twice.Submit("first");
        twice.Submit("second");
        twice.AppendLine(UserOi.Replace("\"oi\"", "\"first\""));
        twice.AppendLine(UserOi.Replace("\"oi\"", "\"second\""));
        Assert.Equal(new[] { "first", "second" }, twice.Items.Cast<UserMessageItem>().Select(u => u.Text));

        var quiet = new ChatConversation { EchoSubmittedMessages = false };
        Assert.True(quiet.Submit("oi"));
        Assert.Empty(quiet.Items);
        Assert.True(quiet.IsBusy);
        quiet.AppendLine(UserOi);
        Assert.Equal("oi", Assert.IsType<UserMessageItem>(Assert.Single(quiet.Items)).Text);
    }

    [Fact]
    public void Messages_keep_their_time()
    {
        var now = new DateTimeOffset(2026, 10, 19, 22, 27, 5, TimeSpan.FromHours(-3));
        var c = new ChatConversation { Clock = () => now };
        c.Submit("Pode tirar o print");
        Assert.Equal(now, Assert.IsType<UserMessageItem>(Assert.Single(c.Items)).Timestamp);

        // from the claude: each line brings its time; a message whose line has none has none
        var stamped = new DateTimeOffset(2026, 10, 19, 21, 31, 35, TimeSpan.Zero);
        var echo = new ChatConversation { EchoSubmittedMessages = false };
        echo.AppendLine("{\"type\":\"user\",\"timestamp\":\"2026-10-19T21:31:35Z\",\"message\":{\"role\":\"user\",\"content\":\"oi\"}}");
        echo.AppendLine("{\"type\":\"assistant\",\"timestamp\":\"2026-10-19T21:31:35Z\",\"message\":{\"id\":\"m1\",\"content\":[{\"type\":\"text\",\"text\":\"ola\"}]}}");
        echo.AppendLine("{\"type\":\"assistant\",\"message\":{\"id\":\"m2\",\"content\":[{\"type\":\"text\",\"text\":\"sem hora\"}]}}");
        Assert.Equal(stamped, Assert.IsType<UserMessageItem>(echo.Items[0]).Timestamp);
        Assert.Equal(stamped, Assert.IsType<AssistantTextItem>(echo.Items[1]).Timestamp);
        Assert.Null(Assert.IsType<AssistantTextItem>(echo.Items[2]).Timestamp);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_streamed_text_takes_the_time_of_its_assistant_line(bool withStreamEvents)
    {
        var line = FixtureFiles.StreamLines("edit").Last(l => LineType(l) == "assistant" && l.Contains(FinalEditText));
        using var doc = JsonDocument.Parse(line);
        var expected = DateTimeOffset.Parse(doc.RootElement.GetProperty("timestamp").GetString()!, System.Globalization.CultureInfo.InvariantCulture);

        var c = Play("edit", withStreamEvents);

        var text = c.Items.OfType<AssistantTextItem>().Single(t => t.Text == FinalEditText);
        Assert.Equal(expected, text.Timestamp);
    }

    [Fact]
    public void A_text_still_arriving_has_no_time_yet()
    {
        var c = new ChatConversation();
        c.AppendLine("{\"type\":\"stream_event\",\"parent_tool_use_id\":null,\"event\":{\"type\":\"message_start\",\"message\":{\"id\":\"m1\"}}}");
        c.AppendLine("{\"type\":\"stream_event\",\"parent_tool_use_id\":null,\"event\":{\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"text\",\"text\":\"\"}}}");
        var text = Assert.IsType<AssistantTextItem>(Assert.Single(c.Items));
        Assert.Null(text.Timestamp); // like the extension, the time shows once the text is whole

        var changed = new List<string?>();
        text.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        c.AppendLine("{\"type\":\"assistant\",\"timestamp\":\"2026-10-19T21:31:35Z\",\"message\":{\"id\":\"m1\",\"content\":[{\"type\":\"text\",\"text\":\"ola\"}]}}");
        Assert.Equal(new DateTimeOffset(2026, 10, 19, 21, 31, 35, TimeSpan.Zero), text.Timestamp);
        Assert.Contains(nameof(AssistantTextItem.Timestamp), changed);
    }

    [Fact]
    public void A_loaded_session_keeps_the_times()
    {
        var c = Load("edit");
        Assert.All(c.Items.OfType<UserMessageItem>(), u => Assert.NotNull(u.Timestamp));
        Assert.All(c.Items.OfType<AssistantTextItem>(), t => Assert.NotNull(t.Timestamp));
    }

    // ---- how long a thought took ----

    private const string ThinkingStart = "{\"type\":\"stream_event\",\"parent_tool_use_id\":null,\"event\":{\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"thinking\",\"thinking\":\"\"}}}";
    private const string ThinkingDelta = "{\"type\":\"stream_event\",\"parent_tool_use_id\":null,\"event\":{\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"thinking_delta\",\"thinking\":\"hm\"}}}";
    private const string ThinkingLine = "{\"type\":\"assistant\",\"message\":{\"id\":\"m1\",\"content\":[{\"type\":\"thinking\",\"thinking\":\"hm\"}]}}";
    private const string ThinkingStop = "{\"type\":\"stream_event\",\"parent_tool_use_id\":null,\"event\":{\"type\":\"content_block_stop\",\"index\":0}}";
    private const string MessageStart = "{\"type\":\"stream_event\",\"parent_tool_use_id\":null,\"event\":{\"type\":\"message_start\",\"message\":{\"id\":\"m1\"}}}";

    [Fact]
    public void A_thought_arriving_takes_its_time_from_the_clock()
    {
        var now = new DateTimeOffset(2026, 10, 19, 22, 0, 0, TimeSpan.Zero);
        var c = new ChatConversation { Clock = () => now };
        c.AppendLine(MessageStart);
        c.AppendLine(ThinkingStart);
        var thought = Assert.IsType<ThinkingItem>(Assert.Single(c.Items));
        Assert.Null(thought.DurationMs); // still thinking

        now = now.AddMilliseconds(2600);
        c.AppendLine(ThinkingDelta);
        Assert.Null(thought.DurationMs);
        c.AppendLine(ThinkingLine); // the whole thought: done
        Assert.Equal(2600, thought.DurationMs);

        now = now.AddSeconds(5);
        c.AppendLine(ThinkingStop); // the stop after the line changes nothing
        Assert.Equal(2600, thought.DurationMs);
    }

    [Fact]
    public void A_thought_stopped_before_its_line_takes_the_stop_time()
    {
        var now = new DateTimeOffset(2026, 10, 19, 22, 0, 0, TimeSpan.Zero);
        var c = new ChatConversation { Clock = () => now };
        c.AppendLine(MessageStart);
        c.AppendLine(ThinkingStart);
        now = now.AddMilliseconds(400);
        c.AppendLine(ThinkingStop);
        Assert.Equal(400, Assert.IsType<ThinkingItem>(Assert.Single(c.Items)).DurationMs);
    }

    [Fact]
    public void A_thought_from_the_history_takes_the_recorded_time()
    {
        var c = new ChatConversation();
        c.LoadTranscript(new[]
        {
            "{\"type\":\"assistant\",\"thinkingDurationMs\":1500,\"message\":{\"id\":\"m1\",\"content\":[{\"type\":\"thinking\",\"thinking\":\"hm\"}]}}",
            "{\"type\":\"assistant\",\"message\":{\"id\":\"m2\",\"content\":[{\"type\":\"thinking\",\"thinking\":\"no time\"}]}}",
        });
        var thoughts = c.Items.OfType<ThinkingItem>().ToList();
        Assert.Equal(1500, thoughts[0].DurationMs);
        Assert.Null(thoughts[1].DurationMs); // nothing to tell: "Thinking"
    }

    [Fact]
    public void Streaming_tells_when_a_text_or_a_thought_is_arriving()
    {
        var c = new ChatConversation();
        var changed = new List<string?>();
        c.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Assert.False(c.IsStreaming);

        c.AppendLine(MessageStart);
        c.AppendLine(ThinkingStart);
        Assert.True(c.IsStreaming);
        Assert.Contains(nameof(ChatConversation.IsStreaming), changed);
        c.AppendLine(ThinkingLine);
        Assert.False(c.IsStreaming); // the thought is whole

        c.AppendLine("{\"type\":\"stream_event\",\"parent_tool_use_id\":null,\"event\":{\"type\":\"content_block_start\",\"index\":1,\"content_block\":{\"type\":\"tool_use\",\"id\":\"t1\",\"name\":\"Read\",\"input\":{}}}}");
        Assert.False(c.IsStreaming); // a tool call arriving is not text

        c.AppendLine("{\"type\":\"stream_event\",\"parent_tool_use_id\":null,\"event\":{\"type\":\"content_block_start\",\"index\":2,\"content_block\":{\"type\":\"text\",\"text\":\"\"}}}");
        Assert.True(c.IsStreaming);
        c.AppendLine("{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false}");
        Assert.False(c.IsStreaming); // the turn's end stops everything
    }

    [Fact]
    public void Every_recording_ends_without_anything_streaming()
    {
        foreach (var scenario in FixtureFiles.Scenarios)
        {
            Assert.False(Play(scenario).IsStreaming, scenario);
            Assert.False(Load(scenario).IsStreaming, scenario);
        }
    }

    [Fact]
    public void Mode_comes_from_claude()
    {
        var c = new ChatConversation();
        Assert.Equal("default", c.PermissionMode);
        string? mode = null;
        c.PermissionModeRequested += (_, e) => mode = e.Mode;

        c.SetPermissionMode("plan");

        Assert.Equal("plan", mode);
        Assert.Equal("default", c.PermissionMode);

        c.Apply(new ControlAcknowledged("mode-1", false, J("{\"mode\":\"plan\"}")));
        Assert.Equal("plan", c.PermissionMode);

        c.Apply(new StatusChanged("requesting", null));
        Assert.Equal("plan", c.PermissionMode);
        c.Apply(new StatusChanged(null, "acceptEdits"));
        Assert.Equal("acceptEdits", c.PermissionMode);
    }

    [Fact]
    public void Model_and_stop_only_raise_events()
    {
        var c = new ChatConversation();
        string? model = null;
        var stops = 0;
        c.ModelRequested += (_, e) => model = e.Model;
        c.StopRequested += (_, _) => stops++;
        c.Submit("go");

        c.SetModel("sonnet");
        c.Stop();

        Assert.Equal("sonnet", model);
        Assert.Null(c.Model);
        Assert.Equal(1, stops);
        Assert.True(c.IsBusy); // busy until the claude ends the turn
    }

    [Fact]
    public void Requested_model_waits_for_the_claude_to_report_one()
    {
        var c = new ChatConversation();
        c.Apply(new SessionStarted("s", "claude-opus-4-5", null, "default", "2.1.286"));
        var changed = new List<string?>();
        string? asked = null;
        c.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        c.ModelRequested += (_, e) => asked = e.Model + " while requested " + c.RequestedModel;
        Assert.Null(c.RequestedModel);

        c.SetModel("sonnet");

        Assert.Equal("sonnet", c.RequestedModel);
        Assert.Equal("claude-opus-4-5", c.Model); // only the claude changes the model
        Assert.Equal("sonnet while requested sonnet", asked); // set before the event
        Assert.Equal(new[] { nameof(ChatConversation.RequestedModel) }, changed);

        changed.Clear();
        c.Apply(new SessionStarted(null, "claude-opus-4-5", null, null, null)); // the next turn: the model in use
        Assert.Null(c.RequestedModel);
        Assert.Equal("claude-opus-4-5", c.Model);
        Assert.Equal(new[] { nameof(ChatConversation.RequestedModel) }, changed);

        c.SetModel("haiku");
        c.Apply(new SessionStarted(null, "claude-haiku-4-5", null, null, null));
        Assert.Null(c.RequestedModel);
        Assert.Equal("claude-haiku-4-5", c.Model);
    }

    [Fact]
    public void Conversation_notifies_its_state()
    {
        var c = new ChatConversation();
        var changed = new List<string?>();
        c.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        foreach (var line in FixtureFiles.StreamLines("mode-and-model")) c.AppendLine(line);

        Assert.Contains(nameof(ChatConversation.IsBusy), changed);
        Assert.Contains(nameof(ChatConversation.PermissionMode), changed);
        Assert.Contains(nameof(ChatConversation.Model), changed);
        Assert.Contains(nameof(ChatConversation.SessionId), changed);
        Assert.Contains(nameof(ChatConversation.WorkingDirectory), changed);
    }

    [Fact]
    public void Tool_items_notify_result_changes()
    {
        var c = new ChatConversation();
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("t1", "Bash", J("{\"command\":\"ls\"}"))));
        var tool = Assert.IsType<ToolCallItem>(Assert.Single(c.Items));
        var changed = new List<string?>();
        tool.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        c.Apply(ToolResult("t1", "a\nb"));
        tool.IsExpanded = true;

        Assert.Contains(nameof(ToolCallItem.Status), changed);
        Assert.Contains(nameof(ToolCallItem.ResultText), changed);
        Assert.Contains(nameof(ToolCallItem.IsExpanded), changed);
    }

    // ---- clear and history ----

    [Fact]
    public void Clear_empties_the_conversation()
    {
        var c = Play("edit");
        c.Apply(Request("r1", "toolu_x"));
        c.Submit("more");

        c.Clear();

        Assert.Empty(c.Items);
        Assert.Empty(c.PendingPermissions);
        Assert.Null(c.CurrentPermission);
        Assert.False(c.IsBusy);

        // nothing carries over: neither the echo nor an interruption of the cleared turn
        c.Submit("oi");
        c.Clear();
        c.AppendLine(UserOi);
        Assert.Equal("oi", Assert.IsType<UserMessageItem>(Assert.Single(c.Items)).Text);
        c.Apply(new UserBlock(null, new TextContentBlock("[Request interrupted by user]"), null, false, false));
        c.Clear();
        c.Apply(new TurnCompleted("error_during_execution", true, null, null, null, null));
        Assert.Contains(c.Items, i => i is NoticeItem { Kind: NoticeKind.TurnFailed });
    }

    [Fact]
    public void Load_transcript_replaces_the_items()
    {
        var c = Play("edit");
        c.LoadTranscript(FixtureFiles.TranscriptLines("error"));

        Assert.StartsWith("Use the Read tool", Assert.IsType<UserMessageItem>(c.Items[0]).Text);
        Assert.Equal(new[] { "Read", "Glob" }, c.Items.OfType<ToolCallItem>().Select(t => t.Name));
        Assert.Empty(c.Items.OfType<TurnFooterItem>());
        Assert.False(c.IsBusy);
    }
}
