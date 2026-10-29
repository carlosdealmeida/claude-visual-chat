using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ClaudeVisualChat.Protocol;
using SkiaSharp;
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
        Assert.Contains(c.Items, i => i is NoticeItem { Kind: NoticeKind.ToolInterrupted });
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
        var editLine = Array.FindIndex(lines, editStart, l => l.StartsWith("{\"type\": \"assistant\"", StringComparison.Ordinal));
        for (var i = editStart + 1; i < editLine; i++) c.AppendLine(lines[i]);
        Assert.DoesNotContain(c.Items, i => i is ToolCallItem { Name: "Edit" }); // the call's input is still arriving: no row yet
        Assert.True(c.IsBusy);

        for (var i = editLine; i <= firstTextDelta; i++) c.AppendLine(lines[i]); // the whole input comes: the row, filled
        var edit = Assert.IsType<ToolCallItem>(Assert.Single(c.Items.OfType<ToolCallItem>(), t => t.Name == "Edit"));
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
        c.Apply(new BlockStopped("m1", 0, null));
        c.Apply(new BlockStarted("m1", 1, null, new TextContentBlock("")));
        c.Apply(new BlockDelta("m1", 1, null, DeltaKind.Text, "Hel"));
        c.Apply(new BlockDelta("m1", 1, null, DeltaKind.Text, "lo"));
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
    public void A_streamed_call_appears_only_when_its_input_is_complete()
    {
        var c = new ChatConversation();
        c.Apply(new BlockStarted("m1", 0, null, new ToolUseBlock("toolu_1", "Bash", J("{}"))));
        c.Apply(new BlockDelta("m1", 0, null, DeltaKind.InputJson, "{\"comm"));
        Assert.Empty(c.Items);          // the input is still arriving: no row yet
        Assert.False(c.IsStreaming);    // and a tool's input is not what "streaming" counts

        c.Apply(new BlockStopped("m1", 0, null));
        var tool = Assert.IsType<ToolCallItem>(Assert.Single(c.Items));
        Assert.Equal("toolu_1", tool.ToolUseId);
        Assert.Equal(ToolStatus.Running, tool.Status);

        // the assistant line completes that same row
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("toolu_1", "Bash", J("{\"command\":\"ls\"}"))));
        Assert.Same(tool, Assert.Single(c.Items));
        Assert.Equal("ls", tool.Summary);
        c.Apply(ToolResult("toolu_1", "a.txt"));
        Assert.Equal(ToolStatus.Succeeded, tool.Status);
    }

    [Fact]
    public void A_streamed_call_the_turn_cuts_short_never_shows()
    {
        var c = new ChatConversation();
        c.Apply(new BlockStarted("m1", 0, null, new TextContentBlock("")));
        c.Apply(new BlockStarted("m1", 1, null, new ToolUseBlock("toolu_1", "Write", J("{}"))));
        c.Apply(new TurnCompleted("error_during_execution", true, null, null, null, null));

        Assert.DoesNotContain(c.Items, i => i is ToolCallItem);
    }

    [Fact]
    public void A_streamed_call_whose_assistant_line_comes_first_shows_then_and_once()
    {
        var c = new ChatConversation();
        c.Apply(new BlockStarted("m1", 0, null, new ToolUseBlock("toolu_1", "Bash", J("{}"))));
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("toolu_1", "Bash", J("{\"command\":\"ls\"}"))));
        var tool = Assert.IsType<ToolCallItem>(Assert.Single(c.Items));
        c.Apply(new BlockStopped("m1", 0, null));
        Assert.Same(tool, Assert.Single(c.Items));
    }

    [Fact]
    public void A_streamed_subagent_call_lands_in_its_parent_when_complete()
    {
        var c = new ChatConversation();
        c.Apply(new AssistantBlock("m0", null, new ToolUseBlock("ta", "Agent", J("{\"description\":\"d\"}"))));
        c.Apply(new BlockStarted("s1", 0, "ta", new ToolUseBlock("tb", "Read", J("{}"))));
        var agent = Assert.IsType<ToolCallItem>(Assert.Single(c.Items));
        Assert.Empty(agent.Children);
        c.Apply(new BlockStopped("s1", 0, "ta"));
        Assert.Equal("tool:Read::Running:0", Describe(Assert.Single(agent.Children)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Tool_search_calls_and_their_results_never_become_items(bool streamed)
    {
        Assert.Contains("ToolSearch", ToolPresentation.HiddenTools);
        var c = new ChatConversation();
        var input = J("{\"query\":\"select:ExitPlanMode\",\"max_results\":1}");
        if (streamed)
        {
            c.Apply(new BlockStarted("m1", 0, null, new ToolUseBlock("t1", "ToolSearch", J("{}"))));
            c.Apply(new BlockStopped("m1", 0, null));
        }
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("t1", "ToolSearch", input)));
        // the result is a list of tool_reference blocks
        c.AppendLine("{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"t1\","
            + "\"content\":[{\"type\":\"tool_reference\",\"tool_name\":\"ExitPlanMode\"}]}]},\"parent_tool_use_id\":null}");
        // a subagent's search too
        c.Apply(new AssistantBlock("m0", null, new ToolUseBlock("ta", "Agent", J("{\"description\":\"d\"}"))));
        c.Apply(new AssistantBlock("s1", "ta", new ToolUseBlock("t2", "ToolSearch", input)));
        c.Apply(new UserBlock("ta", new ToolResultBlock("t2", "", false), null, false, false));

        var agent = Assert.IsType<ToolCallItem>(Assert.Single(c.Items)); // only the Agent call
        Assert.Equal("ta", agent.ToolUseId);
        Assert.Empty(agent.Children);
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
    public void Blocks_that_arrived_without_a_message_id_are_completed_in_place()
    {
        // opened in the middle of a reply: the message_start was missed, so the live blocks carry no message id
        var c = new ChatConversation();
        c.Apply(new BlockStarted("", 0, null, new ThinkingBlock("")));
        c.Apply(new BlockDelta("", 0, null, DeltaKind.Thinking, "hm"));
        c.Apply(new BlockStopped("", 0, null));
        c.Apply(new AssistantBlock("m1", null, new ThinkingBlock("hm")));
        c.Apply(new BlockStarted("", 1, null, new TextContentBlock("")));
        c.Apply(new BlockDelta("", 1, null, DeltaKind.Text, "Hel"));
        c.Apply(new AssistantBlock("m1", null, new TextContentBlock("Hello")));
        c.Apply(new BlockStopped("", 1, null));
        c.Apply(new BlockStarted("", 2, null, new ToolUseBlock("toolu_1", "Bash", J("{}"))));
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("toolu_1", "Bash", J("{\"command\":\"ls\"}"))));

        Assert.Equal(new[] { "thinking:hm", "text:Hello", "tool:Bash:ls:Running:0" }, c.Items.Select(Describe));
        Assert.DoesNotContain(c.Items, IsStreaming);
    }

    [Fact]
    public void A_subagent_line_does_not_complete_a_main_block_without_a_message_id()
    {
        var c = new ChatConversation();
        c.Apply(new AssistantBlock("m0", null, new ToolUseBlock("toolu_task", "Task", J("{}"))));
        c.Apply(new BlockStarted("", 0, null, new TextContentBlock("")));
        c.Apply(new BlockDelta("", 0, null, DeltaKind.Text, "main"));
        c.Apply(new AssistantBlock("m_sub", "toolu_task", new TextContentBlock("from the subagent")));
        c.Apply(new AssistantBlock("m1", null, new TextContentBlock("main text")));

        var task = Assert.IsType<ToolCallItem>(c.Items[0]);
        Assert.Equal("text:from the subagent", Describe(Assert.Single(task.Children)));
        Assert.Equal("text:main text", Describe(Assert.Single(c.Items.OfType<AssistantTextItem>())));
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Opened_in_the_middle_of_a_reply_shows_each_block_once(string scenario)
    {
        var lines = FixtureFiles.StreamLines(scenario);
        var firstStart = Array.FindIndex(lines, l => l.Contains("\"message_start\""));
        Assert.True(firstStart >= 0);
        var after = lines.Skip(firstStart + 1).ToList();

        var partial = new ChatConversation();
        foreach (var line in after) partial.AppendLine(line);
        var full = new ChatConversation();
        foreach (var line in after.Where(l => LineType(l) != "stream_event")) full.AppendLine(line);

        Assert.Equal(All(full.Items).Select(Describe), All(partial.Items).Select(Describe));
        Assert.DoesNotContain(All(partial.Items), IsStreaming);
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

    private static ToolCallItem StartTool(ChatConversation c, string id, string name, string input = "{}")
    {
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock(id, name, J(input))));
        return c.Items.OfType<ToolCallItem>().Single(t => t.ToolUseId == id);
    }

    [Fact]
    public void The_full_result_is_kept_up_to_the_cap()
    {
        var c = new ChatConversation();
        var tool = StartTool(c, "t1", "Bash", "{\"command\":\"seq 30\"}");
        var thirty = string.Join("\n", Enumerable.Range(1, 30));

        c.Apply(ToolResult("t1", thirty));

        Assert.Equal(30, tool.FullResultText!.Split('\n').Length);
        Assert.Equal(thirty, tool.FullResultText);
        Assert.Equal(ToolPresentation.OutputLines, tool.ResultText!.Split('\n').Length); // the short form stays, for compatibility
        Assert.Equal(30 - ToolPresentation.OutputLines, tool.HiddenResultLines);

        Assert.Equal(500_000, ToolPresentation.MaxKeptResultChars);
        var huge = StartTool(c, "t2", "Grep");
        c.Apply(ToolResult("t2", new string('x', ToolPresentation.MaxKeptResultChars + 1000)));
        Assert.Equal(ToolPresentation.MaxKeptResultChars, huge.FullResultText!.Length);

        var edge = StartTool(c, "t3", "Grep");
        c.Apply(ToolResult("t3", new string('y', ToolPresentation.MaxKeptResultChars)));
        Assert.Equal(ToolPresentation.MaxKeptResultChars, edge.FullResultText!.Length);

        var pending = StartTool(c, "t4", "Grep");
        Assert.Null(pending.FullResultText); // nothing before the result
    }

    [Fact]
    public void The_images_of_a_result_go_to_the_call()
    {
        var c = new ChatConversation();
        var shot = StartTool(c, "t1", "mcp__claude-in-chrome__computer", "{\"action\":\"screenshot\"}");
        var plain = StartTool(c, "t2", "Bash", "{\"command\":\"ls\"}");
        Assert.Null(shot.ResultImages);                                  // nothing before the result

        c.Apply(new UserBlock(null, new ToolResultBlock("t1", "ok", false)
        {
            Images = new[] { new ImageBlock("image/png", Convert.ToBase64String(Png(4, 3, false))) },
        }, null, false, false));
        c.Apply(ToolResult("t2", "a.txt"));

        var image = Assert.Single(shot.ResultImages!);
        Assert.Equal("image/png", image.MediaType);
        Assert.Equal((4, 3), (image.Width, image.Height));
        Assert.Null(plain.ResultImages);                                 // a result with no image has none
    }

    [Fact]
    public void Only_the_chrome_computer_action_keeps_the_images_of_its_result()
    {
        // the row of no other call draws them: a Read of an image, an MCP tool, another Chrome action keep nothing of them
        var c = new ChatConversation();
        var read = StartTool(c, "t1", "Read", "{\"file_path\":\"a.png\"}");
        var mcp = StartTool(c, "t2", "mcp__claude_ai_Gmail__get_message", "{\"id\":\"1\"}");
        var tabs = StartTool(c, "t3", "mcp__claude-in-chrome__tabs_context_mcp", "{}");
        var shot = StartTool(c, "t4", "mcp__claude-in-chrome__computer", "{\"action\":\"screenshot\"}");
        foreach (var id in new[] { "t1", "t2", "t3", "t4" })
        {
            c.Apply(new UserBlock(null, new ToolResultBlock(id, "ok", false)
            {
                Images = new[] { new ImageBlock("image/png", Convert.ToBase64String(Png(4, 3, false))) },
            }, null, false, false));
        }

        Assert.Null(read.ResultImages);
        Assert.Null(mcp.ResultImages);
        Assert.Null(tabs.ResultImages);
        Assert.Single(shot.ResultImages!);
        Assert.Equal("ok", read.FullResultText);                         // the text is kept as ever
    }

    [Fact]
    public void A_result_that_is_a_list_of_tool_references_has_an_empty_full_result()
    {
        // ToolSearch answers with [{"type":"tool_reference",...}], not with text; any call could get such a list
        var c = new ChatConversation();
        var tool = StartTool(c, "t1", "Read");

        c.AppendLine("{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"t1\","
            + "\"content\":[{\"type\":\"tool_reference\",\"tool_name\":\"ExitPlanMode\"}]}]},\"parent_tool_use_id\":null}");

        Assert.Equal(ToolStatus.Succeeded, tool.Status);
        Assert.Equal("", tool.FullResultText);
    }

    [Theory]
    [InlineData("Bash", true)]
    [InlineData("PowerShell", true)]
    [InlineData("TaskOutput", true)]
    [InlineData("Read", false)]
    [InlineData("Grep", false)]
    public void Only_command_output_loses_its_ansi_codes(string tool, bool stripped)
    {
        var c = new ChatConversation();
        var item = StartTool(c, "t1", tool);
        var colored = "\u001b[32mok\u001b[0m done";

        c.Apply(ToolResult("t1", colored));

        Assert.Equal(stripped ? "ok done" : colored, item.FullResultText);
    }

    [Fact]
    public void The_cap_counts_the_text_after_the_ansi_codes_are_gone()
    {
        var c = new ChatConversation();
        var item = StartTool(c, "t1", "Bash");
        var line = "\u001b[31m" + new string('z', 10) + "\u001b[0m\n"; // 11 characters of text, 20 with the codes
        var text = string.Concat(Enumerable.Repeat(line, 60_000));    // 660,000 characters of text

        c.Apply(ToolResult("t1", text));

        Assert.Equal(ToolPresentation.MaxKeptResultChars, item.FullResultText!.Length);
        Assert.True(item.FullResultText.IndexOf('\u001b') < 0); // (xunit's DoesNotContain(string) is culture-aware, and ESC is ignorable)
    }

    [Fact]
    public void A_refusal_with_a_typed_reason_keeps_the_reason()
    {
        var write = Assert.Single(Play("denied").Items.OfType<ToolCallItem>());
        Assert.Equal(ToolStatus.Rejected, write.Status);
        Assert.Equal("Do not create files, just explain.", write.RejectionReason);
        Assert.Equal("Do not create files, just explain.", write.FullResultText);

        var fromFile = Assert.Single(Load("denied").Items.OfType<ToolCallItem>());
        Assert.Equal("Do not create files, just explain.", fromFile.RejectionReason); // the session file says it with toolDenialKind
    }

    [Fact]
    public void A_refusal_with_the_standard_sentence_has_no_reason()
    {
        var tool = Assert.Single(Play("interrupted").Items.OfType<ToolCallItem>());
        Assert.Equal(ToolStatus.Rejected, tool.Status);
        Assert.Null(tool.RejectionReason);
        Assert.StartsWith("The user doesn't want to proceed", tool.FullResultText);

        var c = new ChatConversation();
        var plain = StartTool(c, "t1", "Write");
        c.Apply(new UserBlock(null, new ToolResultBlock("t1", "The user doesn't want to proceed with this tool use.", true) { NonExecutionKind = "user-rejected" },
            null, false, false));
        Assert.Equal(ToolStatus.Rejected, plain.Status);
        Assert.Null(plain.RejectionReason);
    }

    [Fact]
    public void A_refusal_with_the_standard_sentence_and_a_typed_reason_keeps_the_text_after_it()
    {
        const string sentence = "The user doesn't want to proceed with this tool use. The tool use was rejected (eg. if it was a file edit, the new_string was NOT written to the file). STOP what you are doing and wait for the user to tell you how to proceed. ";
        const string lead = "The user provided the following reason for the rejection: ";
        var c = new ChatConversation();
        var withMeta = StartTool(c, "t1", "Write");
        var noMeta = StartTool(c, "t2", "Bash");
        var empty = StartTool(c, "t3", "Bash");
        var alone = StartTool(c, "t4", "Bash");

        c.Apply(new UserBlock(null, new ToolResultBlock("t1", sentence + lead + "Use rg instead.\nNot grep.", true) { NonExecutionKind = "user-rejected" }, null, false, false));
        c.Apply(new UserBlock(null, new ToolResultBlock("t2", sentence + lead + "  later ", true), null, false, false)); // the text alone tells it
        c.Apply(new UserBlock(null, new ToolResultBlock("t3", sentence + lead, true) { NonExecutionKind = "user-rejected" }, null, false, false));
        c.Apply(new UserBlock(null, new ToolResultBlock("t4", sentence, true) { NonExecutionKind = "user-rejected" }, null, false, false));

        Assert.Equal("Use rg instead.\nNot grep.", withMeta.RejectionReason);
        Assert.Equal("later", noMeta.RejectionReason);
        Assert.Equal(ToolStatus.Rejected, noMeta.Status);
        Assert.Null(empty.RejectionReason); // the lead with nothing after it
        Assert.Null(alone.RejectionReason); // the sentence alone is still no reason
        Assert.StartsWith("The user doesn't want to proceed", alone.FullResultText); // and the whole text stays
    }

    [Fact]
    public void Only_a_refusal_has_a_reason()
    {
        var c = new ChatConversation();
        var failed = StartTool(c, "t1", "Bash");
        var unknown = StartTool(c, "t2", "Bash");
        var empty = StartTool(c, "t3", "Bash");
        var legacy = StartTool(c, "t4", "Write");

        c.Apply(ToolResult("t1", "exit code 1", isError: true));
        c.Apply(new UserBlock(null, new ToolResultBlock("t2", "boom", true) { NonExecutionKind = "something-new" }, null, false, false));
        c.Apply(new UserBlock(null, new ToolResultBlock("t3", "  ", true) { NonExecutionKind = "permission-rule" }, null, false, false));
        c.Apply(new UserBlock(null, new ToolResultBlock("t4", "nope", true), J("\"User rejected tool use\""), false, false));

        Assert.Null(failed.RejectionReason);  // an error is not a refusal
        Assert.Null(unknown.RejectionReason); // a kind the library does not know keeps the error rule
        Assert.Null(empty.RejectionReason);   // a refusal with nothing typed
        Assert.Null(legacy.RejectionReason);  // no meta says the user typed it
    }

    [Fact]
    public void Tool_use_interrupted_text_becomes_a_tool_interrupted_notice()
    {
        var c = new ChatConversation();
        c.Apply(new UserBlock(null, new TextContentBlock("[Request interrupted by user for tool use]"), null, false, false));
        Assert.Equal(NoticeKind.ToolInterrupted, Assert.IsType<NoticeItem>(Assert.Single(c.Items)).Kind);

        c.Apply(new UserBlock(null, new TextContentBlock("[Request interrupted by user]"), null, false, false));
        Assert.Equal(NoticeKind.Interrupted, Assert.IsType<NoticeItem>(c.Items.Last()).Kind);

        // only the two exact texts count: anything else is the user's message as written
        c.Apply(new UserBlock(null, new TextContentBlock("[Request interrupted by user for something else]"), null, false, false));
        Assert.Equal("[Request interrupted by user for something else]", Assert.IsType<UserMessageItem>(c.Items.Last()).Text);
        Assert.Equal(3, c.Items.Count);
    }

    [Fact]
    public void Either_interruption_text_marks_the_turn_as_interrupted()
    {
        var c = new ChatConversation();
        StartTool(c, "t1", "Bash");
        c.Apply(new UserBlock(null, new TextContentBlock("[Request interrupted by user for tool use]"), null, false, false));
        c.Apply(new TurnCompleted("error_during_execution", true, 1, null, null, null));

        Assert.DoesNotContain(c.Items, i => i is NoticeItem { Kind: NoticeKind.TurnFailed });
        Assert.Equal(ToolStatus.Rejected, c.Items.OfType<ToolCallItem>().Single().Status);
    }

    [Theory]
    [InlineData("stream")]
    [InlineData("transcript")]
    public void The_interrupted_recording_has_the_tool_interrupted_notice(string source)
    {
        var c = source == "stream" ? Play("interrupted") : Load("interrupted");
        Assert.Contains(c.Items, i => i is NoticeItem { Kind: NoticeKind.ToolInterrupted });
        Assert.DoesNotContain(c.Items, i => i is NoticeItem { Kind: NoticeKind.Interrupted });
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
        Assert.Equal("This session is being continued...", Assert.IsType<CompactionItem>(c.Items.Last()).Summary);
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

        // the transcript marks one compaction twice (boundary and summary): one card, with the summary in it; a repeated notice shows once
        var card = Assert.IsType<CompactionItem>(c.Items[0]);
        Assert.Equal("summary of the earlier conversation", card.Summary);
        Assert.Equal(NoticeKind.ApiRetry, Assert.IsType<NoticeItem>(c.Items[1]).Kind);
        Assert.Equal(2, c.Items.Count);
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
        c.Apply(new BlockStarted("m1", 1, null, new ToolUseBlock("t2", "Write", J("{}"))));
        c.Apply(new BlockStopped("m1", 1, null)); // its input is whole: the call shows, and nothing answers it
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
        Assert.Contains(c.Items, i => i is NoticeItem { Kind: NoticeKind.ToolInterrupted });

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

        Assert.True(c.Submit("again")); // works while busy: the claude queues it, and so does the conversation (0.6.0)
        Assert.Equal(new[] { "oi", "again" }, submitted);
        Assert.Single(c.Items);
        Assert.Equal("again", Assert.Single(c.QueuedMessages).Text);

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
    public void Undo_submit_takes_the_message_back()
    {
        var c = new ChatConversation();
        MessageSubmittedEventArgs? sent = null;
        var undone = new List<string>();
        c.MessageSubmitted += (_, e) => sent = e;
        c.SubmitUndone += (_, e) => undone.Add(e.Text);

        c.Submit("oi");
        Assert.True(c.IsBusy);
        Assert.True(c.UndoSubmit(sent!));
        Assert.Empty(c.Items);
        Assert.False(c.IsBusy);
        Assert.Equal(new[] { "oi" }, undone);

        Assert.False(c.UndoSubmit(sent!)); // once
        Assert.False(c.UndoSubmit(new MessageSubmittedEventArgs("oi"))); // not one of its messages
        Assert.False(new ChatConversation().UndoSubmit(sent!));
        Assert.Equal(new[] { "oi" }, undone);

        // the claude never repeats a message taken back, so it does not wait for that repetition
        c.Submit("tchau");
        c.AppendLine(UserOi.Replace("\"oi\"", "\"tchau\""));
        Assert.Equal("tchau", Assert.IsType<UserMessageItem>(Assert.Single(c.Items)).Text);
    }

    [Fact]
    public void Undo_submit_leaves_the_busy_state_to_the_claude()
    {
        // the claude is working: the message taken back leaves, the work goes on
        var working = new ChatConversation();
        MessageSubmittedEventArgs? sent = null;
        working.MessageSubmitted += (_, e) => sent = e;
        working.Apply(new BlockStarted("m1", 0, null, new TextContentBlock("")));
        working.Submit("more");
        Assert.True(working.UndoSubmit(sent!));
        Assert.True(working.IsBusy);
        Assert.IsType<AssistantTextItem>(Assert.Single(working.Items));

        // two messages from idle: busy until both are taken back
        var twice = new ChatConversation();
        var all = new List<MessageSubmittedEventArgs>();
        twice.MessageSubmitted += (_, e) => all.Add(e);
        twice.Submit("first");
        twice.Submit("second");
        twice.UndoSubmit(all[0]);
        Assert.True(twice.IsBusy);
        Assert.Empty(twice.Items);
        Assert.Equal("second", Assert.Single(twice.QueuedMessages).Text); // sent while busy: held (0.6.0)
        twice.UndoSubmit(all[1]);
        Assert.False(twice.IsBusy);

        // the claude answered in between: the turn is the claude's now
        var answered = new ChatConversation();
        answered.MessageSubmitted += (_, e) => sent = e;
        answered.Submit("oi");
        answered.Apply(new AssistantBlock("m1", null, new TextContentBlock("ola")));
        answered.UndoSubmit(sent!);
        Assert.True(answered.IsBusy);
        Assert.Equal("text:ola", Describe(Assert.Single(answered.Items)));
    }

    [Fact]
    public void Undo_submit_works_from_the_handler_and_without_echo()
    {
        var c = new ChatConversation();
        var undone = 0;
        c.MessageSubmitted += (_, e) => Assert.True(c.UndoSubmit(e)); // refused at once
        c.SubmitUndone += (_, _) => undone++;
        Assert.True(c.Submit("oi"));
        Assert.Empty(c.Items);
        Assert.False(c.IsBusy);
        Assert.Equal(1, undone);

        var quiet = new ChatConversation { EchoSubmittedMessages = false };
        MessageSubmittedEventArgs? sent = null;
        quiet.MessageSubmitted += (_, e) => sent = e;
        quiet.Submit("oi");
        Assert.True(quiet.UndoSubmit(sent!));
        Assert.False(quiet.IsBusy);

        // after the items were replaced, the text still comes back
        var loaded = new ChatConversation();
        var back = new List<string>();
        loaded.MessageSubmitted += (_, e) => sent = e;
        loaded.SubmitUndone += (_, e) => back.Add(e.Text);
        loaded.Submit("oi");
        loaded.LoadTranscript(FixtureFiles.TranscriptLines("bash"));
        var items = loaded.Items.Count;
        Assert.True(loaded.UndoSubmit(sent!));
        Assert.Equal(items, loaded.Items.Count);
        Assert.Equal(new[] { "oi" }, back);
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

    // ---- image attachments ----

    private static byte[] Png(int w, int h, bool noise)
    {
        using var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        if (noise)
        {
            var px = new byte[w * h * 4];
            new Random(7).NextBytes(px);
            for (var i = 3; i < px.Length; i += 4) px[i] = 255;
            System.Runtime.InteropServices.Marshal.Copy(px, 0, bmp.GetPixels(), px.Length);
        }
        else
        {
            bmp.Erase(new SKColor(30, 120, 200));
        }
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public async Task Attachments_go_with_the_message()
    {
        var c = new ChatConversation();
        Assert.Equal(AddImageResult.Added, await c.AddImageAsync(Png(20, 10, false)));
        Assert.Equal(AddImageResult.Added, await c.AddImageAsync(Png(30, 15, false)));
        MessageSubmittedEventArgs? sent = null;
        c.MessageSubmitted += (_, e) => sent = e;

        Assert.True(c.Submit("oi"));

        var item = Assert.IsType<UserMessageItem>(Assert.Single(c.Items));
        Assert.Equal(2, item.Images.Count);
        Assert.Equal(2, sent!.Images.Count);
        Assert.Equal(20, item.Images[0].Width);
        Assert.Empty(c.Attachments);
    }

    [Fact]
    public async Task Images_alone_can_be_sent()
    {
        var c = new ChatConversation();
        Assert.False(c.Submit(""));
        await c.AddImageAsync(Png(20, 10, false));

        Assert.True(c.Submit(""));

        var item = Assert.IsType<UserMessageItem>(Assert.Single(c.Items));
        Assert.Equal("", item.Text);
        Assert.Single(item.Images);
        Assert.False(c.Submit(""));
    }

    [Fact]
    public async Task Budget_and_count()
    {
        var c = new ChatConversation { MaxImages = 2 };
        Assert.Equal(AddImageResult.Added, await c.AddImageAsync(Png(8, 8, false)));
        Assert.Equal(AddImageResult.Added, await c.AddImageAsync(Png(8, 8, false)));
        Assert.Equal(AddImageResult.TooMany, await c.AddImageAsync(Png(8, 8, false)));
        Assert.Equal(AddImageResult.NotAnImage, await new ChatConversation().AddImageAsync(new byte[] { 1, 2, 3, 4 }));

        var b = new ChatConversation { ImageBudgetBytes = 50_000 };
        b.Attachments.Add(new ChatImage("image/png", Convert.ToBase64String(new byte[40_000])));
        var result = await b.AddImageAsync(Png(1920, 1080, true));

        Assert.True(result == AddImageResult.Added || result == AddImageResult.TooBig);
        Assert.True(b.Attachments.Sum(i => i.ToBytes().Length) <= 50_000);
    }

    [Fact]
    public async Task Undo_brings_the_images_back()
    {
        var c = new ChatConversation();
        MessageSubmittedEventArgs? sent = null;
        c.MessageSubmitted += (_, e) => sent = e;
        await c.AddImageAsync(Png(20, 10, false));
        c.Submit("oi");
        Assert.Empty(c.Attachments);

        Assert.True(c.UndoSubmit(sent!));
        Assert.Single(c.Attachments);

        c.Submit("de novo");
        var second = sent!;
        await c.AddImageAsync(Png(8, 8, false));
        Assert.True(c.UndoSubmit(second));
        Assert.Single(c.Attachments);
        Assert.Equal(8, c.Attachments[0].Width);
    }

    [Fact]
    public void Images_from_the_stream_and_the_session_file()
    {
        // the recording has no replayed user line (its only image is a tool result), so the stream part is replayed by hand
        var item = Assert.IsType<UserMessageItem>(Load("image").Items[0]);
        var image = Assert.Single(item.Images);
        Assert.Equal(96, image.Width);
        Assert.Equal(48, image.Height);
        Assert.Empty(Play("image").Items.OfType<UserMessageItem>());

        var live = new ChatConversation();
        live.Attachments.Add(ChatImage.FromBytes(Png(96, 48, false), "image/png"));
        live.Submit("What two colors do you see? One line.");
        var sent = Assert.Single(live.Items.OfType<UserMessageItem>()).Images;
        live.Apply(new UserBlock(null, new TextContentBlock("What two colors do you see? One line."), null, false, false)
        {
            Images = new[] { new ImageBlock(sent[0].MediaType, sent[0].Data) },
        });
        foreach (var line in FixtureFiles.StreamLines("image")) live.AppendLine(line);
        Assert.Single(live.Items.OfType<UserMessageItem>());
    }

[Fact]
    public void Image_without_data_is_left_out()
    {
        var c = new ChatConversation();
        c.Apply(new UserBlock(null, new TextContentBlock("oi"), null, false, false)
        {
            Images = new[] { new ImageBlock("image/png", "") },
        });
        Assert.Empty(Assert.IsType<UserMessageItem>(Assert.Single(c.Items)).Images);

        c.Apply(new UserBlock(null, new TextContentBlock(""), null, false, false));
        Assert.Single(c.Items);
    }

    // ---- commands, models, effort and panels ----

    private const string RateLimitLine =
        "{\"type\":\"rate_limit_event\",\"rate_limit_info\":{\"status\":\"allowed\",\"resetsAt\":1792698600,\"rateLimitType\":\"five_hour\",\"unifiedWindows\":{\"five_hour\":{\"utilization\":0.01,\"resetsAt\":1792698600},\"seven_day\":{\"utilization\":0.12,\"resetsAt\":1793016000}}},\"uuid\":\"u1\",\"session_id\":\"s1\"}";

    [Fact]
    public void Commands_come_from_the_claude_and_hide_terminal_ones()
    {
        var c = Play("commands");
        Assert.Contains(c.Commands, x => x.Name == "hello" && x.ArgumentHint == "name");

        c.Apply(new CommandsAvailable(new[] { new ChatCommand("doctor", "", ""), new ChatCommand("hello", "", "name") }));
        c.Apply(new SessionStarted(null, null, null, null, null) { TerminalCommands = new[] { "doctor" } });
        Assert.Equal(new[] { "doctor" }, c.TerminalCommands);

        var names = c.PaletteCommands.Select(x => x.Name).ToList();
        Assert.DoesNotContain("doctor", names);
        Assert.Contains("hello", names);
        foreach (var local in new[] { "context", "usage", "remote-control", "chrome", "copy" })
            Assert.Contains(local, names);
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void Palette_keeps_the_claudes_entry_for_a_local_command_it_lists()
    {
        var c = new ChatConversation();
        c.Commands = new[] { new ChatCommand("context", "Show current context usage", "") };
        Assert.Equal("Show current context usage", c.PaletteCommands.Single(x => x.Name == "context").Description);
    }

    [Fact]
    public void Local_commands_are_context_usage_remote_control_rc_chrome_copy_tasks_and_bashes()
    {
        Assert.Equal(new[] { "context", "usage", "remote-control", "rc", "chrome", "copy", "tasks", "bashes" }, ChatConversation.LocalCommands);
        Assert.IsNotType<string[]>(ChatConversation.LocalCommands);
        Assert.Throws<NotSupportedException>(() => ((System.Collections.Generic.IList<string>)ChatConversation.LocalCommands)[0] = "x");
    }

    [Fact]
    public void Local_commands_include_tasks_and_bashes()
    {
        Assert.Contains("tasks", ChatConversation.LocalCommands);
        Assert.Contains("bashes", ChatConversation.LocalCommands);
        var names = new ChatConversation().PaletteCommands.Select(x => x.Name).ToList();
        Assert.Contains("tasks", names);
        Assert.Contains("bashes", names);
    }

    [Fact]
    public void Tasks_opens_the_agent_map()
    {
        foreach (var name in new[] { "tasks", "bashes" })
        {
            var c = new ChatConversation();
            var invoked = new List<string>();
            c.CommandInvoked += (_, e) => invoked.Add(e.Name);
            c.InvokeCommand(name, "");
            Assert.Equal(ChatPanel.Agents, c.ShownPanel);
            Assert.Equal(new[] { name }, invoked);
            Assert.Empty(c.Items); // nothing is said, nothing goes to the claude
        }

        // typed in the box: the same, and the text is not sent
        var typed = new ChatConversation();
        var sent = 0;
        typed.MessageSubmitted += (_, _) => sent++;
        typed.Submit("/tasks");
        Assert.Equal(ChatPanel.Agents, typed.ShownPanel);
        Assert.Equal(0, sent);
    }

    [Fact]
    public void The_title_is_the_hosts_and_observable()
    {
        var c = new ChatConversation();
        Assert.Null(c.Title);
        var changes = new List<string?>();
        c.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        c.Title = "Fix the build";
        c.Title = "Fix the build";
        Assert.Equal(new[] { nameof(ChatConversation.Title) }, changes);
        c.Clear();
        Assert.Equal("Fix the build", c.Title);
    }

    // ---- /copy ----

    private static ChatConversation WithReplies(params string[] replies)
    {
        var c = new ChatConversation();
        foreach (var reply in replies)
        {
            c.Items.Add(new UserMessageItem("question"));
            if (reply.Length > 0) c.Items.Add(new AssistantTextItem { Text = reply });
            c.Items.Add(new TurnFooterItem(null, null, null, null, false));
        }
        return c;
    }

    private static List<string> Copies(ChatConversation c)
    {
        var copies = new List<string>();
        c.CopyRequested += (_, text) => copies.Add(text);
        return copies;
    }

    private static string LastMeta(ChatConversation c) => c.Items.OfType<MetaLineItem>().Last().Text;

    [Fact]
    public void Copy_without_a_number_copies_the_latest_reply()
    {
        var c = WithReplies("first", "second", "third");
        var copies = Copies(c);
        Assert.True(c.Submit("/copy"));
        Assert.Equal(new[] { "third" }, copies);
        Assert.Empty(c.Items.OfType<MetaLineItem>()); // the line comes with the result of the write
        Assert.False(c.IsBusy);                        // nothing goes to the claude
    }

    [Fact]
    public void Copy_two_copies_the_second_latest_reply()
    {
        var c = WithReplies("first", "second", "third");
        var copies = Copies(c);
        c.Submit("/copy 2");
        c.Submit("/copy  3 ");
        Assert.Equal(new[] { "second", "first" }, copies);
    }

    [Fact]
    public void Copy_takes_the_whole_answer_of_the_turn_as_the_copy_button_does()
    {
        var c = new ChatConversation();
        c.Items.Add(new UserMessageItem("q"));
        c.Items.Add(new AssistantTextItem { Text = "one" });
        c.Items.Add(new ToolCallItem { Name = "Bash" });
        c.Items.Add(new AssistantTextItem { Text = "two" });
        c.Items.Add(new TurnFooterItem(null, null, null, null, false));
        c.Items.Add(new MetaLineItem("a line between"));
        var copies = Copies(c);
        c.Submit("/copy");
        Assert.Equal(new[] { ChatTurns.AnswerText(c.Items, 4) }, copies);
        Assert.Equal("one\n\ntwo", copies[0]);
    }

    [Fact]
    public void Copy_does_not_count_a_turn_that_wrote_nothing_or_one_still_running()
    {
        var c = WithReplies("first", "", "third");
        c.Items.Add(new UserMessageItem("again"));
        c.Items.Add(new AssistantTextItem { Text = "being written" }); // no footer yet: not finished
        var copies = Copies(c);
        c.Submit("/copy 2");
        Assert.Equal(new[] { "first" }, copies);
        c.Submit("/copy 3");
        Assert.Equal("Only 2 assistant messages available to copy", LastMeta(c));
    }

    [Fact]
    public void Copy_with_no_reply_says_no_assistant_message_to_copy()
    {
        var c = new ChatConversation();
        var copies = Copies(c);
        c.Submit("/copy");
        Assert.Equal("No assistant message to copy", LastMeta(c));
        c.Items.Add(new UserMessageItem("q"));
        c.Items.Add(new TurnFooterItem(null, null, null, null, false)); // a turn without text is no reply
        c.Submit("/copy 2");
        Assert.Equal("No assistant message to copy", LastMeta(c));
        Assert.Empty(copies);
    }

    [Fact]
    public void Copy_beyond_the_replies_says_how_many_there_are_and_counts_only_the_latest_20()
    {
        var c = WithReplies("one", "two");
        var copies = Copies(c);
        c.Submit("/copy 3");
        Assert.Equal("Only 2 assistant messages available to copy", LastMeta(c));
        Assert.Empty(copies);

        var many = WithReplies(Enumerable.Range(1, 25).Select(i => "reply " + i).ToArray());
        copies = Copies(many);
        many.Submit("/copy 20");
        Assert.Equal(new[] { "reply 6" }, copies);
        many.Submit("/copy 21");
        Assert.Equal("Only 20 assistant messages available to copy", LastMeta(many));
        Assert.Single(copies);
    }

    [Theory]
    [InlineData("x")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("1.5")]
    [InlineData("2 3")]
    [InlineData("99999999999999999999")]
    public void Copy_with_a_bad_argument_shows_the_usage(string argument)
    {
        var c = WithReplies("one");
        var copies = Copies(c);
        c.Submit("/copy " + argument);
        Assert.Equal("Usage: /copy [N] where N is 1 (latest), 2, 3, … Got: " + argument, LastMeta(c));
        Assert.Empty(copies);
    }

    [Fact]
    public void Copy_while_busy_waits_for_the_reply_to_finish()
    {
        var c = WithReplies("old");
        var copies = Copies(c);
        c.Submit("work");
        Assert.True(c.IsBusy);
        c.Submit("/copy");
        Assert.Equal("Copying once the reply finishes", LastMeta(c));
        Assert.Empty(copies);

        c.Items.Add(new AssistantTextItem { Text = "new" }); // the reply being written
        c.Apply(new TurnCompleted("success", false, 10, null, null, null));
        Assert.Equal(new[] { "new" }, copies);               // the reply that finished, not the one before
        Assert.False(c.IsBusy);

        c.Apply(new TurnCompleted("success", false, 10, null, null, null));
        Assert.Single(copies);                               // once: the wish was spent
    }

    [Fact]
    public void Copy_while_busy_with_a_bad_argument_says_so_at_once_and_clearing_forgets_the_wait()
    {
        var c = WithReplies("old");
        var copies = Copies(c);
        c.Submit("work");
        c.Submit("/copy x");
        Assert.StartsWith("Usage: /copy [N]", LastMeta(c));
        Assert.Empty(copies);

        c.Submit("/copy 2");
        c.Clear(); // the wait belonged to the conversation that went
        c.Items.Add(new AssistantTextItem { Text = "other" });
        c.Apply(new TurnCompleted("success", false, 10, null, null, null));
        Assert.Empty(copies);
    }

    [Fact]
    public void A_copy_waiting_for_the_reply_is_forgotten_when_the_work_ends_without_a_turn_or_is_stopped()
    {
        // the message that made the conversation busy is taken back: no turn will end, and the next one is another's
        var c = WithReplies("old");
        var copies = Copies(c);
        MessageSubmittedEventArgs? sent = null;
        c.MessageSubmitted += (_, e) => sent = e;
        c.Submit("work");
        var work = sent!;
        c.Submit("/copy");
        Assert.Equal("Copying once the reply finishes", LastMeta(c));
        Assert.True(c.UndoSubmit(work));
        Assert.False(c.IsBusy);
        Assert.Equal("Copy cancelled", LastMeta(c));          // the line that said it would copy has its end
        c.Submit("later");
        c.Items.Add(new AssistantTextItem { Text = "another reply" });
        c.Apply(new TurnCompleted("success", false, 10, null, null, null));
        Assert.Empty(copies);

        // Stop: the person gave up on the reply the copy waited for
        var stopped = WithReplies("old");
        copies = Copies(stopped);
        stopped.Submit("work");
        stopped.Submit("/copy");
        stopped.Stop();
        Assert.Equal("Copy cancelled", LastMeta(stopped));
        stopped.Stop();                                           // once: nothing waits any more
        Assert.Single(stopped.Items.OfType<MetaLineItem>(), m => m.Text == "Copy cancelled");
        stopped.Items.Add(new AssistantTextItem { Text = "cut short" });
        stopped.Apply(new TurnCompleted("success", false, 10, null, null, null));
        Assert.Empty(copies);
    }

    [Fact]
    public void Copy_beyond_a_single_reply_says_so_in_the_singular_in_portuguese()
    {
        var c = WithReplies("one");
        c.Strings = ChatStrings.PortugueseBrazil;
        c.Submit("/copy 2");
        Assert.Equal("Só há 1 resposta do assistente para copiar", LastMeta(c));
        c = WithReplies("one", "two");
        c.Strings = ChatStrings.PortugueseBrazil;
        c.Submit("/copy 3");
        Assert.Equal("Só há 2 respostas do assistente para copiar", LastMeta(c));
        c.Submit("work");
        c.Submit("/copy");
        c.Stop();
        Assert.Equal("Cópia cancelada", LastMeta(c));
        c = WithReplies("one");
        c.Submit("/copy 2");
        Assert.Equal("Only 1 assistant messages available to copy", LastMeta(c)); // the English text stays as the extension's
    }

    [Fact]
    public void Reporting_a_copy_says_the_characters_and_lines_or_that_it_failed()
    {
        var c = new ChatConversation();
        c.ReportCopy(true, "hello");
        Assert.Equal("Copied to clipboard (5 characters, 1 line)", LastMeta(c));
        c.ReportCopy(true, "a\nb\n\nc");
        Assert.Equal("Copied to clipboard (6 characters, 4 lines)", LastMeta(c));
        c.ReportCopy(true, "\U0001F600"); // an emoji is two UTF-16 units
        Assert.Equal("Copied to clipboard (2 characters, 1 line)", LastMeta(c));
        c.ReportCopy(true, new string('x', 1234));
        Assert.Equal("Copied to clipboard (1,234 characters, 1 line)", LastMeta(c));
        c.ReportCopy(false, "hello");
        Assert.Equal("Could not copy to clipboard", LastMeta(c));
        Assert.Equal(5, c.Items.Count);
        Assert.All(c.Items, item => Assert.IsType<MetaLineItem>(item));
    }

    [Fact]
    public void Copy_lines_come_in_the_language_of_the_strings_given()
    {
        var c = WithReplies("one");
        c.Strings = ChatStrings.PortugueseBrazil;
        c.Submit("/copy 2");
        Assert.Equal("Só há 1 resposta do assistente para copiar", LastMeta(c));
        c.ReportCopy(true, "a\nb");
        Assert.Equal("Copiado para a área de transferência (3 caracteres, 2 linhas)", LastMeta(c));
        c.Strings = null!;
        Assert.Same(ChatStrings.English, c.Strings);
    }

    [Fact]
    public void Local_commands_raise_an_event_instead_of_a_message()
    {
        var c = new ChatConversation();
        var invoked = new List<(string, string)>();
        var sent = new List<string>();
        c.CommandInvoked += (_, e) => invoked.Add((e.Name, e.Arguments));
        c.MessageSubmitted += (_, e) => sent.Add(e.Text);

        c.Attachments.Add(new ChatImage("image/png", Convert.ToBase64String(new byte[10])));
        Assert.True(c.Submit("  /context  "));
        Assert.Equal(new[] { ("context", "") }, invoked);
        Assert.Empty(sent);
        Assert.Empty(c.Items);
        Assert.False(c.IsBusy);
        Assert.Equal(ChatPanel.Context, c.ShownPanel);
        Assert.Single(c.Attachments);

        c.Submit("/usage");
        Assert.Equal(ChatPanel.Usage, c.ShownPanel);
        c.Submit("/rc on");
        Assert.Equal(("rc", "on"), invoked.Last());
        Assert.Equal(ChatPanel.Usage, c.ShownPanel);
        Assert.Empty(sent);

        c.Submit("/compact");
        c.Submit("/hello Ana");
        c.Submit("/Context");
        c.Submit("/contexts");
        Assert.Equal(new[] { "/compact", "/hello Ana", "/Context", "/contexts" }, sent);
        Assert.Equal(3, invoked.Count);
    }

    [Fact]
    public void Panels_fill_from_the_stream()
    {
        var c = Play("commands");
        Assert.NotNull(c.ContextUsage);
        Assert.True(c.ContextUsage!.MaxTokens > 0);
        Assert.Null(c.UsageLimits);

        c.AppendLine(RateLimitLine);
        Assert.Equal(new[] { "five_hour", "seven_day" }, c.UsageLimits!.Windows.Select(w => w.Name));

        c.InvokeCommand("usage", "");
        Assert.Equal(ChatPanel.Usage, c.ShownPanel);
        c.ClosePanel();
        Assert.Equal(ChatPanel.None, c.ShownPanel);
    }

    [Fact]
    public void Effort_waits_for_the_claude()
    {
        var c = new ChatConversation();
        var asked = new List<string?>();
        c.EffortRequested += (_, e) => asked.Add(e.Effort);
        var changed = new List<string?>();
        c.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        c.SetEffort("high");
        Assert.Equal(new[] { "high" }, asked);
        Assert.Equal("high", c.RequestedEffort);
        Assert.Null(c.Effort);

        c.Apply(new SettingsApplied("claude-opus-5-5", "high", true));
        Assert.Equal("high", c.Effort);
        Assert.Null(c.RequestedEffort);
        Assert.Equal("claude-opus-5-5", c.Model);

        c.Apply(new SettingsApplied("claude-haiku-4-5-20251001", null, false));
        Assert.Equal("high", c.Effort);
        Assert.Equal("claude-haiku-4-5-20251001", c.Model);

        c.Apply(new SettingsApplied(null, null, true));
        Assert.Null(c.Effort);
        Assert.Contains(nameof(ChatConversation.Effort), changed);
    }

    [Fact]
    public void Effort_recording_ends_with_the_efforts_the_claude_reported()
    {
        // the recorded get_settings after each apply_flag_settings answered effort: null (the CLI's real behavior),
        // so the recording never confirms "low": the final state is no effort, on haiku
        var c = Play("effort");
        Assert.Null(c.Effort);
        Assert.Null(c.RequestedEffort);
        Assert.Equal("claude-haiku-4-5-20251001", c.Model);
        Assert.NotNull(c.AvailableModels);
    }

    [Fact]
    public void Current_model_follows_the_claude()
    {
        var c = Play("commands");
        var changed = new List<string?>();
        c.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        c.Apply(new SettingsApplied("claude-opus-5-5", null, false));
        Assert.Equal("Opus 5.5", c.CurrentModel!.DisplayName);
        Assert.Contains(nameof(ChatConversation.CurrentModel), changed);

        Assert.Null(c.ModelSuffix);

        // an id with a suffix finds its model
        c.Apply(new SettingsApplied("claude-opus-5-5[1m]", null, false));
        Assert.Equal("Opus 5.5", c.CurrentModel!.DisplayName);
        Assert.Equal("[1m]", c.ModelSuffix);
        Assert.NotEmpty(c.CurrentModel.EffortLevels);

        // a model that is not offered, with a suffix or only a suffix
        c.Apply(new SettingsApplied("claude-mystery-1[1m]", null, false));
        Assert.Null(c.CurrentModel);
        Assert.Null(c.ModelSuffix);
        c.Apply(new SettingsApplied("[1m]", null, false));
        Assert.Null(c.CurrentModel);

        c.Apply(new SettingsApplied("default", null, false));
        Assert.Equal("default", c.CurrentModel!.Value);

        changed.Clear();
        c.AvailableModels = null;
        Assert.Null(c.CurrentModel);
        Assert.Contains(nameof(ChatConversation.CurrentModel), changed);
    }

    [Fact]
    public void Command_lines_become_items()
    {
        var c = Load("commands");
        Assert.Contains(c.Items, i => i is UserMessageItem { Text: "/hello Ana" });
        Assert.Contains(c.Items, i => i is DividerItem { Text: "Compacted" });          // what /compact printed, in one line
    }

    [Fact]
    public void One_line_command_output_becomes_a_divider()
    {
        var c = new ChatConversation();
        c.Apply(new CommandOutput("Fast mode ON · model set to Opus 5.5\n"));           // the line's end is not another line
        var divider = Assert.IsType<DividerItem>(Assert.Single(c.Items));
        Assert.Equal("Fast mode ON · model set to Opus 5.5", divider.Text);
        Assert.Null(divider.ShortText);
        Assert.False(divider.IsOpen);
    }

    [Fact]
    public void Set_model_to_becomes_switched_to()
    {
        var c = new ChatConversation();
        c.Apply(new CommandOutput("Set model to Opus 5.5"));
        var divider = Assert.IsType<DividerItem>(Assert.Single(c.Items));
        Assert.Equal("Switched to Opus 5.5", divider.Text);
        Assert.Equal("Switched model", divider.ShortText);

        c.Strings = ChatStrings.PortugueseBrazil;                                       // in the language of the view
        c.Apply(new CommandOutput("Set model to Sonnet 5.5"));
        var other = Assert.IsType<DividerItem>(c.Items.Last());
        Assert.Equal("Modelo trocado para Sonnet 5.5", other.Text);
        Assert.Equal("Modelo trocado", other.ShortText);
    }

    [Fact]
    public void A_model_asked_for_with_set_model_prints_nothing()
    {
        var c = new ChatConversation();
        c.SetModel("opus");                                                             // the Kairós path
        c.Apply(new SettingsApplied("opus", null, false));
        Assert.Empty(c.Items);
    }

    [Fact]
    public void Multi_line_command_output_stays_command_output()
    {
        var c = new ChatConversation();
        c.Apply(new CommandOutput("Hello, Ana!\n  two spaces in"));
        c.Apply(new CommandOutput(""));                                                 // nothing to say: not a divider either
        Assert.Equal(new[] { "Hello, Ana!\n  two spaces in", "" }, c.Items.Cast<CommandOutputItem>().Select(i => i.Text));
    }

    [Fact]
    public void A_warning_informational_line_becomes_a_meta_line()
    {
        var c = new ChatConversation();
        c.Apply(new Informational("info", "just so you know"));                         // plain information is not shown
        c.Apply(new Informational("warning", "UserPromptSubmit operation blocked by hook:\n[x]: no\n\n\nOriginal prompt: forbidden"));
        var line = Assert.IsType<MetaLineItem>(Assert.Single(c.Items));
        Assert.StartsWith("UserPromptSubmit operation blocked by hook:", line.Text);
        Assert.EndsWith("Original prompt: forbidden", line.Text);
    }

    [Fact]
    public void The_recording_of_a_blocked_prompt_shows_the_hook_warning_once()
    {
        var c = new ChatConversation();
        foreach (var line in FixtureFiles.StreamLines("hook-blocked")) c.AppendLine(line);
        var meta = Assert.IsType<MetaLineItem>(Assert.Single(c.Items, i => i is MetaLineItem));
        Assert.Contains("UserPromptSubmit operation blocked by hook:", meta.Text);
        Assert.Contains("Original prompt: Now say the word forbidden in one line.", meta.Text);
    }

    [Fact]
    public void Command_echo_already_shown_by_Submit_is_not_repeated()
    {
        var c = new ChatConversation();
        c.Submit("/hello Ana");
        c.Apply(new CommandEcho("hello", "Ana"));
        c.Apply(new CommandEcho("compact", ""));
        Assert.Equal(new[] { "/hello Ana", "/compact" }, c.Items.OfType<UserMessageItem>().Select(i => i.Text));
    }

    // ---- cache, browser and Remote Control ----

    private static DateTimeOffset At(int h, int m, int s = 0) => new DateTimeOffset(2026, 10, 22, h, m, s, TimeSpan.Zero);

    private static UserBlock ToolResultAt(string id, DateTimeOffset at) =>
        new UserBlock(null, new ToolResultBlock(id, "ok", false), null, false, false) { Timestamp = at };

    [Fact]
    public void Cache_expires_an_hour_after_the_request()
    {
        var now = At(10, 0);
        var c = new ChatConversation { Clock = () => now };
        Assert.Null(c.PromptCacheExpiresAt);
        c.Submit("oi");
        now = At(10, 0, 5);
        c.Apply(new PromptCacheUsed(TimeSpan.FromHours(1), null));
        c.Apply(new PromptCacheUsed(TimeSpan.FromHours(1), null)); // repeated per block: idempotent
        Assert.Equal(At(11, 0), c.PromptCacheExpiresAt);
        Assert.Equal(TimeSpan.FromHours(1), c.PromptCacheTtl);

        now = At(10, 20, 3);
        c.Apply(ToolResultAt("t1", At(10, 20)));
        c.Apply(new PromptCacheUsed(null, null));
        Assert.Equal(At(11, 20), c.PromptCacheExpiresAt);
        Assert.Equal(TimeSpan.FromHours(1), c.PromptCacheTtl);
    }

    [Fact]
    public void A_local_command_does_not_move_the_cache_anchor()
    {
        var now = At(10, 0);
        var c = new ChatConversation { Clock = () => now };
        c.Submit("oi");
        now = At(10, 30);
        c.Submit("/usage");
        c.Apply(new PromptCacheUsed(TimeSpan.FromHours(1), null));
        Assert.Equal(At(11, 0), c.PromptCacheExpiresAt);
    }

    [Fact]
    public void Cache_from_the_session_file()
    {
        var c = new ChatConversation { Clock = () => At(18, 0) };
        c.LoadTranscript(FixtureFiles.TranscriptLines("image"));
        Assert.Equal(new DateTimeOffset(2026, 10, 22, 16, 40, 5, 418, TimeSpan.Zero) + TimeSpan.FromHours(1),
            c.PromptCacheExpiresAt);
        Assert.False(c.IsPromptCacheCompacted);

        c.Clear();
        Assert.Null(c.PromptCacheExpiresAt);
        Assert.Null(c.PromptCacheTtl);
    }

    [Fact]
    public void Compaction_marks_the_cache_cold()
    {
        var c = new ChatConversation { Clock = () => At(10, 0) };
        c.Apply(new PromptCacheUsed(TimeSpan.FromHours(1), null));
        c.Apply(new SystemNotice("compact_boundary"));
        Assert.True(c.IsPromptCacheCompacted);
        c.Apply(new PromptCacheUsed(null, null));
        Assert.False(c.IsPromptCacheCompacted);
    }

    [Fact]
    public void Clock_skew_is_capped()
    {
        var now = At(10, 0);
        var c = new ChatConversation { Clock = () => now };
        c.Apply(ToolResultAt("t1", now + TimeSpan.FromHours(2)));
        c.Apply(new PromptCacheUsed(TimeSpan.FromHours(1), null));
        Assert.Equal(now + TimeSpan.FromHours(1), c.PromptCacheExpiresAt);
    }

    [Fact]
    public void Remote_control_follows_the_bridge()
    {
        var c = new ChatConversation();
        var links = new List<string>();
        c.RemoteControlLinkRequested += (_, e) => links.Add(e.Url);
        var changed = new List<string?>();
        c.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        c.OpenRemoteControl();
        Assert.Empty(links);

        c.Apply(new RemoteControlChanged("ready", null, null));
        Assert.Equal(ChatRemoteControlStatus.Connecting, c.RemoteControlStatus);
        c.Apply(new RemoteControlChanged("connected", null, null));
        Assert.Equal(ChatRemoteControlStatus.Connected, c.RemoteControlStatus);
        c.Apply(new RemoteControlChanged("other", null, null));
        Assert.Equal(ChatRemoteControlStatus.Connected, c.RemoteControlStatus);
        c.Apply(new RemoteControlChanged("failed", null, "no network"));
        Assert.Equal(ChatRemoteControlStatus.Error, c.RemoteControlStatus);
        Assert.Equal("no network", c.RemoteControlError);

        c.Apply(new RemoteControlChanged(null, "https://claude.ai/code/x", null));
        Assert.Equal("https://claude.ai/code/x", c.RemoteControlUrl);
        Assert.Equal(ChatRemoteControlStatus.Error, c.RemoteControlStatus);
        Assert.Contains(nameof(ChatConversation.RemoteControlUrl), changed);

        c.OpenRemoteControl();
        Assert.Equal(new[] { "https://claude.ai/code/x" }, links);

        // the same through the stream
        var live = new ChatConversation();
        live.AppendLine("{\"type\":\"system\",\"subtype\":\"bridge_state\",\"state\":\"connected\"}");
        Assert.Equal(ChatRemoteControlStatus.Connected, live.RemoteControlStatus);
    }

    private const string FallbackLine =
        "{\"type\":\"system\",\"subtype\":\"model_refusal_fallback\",\"original_model\":\"claude-opus-5-5\",\"fallback_model\":\"claude-sonnet-5-5\",\"api_refusal_category\":\"cyber\"}";

    [Fact]
    public void A_model_refusal_fallback_line_sets_the_last_fallback()
    {
        var c = new ChatConversation();
        var changed = new List<string?>();
        c.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Assert.Null(c.LastModelFallback);

        c.AppendLine(FallbackLine);

        Assert.Equal(new ModelRefusalFallback("claude-opus-5-5", "claude-sonnet-5-5", "cyber"), c.LastModelFallback);
        Assert.Contains(nameof(ChatConversation.LastModelFallback), changed);
        Assert.Empty(c.Items); // no generic notice for it
    }

    [Fact]
    public void A_second_fallback_replaces_the_first_and_dismissing_clears_it()
    {
        var c = new ChatConversation();
        c.AppendLine(FallbackLine);
        c.AppendLine("{\"type\":\"system\",\"subtype\":\"model_refusal_fallback\",\"original_model\":\"claude-sonnet-5-5\",\"fallback_model\":\"claude-haiku-5\"}");
        Assert.Equal(new ModelRefusalFallback("claude-sonnet-5-5", "claude-haiku-5", null), c.LastModelFallback);

        var changed = new List<string?>();
        c.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        c.DismissModelFallback();
        Assert.Null(c.LastModelFallback);
        Assert.Contains(nameof(ChatConversation.LastModelFallback), changed);

        changed.Clear();
        c.DismissModelFallback(); // nothing to clear: nothing raised
        Assert.Empty(changed);

        c.AppendLine(FallbackLine); // a new one shows again
        Assert.NotNull(c.LastModelFallback);
    }

    [Fact]
    public void A_line_without_a_fallback_model_and_the_no_fallback_line_set_nothing()
    {
        var c = new ChatConversation();
        c.AppendLine("{\"type\":\"system\",\"subtype\":\"model_refusal_fallback\",\"original_model\":\"claude-opus-5-5\"}");
        c.AppendLine("{\"type\":\"system\",\"subtype\":\"model_refusal_no_fallback\",\"original_model\":\"claude-opus-5-5\"}");
        Assert.Null(c.LastModelFallback);
        Assert.Empty(c.Items);
    }

    [Fact]
    public void Loading_a_history_leaves_no_fallback_and_no_item()
    {
        var c = new ChatConversation();
        c.AppendLine(FallbackLine);
        c.LoadTranscript(new[] { FallbackLine });
        Assert.Null(c.LastModelFallback); // a new session load starts clean, and the old line raises nothing
        Assert.Empty(c.Items);
    }

    [Fact]
    public void The_auto_mode_notice_seen_flag_is_observable()
    {
        var c = new ChatConversation();
        var changed = new List<string?>();
        c.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Assert.False(c.AutoModeNoticeSeen);
        c.AutoModeNoticeSeen = true;
        Assert.True(c.AutoModeNoticeSeen);
        Assert.Contains(nameof(ChatConversation.AutoModeNoticeSeen), changed);
    }

    [Fact]
    public void Browser_state_is_the_hosts()
    {
        var c = new ChatConversation();
        var requests = 0;
        c.BrowserDisconnectRequested += (_, _) => requests++;
        c.DisconnectBrowser();
        Assert.Equal(0, requests); // off: nothing to disconnect

        c.BrowserStatus = ChatBrowserStatus.Connected;
        c.DisconnectBrowser();
        Assert.Equal(1, requests);
        Assert.Equal(ChatBrowserStatus.Connected, c.BrowserStatus); // changes only when the host says

        c.BrowserStatus = ChatBrowserStatus.Error;
        c.BrowserError = "boom";
        Assert.Equal("boom", c.BrowserError);

        // Clear keeps what belongs to the host
        c.Clear();
        Assert.Equal(ChatBrowserStatus.Error, c.BrowserStatus);
    }

    // ---- the queue: messages sent while Claude works (0.6.0) ----

    private const string Requesting =
        "{\"type\":\"system\",\"subtype\":\"status\",\"status\":\"requesting\",\"session_id\":\"s\"}";

    private const string ResultLine =
        "{\"type\":\"result\",\"subtype\":\"success\",\"is_error\":false,\"duration_ms\":10,\"total_cost_usd\":0.001,\"usage\":{\"input_tokens\":1,\"output_tokens\":1}}";

    private static string Lifecycle(string uuid, string state, bool ranAhead = false) =>
        "{\"type\":\"command_lifecycle\",\"command_uuid\":\"" + uuid + "\",\"state\":\"" + state + "\""
        + (ranAhead ? ",\"ran_ahead\":true" : "") + ",\"uuid\":\"x\",\"session_id\":\"s\"}";

    /// <summary>A message sent from idle, and Claude at work on it: what is sent now waits.</summary>
    private static ChatConversation Working(List<MessageSubmittedEventArgs>? sent = null)
    {
        var c = new ChatConversation();
        if (sent != null) c.MessageSubmitted += (_, e) => sent.Add(e);
        c.Submit("first");
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("t1", "Bash", J("{\"command\":\"sleep 5\"}"))));
        return c;
    }

    private static List<string> Texts(IEnumerable<UserMessageItem> items) => items.Select(u => u.Text).ToList();

    [Fact]
    public void A_message_sent_while_busy_waits_in_queued_messages()
    {
        var sent = new List<MessageSubmittedEventArgs>();
        var c = Working(sent);
        Assert.True(c.Submit("second"));

        Assert.Equal(new[] { "second" }, Texts(c.QueuedMessages));
        Assert.DoesNotContain(c.Items, i => i is UserMessageItem { Text: "second" });
        Assert.Same(sent[1].Item, c.QueuedMessages[0]);                // the item the host's message is
        Assert.Equal(2, sent.Count);                                   // it goes to the host at once all the same
        Assert.True(c.IsBusy);

        // sent from idle: to the list at once; sent while busy, even before the claude answered anything: held
        var idle = new ChatConversation();
        idle.Submit("one");
        idle.Submit("two");
        Assert.Equal(new[] { "two" }, Texts(idle.QueuedMessages));
        Assert.Equal(new[] { "user:one" }, idle.Items.Select(Describe));

        // a conversation that shows no sent message holds none either
        var quiet = new ChatConversation { EchoSubmittedMessages = false };
        quiet.Apply(new AssistantBlock("m1", null, new TextContentBlock("working")));
        quiet.Submit("more");
        Assert.Empty(quiet.QueuedMessages);
    }

    [Fact]
    public void The_next_turn_releases_the_oldest_queued_message()
    {
        // the recording (2.1.310): the second message, sent while the Bash request waited, is taken into the turn under way. The first
        // request to the API after it was sent is where it goes into the list; no turn of its own, no result of its own
        var lines = FixtureFiles.StreamLines("queued");
        var c = new ChatConversation();
        c.Submit("Run the shell command 'sleep 5 && echo done > done.txt' and then say done in one word.");
        var ask = Array.FindIndex(lines, l => LineType(l) == "control_request");
        var secondRequest = Array.FindLastIndex(lines, l => l.Contains("\"status\": \"requesting\""));
        Assert.True(ask > 0 && secondRequest > ask);
        for (var i = 0; i <= ask; i++) c.AppendLine(lines[i]);           // the first request of the turn came before: it counts for nothing

        c.Submit("After that, say the word second in one line.");
        Assert.Single(c.QueuedMessages);
        c.AnswerPermission(c.CurrentPermission!, PermissionDecision.Allow);
        for (var i = ask + 1; i < secondRequest; i++) c.AppendLine(lines[i]); // the command runs, its result comes back
        Assert.Single(c.QueuedMessages);

        c.AppendLine(lines[secondRequest]);
        Assert.Empty(c.QueuedMessages);
        for (var i = secondRequest + 1; i < lines.Length; i++) c.AppendLine(lines[i]);
        Assert.Equal(
            new[]
            {
                "user:Run the shell command 'sleep 5 && echo done > done.txt' and then say done in one word.",
                "tool:Bash:sleep 5 && echo done > done.txt:Succeeded:0",
                "user:After that, say the word second in one line.",
                "thinking:",
                "text:second",
                "footer:False",
            },
            c.Items.Select(Describe));
        Assert.False(c.IsBusy);
    }

    [Fact]
    public void A_message_sent_at_the_start_of_a_turn_waits_for_the_answer_to_the_one_before()
    {
        // between the turn's first request to the API and Claude's first block the conversation is busy with nothing answered yet:
        // what is sent then waits, and goes in after the answer to the one before, not above it
        var c = new ChatConversation();
        c.Submit("first");
        c.AppendLine(Requesting);
        c.Submit("second");
        Assert.Equal(new[] { "second" }, Texts(c.QueuedMessages));
        c.Apply(new AssistantBlock("m1", null, new TextContentBlock("answer to first")));
        c.AppendLine(Requesting);
        Assert.Empty(c.QueuedMessages);
        Assert.Equal(new[] { "user:first", "text:answer to first", "user:second" }, c.Items.Select(Describe));

        // the unanswered rule still holds: two sent before the claude started, both taken back, and it is idle again
        var sent = new List<MessageSubmittedEventArgs>();
        var twice = new ChatConversation();
        twice.MessageSubmitted += (_, e) => sent.Add(e);
        twice.Submit("a");
        twice.Submit("b");
        twice.UndoSubmit(sent[0]);
        Assert.True(twice.IsBusy);
        twice.UndoSubmit(sent[1]);
        Assert.False(twice.IsBusy);
        Assert.Empty(twice.QueuedMessages);
        Assert.Empty(twice.Items);
    }

    [Fact]
    public void Clearing_forgets_that_the_claude_told_of_its_messages()
    {
        var sent = new List<MessageSubmittedEventArgs>();
        var c = Working(sent);
        c.Submit("a");
        c.AppendLine(Lifecycle(sent[1].Uuid, "started"));
        c.Clear();                                                      // a new session: its claude may not tell
        c.Submit("first");
        c.Apply(new AssistantBlock("m1", null, new TextContentBlock("working")));
        c.Submit("b");
        c.AppendLine(Requesting);                                       // so a request releases again
        Assert.Empty(c.QueuedMessages);
        Assert.Equal("b", c.Items.OfType<UserMessageItem>().Last().Text);
    }

    [Fact]
    public void A_request_releases_every_queued_message_in_order()
    {
        var c = Working();
        c.Submit("a");
        c.Submit("b");
        Assert.Equal(new[] { "a", "b" }, Texts(c.QueuedMessages));

        c.AppendLine(Requesting);
        Assert.Empty(c.QueuedMessages);
        Assert.Equal(new[] { "user:first", "tool:Bash:sleep 5:Running:0", "user:a", "user:b" }, c.Items.Select(Describe));
        Assert.True(c.IsBusy);                                          // the turn goes on

        // a status that is no request (the mode) leaves the queue alone
        c.Submit("c");
        c.AppendLine("{\"type\":\"system\",\"subtype\":\"status\",\"status\":null,\"permissionMode\":\"acceptEdits\"}");
        Assert.Equal(new[] { "c" }, Texts(c.QueuedMessages));
    }

    [Fact]
    public void A_turn_end_without_a_request_releases_the_queued_messages()
    {
        var c = Working();
        c.Submit("a");
        c.Submit("b");
        c.AppendLine(ResultLine);
        Assert.Empty(c.QueuedMessages);
        Assert.Equal(new[] { "user:first", "tool:Bash:sleep 5:Running:0", "footer:False", "user:a", "user:b" }, c.Items.Select(Describe));
        Assert.False(c.IsBusy);                                         // the busy state stays the claude's
    }

    [Fact]
    public void A_started_lifecycle_releases_that_message_and_every_earlier_one()
    {
        var sent = new List<MessageSubmittedEventArgs>();
        var c = Working(sent);
        c.Submit("a");
        c.Submit("b");
        c.Submit("c");
        c.Submit("d");

        c.AppendLine(Lifecycle("not-one-of-them", "started"));
        Assert.Equal(4, c.QueuedMessages.Count);
        c.AppendLine(Lifecycle(sent[2].Uuid, "started"));               // "b": it and "a"
        Assert.Equal(new[] { "c", "d" }, Texts(c.QueuedMessages));
        Assert.Equal(new[] { "first", "a", "b" }, Texts(c.Items.OfType<UserMessageItem>()));

        // with a host that sends the uuid, the claude says when it takes each one: a request to the API no longer releases them
        c.AppendLine(Requesting);
        Assert.Equal(new[] { "c", "d" }, Texts(c.QueuedMessages));

        c.AppendLine(Lifecycle(sent[4].Uuid, "discarded"));             // "d" alone, ahead of "c"
        Assert.Equal(new[] { "c" }, Texts(c.QueuedMessages));
        Assert.Equal("d", c.Items.OfType<UserMessageItem>().Last().Text);
        foreach (var state in new[] { "completed", "cancelled", "refused" })
        {
            c.Submit(state);
            c.AppendLine(Lifecycle(sent.Last().Uuid, state));
            Assert.Equal(new[] { "c" }, Texts(c.QueuedMessages));
            Assert.Equal(state, c.Items.OfType<UserMessageItem>().Last().Text);
        }
        c.AppendLine(Lifecycle(sent[3].Uuid, "queued"));                // only queued: it stays
        Assert.Equal(new[] { "c" }, Texts(c.QueuedMessages));

        c.AppendLine(ResultLine);                                       // and the turn end takes the rest
        Assert.Empty(c.QueuedMessages);
    }

    [Fact]
    public void A_command_that_ran_at_once_says_so_under_its_message()
    {
        var sent = new List<MessageSubmittedEventArgs>();
        var c = Working(sent);
        c.Strings = ChatStrings.PortugueseBrazil;
        c.Submit("/status");
        c.AppendLine(Lifecycle(sent[1].Uuid, "queued", ranAhead: true));
        var held = Assert.Single(c.QueuedMessages);                     // it ran, and is still held until the claude says it is done
        Assert.Equal(ChatStrings.PortugueseBrazil.RanAtOnce, held.QueuedNotice);

        c.AppendLine(Lifecycle(sent[1].Uuid, "completed"));
        Assert.Empty(c.QueuedMessages);
        Assert.Equal(new[] { "user:/status", "MetaLineItem" }, c.Items.Skip(2).Select(Describe));
        Assert.Equal(ChatStrings.PortugueseBrazil.RanAtOnce, ((MetaLineItem)c.Items.Last()).Text);
    }

    [Fact]
    public void Undo_submit_takes_a_queued_message_back()
    {
        var sent = new List<MessageSubmittedEventArgs>();
        var undone = new List<string>();
        var c = Working(sent);
        c.SubmitUndone += (_, e) => undone.Add(e.Text);
        c.Submit("a");
        c.Submit("b");

        Assert.True(c.UndoSubmit(sent[1]));
        Assert.Equal(new[] { "b" }, Texts(c.QueuedMessages));
        Assert.Equal(new[] { "a" }, undone);
        Assert.True(c.IsBusy);                                          // the claude still works
        Assert.False(c.UndoSubmit(sent[1]));

        c.AppendLine(Requesting);
        Assert.Equal(new[] { "first", "b" }, Texts(c.Items.OfType<UserMessageItem>()));
    }

    [Fact]
    public void Clearing_or_loading_a_session_empties_the_queue()
    {
        var c = Working();
        c.Submit("a");
        c.Clear();
        Assert.Empty(c.QueuedMessages);
        c.AppendLine(Requesting);
        Assert.Empty(c.Items);                                          // what was held is gone, not released later

        var loaded = Working();
        loaded.Submit("a");
        loaded.LoadTranscript(FixtureFiles.TranscriptLines("bash"));
        Assert.Empty(loaded.QueuedMessages);
        Assert.DoesNotContain(loaded.Items, i => i is UserMessageItem { Text: "a" });
    }

    [Fact]
    public void The_claude_repeating_a_queued_message_releases_it()
    {
        var c = Working();
        c.AppendLine(UserOi.Replace("\"oi\"", "\"first\""));            // the first one's repetition: dropped, as ever
        c.Submit("oi");
        c.AppendLine(UserOi);                                           // a stream that repeats the messages took it
        Assert.Empty(c.QueuedMessages);
        Assert.Equal(new[] { "first", "oi" }, Texts(c.Items.OfType<UserMessageItem>()));
    }

    [Fact]
    public void Every_message_sent_has_its_own_uuid()
    {
        var sent = new List<MessageSubmittedEventArgs>();
        var c = new ChatConversation();
        c.MessageSubmitted += (_, e) => sent.Add(e);
        c.Submit("a");
        c.Submit("b");
        Assert.True(Guid.TryParse(sent[0].Uuid, out _));
        Assert.True(Guid.TryParse(sent[1].Uuid, out _));
        Assert.NotEqual(sent[0].Uuid, sent[1].Uuid);
        Assert.True(Guid.TryParse(new MessageSubmittedEventArgs("by hand").Uuid, out _));
    }

    // ---- the thinking tokens and the compaction card (0.6.0) ----

    private static ThinkingItem StartThought(ChatConversation c, string messageId = "m1", string? parent = null)
    {
        c.Apply(new BlockStarted(messageId, 0, parent, new ThinkingBlock("")));
        return c.Items.OfType<ThinkingItem>().Last();
    }

    [Fact]
    public void Thinking_tokens_set_the_estimate_and_later_deltas_are_ignored()
    {
        var c = new ChatConversation();
        var thought = StartThought(c);
        Assert.Null(thought.EstimatedTokens);

        c.Apply(new ThinkingTokens(217));
        Assert.Equal(217, thought.EstimatedTokens);
        c.Apply(new BlockDelta("m1", 0, null, DeltaKind.Thinking, "") { EstimatedTokens = 999 }); // once the claude counts, its deltas do not
        Assert.Equal(217, thought.EstimatedTokens);
        c.Apply(new ThinkingTokens(300));
        Assert.Equal(300, thought.EstimatedTokens);
    }

    [Fact]
    public void Without_the_count_the_deltas_are_summed_and_each_block_starts_over()
    {
        var c = new ChatConversation();
        var first = StartThought(c);
        c.Apply(new BlockDelta("m1", 0, null, DeltaKind.Thinking, "a") { EstimatedTokens = 50 });
        c.Apply(new BlockDelta("m1", 0, null, DeltaKind.Thinking, "b") { EstimatedTokens = null });
        c.Apply(new BlockDelta("m1", 0, null, DeltaKind.Thinking, "c") { EstimatedTokens = 30 });
        Assert.Equal(80, first.EstimatedTokens);

        c.Apply(new BlockStopped("m1", 0, null));
        var second = StartThought(c, "m2");
        Assert.Null(second.EstimatedTokens);
        c.Apply(new BlockDelta("m2", 0, null, DeltaKind.Thinking, "d") { EstimatedTokens = 20 });
        Assert.Equal(20, second.EstimatedTokens);
    }

    [Fact]
    public void A_subagents_thought_has_no_estimate()
    {
        var c = new ChatConversation();
        c.Apply(new AssistantBlock("m0", null, new ToolUseBlock("t1", "Agent", J("{\"description\":\"x\",\"prompt\":\"y\"}"))));
        var tool = c.Items.OfType<ToolCallItem>().Single();
        c.Apply(new BlockStarted("m1", 0, "t1", new ThinkingBlock("")));
        var thought = Assert.IsType<ThinkingItem>(Assert.Single(tool.Children));
        c.Apply(new BlockDelta("m1", 0, "t1", DeltaKind.Thinking, "a") { EstimatedTokens = 50 });
        c.Apply(new ThinkingTokens(70));                                  // the line does not say whose it is: it is the main agent's
        Assert.Null(thought.EstimatedTokens);
    }

    [Fact]
    public void The_estimate_clears_when_the_turn_ends()
    {
        var c = new ChatConversation();
        var thought = StartThought(c);
        c.Apply(new ThinkingTokens(217));
        Assert.Equal(217, thought.EstimatedTokens);

        c.Apply(new TurnCompleted("success", false, 1, null, null, null));
        Assert.Null(thought.EstimatedTokens);
        Assert.False(thought.IsStreaming);

        // and the next session counts afresh: the deltas count until the claude tells its count again
        var next = StartThought(c, "m2");
        c.Apply(new BlockDelta("m2", 0, null, DeltaKind.Thinking, "") { EstimatedTokens = 40 });
        Assert.Equal(40, next.EstimatedTokens);
    }

    [Fact]
    public void The_estimate_goes_when_the_thought_is_whole()
    {
        var c = new ChatConversation();
        var thought = StartThought(c);
        c.Apply(new ThinkingTokens(217));
        c.Apply(new AssistantBlock("m1", null, new ThinkingBlock("hm")));
        Assert.Null(thought.EstimatedTokens);
        c.Apply(new ThinkingTokens(300));                                 // a late count has no thought to land on
        Assert.Null(thought.EstimatedTokens);
    }

    [Fact]
    public void The_recorded_thinking_tokens_reach_the_item_while_it_streams()
    {
        var c = new ChatConversation();
        long? atSecondCount = null;
        foreach (var line in FixtureFiles.StreamLines("effort"))
        {
            c.AppendLine(line);
            if (line.Contains("\"estimated_tokens\": 195")) atSecondCount = c.Items.OfType<ThinkingItem>().Single().EstimatedTokens;
        }
        Assert.Equal(195, atSecondCount);
        Assert.Null(c.Items.OfType<ThinkingItem>().Single().EstimatedTokens);   // the turn is over
    }

    [Theory]
    [InlineData(0, 200, 0)]
    [InlineData(600, 200, 100)]
    [InlineData(1200, 200, 200)]
    [InlineData(5000, 200, 200)]
    public void The_first_count_climbs_for_1200_ms(long at, long target, long expected)
    {
        var up = new TokenCountUp();
        up.Set(target, 0);
        Assert.Equal(expected, up.Display(at));
    }

    [Fact]
    public void The_next_counts_climb_for_the_time_since_the_last_one_between_300_and_3000_ms()
    {
        var up = new TokenCountUp();
        up.Set(100, 0);
        Assert.Equal(100, up.Display(1200));
        up.Set(200, 1500);                                                // 1500 ms since the last: 100 to 200 in 1500 ms
        Assert.Equal(100, up.Display(1500));
        Assert.Equal(150, up.Display(2250));
        Assert.Equal(200, up.Display(3000));
        Assert.False(up.IsMoving(3000));
        up.Set(400, 3200);                                                // 1700 ms since the last
        Assert.True(up.IsMoving(4899));
        Assert.False(up.IsMoving(4900));
        up.Set(500, 3300);                                                // 100 ms since: no less than 300
        Assert.True(up.IsMoving(3599));
        Assert.False(up.IsMoving(3600));
        up.Set(600, 100_000);                                             // a long wait: no more than 3000
        Assert.True(up.IsMoving(102_999));
        Assert.False(up.IsMoving(103_000));
    }

    [Fact]
    public void A_lower_count_is_shown_at_once_and_none_forgets_the_count()
    {
        var up = new TokenCountUp();
        up.Set(500, 0);
        Assert.Equal(500, up.Display(1200));
        up.Set(120, 2000);
        Assert.Equal(120, up.Display(2000));
        Assert.False(up.IsMoving(2000));
        up.Set(null, 2100);
        Assert.Equal(0, up.Display(2100));
        up.Set(80, 2200);                                                 // as the first count again
        Assert.True(up.IsMoving(3399));
        Assert.False(up.IsMoving(3400));
    }

    [Fact]
    public void A_compact_boundary_makes_a_compaction_card_with_trigger_and_tokens()
    {
        var c = Play("commands");
        var card = Assert.Single(c.Items.OfType<CompactionItem>());
        Assert.Equal("manual", card.Trigger);
        Assert.Equal(36326, card.TokensBefore);
        Assert.False(card.IsExpanded);
        Assert.Equal("Compacted chat · manual · 36k tokens freed", ChatStrings.English.FormatCompaction(card.Trigger, card.TokensBefore));
        Assert.DoesNotContain(c.Items, i => i is NoticeItem { Kind: NoticeKind.Compacted });
    }

    [Fact]
    public void The_live_synthetic_summary_goes_into_the_card_not_into_a_user_message()
    {
        var c = Play("commands");
        var card = Assert.Single(c.Items.OfType<CompactionItem>());
        Assert.StartsWith("This session is being continued from a previous conversation", card.Summary);
        Assert.DoesNotContain(c.Items.OfType<UserMessageItem>(), m => m.Text.StartsWith("This session is being continued", StringComparison.Ordinal));
    }

    [Fact]
    public void Only_the_first_synthetic_line_after_the_boundary_is_the_summary()
    {
        var c = new ChatConversation();
        c.Apply(new UserBlock(null, new TextContentBlock("lonely"), null, false, false) { IsSynthetic = true });
        Assert.Equal("lonely", Assert.IsType<UserMessageItem>(Assert.Single(c.Items)).Text);          // no boundary before it: a message as ever

        c.Apply(new SystemNotice("compact_boundary") { Trigger = "auto", PreTokens = 160_000, PostTokens = 3000 });
        c.Apply(new UserBlock(null, new TextContentBlock("the summary"), null, false, false) { IsSynthetic = true });
        c.Apply(new UserBlock(null, new TextContentBlock("another"), null, false, false) { IsSynthetic = true });
        var card = Assert.Single(c.Items.OfType<CompactionItem>());
        Assert.Equal("the summary", card.Summary);
        Assert.Equal("auto", card.Trigger);
        Assert.Equal("another", Assert.IsType<UserMessageItem>(c.Items.Last()).Text);
        Assert.True(c.IsPromptCacheCompacted);
    }

    [Fact]
    public void A_subagents_synthetic_line_after_the_boundary_is_not_the_summary()
    {
        var c = new ChatConversation();
        c.Apply(new SystemNotice("compact_boundary") { Trigger = "auto", PreTokens = 160_000 });
        c.Apply(new UserBlock("toolu_agent", new TextContentBlock("a subagent's own line"), null, false, false) { IsSynthetic = true });
        Assert.Null(Assert.Single(c.Items.OfType<CompactionItem>()).Summary); // the main agent's compaction waits for its own
        c.Apply(new UserBlock(null, new TextContentBlock("the summary"), null, false, false) { IsSynthetic = true });
        Assert.Equal("the summary", Assert.Single(c.Items.OfType<CompactionItem>()).Summary);
    }

    [Fact]
    public void A_synthetic_line_after_the_claude_went_on_is_not_a_summary()
    {
        var c = new ChatConversation();
        c.Apply(new SystemNotice("compact_boundary") { Trigger = "manual", PreTokens = 1000 });
        c.Apply(new AssistantBlock("m1", null, new TextContentBlock("hello")));      // the claude went on without a summary
        c.Apply(new UserBlock(null, new TextContentBlock("later"), null, false, false) { IsSynthetic = true });
        Assert.Null(Assert.Single(c.Items.OfType<CompactionItem>()).Summary);
        Assert.Equal("later", Assert.IsType<UserMessageItem>(c.Items.Last()).Text);
    }

    [Fact]
    public void A_session_file_with_a_compaction_gives_one_card_with_its_summary()
    {
        var c = Load("commands");
        var card = Assert.Single(c.Items.OfType<CompactionItem>());
        Assert.StartsWith("This session is being continued", card.Summary);
        Assert.Equal("", card.Trigger);                                   // the file's boundary does not tell
        Assert.Null(card.TokensBefore);
        Assert.Equal("Compacted chat", ChatStrings.English.FormatCompaction(card.Trigger, card.TokensBefore));
    }

    [Fact]
    public void Clearing_forgets_a_card_waiting_for_its_summary()
    {
        var c = new ChatConversation();
        c.Apply(new SystemNotice("compact_boundary") { Trigger = "manual", PreTokens = 1000 });
        c.Clear();
        c.Apply(new UserBlock(null, new TextContentBlock("just a line"), null, false, false) { IsSynthetic = true });
        Assert.IsType<UserMessageItem>(Assert.Single(c.Items));
    }

    [Fact]
    public void The_status_is_what_the_claude_last_said_and_null_when_it_says_nothing()
    {
        var c = new ChatConversation();
        var changes = new List<string?>();
        c.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ChatConversation.Status)) changes.Add(c.Status); };
        Assert.Null(c.Status);

        c.Apply(new StatusChanged("compacting", null));
        Assert.Equal("compacting", c.Status);
        c.Apply(new StatusChanged("compacting", "plan"));       // the same word again: nothing to say
        c.Apply(new StatusChanged(null, "plan"));                // a line without the word clears it
        Assert.Null(c.Status);
        c.Apply(new StatusChanged("", null));
        Assert.Null(c.Status);
        Assert.Equal(new string?[] { "compacting", null }, changes);

        c.Apply(new StatusChanged("compacting", null));          // the turn ends, or the chat is cleared: it goes
        c.Apply(new TurnCompleted("success", false, 10, 0, 1, 1));
        Assert.Null(c.Status);
        c.Apply(new StatusChanged("compacting", null));
        c.Clear();
        Assert.Null(c.Status);
    }

    [Fact]
    public void A_loaded_file_with_an_unanswered_start_up_hook_does_not_leave_the_line_running()
    {
        var c = new ChatConversation();
        c.Apply(new StatusChanged("compacting", null));
        c.LoadTranscript(new[]
        {
            "{\"type\":\"system\",\"subtype\":\"hook_started\",\"hook_id\":\"h1\",\"hook_event\":\"SessionStart\"}",
            "{\"type\":\"user\",\"uuid\":\"u1\",\"timestamp\":\"2026-10-23T10:00:00.000Z\",\"message\":{\"role\":\"user\",\"content\":\"hi\"}}",
        });
        Assert.False(c.IsRunningStartupHooks);
        Assert.Null(c.Status);
        Assert.NotEmpty(c.Items);
    }

    [Fact]
    public void Start_up_hooks_run_from_their_start_to_their_response()
    {
        var c = new ChatConversation();
        var changes = 0;
        c.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ChatConversation.IsRunningStartupHooks)) changes++; };
        Assert.False(c.IsRunningStartupHooks);

        c.Apply(new HookStarted("a", "PreToolUse"));              // only SessionStart and Setup count
        c.Apply(new HookStarted("b", "UserPromptSubmit"));
        Assert.False(c.IsRunningStartupHooks);

        c.Apply(new HookStarted("c", "SessionStart"));
        c.Apply(new HookStarted("d", "Setup"));
        Assert.True(c.IsRunningStartupHooks);
        c.Apply(new HookFinished("a"));                            // another hook's answer changes nothing
        Assert.True(c.IsRunningStartupHooks);
        c.Apply(new HookFinished("c"));
        Assert.True(c.IsRunningStartupHooks);                      // one still runs
        c.Apply(new HookFinished("d"));
        Assert.False(c.IsRunningStartupHooks);
        c.Apply(new HookFinished("d"));                            // an answer twice is harmless
        Assert.Equal(2, changes);

        c.Apply(new HookStarted("e", "SessionStart"));             // one that never answers does not hold the line for ever
        c.Apply(new TurnCompleted("success", false, 10, 0, 1, 1));
        Assert.False(c.IsRunningStartupHooks);
        c.Apply(new HookStarted("f", "Setup"));
        c.Clear();
        Assert.False(c.IsRunningStartupHooks);
    }

    // ---- messages from another session, an agent, a channel ----

    private static string UserLineWith(string content, string? origin = null, string extra = "") =>
        "{\"type\":\"user\"," + (origin == null ? "" : "\"origin\":" + origin + ",") + extra +
        "\"message\":{\"role\":\"user\",\"content\":" + JsonSerializer.Serialize(content) + "},\"parent_tool_use_id\":null}";

    private const string CrossSession =
        "<cross-session-message from=\"sess-a\" from-session=\"abc123\">\nrun the tests\nplease\n</cross-session-message>";

    [Fact]
    public void A_history_task_notification_feeds_the_map_and_becomes_a_card()
    {
        var c = Load("subagent");

        // the map still reads it
        var agent = Assert.Single(c.Agents);
        Assert.Equal(AgentStatus.Completed, agent.Status);
        Assert.Equal(26316, agent.Tokens);
        Assert.Equal(3, agent.ToolUses);
        Assert.Equal(5850, agent.DurationMs);
        // and now the conversation shows it, as a message of unknown sender (the file keeps no origin)
        var card = Assert.Single(All(c.Items).OfType<ExternalMessageItem>());
        Assert.Equal("task-notification", card.Kind);
        Assert.Null(card.Sender);
        Assert.Equal("Agent \"Read and summarize three text files\" finished", card.Body);
        Assert.Equal(card.Body, card.Preview);
        Assert.True(card.UsesColumns);
        Assert.Contains(card.Sections, s => s.Label == "result" && s.Text.StartsWith("a.txt: Simple sentence", StringComparison.Ordinal));
        Assert.False(card.IsExpanded);
        Assert.DoesNotContain(All(c.Items).OfType<UserMessageItem>(), u => u.Text.Contains("task-notification"));   // still no prompt
    }

    [Fact]
    public void A_message_from_another_session_with_its_origin_is_a_card_that_names_the_sender()
    {
        var c = new ChatConversation();
        c.AppendLine(UserLineWith(CrossSession, "{\"kind\":\"peer\",\"name\":\"Maria Souza\"}"));

        var card = Assert.IsType<ExternalMessageItem>(Assert.Single(c.Items));
        Assert.Equal("cross-session", card.Kind);
        Assert.Equal("Maria-Souza", card.Sender);
        Assert.Equal("Maria Souza", card.SenderName);
        Assert.Equal("run the tests", card.Preview);
        Assert.Equal("run the tests\nplease", card.Body);
        Assert.Equal(new[] { "from", "from-session" }, card.Attributes.Select(a => a.Key).ToArray());
        Assert.Empty(c.Items.OfType<UserMessageItem>());
    }

    [Fact]
    public void A_channel_message_takes_its_sender_from_the_tag_and_an_agent_message_from_its_task()
    {
        var c = new ChatConversation();
        c.AppendLine(UserLineWith(
            "A message arrived from slack:\n<channel source=\"slack\" chat_id=\"7\">\nhi\n</channel>\n\nIMPORTANT: This is NOT from your user — it came from an external channel.",
            "{\"kind\":\"channel\"}"));
        c.AppendLine(UserLineWith(
            "<agent-message from=\"reviewer\">\nlooks good\n</agent-message>", "{\"kind\":\"peer\",\"senderTaskId\":\"a1\",\"name\":\"reviewer\"}"));

        var channel = Assert.IsType<ExternalMessageItem>(c.Items[0]);
        Assert.Equal("channel", channel.Kind);
        Assert.Equal("slack", channel.Sender);
        Assert.Equal(new[] { "source", "chat_id" }, channel.Attributes.Select(x => x.Key).ToArray());
        var agent = Assert.IsType<ExternalMessageItem>(c.Items[1]);
        Assert.Equal("agent", agent.Kind);
        Assert.Equal("reviewer", agent.Sender);
        Assert.Null(agent.SenderName);                                // the same as the short name
    }

    [Fact]
    public void Without_an_origin_the_message_is_a_card_of_unknown_sender()
    {
        var c = new ChatConversation();
        c.AppendLine(UserLineWith(CrossSession));

        var card = Assert.IsType<ExternalMessageItem>(Assert.Single(c.Items));
        Assert.Null(card.Sender);
        Assert.Equal("run the tests", card.Preview);
        Assert.Equal("run the tests\nplease", Assert.Single(card.Sections).Text);
    }

    [Fact]
    public void A_message_whose_origin_does_not_fit_stays_the_text_it_was()
    {
        var c = new ChatConversation();
        c.AppendLine(UserLineWith(CrossSession, "{\"kind\":\"human\"}"));
        c.AppendLine(UserLineWith(CrossSession, "{\"kind\":\"peer\",\"senderTaskId\":\"a1\"}"));   // a task sends agent messages, not this
        c.AppendLine(UserLineWith(CrossSession + "\n" + CrossSession));                           // two blocks
        c.AppendLine(UserLineWith("<teammate-message>hi</teammate-message>", "{\"kind\":\"human\"}"));

        Assert.Equal(4, c.Items.Count);
        Assert.All(c.Items, i => Assert.IsType<UserMessageItem>(i));
    }

    [Fact]
    public void A_message_is_a_card_only_from_the_sender_it_claims()
    {
        var c = new ChatConversation();
        const string channel = "<channel source=\"slack\">\nhi\n</channel>";
        const string agent = "<agent-message from=\"reviewer\">\nlooks good\n</agent-message>";
        c.AppendLine(UserLineWith(channel, "{\"kind\":\"peer\",\"name\":\"slack\"}"));      // a channel's message from a peer
        c.AppendLine(UserLineWith(channel, "{\"kind\":\"human\"}"));
        c.AppendLine(UserLineWith(agent, "{\"kind\":\"peer\",\"name\":\"reviewer\"}"));     // an agent's message from no task
        c.AppendLine(UserLineWith(agent, "{\"kind\":\"channel\"}"));
        c.AppendLine(UserLineWith(agent, "{\"kind\":\"human\"}"));
        c.AppendLine(UserLineWith(CrossSession, "{\"kind\":\"channel\"}"));

        Assert.Equal(6, c.Items.Count);
        Assert.All(c.Items, i => Assert.IsType<UserMessageItem>(i));
    }

    [Fact]
    public void The_agent_message_the_cli_writes_is_a_card_though_its_line_is_meta()
    {
        // the real line: origin peer with the task of the subagent, and isMeta
        var c = new ChatConversation();
        c.AppendLine(UserLineWith(
            TaggedMessageTests.RealAgentMessage,
            "{\"kind\":\"peer\",\"from\":\"a104223c975e455f4\",\"senderTaskId\":\"a104223c975e455f4\",\"name\":\"general-purpose\",\"body\":\"Task 6 question\"}",
            "\"isMeta\":true,"));

        var card = Assert.IsType<ExternalMessageItem>(Assert.Single(c.Items));
        Assert.Equal("agent", card.Kind);
        Assert.Equal("general-purpose", card.Sender);
        Assert.StartsWith("Task 6 question on R19:", card.Preview, StringComparison.Ordinal);
        Assert.Contains("(Allow -> Approved, Deny -> Rejected)", card.Body);
        Assert.DoesNotContain("agent-message", card.Body);
    }

    [Fact]
    public void A_meta_line_is_a_card_only_when_its_origin_fits()
    {
        var c = new ChatConversation();
        c.AppendLine(UserLineWith(CrossSession, extra: "\"isMeta\":true,"));                                  // no origin: stays out, as before
        c.AppendLine(UserLineWith(CrossSession, "{\"kind\":\"human\"}", "\"isMeta\":true,"));                // does not fit: stays out
        c.AppendLine(UserLineWith("<teammate-message>\nhi\n</teammate-message>", "{\"kind\":\"peer\"}", "\"isMeta\":true,"));
        Assert.Empty(c.Items);

        c.AppendLine(UserLineWith(CrossSession, "{\"kind\":\"peer\",\"name\":\"Ana\"}", "\"isMeta\":true,"));
        Assert.Equal("Ana", Assert.IsType<ExternalMessageItem>(Assert.Single(c.Items)).Sender);
    }

    [Fact]
    public void A_cross_session_message_with_the_note_of_an_agent_of_this_session_needs_that_agent()
    {
        var c = new ChatConversation();
        var text = "<cross-session-message from=\"s1\">\nhi\n</cross-session-message>\n\n" +
                   "That \"other Claude session\" is an agent working inside this same session — so this was not typed by your user.";
        c.AppendLine(UserLineWith(text, "{\"kind\":\"peer\",\"name\":\"s1\"}"));

        Assert.IsType<UserMessageItem>(Assert.Single(c.Items));                   // the note and the origin disagree: the text it was
    }

    [Fact]
    public void A_cross_session_message_without_a_name_is_a_card_from_a_peer()
    {
        var c = new ChatConversation();
        c.AppendLine(UserLineWith("<cross-session-message>\nhi\n</cross-session-message>", "{\"kind\":\"peer\"}"));
        c.AppendLine(UserLineWith("<cross-session-message from-name=\"Ana Lima\">\nhi\n</cross-session-message>", "{\"kind\":\"peer\"}"));

        Assert.Equal("peer", Assert.IsType<ExternalMessageItem>(c.Items[0]).Sender);   // the generic name the card already had
        var named = Assert.IsType<ExternalMessageItem>(c.Items[1]);
        Assert.Equal("Ana-Lima", named.Sender);
        Assert.Equal("Ana Lima", named.SenderName);
    }

    [Fact]
    public void A_wrapped_notice_is_a_card_only_without_an_origin()
    {
        var c = new ChatConversation();
        c.AppendLine(UserLineWith("<teammate-message teammate_id=\"alice\">\nplease review\n</teammate-message>"));
        c.AppendLine(UserLineWith("<local-command-caveat>Caveat: do not answer</local-command-caveat>", extra: "\"isMeta\":true,"));

        var card = Assert.IsType<ExternalMessageItem>(Assert.Single(c.Items));      // the meta line stays out, as it did
        Assert.Equal("teammate-message", card.Kind);
        Assert.Equal("teammate_id", Assert.Single(card.Attributes).Key);
        Assert.Null(card.Sender);
    }

    [Fact]
    public void A_live_background_task_notification_adds_a_meta_line_to_the_items()
    {
        var c = new ChatConversation();
        c.AppendLine(
            "{\"type\":\"system\",\"subtype\":\"task_started\",\"task_id\":\"t2\",\"tool_use_id\":\"toolu_bg\"," +
            "\"description\":\"Background look\",\"subagent_type\":\"general-purpose\",\"is_backgrounded\":true,\"spawn_depth\":1," +
            "\"task_type\":\"local_agent\"}");
        Assert.Empty(c.Items.OfType<MetaLineItem>());

        c.AppendLine(
            "{\"type\":\"system\",\"subtype\":\"task_notification\",\"task_id\":\"t2\",\"tool_use_id\":\"toolu_bg\"," +
            "\"status\":\"completed\",\"summary\":\"Agent finished: 3 files read\",\"usage\":{\"total_tokens\":10,\"tool_uses\":1,\"duration_ms\":20}}");

        var line = Assert.Single(c.Items.OfType<MetaLineItem>());
        Assert.Equal("Agent finished: 3 files read", line.Text);
        Assert.Equal(AgentStatus.Completed, Assert.Single(c.Agents).Status);        // the map reads the same event
        Assert.Empty(c.Items.OfType<ExternalMessageItem>());
    }

    [Fact]
    public void A_notification_of_a_foreground_agent_or_without_a_summary_adds_no_meta_line()
    {
        var c = new ChatConversation();
        c.AppendLine(
            "{\"type\":\"system\",\"subtype\":\"task_started\",\"task_id\":\"t1\",\"tool_use_id\":\"toolu_fg\"," +
            "\"description\":\"Look\",\"is_backgrounded\":false,\"spawn_depth\":1,\"task_type\":\"local_agent\"}");
        c.AppendLine(
            "{\"type\":\"system\",\"subtype\":\"task_started\",\"task_id\":\"t2\",\"tool_use_id\":\"toolu_bg\"," +
            "\"description\":\"Background look\",\"is_backgrounded\":true,\"spawn_depth\":1,\"task_type\":\"local_agent\"}");
        c.AppendLine("{\"type\":\"system\",\"subtype\":\"task_notification\",\"task_id\":\"t1\",\"tool_use_id\":\"toolu_fg\",\"status\":\"completed\",\"summary\":\"done\"}");
        c.AppendLine("{\"type\":\"system\",\"subtype\":\"task_notification\",\"task_id\":\"t2\",\"tool_use_id\":\"toolu_bg\",\"status\":\"completed\"}");

        Assert.Empty(c.Items.OfType<MetaLineItem>());
    }

    private const string ReplayedNotice =
        "<task-notification>\n<task-id>t9</task-id>\n<status>completed</status>\n<summary>Agent finished</summary>\n</task-notification>";

    [Fact]
    public void A_task_notification_line_with_its_origin_is_a_card_when_the_task_did_not_end_live()
    {
        // a real session file writes the origin of the notice on the line
        var c = new ChatConversation();
        c.AppendLine(UserLineWith(ReplayedNotice, "{\"kind\":\"task-notification\",\"producer\":\"session-task\"}"));

        var card = Assert.IsType<ExternalMessageItem>(Assert.Single(c.Items));
        Assert.Equal("task-notification", card.Kind);
        Assert.Null(card.Sender);
        Assert.Equal("Agent finished", card.Preview);
    }

    [Fact]
    public void A_task_notification_line_is_not_a_second_card_when_the_task_ended_live_before_it()
    {
        var c = new ChatConversation();
        c.AppendLine("{\"type\":\"system\",\"subtype\":\"task_notification\",\"task_id\":\"t9\",\"tool_use_id\":\"toolu_9\",\"status\":\"completed\",\"summary\":\"Agent finished\"}");
        c.AppendLine(UserLineWith(ReplayedNotice, "{\"kind\":\"task-notification\",\"producer\":\"session-task\"}"));
        c.AppendLine(UserLineWith(ReplayedNotice));                    // with or without the origin

        Assert.Empty(c.Items.OfType<ExternalMessageItem>());
        // another task is another matter
        c.AppendLine(UserLineWith(ReplayedNotice.Replace("t9", "t10"), "{\"kind\":\"task-notification\"}"));
        Assert.Single(c.Items.OfType<ExternalMessageItem>());
        // and clearing forgets which tasks ended live
        c.Clear();
        c.AppendLine(UserLineWith(ReplayedNotice));
        Assert.Single(c.Items.OfType<ExternalMessageItem>());
    }

    [Fact]
    public void A_notification_line_from_another_origin_stays_out_of_the_conversation()
    {
        var c = new ChatConversation();
        c.AppendLine(UserLineWith(ReplayedNotice, "{\"kind\":\"human\"}"));

        Assert.Empty(c.Items);
    }

    [Fact]
    public void The_meta_line_of_a_background_agent_is_the_first_line_of_its_summary()
    {
        var c = Play("subagent");                                      // its summary is three lines: the agent's answer

        var line = Assert.Single(c.Items.OfType<MetaLineItem>());
        Assert.Equal("a.txt: Simple sentence describing a cat resting on a mat.…", line.Text);
    }

    // ---- context lines under a prompt ----

    private static string UserBlocks(params string[] texts) =>
        "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":[" +
        string.Join(",", texts.Select(t => "{\"type\":\"text\",\"text\":" + JsonSerializer.Serialize(t) + "}")) +
        "]},\"parent_tool_use_id\":null}";

    private static UserMessageItem LoadedUser(params string[] texts)
    {
        var c = new ChatConversation();
        c.LoadTranscript(new[] { UserBlocks(texts) });
        return Assert.Single(c.Items.OfType<UserMessageItem>());
    }

    [Fact]
    public void A_terminal_output_block_becomes_a_context_line_on_the_user_message()
    {
        var c = new ChatConversation();
        c.LoadTranscript(new[] { UserBlocks("<terminal name=\"build\">\nerror CS1002\nerror CS1003\n</terminal>", "why does it fail?") });

        var user = Assert.IsType<UserMessageItem>(Assert.Single(c.Items));
        Assert.Equal("why does it fail?", user.Text);
        var line = Assert.Single(user.Context);
        Assert.Equal("terminal", line.Kind);
        Assert.Equal("Terminal output: build", line.Label);
        Assert.Equal("error CS1002\nerror CS1003", line.Content);
    }

    [Fact]
    public void Two_context_blocks_and_the_prompt_are_one_message_with_two_lines_in_order()
    {
        var user = LoadedUser(
            "<browser tabGroupId=\"g1\" tabId=\"42\">https://example.com/docs/intro</browser>",
            "<ide_selection>The user selected the lines 3 to 9 from C:\\src\\app\\Program.cs:\nvar x = 1;\n</ide_selection>",
            "explain this");

        Assert.Equal("explain this", user.Text);
        Assert.Equal(2, user.Context.Count);
        Assert.Equal("browser-tab", user.Context[0].Kind);
        Assert.Equal("Browser tab: example.com/docs/intro", user.Context[0].Label);
        Assert.Equal("https://example.com/docs/intro", user.Context[0].Content);
        Assert.Equal("selection", user.Context[1].Kind);
        Assert.Equal("Selected code: Program.cs#3-9", user.Context[1].Label);
        Assert.Contains("var x = 1;", user.Context[1].Content);
    }

    [Fact]
    public void The_other_context_formats_read_as_the_extension_does()
    {
        var bare = LoadedUser("<browser tabGroupId=\"g\" tabId=\"1\"></browser>", "x");
        Assert.Equal("Browser tab", bare.Context[0].Label);
        Assert.Equal("", bare.Context[0].Content);          // an empty block reads "(none)" when opened

        var root = LoadedUser("<browser tabGroupId=\"g\" tabId=\"1\">https://example.com/</browser>", "x");
        Assert.Equal("Browser tab: example.com", root.Context[0].Label); // the path of "/" is left out

        var instructions = LoadedUser("<browser_instruction>Use the tab 3.</browser_instruction>", "x");
        Assert.Equal("browser-instructions", instructions.Context[0].Kind);
        Assert.Equal("Browser instructions", instructions.Context[0].Label);
        Assert.Equal("Use the tab 3.", instructions.Context[0].Content);

        var whole = LoadedUser("<ide_selection>The user selected from /home/a/b.py:\nprint(1)\n</ide_selection>", "x");
        Assert.Equal("Selected code: b.py", whole.Context[0].Label); // no lines named: the file alone

        var empty = LoadedUser("<terminal name=\"t\"></terminal>", "x");
        Assert.Equal("", empty.Context[0].Content);
    }

    [Fact]
    public void A_context_line_is_labelled_in_the_language_of_the_conversation()
    {
        var c = new ChatConversation { Strings = ChatStrings.PortugueseBrazil };
        c.LoadTranscript(new[] { UserBlocks("<terminal name=\"build\">\nx\n</terminal>", "oi") });
        var user = Assert.Single(c.Items.OfType<UserMessageItem>());
        Assert.Equal("Saída do terminal: build", user.Context[0].Label);
    }

    [Fact]
    public void A_block_that_is_not_a_context_stays_a_message_of_its_own_as_before()
    {
        var c = new ChatConversation();
        c.LoadTranscript(new[]
        {
            UserBlocks("just a note", "and the question"),
            UserBlocks("<ide_opened_file>The user opened the file a.cs in the IDE.</ide_opened_file>", "next"),
        });

        var users = c.Items.OfType<UserMessageItem>().ToList();
        Assert.Equal(4, users.Count);
        Assert.All(users, u => Assert.Empty(u.Context));
    }

    [Fact]
    public void A_context_block_without_a_prompt_after_it_or_from_another_origin_is_no_context()
    {
        var onlyContext = new ChatConversation();
        onlyContext.LoadTranscript(new[] { UserBlocks("<terminal name=\"t\">\nx\n</terminal>") });
        Assert.All(onlyContext.Items.OfType<UserMessageItem>(), u => Assert.Empty(u.Context));

        var fromPeer = new ChatConversation();
        fromPeer.LoadTranscript(new[]
        {
            UserBlocks("<terminal name=\"t\">\nx\n</terminal>", "hello")
                .Replace("{\"type\":\"user\",", "{\"type\":\"user\",\"origin\":{\"kind\":\"peer\"},"),
        });
        Assert.All(fromPeer.Items.OfType<UserMessageItem>(), u => Assert.Empty(u.Context));
    }

    [Fact]
    public void Context_lines_are_not_lost_when_the_last_block_is_a_command_or_blank()
    {
        // a slash command is no message: the context blocks stay messages of their own, as before
        var c = new ChatConversation();
        c.LoadTranscript(new[]
        {
            UserBlocks("<terminal name=\"t\">\nx\n</terminal>", "<command-name>/clear</command-name><command-args></command-args>"),
        });
        Assert.Contains(c.Items.OfType<UserMessageItem>(), u => u.Text.Contains("terminal"));

        // a blank text: the same
        var blank = new ChatConversation();
        blank.LoadTranscript(new[] { UserBlocks("<terminal name=\"t\">\nx\n</terminal>", "  ") });
        Assert.Contains(blank.Items.OfType<UserMessageItem>(), u => u.Text.Contains("terminal"));

        // a text that is only the markers of attached files: the message stays, with its lines
        var files = new ChatConversation();
        files.LoadTranscript(new[] { UserBlocks("<terminal name=\"t\">\nx\n</terminal>", "[Attached file: /tmp/a.txt]") });
        var user = Assert.Single(files.Items.OfType<UserMessageItem>(), u => u.Context.Count > 0);
        Assert.Equal("terminal", user.Context[0].Kind);
    }

    [Fact]
    public void A_failed_turn_and_an_api_retry_carry_the_message_the_claude_gave()
    {
        var c = new ChatConversation();
        c.AppendLine("{\"type\":\"system\",\"subtype\":\"api_retry\",\"error\":\"overloaded_error\"}");
        c.AppendLine("{\"type\":\"result\",\"subtype\":\"error_during_execution\",\"is_error\":true,\"result\":\"Credit balance is too low\",\"duration_ms\":5}");
        var notices = c.Items.OfType<NoticeItem>().ToList();
        Assert.Equal("overloaded_error", notices.Single(n => n.Kind == NoticeKind.ApiRetry).Message);
        Assert.Equal("Credit balance is too low", notices.Single(n => n.Kind == NoticeKind.TurnFailed).Message);
    }
}
