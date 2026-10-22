using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using ClaudeVisualChat.Controls;
using Xunit;
using Xunit.Abstractions;
using AssistantBlock = ClaudeVisualChat.Protocol.AssistantBlock;
using ToolResultBlock = ClaudeVisualChat.Protocol.ToolResultBlock;
using ToolUseBlock = ClaudeVisualChat.Protocol.ToolUseBlock;
using UserBlock = ClaudeVisualChat.Protocol.UserBlock;

namespace ClaudeVisualChat.Tests.Ui;

public class ChatViewTests
{
    private readonly ITestOutputHelper _output;

    public ChatViewTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public static IEnumerable<object[]> Recordings =>
        FixtureFiles.Scenarios.SelectMany(s => new[] { new object[] { s, "stream" }, new object[] { s, "transcript" } });

    private static JsonElement J(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static (ClaudeChatView View, Window Window) Open(ChatConversation conversation, ChatStrings? strings = null)
    {
        var view = new ClaudeChatView { Conversation = conversation };
        if (strings != null) view.Strings = strings;
        return (view, UiHelpers.Show(view));
    }

    private static ToolCallView ViewOf(Visual root, ToolCallItem item) =>
        root.GetVisualDescendants().OfType<ToolCallView>().Single(v => ReferenceEquals(v.DataContext, item));

    private static IBrush? DotFill(Visual root, ToolCallItem item) =>
        ViewOf(root, item).GetVisualDescendants().OfType<Ellipse>().First().Fill;

    private static object? Resource(Control view, string key) =>
        view.TryFindResource(key, out var value) ? value : null;

    private static ChatConversation BashWithOutput(string output)
    {
        var c = new ChatConversation();
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("t1", "Bash", J("{\"command\":\"seq 10000\"}"))));
        c.Apply(new UserBlock(null, new ToolResultBlock("t1", output, false), null, false, false));
        return c;
    }

    // ---- the recordings ----

    [AvaloniaTheory]
    [MemberData(nameof(Recordings))]
    public void Renders_every_fixture(string scenario, string source)
    {
        TestLog.Instance.Clear();
        var conversation = UiHelpers.Recording(scenario, source);
        var (view, window) = Open(conversation);

        // Recordings without a tool call (commands, effort) only have to open without logging errors.
        var first = conversation.Items.OfType<ToolCallItem>().FirstOrDefault();
        if (first != null)
        {
            Assert.NotEqual("", first.Summary);
            UiHelpers.ItemList(view).ScrollIntoView(first);
            UiHelpers.Pump();

            Assert.Contains(first.Summary, UiHelpers.Texts(view));
        }
        else
        {
            UiHelpers.Pump();
        }

        Assert.Empty(TestLog.Instance.Entries);
        window.Close();
    }

    // ---- texts ----

    [AvaloniaFact]
    public void Strings_switch_language()
    {
        var c = new ChatConversation();
        c.Items.Add(new NoticeItem(NoticeKind.Interrupted));
        c.Items.Add(new ThinkingItem());
        c.Items.Add(new ToolCallItem { Name = "Write", Summary = "a.txt", Status = ToolStatus.Rejected });
        var (view, window) = Open(c, ChatStrings.PortugueseBrazil);

        var texts = UiHelpers.Texts(view);
        Assert.Contains("Interrompido por você", texts);
        Assert.Contains("Pensando", texts);
        Assert.Contains("Recusado", texts);

        view.Strings = ChatStrings.English;
        UiHelpers.Pump();

        texts = UiHelpers.Texts(view);
        Assert.Contains("Interrupted by the user", texts);
        Assert.Contains("Thinking", texts);
        Assert.Contains("Rejected", texts);
        Assert.DoesNotContain("Interrompido por você", texts);
        window.Close();
    }

    [AvaloniaFact]
    public void Notices_use_their_texts_and_a_failed_turn_is_red()
    {
        var c = new ChatConversation();
        foreach (NoticeKind kind in Enum.GetValues(typeof(NoticeKind))) c.Items.Add(new NoticeItem(kind));
        var (view, window) = Open(c);

        var texts = UiHelpers.Texts(view);
        Assert.Contains("Interrupted by the user", texts);
        Assert.Contains("Conversation compacted", texts);
        Assert.Contains("Retrying the API request…", texts);
        var failed = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "The turn ended with an error");
        Assert.Same(Resource(view, "ClaudeChat.Error"), failed.Foreground);
        var compacted = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Conversation compacted");
        Assert.Same(Resource(view, "ClaudeChat.ForegroundMuted"), compacted.Foreground);
        window.Close();
    }

    // ---- the scroll bar ----

    /// <summary>A list long enough to scroll, in a window that may bring its own theme for the standard controls.</summary>
    private static (ClaudeChatView View, Window Window, ScrollBar Bar) OpenLongList(Avalonia.Styling.IStyle? theme = null, bool allowAutoHide = true)
    {
        var c = new ChatConversation();
        for (var i = 0; i < 80; i++) c.Items.Add(new NoticeItem(NoticeKind.Compacted));
        var view = new ClaudeChatView { Conversation = c };
        var window = new Window { Width = UiHelpers.Width, Height = UiHelpers.Height };
        if (theme != null) window.Styles.Add(theme); // before the view comes in, which takes its controls' themes then
        window.Content = view;
        UiHelpers.Scroller(view).AllowAutoHide = allowAutoHide;
        window.Show();
        UiHelpers.Pump();
        var scroller = UiHelpers.Scroller(view);
        Assert.True(scroller.Extent.Height > scroller.Viewport.Height, "the list scrolls");
        var bar = scroller.GetVisualDescendants().OfType<ScrollBar>()
            .Single(b => b.Orientation == Avalonia.Layout.Orientation.Vertical && ReferenceEquals(b.TemplatedParent, scroller));
        Assert.True(bar.IsVisible);
        return (view, window, bar);
    }

    private static double Left(Visual visual, Visual root) => visual.TranslatePoint(default, root)!.Value.X;

    private static double Right(Visual visual, Visual root) => visual.TranslatePoint(new Point(visual.Bounds.Width, 0), root)!.Value.X;

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_scroll_bar_drawn_over_the_list_gets_its_room(bool allowAutoHide)
    {
        // Fluent draws its bar over the content, hiding itself or not (11.0 keeps the content under it either way):
        // the list leaves it ClaudeChat.ScrollBarGutter
        var (view, window, bar) = OpenLongList(allowAutoHide: allowAutoHide);
        var list = UiHelpers.ItemList(view);
        Assert.Equal(16, list.Margin.Right);
        Assert.True(Right(list, view) <= Left(bar, view) + 0.5, $"the list ends at {Right(list, view)}, the bar starts at {Left(bar, view)}");
        window.Close();
    }

    [AvaloniaFact]
    public void A_scroll_bar_beside_the_list_takes_its_room_once()
    {
        // Simple's bar has a column of its own: the list goes up to it, with no room kept twice
        var (view, window, bar) = OpenLongList(new Avalonia.Themes.Simple.SimpleTheme());
        var list = UiHelpers.ItemList(view);
        Assert.Equal(0, list.Margin.Right);
        Assert.Equal(Left(bar, view), Right(list, view), 1);
        window.Close();
    }

    // ---- tool calls ----

    [AvaloniaFact]
    public void Huge_tool_output_shows_more_lines()
    {
        var output = string.Join("\n", Enumerable.Range(1, 10000).Select(i => "line " + i));
        Open(BashWithOutput("warm\nup")).Window.Close();

        var c = BashWithOutput(output);
        var view = new ClaudeChatView { Conversation = c };
        var window = new Window { Width = UiHelpers.Width, Height = UiHelpers.Height, Content = view };
        var watch = Stopwatch.StartNew();
        window.Show();
        UiHelpers.Pump();
        watch.Stop();
        _output.WriteLine($"10,000 lines of output: {watch.ElapsedMilliseconds} ms");

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), $"took {watch.ElapsedMilliseconds} ms");
        var texts = UiHelpers.Texts(view);
        Assert.Contains("+9,988 lines", texts);
        Assert.Contains(texts, t => t.EndsWith("line 12", StringComparison.Ordinal));
        Assert.DoesNotContain(texts, t => t.Contains("line 13"));

        view.Strings = ChatStrings.PortugueseBrazil;
        UiHelpers.Pump();
        Assert.Contains("+9.988 linhas", UiHelpers.Texts(view));
        window.Close();
    }

    [AvaloniaFact]
    public void Very_long_lines_are_clipped()
    {
        var c = BashWithOutput(new string('x', 200_000));
        c.Items.Add(new ToolCallItem
        {
            Name = "Edit", Summary = "min.js", Status = ToolStatus.Succeeded,
            Diff = new[] { new DiffLine(DiffKind.Added, new string('y', 200_000)) },
        });
        var (view, window) = Open(c);

        var texts = UiHelpers.Texts(view);
        var output = Assert.Single(texts, t => t.StartsWith("xxx", StringComparison.Ordinal));
        Assert.InRange(output.Length, 100, ToolCallView.MaxOutputChars + 1);
        var added = Assert.Single(texts, t => t.StartsWith("yyy", StringComparison.Ordinal)); // the diff's colors tell it was added
        Assert.InRange(added.Length, 100, ToolCallView.MaxLineChars + 1);
        window.Close();
    }

    [Theory]
    [InlineData("", 8, 4, "")]
    [InlineData("a\r\nb\r\nc", 8, 4, "a\nb\nc")]
    [InlineData("a\nb\n", 8, 4, "a\nb\n")]
    [InlineData("1\n2\n3\n4", 2, 4, "1\n2")]
    [InlineData("abcdef\nxy", 8, 4, "abcd…\nxy")]
    [InlineData("abc😀z", 1, 4, "abc…")] // never half of a surrogate pair
    public void Clip_lines_keeps_the_first_lines_each_cut(string text, int lines, int chars, string expected) =>
        Assert.Equal(expected, ChatText.ClipLines(text, lines, chars));

    [AvaloniaFact]
    public void Tool_rows_show_status_diff_todos_and_mcp_name()
    {
        var edit = new ToolCallItem
        {
            Name = "MultiEdit", Summary = "src/a.cs", Status = ToolStatus.Succeeded, ResultText = "updated",
            Diff = new[]
            {
                new DiffLine(DiffKind.Gap, "+4"),
                new DiffLine(DiffKind.Context, "ctx"),
                new DiffLine(DiffKind.Removed, "old"),
                new DiffLine(DiffKind.Added, "new"),
                new DiffLine(DiffKind.Gap, ""),
                new DiffLine(DiffKind.Added, "more"),
            },
        };
        var todos = new ToolCallItem
        {
            Name = "TodoWrite", Status = ToolStatus.Running,
            Todos = new[]
            {
                new TodoEntry("plan", TodoStatus.Completed),
                new TodoEntry("build", TodoStatus.InProgress),
                new TodoEntry("ship", TodoStatus.Pending),
            },
        };
        var mcp = new ToolCallItem
        {
            Name = "mcp__github__search_issues", Summary = "crash", Status = ToolStatus.Failed, ResultText = "boom",
        };
        var rejected = new ToolCallItem
        {
            Name = "Write", Summary = "b.txt", Status = ToolStatus.Rejected, ResultText = "The user doesn't want to proceed",
        };
        var c = new ChatConversation();
        foreach (var item in new[] { edit, todos, mcp, rejected }) c.Items.Add(item);
        var (view, window) = Open(c);

        var texts = UiHelpers.Texts(ViewOf(view, edit));
        Assert.Contains("MultiEdit", texts);
        Assert.Contains("src/a.cs", texts);
        Assert.Contains("+4 lines", texts);
        Assert.Contains("ctx", texts); // the diff's text as written; the colors tell old and new
        Assert.Contains("old", texts);
        Assert.Contains("new", texts);
        Assert.Contains("more", texts);
        Assert.Contains("⋯", texts);
        Assert.DoesNotContain("updated", texts); // a diff stands for the output
        Border Tint(string kind) => ViewOf(view, edit).GetVisualDescendants().OfType<Border>()
            .First(b => b.Classes.Contains("ClaudeChatDiffLine") && b.Classes.Contains(kind));
        Assert.Same(Resource(view, "ClaudeChat.DiffRemoved"), Tint("removed").Background);
        Assert.Same(Resource(view, "ClaudeChat.DiffAdded"), Tint("added").Background);

        // the extension's to-do boxes: checked when done, a star while doing, empty before
        texts = UiHelpers.Texts(ViewOf(view, todos));
        foreach (var expected in new[] { "✓", "plan", "✽", "build", "ship" }) Assert.Contains(expected, texts);

        texts = UiHelpers.Texts(ViewOf(view, mcp));
        Assert.Contains("github · search_issues", texts);
        Assert.Contains("boom", texts);

        texts = UiHelpers.Texts(ViewOf(view, rejected));
        Assert.Contains("Rejected", texts);
        Assert.DoesNotContain(texts, t => t.StartsWith("The user", StringComparison.Ordinal));

        // the extension's dot colors
        Assert.Same(Resource(view, "ClaudeChat.DotSucceeded"), DotFill(view, edit));
        Assert.Same(Resource(view, "ClaudeChat.Dot"), DotFill(view, todos));
        Assert.Same(Resource(view, "ClaudeChat.DotFailed"), DotFill(view, mcp));
        Assert.Same(Resource(view, "ClaudeChat.DotRejected"), DotFill(view, rejected));

        mcp.Status = ToolStatus.Succeeded; // the dot follows the item
        UiHelpers.Pump();
        Assert.Same(Resource(view, "ClaudeChat.DotSucceeded"), DotFill(view, mcp));
        window.Close();
    }

    [AvaloniaFact]
    public void Read_and_agent_output_shows_only_when_something_went_wrong()
    {
        // like the extension: a Read that worked does not echo the file, nor a subagent its launch details
        var edit = UiHelpers.Recording("edit", "stream");
        var read = edit.Items.OfType<ToolCallItem>().First(t => t.Name == "Read");
        Assert.Contains("line one", read.ResultText);
        var (view, window) = Open(edit);
        var texts = UiHelpers.Texts(ViewOf(view, read));
        Assert.Contains("notes.md", texts);
        Assert.DoesNotContain(texts, t => t.Contains("line one"));
        window.Close();

        var subagent = UiHelpers.Recording("subagent", "stream");
        var agent = subagent.Items.OfType<ToolCallItem>().Single();
        Assert.False(string.IsNullOrEmpty(agent.ResultText));
        (view, window) = Open(subagent);
        UiHelpers.ItemList(view).ScrollIntoView(agent);
        UiHelpers.Pump();
        Assert.DoesNotContain(UiHelpers.Texts(ViewOf(view, agent)), t => t.Contains(agent.ResultText!.Split('\n')[0]));
        window.Close();

        var error = UiHelpers.Recording("error", "stream");
        var failed = error.Items.OfType<ToolCallItem>().Single(t => t.Name == "Read");
        Assert.Equal(ToolStatus.Failed, failed.Status);
        (view, window) = Open(error);
        UiHelpers.ItemList(view).ScrollIntoView(failed);
        UiHelpers.Pump();
        Assert.Contains(UiHelpers.Texts(ViewOf(view, failed)), t => t.StartsWith("File does not exist", StringComparison.Ordinal));
        window.Close();

        // a subagent call that failed tells why
        var c = new ChatConversation();
        c.Items.Add(new ToolCallItem { Name = "Task", Summary = "dig", Status = ToolStatus.Failed, ResultText = "agent crashed" });
        c.Items.Add(new ToolCallItem { Name = "Agent", Summary = "dig", Status = ToolStatus.Succeeded, ResultText = "agentId: 42", HiddenResultLines = 5 });
        (view, window) = Open(c);
        texts = UiHelpers.Texts(view);
        Assert.Contains("agent crashed", texts);
        Assert.DoesNotContain("agentId: 42", texts);
        Assert.DoesNotContain("+5 lines", texts);
        window.Close();
    }

    [AvaloniaFact]
    public void Subagent_children_expand()
    {
        var c = UiHelpers.Recording("subagent", "stream");
        var agent = c.Items.OfType<ToolCallItem>().Single();
        var childSummaries = agent.Children.OfType<ToolCallItem>().Select(t => t.Summary).ToList();
        Assert.NotEmpty(childSummaries);
        var (view, window) = Open(c);
        UiHelpers.ItemList(view).ScrollIntoView(agent);
        UiHelpers.Pump();

        Assert.DoesNotContain(childSummaries[0], UiHelpers.Texts(view));

        agent.IsExpanded = true;
        UiHelpers.Pump();
        var texts = UiHelpers.Texts(ViewOf(view, agent));
        Assert.All(childSummaries, s => Assert.Contains(s, texts));

        // the arrow is a toggle bound both ways
        var header = ViewOf(view, agent).GetVisualDescendants().OfType<ToggleButton>().First();
        header.IsChecked = false;
        UiHelpers.Pump();
        Assert.False(agent.IsExpanded);
        Assert.DoesNotContain(childSummaries[0], UiHelpers.Texts(view));
        window.Close();
    }

    // ---- the dot of a running call, the output, a command in the background ----

    /// <summary>The dot's opacity over about 0.6 s of the animation clock, which runs on real time.</summary>
    private static List<double> DotOpacities(Ellipse dot)
    {
        var seen = new List<double>();
        for (var i = 0; i < 6; i++)
        {
            System.Threading.Thread.Sleep(100);
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            UiHelpers.Pump();
            seen.Add(dot.Opacity);
        }
        return seen;
    }

    [AvaloniaFact]
    public void A_running_call_blinks_only_while_the_conversation_works()
    {
        // a history with a call that never got its result: nothing works, the dot stays still and gray
        var c = new ChatConversation();
        c.LoadTranscript(new[]
        {
            "{\"type\":\"assistant\",\"message\":{\"id\":\"m1\",\"content\":[{\"type\":\"tool_use\",\"id\":\"t1\",\"name\":\"Bash\",\"input\":{\"command\":\"ls\"}}]}}",
        });
        var tool = Assert.Single(c.Items.OfType<ToolCallItem>());
        Assert.Equal(ToolStatus.Running, tool.Status);
        Assert.False(c.IsBusy);
        var (view, window) = Open(c);
        var row = ViewOf(view, tool).GetVisualDescendants().OfType<ChatTimelineRow>().Single();
        var dot = ViewOf(view, tool).GetVisualDescendants().OfType<Ellipse>().First();
        Assert.DoesNotContain("running", row.Classes);
        Assert.All(DotOpacities(dot), o => Assert.Equal(1, o));
        Assert.Same(Resource(view, "ClaudeChat.Dot"), dot.Fill);

        // the same call while the conversation works: it blinks
        c.Submit("again");
        UiHelpers.Pump();
        Assert.Contains("running", row.Classes);
        Assert.Contains(DotOpacities(dot), o => o < 0.9);

        // the turn ends with the call still without a result (a subagent's, say): still again
        c.Apply(new ClaudeVisualChat.Protocol.TurnCompleted("success", false, 10, 0, 1, 1));
        UiHelpers.Pump();
        Assert.DoesNotContain("running", row.Classes);
        var after = DotOpacities(dot);
        Assert.Equal(1, after[after.Count - 1]);
        window.Close();
    }

    [AvaloniaFact]
    public void A_long_output_wraps_and_the_command_does_not()
    {
        var error = "error: " + string.Join(" ", Enumerable.Range(1, 80).Select(i => "word" + i));
        var c = new ChatConversation();
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("t1", "Bash", J("{\"command\":\"" + new string('x', 300) + "\"}"))));
        c.Apply(new UserBlock(null, new ToolResultBlock("t1", error, true), null, false, false));
        var (view, window) = Open(c);
        var tool = c.Items.OfType<ToolCallItem>().Single();

        var output = ViewOf(view, tool).GetVisualDescendants().OfType<SelectableTextBlock>().Single(t => t.Text == error);
        var box = ViewOf(view, tool).GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("ClaudeChatToolBody"));
        Assert.Equal(TextWrapping.Wrap, output.TextWrapping);
        Assert.True(output.Bounds.Height > 2 * 16, $"{output.Bounds.Height} px: one line"); // several lines
        Assert.True(output.Bounds.Right <= box.Bounds.Width, "the output stays in its box");

        var command = ViewOf(view, tool).GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == new string('x', 300));
        Assert.Equal(TextWrapping.NoWrap, command.TextWrapping); // cut at the box's edge, like the extension
        window.Close();
    }

    [AvaloniaFact]
    public void A_command_in_the_background_says_so()
    {
        var c = new ChatConversation();
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("t1", "PowerShell", J("{\"command\":\"Start-Sleep 60\",\"run_in_background\":true}"))));
        c.Apply(new AssistantBlock("m2", null, new ToolUseBlock("t2", "Bash", J("{\"command\":\"sleep 60\",\"run_in_background\":false}"))));
        c.Apply(new AssistantBlock("m3", null, new ToolUseBlock("t3", "Bash", J("{\"command\":\"ls\"}"))));
        var (view, window) = Open(c, ChatStrings.PortugueseBrazil);
        var tools = c.Items.OfType<ToolCallItem>().ToList();

        Assert.Contains("Rodando em segundo plano", UiHelpers.Texts(ViewOf(view, tools[0])));
        Assert.DoesNotContain("Rodando em segundo plano", UiHelpers.Texts(ViewOf(view, tools[1])));
        Assert.DoesNotContain("Rodando em segundo plano", UiHelpers.Texts(ViewOf(view, tools[2])));
        var line = ViewOf(view, tools[0]).GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Rodando em segundo plano");
        Assert.Contains("ClaudeChatSecondary", line.Classes); // the muted line under the box
        var box = ViewOf(view, tools[0]).GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("ClaudeChatToolBody"));
        Assert.True(line.TranslatePoint(default, view)!.Value.Y > box.TranslatePoint(new Point(0, box.Bounds.Height), view)!.Value.Y);

        view.Strings = ChatStrings.English;
        UiHelpers.Pump();
        Assert.Contains("Run in background", UiHelpers.Texts(ViewOf(view, tools[0])));
        window.Close();
    }

    // ---- "Working…" ----

    private static Panel WorkingRow(ClaudeChatView view) =>
        view.GetVisualDescendants().OfType<Panel>().Single(p => p.Classes.Contains("ClaudeChatWorking"));

    [AvaloniaFact]
    public void The_working_dot_blinks_only_while_its_line_shows()
    {
        var c = new ChatConversation();
        var (view, window) = Open(c);
        var dot = WorkingRow(view).GetVisualDescendants().OfType<Ellipse>().Single();

        // hidden before anything is sent: still
        Assert.False(WorkingRow(view).IsEffectivelyVisible);
        Assert.All(DotOpacities(dot), o => Assert.Equal(1, o));

        // shown while Claude works: it blinks
        c.Submit("go");
        UiHelpers.Pump();
        Assert.True(WorkingRow(view).IsEffectivelyVisible);
        Assert.Contains(DotOpacities(dot), o => o < 0.9);

        // hidden while a text arrives: still
        c.AppendLine("{\"type\":\"stream_event\",\"parent_tool_use_id\":null,\"event\":{\"type\":\"message_start\",\"message\":{\"id\":\"m1\"}}}");
        c.AppendLine("{\"type\":\"stream_event\",\"parent_tool_use_id\":null,\"event\":{\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"text\",\"text\":\"\"}}}");
        UiHelpers.Pump();
        Assert.False(WorkingRow(view).IsEffectivelyVisible);
        Assert.All(DotOpacities(dot), o => Assert.Equal(1, o));

        // after the turn: still
        c.Apply(new ClaudeVisualChat.Protocol.TurnCompleted("success", false, 10, 0, 1, 1));
        UiHelpers.Pump();
        Assert.False(WorkingRow(view).IsEffectivelyVisible);
        Assert.All(DotOpacities(dot), o => Assert.Equal(1, o));
        window.Close();
    }

    [AvaloniaFact]
    public void Working_shows_at_the_end_while_claude_works_and_nothing_arrives()
    {
        var c = new ChatConversation();
        var (view, window) = Open(c);
        Assert.False(WorkingRow(view).IsEffectivelyVisible); // idle

        c.Submit("go");
        UiHelpers.Pump();
        Assert.True(WorkingRow(view).IsEffectivelyVisible);
        Assert.Contains("Working…", UiHelpers.Texts(WorkingRow(view)));
        var user = view.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatUser"));
        Assert.True(WorkingRow(view).TranslatePoint(default, view)!.Value.Y > user.TranslatePoint(new Point(0, user.Bounds.Height), view)!.Value.Y);

        // a text arriving: the text itself shows the work
        c.AppendLine("{\"type\":\"stream_event\",\"parent_tool_use_id\":null,\"event\":{\"type\":\"message_start\",\"message\":{\"id\":\"m1\"}}}");
        c.AppendLine("{\"type\":\"stream_event\",\"parent_tool_use_id\":null,\"event\":{\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"text\",\"text\":\"\"}}}");
        UiHelpers.Pump();
        Assert.False(WorkingRow(view).IsEffectivelyVisible);

        // the text is whole and a tool runs: working again, under the last entry
        c.AppendLine("{\"type\":\"assistant\",\"message\":{\"id\":\"m1\",\"content\":[{\"type\":\"text\",\"text\":\"Let me look.\"}]}}");
        c.AppendLine("{\"type\":\"assistant\",\"message\":{\"id\":\"m2\",\"content\":[{\"type\":\"tool_use\",\"id\":\"t1\",\"name\":\"Bash\",\"input\":{\"command\":\"ls\"}}]}}");
        UiHelpers.Pump();
        Assert.True(WorkingRow(view).IsEffectivelyVisible);
        var tool = view.GetVisualDescendants().OfType<ToolCallView>().Single();
        Assert.True(WorkingRow(view).TranslatePoint(default, view)!.Value.Y >= tool.TranslatePoint(new Point(0, tool.Bounds.Height), view)!.Value.Y);

        // the dot blinks, in the color of the mode; the text in Portuguese
        var dot = WorkingRow(view).GetVisualDescendants().OfType<Ellipse>().Single();
        Assert.Same(Resource(view, "ClaudeChat.Spinner"), dot.Fill);
        Assert.Contains(DotOpacities(dot), o => o < 0.9);
        c.Apply(new ClaudeVisualChat.Protocol.StatusChanged(null, "acceptEdits"));
        view.Strings = ChatStrings.PortugueseBrazil;
        UiHelpers.Pump();
        Assert.Same(Resource(view, "ClaudeChat.Foreground"), dot.Fill);
        Assert.Contains("Trabalhando…", UiHelpers.Texts(WorkingRow(view)));

        c.Apply(new ClaudeVisualChat.Protocol.TurnCompleted("success", false, 10, 0, 1, 1));
        UiHelpers.Pump();
        Assert.False(WorkingRow(view).IsEffectivelyVisible); // the turn is over
        window.Close();
    }

    // ---- thinking, footer and notices ----

    [AvaloniaFact]
    public void A_whole_thought_tells_how_long_it_took()
    {
        var now = new DateTimeOffset(2026, 10, 19, 22, 0, 0, TimeSpan.Zero);
        var c = new ChatConversation { Clock = () => now };
        var (view, window) = Open(c);
        c.AppendLine("{\"type\":\"stream_event\",\"parent_tool_use_id\":null,\"event\":{\"type\":\"message_start\",\"message\":{\"id\":\"m1\"}}}");
        c.AppendLine("{\"type\":\"stream_event\",\"parent_tool_use_id\":null,\"event\":{\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"thinking\",\"thinking\":\"\"}}}");
        UiHelpers.Pump();
        var row = view.GetVisualDescendants().OfType<ChatTimelineRow>().Single();
        Assert.Contains("Thinking", UiHelpers.Texts(row)); // still arriving

        now = now.AddMilliseconds(2600);
        c.AppendLine("{\"type\":\"assistant\",\"message\":{\"id\":\"m1\",\"content\":[{\"type\":\"thinking\",\"thinking\":\"hm\"}]}}");
        UiHelpers.Pump();
        Assert.Contains("Thought for 3s", UiHelpers.Texts(row));

        view.Strings = ChatStrings.PortugueseBrazil;
        UiHelpers.Pump();
        Assert.Contains("Pensou por 3 s", UiHelpers.Texts(row));
        window.Close();

        // from the history: the recorded time; without one, "Thinking"
        var h = new ChatConversation();
        h.LoadTranscript(new[]
        {
            "{\"type\":\"assistant\",\"thinkingDurationMs\":400,\"message\":{\"id\":\"m1\",\"content\":[{\"type\":\"thinking\",\"thinking\":\"\"}]}}",
            "{\"type\":\"assistant\",\"message\":{\"id\":\"m2\",\"content\":[{\"type\":\"thinking\",\"thinking\":\"\"}]}}",
        });
        (view, window) = Open(h);
        var texts = UiHelpers.Texts(view);
        Assert.Contains("Thought for 0s", texts); // the print's line
        Assert.Contains("Thinking", texts);
        window.Close();
    }

    [AvaloniaFact]
    public void Thinking_expands_only_with_text()
    {
        var empty = new ThinkingItem();
        var deep = new ThinkingItem { Text = "a deep thought" };
        var c = new ChatConversation();
        c.Items.Add(empty);
        c.Items.Add(deep);
        var (view, window) = Open(c);

        var emptyView = UiHelpers.ItemList(view).ContainerFromIndex(0)!;
        Assert.Equal(new[] { "Thinking" }, UiHelpers.Texts(emptyView));
        Assert.DoesNotContain(emptyView.GetVisualDescendants().OfType<Path>(), p => p.IsEffectivelyVisible);
        Assert.False(emptyView.GetVisualDescendants().OfType<ToggleButton>().Single().IsHitTestVisible);

        var deepView = UiHelpers.ItemList(view).ContainerFromIndex(1)!;
        Assert.Contains(deepView.GetVisualDescendants().OfType<Path>(), p => p.IsEffectivelyVisible);
        Assert.DoesNotContain("a deep thought", UiHelpers.Texts(deepView));

        deep.IsExpanded = true;
        UiHelpers.Pump();
        var thought = deepView.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "a deep thought");
        Assert.True(thought.IsEffectivelyVisible);
        Assert.Equal(FontStyle.Normal, thought.FontStyle); // the extension's thinking, upright and muted
        Assert.Same(Resource(view, "ClaudeChat.ForegroundMuted"), thought.Foreground);
        window.Close();
    }

    [AvaloniaFact]
    public void Footer_formats()
    {
        var c = new ChatConversation();
        c.Items.Add(new TurnFooterItem(4200, 38557, 502, 0.0839, false));
        c.Items.Add(new TurnFooterItem(125000, 1200, 3, 1.5, false));
        c.Items.Add(new TurnFooterItem(null, null, null, null, true));
        var (view, window) = Open(c);
        var list = UiHelpers.ItemList(view);

        Assert.Equal(new[] { "4.2 s · 38,557 in · 502 out · $0.08" }, UiHelpers.Texts(list.ContainerFromIndex(0)!));
        Assert.Equal(new[] { "2:05 · 1,200 in · 3 out · $1.50" }, UiHelpers.Texts(list.ContainerFromIndex(1)!));
        Assert.Empty(UiHelpers.Texts(list.ContainerFromIndex(2)!)); // nothing to tell: the footer is gone

        view.Strings = ChatStrings.PortugueseBrazil;
        UiHelpers.Pump();
        Assert.Equal(new[] { "4,2 s · 38.557 entrada · 502 saída · US$ 0,08" }, UiHelpers.Texts(list.ContainerFromIndex(0)!));
        window.Close();
    }

    // ---- the list ----

    [AvaloniaFact]
    public void Sticks_to_the_end_until_the_user_scrolls_up()
    {
        var c = new ChatConversation();
        for (var i = 0; i < 100; i++) c.Items.Add(new UserMessageItem("message " + i));
        var (view, window) = Open(c);
        var scroller = UiHelpers.Scroller(view);
        Assert.True(UiHelpers.AtEnd(scroller));
        Assert.True(scroller.Offset.Y > 0);

        c.Items.Add(new UserMessageItem("new one"));
        UiHelpers.Pump();
        Assert.True(UiHelpers.AtEnd(scroller));
        Assert.Contains("new one", UiHelpers.Texts(view));

        scroller.Offset = new Vector(0, 100); // the user scrolls up to read
        UiHelpers.Pump();
        c.Items.Add(new UserMessageItem("while reading"));
        UiHelpers.Pump();
        Assert.Equal(100, scroller.Offset.Y);

        scroller.ScrollToEnd(); // back at the end, it sticks again
        UiHelpers.Pump();
        c.Items.Add(new UserMessageItem("last"));
        UiHelpers.Pump();
        Assert.True(UiHelpers.AtEnd(scroller));
        Assert.Contains("last", UiHelpers.Texts(view));
        window.Close();
    }

    [AvaloniaFact]
    public void Keeps_following_when_the_request_closes_as_the_next_lines_arrive()
    {
        // the request under the list goes (the list gets taller) in the same layout pass as the claude's next lines
        // come: the scroll viewer brings the offset down to keep it in range, and that is not the user scrolling up
        var lines = FixtureFiles.StreamLines("mode-and-model");
        var c = new ChatConversation();
        var next = 0;
        while (c.CurrentPermission == null) c.AppendLine(lines[next++]);
        var (view, window) = Open(c);
        var scroller = UiHelpers.Scroller(view);
        Assert.True(UiHelpers.AtEnd(scroller));
        Assert.True(scroller.Offset.Y > 0);

        c.AnswerPermission(c.CurrentPermission!, PermissionDecision.Allow);
        while (next < lines.Length) c.AppendLine(lines[next++]);
        UiHelpers.Pump();

        Assert.True(UiHelpers.AtEnd(scroller), $"offset {scroller.Offset.Y} of {scroller.Extent.Height - scroller.Viewport.Height}");
        c.Items.Add(new UserMessageItem("and the next one"));
        UiHelpers.Pump();
        Assert.True(UiHelpers.AtEnd(scroller));
        Assert.Contains("and the next one", UiHelpers.Texts(view));
        window.Close();
    }

    [AvaloniaFact]
    public void Reading_above_stays_when_the_request_closes_as_the_next_lines_arrive()
    {
        // the other side of the test above: the user went up to read before the request closed
        var lines = FixtureFiles.StreamLines("mode-and-model");
        var c = new ChatConversation();
        for (var i = 0; i < 60; i++) c.Items.Add(new UserMessageItem("earlier " + i));
        var next = 0;
        while (c.CurrentPermission == null) c.AppendLine(lines[next++]);
        var (view, window) = Open(c);
        var scroller = UiHelpers.Scroller(view);
        Assert.True(UiHelpers.AtEnd(scroller));
        scroller.Offset = new Vector(0, 50); // the user goes up to read
        UiHelpers.Pump();
        Assert.Equal(50, scroller.Offset.Y);

        c.AnswerPermission(c.CurrentPermission!, PermissionDecision.Allow);
        while (next < lines.Length) c.AppendLine(lines[next++]);
        UiHelpers.Pump();

        Assert.Equal(50, scroller.Offset.Y); // the list stays where the user is
        Assert.False(UiHelpers.AtEnd(scroller));
        window.Close();
    }

    // ---- images in a message, and what a command printed ----

    private static byte[] Png(int w, int h)
    {
        using var bmp = new SkiaSharp.SKBitmap(w, h, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Premul);
        bmp.Erase(new SkiaSharp.SKColor(30, 120, 200));
        using var img = SkiaSharp.SKImage.FromBitmap(bmp);
        using var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static List<Button> Thumbnails(Visual root) =>
        root.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("ClaudeChatMessageImage")).ToList();

    [AvaloniaFact]
    public void User_message_shows_thumbnails()
    {
        var c = new ChatConversation();
        c.Items.Add(new UserMessageItem("look at these", new[]
        {
            ChatImage.FromBytes(Png(96, 48), "image/png"),
            ChatImage.FromBytes(Png(1000, 500), "image/jpeg"),
        }));
        c.Items.Add(new UserMessageItem("", new[] { ChatImage.FromBytes(Png(20, 30), "image/png") }));
        var (view, window) = Open(c);

        var boxes = view.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("ClaudeChatUser")).ToList();
        Assert.Equal(2, boxes.Count);
        var thumbs = Thumbnails(boxes[0]);
        Assert.Equal(2, thumbs.Count);

        // under the text, 64 px high at most and in proportion, never larger than the image; 4 px apart
        var text = boxes[0].GetVisualDescendants().OfType<SelectableTextBlock>().Single();
        Assert.Equal("look at these", text.Text);
        var first = thumbs[0].TranslatePoint(default, boxes[0])!.Value;
        Assert.True(first.Y >= text.TranslatePoint(default, boxes[0])!.Value.Y + text.Bounds.Height);
        Assert.Equal(new Size(96, 48), thumbs[0].Bounds.Size);
        Assert.Equal(new Size(128, 64), thumbs[1].Bounds.Size);
        Assert.Equal(first.X + 96 + 4, thumbs[1].TranslatePoint(default, boxes[0])!.Value.X, 1);
        Assert.Equal(new CornerRadius(4), thumbs[0].GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_ChatButton").CornerRadius);
        Assert.Equal("image.png", Avalonia.Automation.AutomationProperties.GetName(thumbs[0]));
        Assert.Equal("image-2.jpg", Avalonia.Automation.AutomationProperties.GetName(thumbs[1]));

        // a message with only an image: no empty line of text over it
        Assert.False(boxes[1].GetVisualDescendants().OfType<SelectableTextBlock>().Single().IsVisible);
        Assert.Equal(new Size(20, 30), Thumbnails(boxes[1])[0].Bounds.Size);

        // a click opens the preview, the one of the reply box's images
        var preview = view.GetVisualDescendants().OfType<ImagePreview>().Single();
        UiHelpers.Click(window, thumbs[1]);
        Assert.True(preview.IsEffectivelyVisible);
        Assert.Equal("image-2.jpg", Avalonia.Automation.AutomationProperties.GetName(preview));
        Assert.Equal(1000, ((Avalonia.Media.Imaging.Bitmap)preview.GetVisualDescendants().OfType<Image>().Single().Source!).PixelSize.Width);
        UiHelpers.Press(window, Avalonia.Input.Key.Escape);
        Assert.False(preview.IsVisible);

        // read-only: the thumbnails stay, and still open the preview
        view.IsReadOnly = true;
        UiHelpers.Pump();
        thumbs = Thumbnails(view);
        Assert.Equal(3, thumbs.Count);
        Assert.All(thumbs, t => Assert.True(t.IsEffectivelyVisible));
        UiHelpers.Click(window, thumbs[0]);
        Assert.True(preview.IsEffectivelyVisible);
        Assert.Equal("image.png", Avalonia.Automation.AutomationProperties.GetName(preview));
        UiHelpers.Click(window, UiHelpers.ButtonWithText(preview, "Close"));
        Assert.False(preview.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void Thumbnails_decode_small()
    {
        var c = new ChatConversation();
        c.Items.Add(new UserMessageItem("", new[]
        {
            ChatImage.FromBytes(Png(1000, 500), "image/png"),
            ChatImage.FromBytes(Png(2000, 50), "image/png"), // so wide that the width is what limits it
        }));
        c.Attachments.Add(ChatImage.FromBytes(Png(1568, 39), "image/png"));
        var (view, window) = Open(c);

        var images = Thumbnails(view).Select(t => t.GetVisualDescendants().OfType<Image>().Single()).ToList();
        Assert.Equal(2, images.Count);
        UiHelpers.PumpUntil(() => images.All(i => i.Source != null)); // decoded off the UI thread
        foreach (var image in images)
        {
            var size = ((Avalonia.Media.Imaging.Bitmap)image.Source!).PixelSize;
            Assert.True(size.Width <= 2 * image.Bounds.Width + 1, size + " for " + image.Bounds.Size);
            Assert.True(size.Height <= 2 * image.Bounds.Height + 1, size + " for " + image.Bounds.Size);
        }
        Assert.Equal(new PixelSize(256, 128), ((Avalonia.Media.Imaging.Bitmap)images[0].Source!).PixelSize);

        // the reply box's 12 px thumbnail: both sides at 24 px at most, however long the image
        var strip = view.GetVisualDescendants().OfType<AttachmentStrip>().Single();
        var pill = ((Avalonia.Media.Imaging.Bitmap)strip.GetVisualDescendants().OfType<Image>().Single().Source!).PixelSize;
        Assert.True(pill.Width <= 24 && pill.Height <= 24, pill.ToString());
        window.Close();
    }

    [AvaloniaFact]
    public void Thumbnails_decode_off_the_ui_thread()
    {
        var cache = MessageImages.Thumbnails;
        var run = cache.Run;
        var pending = new List<(Func<Avalonia.Media.Imaging.Bitmap?> Work, System.Threading.Tasks.TaskCompletionSource<Avalonia.Media.Imaging.Bitmap?> Done)>();
        cache.Run = work =>
        {
            var done = new System.Threading.Tasks.TaskCompletionSource<Avalonia.Media.Imaging.Bitmap?>();
            pending.Add((work, done));
            return done.Task;
        };
        // the decode ends on another thread, as Task.Run's does
        void Finish(int i) => System.Threading.Tasks.Task.Run(() => pending[i].Done.SetResult(pending[i].Work()));
        try
        {
            var c = new ChatConversation();
            c.Items.Add(new UserMessageItem("", new[] { ChatImage.FromBytes(Png(100, 50), "image/png") }));
            var (view, window) = Open(c);
            Image Shown() => Thumbnails(view).Single().GetVisualDescendants().OfType<Image>().Single();

            // realized without its picture, at its size already; the picture comes when the decode ends
            var first = Shown();
            Assert.Null(first.Source);
            Assert.Equal(new Size(100, 50), Thumbnails(view).Single().Bounds.Size); // its place is kept
            Assert.Single(pending);
            Finish(0);
            UiHelpers.PumpUntil(() => first.Source != null);
            Assert.Equal(new PixelSize(100, 50), ((Avalonia.Media.Imaging.Bitmap)first.Source!).PixelSize);

            // the control taken for other messages (the list recycles it) while their decodes run: the one that ends
            // for a message no longer shown does not reach it
            var images = view.GetVisualDescendants().OfType<MessageImages>().Single();
            images.Images = new[] { ChatImage.FromBytes(Png(60, 30), "image/png") };
            UiHelpers.Pump();
            var stale = Shown();
            images.Images = new[] { ChatImage.FromBytes(Png(30, 60), "image/png") };
            UiHelpers.Pump();
            var current = Shown();
            Assert.Equal(3, pending.Count);
            Finish(1);
            UiHelpers.Settle();
            Assert.Null(current.Source);
            Assert.Null(stale.Source);
            Finish(2);
            UiHelpers.PumpUntil(() => current.Source != null);
            Assert.Equal(new PixelSize(30, 60), ((Avalonia.Media.Imaging.Bitmap)current.Source!).PixelSize);

            // decoded once: shown again, it is there at once
            images.Images = new[] { c.Items.OfType<UserMessageItem>().Single().Images[0] };
            UiHelpers.Pump();
            Assert.NotNull(Shown().Source);
            Assert.Equal(3, pending.Count);
            window.Close();
        }
        finally
        {
            cache.Run = run;
        }
    }

    [AvaloniaFact]
    public void Command_output_is_text()
    {
        var c = new ChatConversation();
        c.Items.Add(new UserMessageItem("/hello Ana"));
        c.Items.Add(new CommandOutputItem("Hello, Ana!\n  two spaces in"));
        c.Items.Add(new AssistantTextItem { Text = "after" });
        var (view, window) = Open(c);

        var output = view.GetVisualDescendants().OfType<SelectableTextBlock>().Single(t => t.Classes.Contains("ClaudeChatCommandOutput"));
        Assert.Equal("Hello, Ana!\n  two spaces in", output.Text);
        Assert.Equal(Resource(view, "ClaudeChat.MonoFontFamily"), output.FontFamily);
        Assert.Equal(Resource(view, "ClaudeChat.CodeFontSize"), output.FontSize);
        Assert.Equal(Assert.IsAssignableFrom<ISolidColorBrush>(Resource(view, "ClaudeChat.ForegroundMuted")).Color,
            Assert.IsAssignableFrom<ISolidColorBrush>(output.Foreground).Color);

        // in the timeline like an answer, without a dot
        var row = output.FindAncestorOfType<ChatTimelineRow>()!;
        Assert.Equal(30, output.TranslatePoint(default, row)!.Value.X, 0);
        Assert.False(row.GetVisualDescendants().OfType<Ellipse>().Single(e => e.Name == "PART_Dot").IsEffectivelyVisible);

        // the line of the run starts where its dot would be and goes on, unbroken, into the answer after it
        var line = row.GetVisualDescendants().OfType<Rectangle>().Single(r => r.Name == "PART_Line");
        Assert.True(line.IsEffectivelyVisible);
        Assert.Equal(18, line.TranslatePoint(default, row)!.Value.Y, 0);
        Assert.Equal(row.Bounds.Height, line.TranslatePoint(default, row)!.Value.Y + line.Bounds.Height, 0);
        var next = view.GetVisualDescendants().OfType<ChatTimelineRow>().Single(r => r.DataContext is AssistantTextItem);
        Assert.Equal(row.TranslatePoint(new Point(0, row.Bounds.Height), view)!.Value.Y, next.TranslatePoint(default, view)!.Value.Y, 0);
        var nextLine = next.GetVisualDescendants().OfType<Rectangle>().Single(r => r.Name == "PART_Line");
        Assert.True(nextLine.IsEffectivelyVisible);
        Assert.Equal(0, nextLine.TranslatePoint(default, next)!.Value.Y, 0);

        // alone between the user's messages: no line at all
        c.Items.RemoveAt(2);
        c.Items.Add(new UserMessageItem("thanks"));
        UiHelpers.Pump();
        Assert.False(line.IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void Command_output_of_the_recording_shows()
    {
        var c = UiHelpers.Recording("commands", "transcript");
        var item = c.Items.OfType<CommandOutputItem>().First();
        var (view, window) = Open(c);
        UiHelpers.ItemList(view).ScrollIntoView(item);
        UiHelpers.Pump();
        Assert.Contains(view.GetVisualDescendants().OfType<SelectableTextBlock>(),
            t => t.Classes.Contains("ClaudeChatCommandOutput") && t.Text == item.Text && t.IsEffectivelyVisible);
        window.Close();
    }

    // ---- theme ----

    [AvaloniaFact]
    public void Theme_resources_can_be_overridden()
    {
        var c = new ChatConversation();
        c.Items.Add(new UserMessageItem("hello"));
        var view = new ClaudeChatView { Conversation = c };
        var window = UiHelpers.Show(view);
        var root = Assert.IsAssignableFrom<Border>(view.Content);
        Assert.Equal(Color.Parse("#191A1B"), Assert.IsAssignableFrom<ISolidColorBrush>(root.Background).Color); // Dark 2026
        // the diff's line colors, those of the extension's editor
        Assert.Equal(Color.Parse("#339BB955"), Assert.IsAssignableFrom<ISolidColorBrush>(Resource(view, "ClaudeChat.DiffAdded")).Color);
        Assert.Equal(Color.Parse("#33FF0000"), Assert.IsAssignableFrom<ISolidColorBrush>(Resource(view, "ClaudeChat.DiffRemoved")).Color);

        view.Resources["ClaudeChat.Background"] = new SolidColorBrush(Colors.DarkRed);
        view.Resources["ClaudeChat.Foreground"] = new SolidColorBrush(Colors.Gold);
        UiHelpers.Pump();

        Assert.Equal(Colors.DarkRed, Assert.IsAssignableFrom<ISolidColorBrush>(root.Background).Color);
        var hello = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "hello");
        Assert.Equal(Colors.Gold, Assert.IsAssignableFrom<ISolidColorBrush>(hello.Foreground).Color);
        window.Close();
    }
}
