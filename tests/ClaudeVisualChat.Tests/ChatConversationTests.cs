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
        Assert.Equal("second", Assert.IsType<UserMessageItem>(Assert.Single(twice.Items)).Text);
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
        foreach (var local in new[] { "context", "usage", "remote-control", "chrome" })
            Assert.Contains(local, names);
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void Palette_keeps_the_claudes_entry_for_a_local_command_it_lists()
    {
        var c = new ChatConversation();
        c.Commands = new[] { new ChatCommand("context", "Show current context usage", "") };
        Assert.Equal("Show current context usage", c.PaletteCommands.Single(x => x.Name == "context").Description);
        Assert.Equal(new[] { "context", "usage", "remote-control", "rc", "chrome" }, ChatConversation.LocalCommands);
        Assert.IsNotType<string[]>(ChatConversation.LocalCommands);
        Assert.Throws<NotSupportedException>(() => ((System.Collections.Generic.IList<string>)ChatConversation.LocalCommands)[0] = "x");
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
        Assert.Contains(c.Items, i => i is CommandOutputItem o && o.Text.Length > 0);
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
}
