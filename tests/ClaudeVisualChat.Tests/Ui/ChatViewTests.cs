using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Automation;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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
        Assert.Contains("Interrompido", texts);
        Assert.Contains("Pensando", texts);
        Assert.DoesNotContain("Recusado", texts); // a refused tool says it with its dot, not with a word

        view.Strings = ChatStrings.English;
        UiHelpers.Pump();

        texts = UiHelpers.Texts(view);
        Assert.Contains("Interrupted", texts);
        Assert.Contains("Thinking", texts);
        Assert.DoesNotContain("Rejected", texts);
        Assert.DoesNotContain("Interrompido", texts);
        window.Close();
    }

    [AvaloniaFact]
    public void Notices_use_their_texts_and_a_failed_turn_is_red()
    {
        var c = new ChatConversation();
        foreach (NoticeKind kind in Enum.GetValues(typeof(NoticeKind))) c.Items.Add(new NoticeItem(kind));
        var (view, window) = Open(c);

        var texts = UiHelpers.Texts(view);
        Assert.Contains("Interrupted", texts);
        Assert.Contains("Tool interrupted", texts);
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
    public void Huge_tool_output_opens_fast_and_shows_its_first_lines_cut()
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
        Assert.Contains(texts, t => t.EndsWith("line 8", StringComparison.Ordinal));
        Assert.DoesNotContain(texts, t => t.Contains("line 9"));
        Assert.DoesNotContain(texts, t => t.StartsWith("+", StringComparison.Ordinal) && t.Contains("lines")); // no "+N lines": the cut is the 60 px
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
        Assert.InRange(output.Length, 100, ToolCallView.MaxLineChars + 1);
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
        Assert.Contains("GitHub [search_issues]", texts);
        Assert.Contains("boom", texts);

        texts = UiHelpers.Texts(ViewOf(view, rejected));
        Assert.DoesNotContain("Rejected", texts); // the extension never writes it
        Assert.DoesNotContain(texts, t => t.StartsWith("The user", StringComparison.Ordinal));

        // the extension's dot colors
        Assert.Same(Resource(view, "ClaudeChat.DotSucceeded"), DotFill(view, edit));
        Assert.Same(Resource(view, "ClaudeChat.Dot"), DotFill(view, todos));
        Assert.Same(Resource(view, "ClaudeChat.DotFailed"), DotFill(view, mcp));
        Assert.Same(Resource(view, "ClaudeChat.DotFailed"), DotFill(view, rejected)); // red, like an error

        mcp.Status = ToolStatus.Succeeded; // the dot follows the item
        UiHelpers.Pump();
        Assert.Same(Resource(view, "ClaudeChat.DotSucceeded"), DotFill(view, mcp));
        window.Close();
    }

    private static ClaudeChatView OpenIn(ChatConversation c, bool touch)
    {
        var view = touch ? UiHelpers.TouchView(c) : new ClaudeChatView { Conversation = c };
        if (touch) UiHelpers.ShowPhone(view);
        else UiHelpers.Show(view);
        return view;
    }

    /// <summary>The dot of a tool call that the layout draws: the timeline's on the desktop, the header's in the touch layout.</summary>
    private static Ellipse VisibleDot(Visual root, ToolCallItem item) =>
        ViewOf(root, item).GetVisualDescendants().OfType<Ellipse>().First(e => e.IsEffectivelyVisible);

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_rejected_tool_has_a_red_dot_and_no_rejected_label(bool touch)
    {
        var rejected = new ToolCallItem
        {
            Name = "Write", Summary = "b.txt", Status = ToolStatus.Rejected, ResultText = "The user doesn't want to proceed",
        };
        var failed = new ToolCallItem { Name = "Write", Summary = "c.txt", Status = ToolStatus.Failed, ResultText = "boom" };
        var c = new ChatConversation();
        c.Items.Add(rejected);
        c.Items.Add(failed);
        var view = OpenIn(c, touch);

        var texts = UiHelpers.Texts(ViewOf(view, rejected));
        Assert.DoesNotContain(ChatStrings.English.ToolRejected, texts);
        Assert.DoesNotContain(ChatStrings.PortugueseBrazil.ToolRejected, texts);
        var red = Resource(view, "ClaudeChat.DotFailed");
        Assert.Same(red, VisibleDot(view, rejected).Fill);
        Assert.Same(VisibleDot(view, failed).Fill, VisibleDot(view, rejected).Fill); // red like an error
        Assert.NotSame(Resource(view, "ClaudeChat.DotRejected"), VisibleDot(view, rejected).Fill);
        Assert.Equal(ToolStatus.Rejected, rejected.Status); // the model still tells them apart
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_tool_cut_by_stop_turns_red_when_the_session_stops(bool touch)
    {
        var c = new ChatConversation();
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("t1", "Bash", J("{\"command\":\"sleep 60\"}"))));
        var tool = c.Items.OfType<ToolCallItem>().Single();
        var view = OpenIn(c, touch);
        Assert.NotSame(Resource(view, "ClaudeChat.DotFailed"), VisibleDot(view, tool).Fill); // still running: not red

        c.Apply(new UserBlock(null, new ClaudeVisualChat.Protocol.TextContentBlock("[Request interrupted by user for tool use]"), null, false, false));
        c.Apply(new ClaudeVisualChat.Protocol.TurnCompleted("error_during_execution", true, 1, null, null, null));
        UiHelpers.Pump();

        Assert.Equal(ToolStatus.Rejected, tool.Status);
        Assert.Same(Resource(view, "ClaudeChat.DotFailed"), VisibleDot(view, tool).Fill);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_interrupted_line_reads_interrupted_without_a_dot(bool touch)
    {
        var c = new ChatConversation();
        c.Items.Add(new UserMessageItem("do it"));
        c.Items.Add(new NoticeItem(NoticeKind.Interrupted));
        c.Items.Add(new NoticeItem(NoticeKind.ToolInterrupted));
        var view = OpenIn(c, touch);
        var strings = touch ? ChatStrings.PortugueseBrazil : ChatStrings.English;

        foreach (var (kind, text) in new[] { (NoticeKind.Interrupted, strings.Interrupted), (NoticeKind.ToolInterrupted, strings.ToolInterrupted) })
        {
            var line = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == text);
            Assert.Equal(text, strings.Notice(kind));
            Assert.Equal(FontStyle.Italic, line.FontStyle);
            Assert.Same(Resource(view, "ClaudeChat.ForegroundMuted"), line.Foreground);
            Assert.Equal(new Thickness(0, 8), line.Margin);                                  // 8 px over and under, no indent
            Assert.Equal(Avalonia.Layout.HorizontalAlignment.Stretch, line.HorizontalAlignment);
            // a plain line: no timeline row (so no dot), no bubble around it
            Assert.DoesNotContain(line.GetVisualAncestors(), a => a is ChatTimelineRow || a is Border b && b.Classes.Contains("ClaudeChatUser"));
            var holder = line.GetVisualAncestors().OfType<Avalonia.Controls.Presenters.ContentPresenter>().First();
            Assert.Equal(0, line.TranslatePoint(default, holder)!.Value.X, 1);              // starts at the column's edge
            Assert.Equal(holder.Bounds.Width, line.Bounds.Width, 1);                        // and spans it
            Assert.DoesNotContain(holder.GetVisualDescendants(), d => d is Ellipse);
        }
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

        // a subagent call never has an OUT row, even when it failed: like the extension's (a background one says why on its line)
        var c = new ChatConversation();
        c.Items.Add(new ToolCallItem { Name = "Task", Summary = "dig", Status = ToolStatus.Failed, ResultText = "agent crashed" });
        c.Items.Add(new ToolCallItem { Name = "Agent", Summary = "dig", Status = ToolStatus.Succeeded, ResultText = "agentId: 42", HiddenResultLines = 5 });
        (view, window) = Open(c);
        texts = UiHelpers.Texts(view);
        Assert.DoesNotContain("agent crashed", texts);
        Assert.DoesNotContain(view.Strings.ToolOutput, texts);
        Assert.DoesNotContain("agentId: 42", texts);
        Assert.DoesNotContain("+5 lines", texts);
        window.Close();
    }

    [AvaloniaFact]
    public void Subagent_children_expand()
    {
        // a foreground agent keeps its steps behind the arrow (the background one of the recording shows none, see below)
        var c = new ChatConversation();
        foreach (var line in new[]
        {
            AgentMapTests.AgentCall("toolu_fg", "Look around", background: false), AgentMapTests.BashCall("toolu_c1", parent: "toolu_fg"),
        })
        {
            c.AppendLine(line);
        }
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

    // ---- the Agent row: "Agent:", the prompt in the IN row, the background line (0.6.0) ----

    private static ChatConversation Playing(params string[] lines)
    {
        var c = new ChatConversation();
        foreach (var line in lines) c.AppendLine(line);
        return c;
    }

    /// <summary>The line under a background agent's row, or null when the row has none.</summary>
    private static Panel? AgentLine(Visual root, ToolCallItem item) =>
        ViewOf(root, item).GetVisualDescendants().OfType<Panel>().SingleOrDefault(p => p.Classes.Contains("ClaudeChatAgentLine"));

    private static string AgentLineText(Visual root, ToolCallItem item) =>
        AgentLine(root, item)!.GetVisualDescendants().OfType<TextBlock>().Single().Text ?? "";

    private static Ellipse AgentDot(Visual root, ToolCallItem item) =>
        AgentLine(root, item)!.GetVisualDescendants().OfType<Ellipse>().Single();

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_agent_row_reads_agent_colon_and_shows_its_prompt_in_the_in_row(bool touch)
    {
        // eight lines (the JSON's line breaks): past three lines, it opens whole
        var prompt = "Read the three files and summarize them." + string.Concat(Enumerable.Range(2, 7).Select(i => "\\nStep " + i + ": check it again."));
        var c = Playing(AgentMapTests.AgentCall("toolu_fg", "Summarize files", prompt: prompt, background: false));
        c.Items.Add(new ToolCallItem { Name = "Task", Summary = "dig", Status = ToolStatus.Succeeded, Input = J("{\"description\":\"dig\",\"prompt\":\"Dig.\"}") });
        var agent = c.Items.OfType<ToolCallItem>().First();
        var task = c.Items.OfType<ToolCallItem>().Last();
        var view = OpenIn(c, touch);
        var strings = view.Strings;
        var row = ViewOf(view, agent);

        // "Agent:" in bold, with the colon, then the description in the editor's font at .85em
        var name = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "ToolName");
        var detail = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "DetailText");
        Assert.Equal("Agent:", name.Text);
        Assert.Equal(FontWeight.Bold, name.FontWeight);
        Assert.Equal("Summarize files", detail.Text);
        Assert.Same(Resource(view, "ClaudeChat.MonoFontFamily"), detail.FontFamily);
        Assert.Equal(0.85, detail.FontSize / name.FontSize, 2);
        Assert.True(Left(detail, row) > Right(name, row));                          // after the name, on its line
        // a Task call has the same header
        Assert.Equal("Agent:", ViewOf(view, task).GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "ToolName").Text);

        // one box, one IN row with the prompt, cut at 60 px; a long prompt opens whole in place
        var contents = Contents(view, agent);
        var content = Assert.Single(contents);
        Assert.Contains(strings.ToolInput, UiHelpers.Texts(row));
        Assert.DoesNotContain(strings.ToolOutput, UiHelpers.Texts(row));
        Assert.StartsWith("Read the three files", content.GetVisualDescendants().OfType<TextBlock>().Single().Text);
        Assert.True(content.Bounds.Height <= 68.5);
        Assert.Contains("expandable", content.Classes);
        Assert.Null(AgentLine(view, agent));                                       // a foreground agent has no line under it
        Assert.DoesNotContain(row.GetVisualDescendants().OfType<TextBlock>(), t => t.Classes.Contains("ClaudeChatSecondary"));
        agent.IsInputExpanded = true;
        UiHelpers.Pump();
        content = Assert.Single(Contents(view, agent));
        Assert.True(content.Bounds.Height > 68.5);
        Assert.Single(Contents(view, task));                                        // the Task's prompt too

        // its result, even a failure, never makes an OUT row
        c.AppendLine(
            "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":[{\"tool_use_id\":\"toolu_fg\",\"type\":\"tool_result\"," +
            "\"content\":\"The agent crashed\",\"is_error\":true}]},\"parent_tool_use_id\":null,\"session_id\":\"s1\"}");
        UiHelpers.Pump();
        Assert.Equal(ToolStatus.Failed, agent.Status);
        Assert.Single(Contents(view, agent));
        Assert.DoesNotContain(UiHelpers.Texts(ViewOf(view, agent)), t => t.Contains("The agent crashed"));
        WindowOf(view).Close();
    }

    [AvaloniaFact]
    public void The_agent_row_takes_the_prompt_of_the_task_line_when_the_call_has_none()
    {
        var c = Playing(
            AgentMapTests.TaskStarted("ta1", "toolu_a1", "Look", prompt: "Look around the repo."),
            "{\"type\":\"assistant\",\"message\":{\"id\":\"msg_a1\",\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"tool_use\"," +
            "\"id\":\"toolu_a1\",\"name\":\"Agent\",\"input\":{\"description\":\"Look\"}}]},\"parent_tool_use_id\":null,\"session_id\":\"s1\"}");
        var agent = c.Items.OfType<ToolCallItem>().Single();
        var (view, window) = Open(c);
        Assert.Contains("Look around the repo.", UiHelpers.Texts(ViewOf(view, agent)));
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_background_agent_keeps_its_row_and_adds_working_in_the_background(bool touch)
    {
        var c = Playing(AgentMapTests.AgentCall("toolu_a1", "Read the logs"), AgentMapTests.TaskStarted("ta1", "toolu_a1", "Read the logs"));
        var agent = c.Items.OfType<ToolCallItem>().Single();
        var view = OpenIn(c, touch);
        var strings = view.Strings;
        string Line(string head) => head + " · " + strings.AgentTasksHint;

        // the row stays (its prompt in the IN row), and the line comes under it: an 8 px green dot, .85em muted at 70 %
        Assert.Single(Contents(view, agent));
        var line = AgentLine(view, agent);
        Assert.NotNull(line);
        Assert.Equal(Line(strings.AgentWorkingInBackground), AgentLineText(view, agent));
        Assert.Equal(touch ? "Trabalhando em segundo plano · digite /tasks para ver a transcrição dele" : "Working in the background · type /tasks to see its transcript",
            AgentLineText(view, agent));
        var dot = AgentDot(view, agent);
        Assert.Equal((8.0, 8.0), (dot.Bounds.Width, dot.Bounds.Height));
        Assert.Same(Resource(view, "ClaudeChat.AgentRunning"), dot.Fill);
        Assert.Equal(1, dot.Opacity);
        Assert.Equal(0.7, line!.Opacity, 2);
        var text = line.GetVisualDescendants().OfType<TextBlock>().Single();
        Assert.Same(Resource(view, "ClaudeChat.ForegroundMuted"), text.Foreground);
        Assert.Equal(0.85, text.FontSize / ViewOf(view, agent).GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "ToolName").FontSize, 2);
        Assert.True(Left(text, line) > Right(dot, line));                           // the dot first, then the text
        var box = ViewOf(view, agent).GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("ClaudeChatToolBody"));
        Assert.True(line.TranslatePoint(default, view)!.Value.Y >= box.TranslatePoint(new Point(0, box.Bounds.Height), view)!.Value.Y); // under the box

        // its activity, cut at 120 characters; left out when it is the description
        c.AppendLine(AgentMapTests.TaskProgress("ta1", "toolu_a1", "Reading a.txt"));
        UiHelpers.Pump();
        Assert.Equal(Line(strings.AgentWorkingInBackgroundOn.Replace("{0}", "Reading a.txt")), AgentLineText(view, agent));
        var longActivity = new string('x', 130);
        c.AppendLine(AgentMapTests.TaskProgress("ta1", "toolu_a1", longActivity));
        UiHelpers.Pump();
        Assert.Equal(Line(strings.AgentWorkingInBackgroundOn.Replace("{0}", new string('x', 119) + "…")), AgentLineText(view, agent));
        c.AppendLine(AgentMapTests.TaskProgress("ta1", "toolu_a1", "Read the logs"));
        UiHelpers.Pump();
        Assert.Equal(Line(strings.AgentWorkingInBackground), AgentLineText(view, agent));

        // an agent it started fails, then it finishes: the gray dot at 40 %, with the suffix (and the dot of the failure)
        c.AppendLine(AgentMapTests.AgentCall("toolu_kid", "Kid", parent: "toolu_a1"));
        c.AppendLine(AgentMapTests.TaskStarted("tkid", "toolu_kid", "Kid", depth: 2));
        c.AppendLine(AgentMapTests.TaskFinished("tkid", "toolu_kid", "failed", "Boom"));
        c.AppendLine(AgentMapTests.TaskFinished("ta1", "toolu_a1", "completed", "All read"));
        UiHelpers.Pump();
        Assert.Equal(strings.AgentFinished + " · " + strings.AgentNestedFailed + " · " + strings.AgentTasksHint, AgentLineText(view, agent));
        Assert.Same(Resource(view, "ClaudeChat.Error"), AgentDot(view, agent).Fill);
        WindowOf(view).Close();

        // the states one by one: finished, failed (with what it said), stopped
        foreach (var (status, summary, head, brush, opacity) in new[]
        {
            ("completed", "All read", strings.AgentFinished, "ClaudeChat.ForegroundMuted", 0.4),
            ("failed", "Disk full", strings.AgentFailedWith.Replace("{0}", "Disk full"), "ClaudeChat.Error", 1.0),
            ("killed", "Stopped", strings.AgentStopped, "ClaudeChat.Error", 1.0),
        })
        {
            c = Playing(
                AgentMapTests.AgentCall("toolu_a1", "Read the logs"), AgentMapTests.TaskStarted("ta1", "toolu_a1", "Read the logs"),
                AgentMapTests.TaskFinished("ta1", "toolu_a1", status, summary));
            agent = c.Items.OfType<ToolCallItem>().Single();
            view = OpenIn(c, touch);
            Assert.Equal(Line(head), AgentLineText(view, agent));
            Assert.Same(Resource(view, brush), AgentDot(view, agent).Fill);
            Assert.Equal(opacity, AgentDot(view, agent).Opacity, 2);
            WindowOf(view).Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void A_waiting_background_agent_reads_waiting_for_your_permission(bool touch, bool withAgentId)
    {
        // the request of its step: with agent_id (2.1.310), or without it, by the step that asks
        var c = Playing(
            AgentMapTests.AgentCall("toolu_a1", "Write a file"), AgentMapTests.TaskStarted("ta1", "toolu_a1", "Write a file"),
            AgentMapTests.BashCall("toolu_b1", parent: "toolu_a1"));
        var agent = c.Items.OfType<ToolCallItem>().Single();
        var view = OpenIn(c, touch);
        var strings = view.Strings;
        Assert.Equal(strings.AgentWorkingInBackground + " · " + strings.AgentTasksHint, AgentLineText(view, agent));

        c.AppendLine(AgentMapTests.BashRequest("r1", "toolu_b1", withAgentId ? "ta1" : null));
        UiHelpers.Pump();
        Assert.Equal(strings.AgentWaitingForPermission + " · " + strings.AgentTasksHint, AgentLineText(view, agent));
        Assert.Equal(touch ? "Aguardando sua permissão: veja o pedido abaixo · digite /tasks para ver a transcrição dele"
            : "Waiting for your permission: see the request below · type /tasks to see its transcript", AgentLineText(view, agent));
        Assert.Same(Resource(view, "ClaudeChat.AgentWaiting"), AgentDot(view, agent).Fill);
        Assert.Equal(Color.Parse("#3B82F6"), ((ISolidColorBrush)AgentDot(view, agent).Fill!).Color);
        Assert.True(UiHelpers.Prompt(view).IsEffectivelyVisible);                   // the request is below, in the usual card

        // an agent it started waits too: both in one sentence
        c.AppendLine(AgentMapTests.AgentCall("toolu_kid", "Kid", parent: "toolu_a1"));
        c.AppendLine(AgentMapTests.TaskStarted("tkid", "toolu_kid", "Kid", depth: 2));
        c.AppendLine(AgentMapTests.BashRequest("r2", "toolu_kb", "tkid"));
        UiHelpers.Pump();
        Assert.Equal(strings.AgentWaitingAlongWithNested + " · " + strings.AgentTasksHint, AgentLineText(view, agent));

        // answered, it works again, and only the agent it started waits
        c.AnswerPermission(c.PendingPermissions.Single(p => p.RequestId == "r1"), PermissionDecision.Allow);
        UiHelpers.Pump();
        Assert.Equal(strings.AgentWorkingInBackground + " · " + strings.AgentNestedWaiting + " · " + strings.AgentTasksHint, AgentLineText(view, agent));
        Assert.Same(Resource(view, "ClaudeChat.AgentWaiting"), AgentDot(view, agent).Fill);
        c.DismissPermission("r2");
        UiHelpers.Pump();
        Assert.Equal(strings.AgentWorkingInBackground + " · " + strings.AgentTasksHint, AgentLineText(view, agent)); // both work: nothing to add
        Assert.Same(Resource(view, "ClaudeChat.AgentRunning"), AgentDot(view, agent).Fill);

        // it ends while the agent it started still works
        c.AppendLine(AgentMapTests.TaskFinished("ta1", "toolu_a1", "completed", "Done"));
        UiHelpers.Pump();
        Assert.Equal(strings.AgentFinished + " · " + strings.AgentNestedWorking + " · " + strings.AgentTasksHint, AgentLineText(view, agent));
        Assert.Same(Resource(view, "ClaudeChat.AgentRunning"), AgentDot(view, agent).Fill);
        WindowOf(view).Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_background_agents_steps_do_not_show_in_the_conversation(bool touch)
    {
        // the recorded agent runs in the background: its steps are in the item, for the map, but not on the list
        var c = UiHelpers.Recording("subagent", "stream");
        var agent = c.Items.OfType<ToolCallItem>().Single();
        Assert.True(Assert.Single(c.Agents).IsBackground);
        var steps = agent.Children.OfType<ToolCallItem>().Select(t => t.Summary).ToList();
        Assert.NotEmpty(steps);
        var view = OpenIn(c, touch);
        UiHelpers.ItemList(view).ScrollIntoView(agent);
        UiHelpers.Pump();
        var row = ViewOf(view, agent);
        var header = row.GetVisualDescendants().OfType<ToggleButton>().First();
        Assert.False(header.Focusable);                                             // nothing to open
        Assert.False(row.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single(p => p.Name == "Arrow").IsVisible);
        Assert.NotNull(AgentLine(view, agent));
        Assert.StartsWith(view.Strings.AgentFinished, AgentLineText(view, agent));

        agent.IsExpanded = true;                                                    // even an item set open shows none
        UiHelpers.Pump();
        var texts = UiHelpers.Texts(row);
        Assert.All(steps, s => Assert.DoesNotContain(s, texts));
        Assert.DoesNotContain(row.GetVisualDescendants().OfType<ToolCallView>(), v => v != row && v.IsEffectivelyVisible);
        WindowOf(view).Close();
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
    public void An_output_and_a_command_do_not_wrap_they_scroll_sideways()
    {
        var error = "error: " + string.Join(" ", Enumerable.Range(1, 80).Select(i => "word" + i));
        var c = new ChatConversation();
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("t1", "Bash", J("{\"command\":\"" + new string('x', 300) + "\"}"))));
        c.Apply(new UserBlock(null, new ToolResultBlock("t1", error, true), null, false, false));
        var (view, window) = Open(c);
        var tool = c.Items.OfType<ToolCallItem>().Single();

        var output = ViewOf(view, tool).GetVisualDescendants().OfType<SelectableTextBlock>().Single(t => (t.Text ?? "").StartsWith("error: word1 ", StringComparison.Ordinal));
        Assert.Equal(TextWrapping.NoWrap, output.TextWrapping);
        Assert.True(output.Bounds.Height < 2 * 16, $"{output.Bounds.Height} px: more than one line"); // one line, as long as it is
        var scroller = output.GetVisualAncestors().OfType<ScrollViewer>().First();
        Assert.Equal(ScrollBarVisibility.Auto, scroller.HorizontalScrollBarVisibility);
        Assert.Equal(ScrollBarVisibility.Disabled, scroller.VerticalScrollBarVisibility);
        Assert.True(scroller.Extent.Width > scroller.Viewport.Width, "the long line scrolls sideways");

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

    // ---- the 60 px cut, the click that opens it, and the reason of a refusal ----

    private static string Lines(int count) => string.Join("\n", Enumerable.Range(1, count).Select(i => "line " + i));

    private const string StandardRefusal = "The user doesn't want to proceed with this tool use. The tool use was rejected.";

    /// <summary>A click near the top of a content, where its first line is: a scroll bar may cover the bottom of a wide one.</summary>
    private static void ClickTop(Window window, Control control)
    {
        var at = control.TranslatePoint(new Point(control.Bounds.Width / 2, 6), window)!.Value;
        window.MouseMove(at, RawInputModifiers.None);
        window.MouseDown(at, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
        UiHelpers.Pump();
    }

    private static Window WindowOf(Visual visual) => (Window)TopLevel.GetTopLevel(visual)!;

    /// <summary>The contents of the box's rows (IN, then OUT), in the order they are drawn.</summary>
    private static List<Border> Contents(Visual root, ToolCallItem item) =>
        ViewOf(root, item).GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("ClaudeChatToolContent")).ToList();

    private static string ResultOf(Visual root, ToolCallItem item) =>
        ViewOf(root, item).GetVisualDescendants().OfType<SelectableTextBlock>().Single().Text ?? "";

    private static (ChatConversation C, ToolCallItem Tool) Answered(string output, bool error = false, string command = "seq 20")
    {
        var c = new ChatConversation();
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("t1", "Bash", J(JsonSerializer.Serialize(new { command })))));
        c.Apply(new UserBlock(null, new ToolResultBlock("t1", output, error), null, false, false));
        return (c, c.Items.OfType<ToolCallItem>().Single());
    }

    private static UserBlock Refused(string id, string text) =>
        new UserBlock(null, new ToolResultBlock(id, text, true) { NonExecutionKind = "user-rejected" }, null, false, false);

    private static ChatConversation Refusing(string tool, object input, string text)
    {
        var c = new ChatConversation();
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("t1", tool, J(JsonSerializer.Serialize(input)))));
        c.Apply(Refused("t1", text));
        return c;
    }

    [AvaloniaFact]
    public void A_long_output_is_cut_at_sixty_pixels_with_a_fade()
    {
        var (c, tool) = Answered(Lines(20));
        var (view, window) = Open(c);

        var content = Contents(view, tool)[1];
        Assert.Equal(60, content.Bounds.Height - content.Padding.Top - content.Padding.Bottom, 1); // 60 px of text, however long it is
        var mask = Assert.IsType<LinearGradientBrush>(content.OpacityMask);
        Assert.Equal(60, mask.EndPoint.Point.Y, 3);                                              // opaque down to 50, gone at 60
        Assert.Equal(RelativeUnit.Absolute, mask.EndPoint.Unit);
        Assert.Equal(3, mask.GradientStops.Count);
        Assert.Equal(0, mask.GradientStops[0].Offset, 3);
        Assert.Equal(50.0 / 60, mask.GradientStops[1].Offset, 3);
        Assert.Equal(1, mask.GradientStops[2].Offset, 3);
        Assert.Equal(Colors.Black, mask.GradientStops[1].Color);
        Assert.Equal(Colors.Transparent, mask.GradientStops[2].Color);
        Assert.DoesNotContain(UiHelpers.Texts(view), t => t.Contains(" lines")); // no "+N lines": the cut is the 60 px
        window.Close();
    }

    [AvaloniaFact]
    public void A_click_shows_the_whole_output_and_another_cuts_it_again()
    {
        var (c, tool) = Answered(Lines(20));
        var (view, window) = Open(c);
        var cut = Contents(view, tool)[1].Bounds.Height;
        Assert.False(tool.IsOutputExpanded);
        Assert.DoesNotContain("line 20", ResultOf(view, tool));

        UiHelpers.Click(window, Contents(view, tool)[1]);

        Assert.True(tool.IsOutputExpanded);
        Assert.Equal(Lines(20), ResultOf(view, tool));
        var open = Contents(view, tool)[1];
        Assert.True(open.Bounds.Height > cut + 100, $"{open.Bounds.Height} px open, {cut} px cut");
        Assert.Null(open.OpacityMask); // nothing fades once it is whole

        UiHelpers.Click(window, open);

        Assert.False(tool.IsOutputExpanded);
        Assert.DoesNotContain("line 20", ResultOf(view, tool));
        Assert.Equal(cut, Contents(view, tool)[1].Bounds.Height, 1);
        window.Close();
    }

    [AvaloniaFact]
    public void Enter_and_Space_open_and_cut_the_output_and_the_focus_stays()
    {
        var (c, tool) = Answered(Lines(20));
        var (view, window) = Open(c);
        Assert.True(Contents(view, tool)[1].Focus());

        UiHelpers.Press(window, Key.Enter);
        Assert.True(tool.IsOutputExpanded);
        Assert.Same(Contents(view, tool)[1], UiHelpers.Focused(window)); // the row is drawn again, and keeps the keyboard

        UiHelpers.Press(window, Key.Space);
        Assert.False(tool.IsOutputExpanded);
        Assert.Same(Contents(view, tool)[1], UiHelpers.Focused(window));
        window.Close();
    }

    [AvaloniaFact]
    public void A_double_click_toggles_once_and_a_redraw_gives_the_focus_back_the_way_it_was_taken()
    {
        var (c, tool) = Answered(Lines(20));
        var (view, window) = Open(c);

        // the second click of a double click is the text's (it selects a word): only the first one toggles
        ClickTop(window, Contents(view, tool)[1]);
        ClickTop(window, Contents(view, tool)[1]);
        Assert.True(tool.IsOutputExpanded);
        System.Threading.Thread.Sleep(600);           // a click after the double-click time is a first click again
        ClickTop(window, Contents(view, tool)[1]);
        Assert.False(tool.IsOutputExpanded);

        // taken with the keyboard, the focus comes back with its ring after the redraw; taken with the pointer, without
        Assert.True(UiHelpers.Input(view).Focus()); // (the pointer's clicks left the focus on the text: take it away first)
        Assert.True(Contents(view, tool)[1].Focus(NavigationMethod.Tab));
        Assert.Contains(":focus-visible", Contents(view, tool)[1].Classes); // (the premise: Tab gives the ring)
        UiHelpers.Press(window, Key.Enter);
        Assert.True(tool.IsOutputExpanded);
        Assert.Contains(":focus-visible", Contents(view, tool)[1].Classes);
        Assert.True(UiHelpers.Input(view).Focus());
        Assert.True(Contents(view, tool)[1].Focus(NavigationMethod.Pointer));
        UiHelpers.Press(window, Key.Enter);
        Assert.False(tool.IsOutputExpanded);
        Assert.DoesNotContain(":focus-visible", Contents(view, tool)[1].Classes);
        Assert.Same(Contents(view, tool)[1], UiHelpers.Focused(window));
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("a\nb\nc", false)]
    [InlineData("a\nb\nc\nd", true)]
    [InlineData("one line of forty characters, give or take", false)]
    [InlineData("250", false)]
    [InlineData("251", true)]
    public void Only_a_text_past_250_characters_or_3_lines_is_clickable(string text, bool clickable)
    {
        if (text == "250") text = new string('x', ToolCallView.ExpandableChars);
        if (text == "251") text = new string('x', ToolCallView.ExpandableChars + 1);
        Assert.Equal(3, ToolCallView.ExpandableLines);
        var (c, tool) = Answered(text);
        var (view, window) = Open(c);

        var content = Contents(view, tool)[1];
        Assert.Equal(clickable, content.Focusable);
        Assert.Equal(clickable, content.Cursor != null);
        Assert.Equal(clickable, !string.IsNullOrEmpty(AutomationProperties.GetName(content)));
        ClickTop(window, content);
        Assert.Equal(clickable, tool.IsOutputExpanded);
        Assert.False(Contents(view, tool)[0].Focusable); // the short command (IN) is never one
        window.Close();
    }

    [AvaloniaFact]
    public void A_command_opens_on_its_own_and_the_output_stays_cut()
    {
        var command = string.Join("\n", Enumerable.Range(1, 12).Select(i => "echo " + i));
        var (c, tool) = Answered(Lines(20), command: command);
        var (view, window) = Open(c);
        TextBlock CommandText() => ViewOf(view, tool).GetVisualDescendants().OfType<TextBlock>().Single(t => (t.Text ?? "").StartsWith("echo 1\n", StringComparison.Ordinal));
        Assert.DoesNotContain("echo 12", CommandText().Text); // the box shows the first lines

        UiHelpers.Click(window, Contents(view, tool)[0]);

        Assert.True(tool.IsInputExpanded);
        Assert.False(tool.IsOutputExpanded);
        Assert.EndsWith("echo 12", CommandText().Text);
        Assert.DoesNotContain("line 20", ResultOf(view, tool));
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_cut_text_is_a_finger_tall_target_in_the_touch_layout(bool touch)
    {
        var (c, tool) = Answered(new string('x', 300)); // one line, past 250 characters
        var view = OpenIn(c, touch);

        var content = Contents(view, tool)[1];
        Assert.True(content.Focusable);
        if (touch) Assert.True(content.Bounds.Height >= 44 - 0.5, $"{content.Bounds.Height} px: under a finger");
        else Assert.True(content.Bounds.Height < 44, $"{content.Bounds.Height} px: the touch height on the desktop");
        ClickTop(WindowOf(view), content);
        Assert.True(tool.IsOutputExpanded); // and a tap on it opens it
    }

    [AvaloniaFact]
    public void The_screen_reader_hears_how_to_open_and_how_to_cut_in_the_language_of_the_view()
    {
        var (c, tool) = Answered(Lines(5));
        var (view, window) = Open(c);
        var strings = ChatStrings.English;
        Assert.Equal("Press Enter to show the full text", strings.ToolShowFullText);
        Assert.Equal(strings.ToolShowFullText, AutomationProperties.GetName(Contents(view, tool)[1]));

        tool.IsOutputExpanded = true;
        UiHelpers.Pump();
        Assert.Equal(strings.ToolCutText, AutomationProperties.GetName(Contents(view, tool)[1]));

        view.Strings = ChatStrings.PortugueseBrazil;
        UiHelpers.Pump();
        Assert.Equal(ChatStrings.PortugueseBrazil.ToolCutText, AutomationProperties.GetName(Contents(view, tool)[1]));
        tool.IsOutputExpanded = false;
        UiHelpers.Pump();
        Assert.Equal(ChatStrings.PortugueseBrazil.ToolShowFullText, AutomationProperties.GetName(Contents(view, tool)[1]));
        Assert.NotEqual(strings.ToolShowFullText, ChatStrings.PortugueseBrazil.ToolShowFullText);
        window.Close();
    }

    [AvaloniaFact]
    public void The_open_state_lives_in_the_item_so_a_recycled_row_shows_it()
    {
        var (c, tool) = Answered(Lines(20));
        tool.IsOutputExpanded = true; // before any row exists
        var (view, window) = Open(c);
        Assert.Equal(Lines(20), ResultOf(view, tool));

        window.Close();
        var (again, other) = Open(c);
        Assert.Equal(Lines(20), ResultOf(again, tool)); // another row for the same item
        tool.IsOutputExpanded = false;                  // and the code can cut it
        UiHelpers.Pump();
        Assert.DoesNotContain("line 20", ResultOf(again, tool));
        other.Close();
    }

    [AvaloniaFact]
    public void A_text_past_the_cap_ends_with_an_ellipsis_when_open()
    {
        var text = string.Join("\n", Enumerable.Repeat(new string('z', 49), 3100)); // about 155,000 characters
        var (c, tool) = Answered(text);
        tool.IsOutputExpanded = true;
        var (view, window) = Open(c);

        var shown = ResultOf(view, tool);
        Assert.Equal(ToolCallView.MaxExpandedChars + 1, shown.Length);
        Assert.EndsWith("…", shown, StringComparison.Ordinal);
        Assert.StartsWith(text.Substring(0, 1000), shown, StringComparison.Ordinal);
        window.Close();

        var (c2, whole) = Answered(Lines(2000)); // under the cap: untouched
        whole.IsOutputExpanded = true;
        (view, window) = Open(c2);
        Assert.Equal(Lines(2000), ResultOf(view, whole));
        window.Close();
    }

    [AvaloniaFact]
    public void Result_strings_sit_on_the_code_background_and_error_text_is_not_colored()
    {
        var (c, tool) = Answered("error: " + Lines(5), error: true);
        var (view, window) = Open(c);

        var contents = Contents(view, tool);
        Assert.DoesNotContain(contents[0].GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("ClaudeChatToolResult")); // the command (IN) has none
        var result = Assert.Single(contents[1].GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("ClaudeChatToolResult"));
        Assert.Same(Resource(view, "ClaudeChat.CodeBlockBackground"), result.Background);
        var text = result.GetVisualDescendants().OfType<SelectableTextBlock>().Single();
        Assert.Equal(TextWrapping.NoWrap, text.TextWrapping);
        Assert.NotSame(Resource(view, "ClaudeChat.Error"), text.Foreground);
        Assert.Equal(ToolStatus.Failed, tool.Status); // the red dot says it failed, not the text
        window.Close();
    }

    [AvaloniaFact]
    public void A_refused_bash_with_a_reason_shows_reason_instead_of_the_output()
    {
        var c = Refusing("Bash", new { command = "rm -rf build" }, "Use rg instead");
        var tool = c.Items.OfType<ToolCallItem>().Single();
        Assert.Equal(ToolStatus.Rejected, tool.Status);
        var (view, window) = Open(c);

        var texts = UiHelpers.Texts(ViewOf(view, tool));
        Assert.Contains("Reason: Use rg instead", texts);
        Assert.DoesNotContain("Use rg instead", texts);       // the reason is not also the output
        Assert.DoesNotContain("OUT", texts);
        Assert.Contains("IN", texts);                         // the command still shows
        Assert.Contains("rm -rf build", texts);
        var line = ViewOf(view, tool).GetVisualDescendants().OfType<TextBlock>().Single(t => t.Inlines?.Text == "Reason: Use rg instead");
        Assert.Contains("ClaudeChatSecondary", line.Classes);
        Assert.Equal("Reason:", Assert.IsType<Run>(line.Inlines![0]).Text);
        Assert.Equal(FontWeight.Bold, Assert.IsType<Run>(line.Inlines[0]).FontWeight);
        Assert.NotEqual(FontWeight.Bold, Assert.IsType<Run>(line.Inlines[1]).FontWeight);

        var pt = ChatStrings.PortugueseBrazil;
        view.Strings = pt;
        UiHelpers.Pump();
        Assert.Contains("Motivo: Use rg instead", UiHelpers.Texts(ViewOf(view, tool)));
        Assert.NotEqual(ChatStrings.English.ToolReason, pt.ToolReason);
        window.Close();
    }

    [AvaloniaFact]
    public void A_refused_edit_keeps_edit_failed_and_adds_the_reason()
    {
        var c = Refusing("Edit", new { file_path = "src/a.cs", old_string = "x", new_string = "y" }, "Too risky");
        var tool = c.Items.OfType<ToolCallItem>().Single();
        var (view, window) = Open(c);

        var edit = ViewOf(view, tool);
        var texts = UiHelpers.Texts(edit);
        Assert.Contains("Edit failed", texts);
        Assert.Contains("Reason: Too risky", texts);
        var diff = edit.GetVisualDescendants().OfType<DiffView>().Single(); // and the diff
        double Y(Visual v) => v.TranslatePoint(default, view)!.Value.Y;
        var failed = edit.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Edit failed");
        var reason = edit.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Inlines?.Text == "Reason: Too risky");
        Assert.True(Y(failed) < Y(reason) && Y(reason) < Y(diff), "Edit failed, then the reason, then the diff");

        var write = Refusing("Write", new { file_path = "b.txt", content = "hello" }, "Not now");
        var writeTool = write.Items.OfType<ToolCallItem>().Single();
        var (view2, window2) = Open(write);
        var writeTexts = UiHelpers.Texts(ViewOf(view2, writeTool));
        Assert.Contains("Write failed", writeTexts);
        Assert.Contains("Reason: Not now", writeTexts);
        Assert.Contains("hello", writeTexts); // the written text stays
        window2.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void A_plain_refusal_shows_only_the_dot_and_failed_for_edit_and_write()
    {
        var edit = Refusing("Edit", new { file_path = "src/a.cs", old_string = "x", new_string = "y" }, StandardRefusal);
        var bash = Refusing("Bash", new { command = "ls" }, StandardRefusal);
        var grep = Refusing("Grep", new { pattern = "needle" }, StandardRefusal);
        foreach (var c in new[] { edit, bash, grep })
        {
            var tool = c.Items.OfType<ToolCallItem>().Single();
            Assert.Null(tool.RejectionReason);
            var (view, window) = Open(c);
            var texts = UiHelpers.Texts(ViewOf(view, tool));
            Assert.DoesNotContain(texts, t => t.StartsWith("Reason:", StringComparison.Ordinal));
            Assert.Same(Resource(view, "ClaudeChat.DotFailed"), VisibleDot(view, tool).Fill);
            if (tool.Name == "Edit") Assert.Contains("Edit failed", texts);
            if (tool.Name == "Bash") Assert.DoesNotContain(texts, t => t.StartsWith("The user", StringComparison.Ordinal)); // only the dot
            if (tool.Name == "Grep") Assert.Contains(texts, t => t.StartsWith("The user doesn't want to proceed", StringComparison.Ordinal)); // the raw text
            window.Close();
        }
    }

    // ---- the working line ----

    private static WorkingIndicator WorkingRow(ClaudeChatView view) =>
        view.GetVisualDescendants().OfType<WorkingIndicator>().Single(p => p.Classes.Contains("ClaudeChatWorking"));

    private static TextBlock WorkingGlyph(ClaudeChatView view) =>
        WorkingRow(view).GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("ClaudeChatWorkingGlyph"));

    [AvaloniaFact]
    public void The_working_star_moves_only_while_its_line_shows()
    {
        var c = new ChatConversation();
        var (view, window) = Open(c);

        // hidden before anything is sent: still
        Assert.False(WorkingRow(view).IsEffectivelyVisible);
        Assert.False(WorkingRow(view).IsGlyphMoving);
        Assert.False(WorkingRow(view).IsTicking);

        // shown while Claude works: it moves
        c.Submit("go");
        UiHelpers.Pump();
        Assert.True(WorkingRow(view).IsEffectivelyVisible);
        Assert.True(WorkingRow(view).IsGlyphMoving);
        Assert.True(WorkingRow(view).IsTicking);

        // hidden while a text arrives: still
        c.AppendLine("{\"type\":\"stream_event\",\"parent_tool_use_id\":null,\"event\":{\"type\":\"message_start\",\"message\":{\"id\":\"m1\"}}}");
        c.AppendLine("{\"type\":\"stream_event\",\"parent_tool_use_id\":null,\"event\":{\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"text\",\"text\":\"\"}}}");
        UiHelpers.Pump();
        Assert.False(WorkingRow(view).IsEffectivelyVisible);
        Assert.False(WorkingRow(view).IsGlyphMoving);

        // after the turn: still
        c.Apply(new ClaudeVisualChat.Protocol.TurnCompleted("success", false, 10, 0, 1, 1));
        UiHelpers.Pump();
        Assert.False(WorkingRow(view).IsEffectivelyVisible);
        Assert.False(WorkingRow(view).IsGlyphMoving);
        Assert.False(WorkingRow(view).IsTicking);
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
        Assert.EndsWith("...", WorkingRow(view).FullText);
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

        // the star moves, in the color of the mode; the verb in Portuguese
        var glyph = WorkingGlyph(view);
        Assert.Same(Resource(view, "ClaudeChat.Spinner"), glyph.Foreground);
        Assert.True(WorkingRow(view).IsGlyphMoving);
        c.Apply(new ClaudeVisualChat.Protocol.StatusChanged(null, "acceptEdits"));
        view.Strings = ChatStrings.PortugueseBrazil;
        UiHelpers.Pump();
        Assert.Same(Resource(view, "ClaudeChat.Foreground"), glyph.Foreground);
        Assert.Contains(WorkingRow(view).Verb, ChatStrings.PortugueseBrazil.WorkingVerbs);

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
        Assert.Contains("Thinking...", UiHelpers.Texts(row)); // still arriving

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

    // ---- scrolling on send, and what counts as the end ----

    /// <summary>A hundred messages, the list at its end, then the user up at 100 px from the top.</summary>
    private static (ChatConversation C, ClaudeChatView View, Window Window, ScrollViewer Scroller) ReadingAbove()
    {
        var c = new ChatConversation();
        for (var i = 0; i < 100; i++) c.Items.Add(new UserMessageItem("message " + i));
        var (view, window) = Open(c);
        var scroller = UiHelpers.Scroller(view);
        Assert.True(UiHelpers.AtEnd(scroller));
        scroller.Offset = new Vector(0, 100); // the user scrolls up to read
        UiHelpers.Pump();
        c.Items.Add(new AssistantTextItem { Text = "while reading" });
        UiHelpers.Pump();
        Assert.Equal(100, scroller.Offset.Y); // it no longer follows
        return (c, view, window, scroller);
    }

    [AvaloniaFact]
    public void Sending_scrolls_to_the_end_even_after_scrolling_up()
    {
        var (c, view, window, scroller) = ReadingAbove();
        Assert.True(view.ScrollToEndOnSend); // the default

        c.Submit("go on");
        UiHelpers.Pump();
        Assert.True(UiHelpers.AtEnd(scroller), $"offset {scroller.Offset.Y} of {scroller.Extent.Height - scroller.Viewport.Height}");
        Assert.Contains("go on", UiHelpers.Texts(view));

        c.Items.Add(new UserMessageItem("the next one")); // and it follows the end again
        UiHelpers.Pump();
        Assert.True(UiHelpers.AtEnd(scroller));
        Assert.Contains("the next one", UiHelpers.Texts(view));
        window.Close();

        // typed in the reply box and sent with Enter, the same
        (c, view, window, scroller) = ReadingAbove();
        UiHelpers.Input(view).Text = "typed";
        UiHelpers.Input(view).Focus();
        UiHelpers.Press(window, Key.Enter);
        UiHelpers.Pump();
        Assert.Contains(c.Items, i => i is UserMessageItem { Text: "typed" });
        Assert.True(UiHelpers.AtEnd(scroller));
        window.Close();
    }

    [AvaloniaFact]
    public void With_scroll_to_end_on_send_off_the_list_stays_put()
    {
        var (c, view, window, scroller) = ReadingAbove();
        view.ScrollToEndOnSend = false;

        c.Submit("go on");
        UiHelpers.Pump();
        Assert.Equal(100, scroller.Offset.Y); // where the user left it
        Assert.False(UiHelpers.AtEnd(scroller));
        window.Close();

        // at the end, it follows the new message as ever
        var at = new ChatConversation();
        for (var i = 0; i < 100; i++) at.Items.Add(new UserMessageItem("message " + i));
        (view, window) = Open(at);
        view.ScrollToEndOnSend = false;
        scroller = UiHelpers.Scroller(view);
        at.Submit("go on");
        UiHelpers.Pump();
        Assert.True(UiHelpers.AtEnd(scroller));
        window.Close();
    }

    [AvaloniaFact]
    public void Fifty_pixels_from_the_end_still_counts_as_the_end()
    {
        var c = new ChatConversation();
        for (var i = 0; i < 100; i++) c.Items.Add(new UserMessageItem("message " + i));
        var (view, window) = Open(c);
        var scroller = UiHelpers.Scroller(view);
        double End() => scroller.Extent.Height - scroller.Viewport.Height;

        scroller.Offset = new Vector(0, 0); // up to read: it stops following
        UiHelpers.Pump();
        scroller.Offset = new Vector(0, End() - 50); // back down to 50 px from the end: that is the end, it follows again
        UiHelpers.Pump();
        c.Items.Add(new UserMessageItem("new one"));
        UiHelpers.Pump();
        Assert.True(UiHelpers.AtEnd(scroller), $"offset {scroller.Offset.Y} of {End()}");

        scroller.Offset = new Vector(0, 0);
        UiHelpers.Pump();
        var away = End() - 80; // 80 px is not the end
        scroller.Offset = new Vector(0, away);
        UiHelpers.Pump();
        c.Items.Add(new UserMessageItem("another"));
        UiHelpers.Pump();
        Assert.Equal(away, scroller.Offset.Y);

        scroller.ScrollToEnd();
        UiHelpers.Pump();
        var up = End() - 30; // and going up from the end, even by less than 50 px, stops following
        scroller.Offset = new Vector(0, up);
        UiHelpers.Pump();
        c.Items.Add(new UserMessageItem("one more"));
        UiHelpers.Pump();
        Assert.Equal(up, scroller.Offset.Y);
        window.Close();
    }

    [AvaloniaFact]
    public void Without_following_the_first_message_on_screen_stays_in_place()
    {
        var c = new ChatConversation();
        for (var i = 0; i < 100; i++) c.Items.Add(new UserMessageItem("message " + i));
        var (view, window) = Open(c);
        var scroller = UiHelpers.Scroller(view);
        var list = UiHelpers.ItemList(view);
        double? Top(Control row) => row.TranslatePoint(default, scroller)?.Y;
        scroller.Offset = new Vector(0, 1500); // up, reading
        UiHelpers.Pump();
        var first = list.GetRealizedContainers().Where(r => r.IsVisible && Top(r) + r.Bounds.Height > 0.5).OrderBy(r => Top(r)).First();
        var item = first.DataContext!;
        var top = Top(first)!.Value;

        c.Items.Insert(0, new UserMessageItem("put in at the top\nover\nthree lines")); // a row comes above it
        UiHelpers.Pump();
        var row = list.ContainerFromItem(item)!;
        Assert.Equal(top, Top(row)!.Value, 0.5); // what the user reads did not move
        Assert.True(scroller.Offset.Y > 1500);  // the list did

        // following the end, the end is what stays
        scroller.ScrollToEnd();
        UiHelpers.Pump();
        c.Items.Insert(0, new UserMessageItem("another at the top"));
        c.Items.Add(new UserMessageItem("and one at the end"));
        UiHelpers.Pump();
        Assert.True(UiHelpers.AtEnd(scroller));
        Assert.Contains("and one at the end", UiHelpers.Texts(view));
        window.Close();
    }

    /// <summary>A time of a given local day.</summary>
    private static DateTimeOffset LocalTime(int year, int month, int day, int hour, int minute)
    {
        var time = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(time, TimeZoneInfo.Local.GetUtcOffset(time));
    }

    /// <summary>The date lines on screen, top to bottom.</summary>
    private static List<string?> DateLines(Visual root) =>
        root.GetVisualDescendants().OfType<DateSeparator>().Where(d => d.IsEffectivelyVisible)
            .OrderBy(d => d.TranslatePoint(default, root)!.Value.Y).Select(d => d.Date).ToList();

    [AvaloniaTheory]
    [InlineData(ChatLayout.Desktop)]
    [InlineData(ChatLayout.Touch)]
    public void A_date_line_marks_the_first_turn_and_each_day_change(ChatLayout layout)
    {
        // a turn sent before midnight, answered after it: "Sat, Oct 25" over the prompt, "Sun, Oct 26" over the answer of the next day
        var c = new ChatConversation { Clock = () => LocalTime(2025, 10, 26, 10, 0) };
        var prompt = new UserMessageItem("late question") { Timestamp = LocalTime(2025, 10, 25, 23, 50) };
        var before = new AssistantTextItem { Text = "before midnight", Timestamp = LocalTime(2025, 10, 25, 23, 55) };
        var after = new AssistantTextItem { Text = "after midnight", Timestamp = LocalTime(2025, 10, 26, 0, 5) };
        c.Items.Add(prompt);
        c.Items.Add(before);
        c.Items.Add(new ThinkingItem { Text = "no time of its own" });
        c.Items.Add(after);
        c.Items.Add(new TurnFooterItem(1000, 1, 1, null, false));
        c.Items.Add(new UserMessageItem("next morning") { Timestamp = LocalTime(2025, 10, 26, 9, 0) }); // the same day: no line
        var view = new ClaudeChatView { Conversation = c, Layout = layout };
        var window = layout == ChatLayout.Touch ? UiHelpers.ShowPhone(view) : UiHelpers.Show(view);
        UiHelpers.Scroller(view).ScrollToHome();
        UiHelpers.Pump();

        Assert.Equal(new[] { "Sat, Oct 25", "Sun, Oct 26" }, DateLines(view));
        // each at the top of its row: over the prompt's box, over the answer's text
        DateSeparator LineOf(ChatItem item) =>
            view.GetVisualDescendants().OfType<DateSeparator>().Single(d => d.IsEffectivelyVisible && ReferenceEquals(d.DataContext, item));
        var box = view.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatUser") && ReferenceEquals(b.DataContext, prompt));
        Assert.True(LineOf(prompt).TranslatePoint(new Point(0, LineOf(prompt).Bounds.Height), view)!.Value.Y <= box.TranslatePoint(default, view)!.Value.Y + 0.5);
        var answer = view.GetVisualDescendants().OfType<MarkdownBlock>().Single(m => ReferenceEquals(m.DataContext, after));
        Assert.True(LineOf(after).TranslatePoint(new Point(0, LineOf(after).Bounds.Height), view)!.Value.Y <= answer.TranslatePoint(default, view)!.Value.Y + 0.5);

        // a message of a new day at the end gets its own; one of the same day none
        c.Items.Add(new UserMessageItem("a day later") { Timestamp = LocalTime(2025, 10, 27, 8, 0) });
        c.Items.Add(new AssistantTextItem { Text = "same day", Timestamp = LocalTime(2025, 10, 27, 8, 1) });
        UiHelpers.Pump();
        UiHelpers.Scroller(view).ScrollToEnd();
        UiHelpers.Pump();
        Assert.Contains("Mon, Oct 27", DateLines(view));
        Assert.Single(DateLines(view), d => d == "Mon, Oct 27");

        // in the view's language
        view.Strings = ChatStrings.PortugueseBrazil;
        UiHelpers.Pump();
        Assert.Contains("seg, 27 de out", DateLines(view));

        // a message put in before the first takes the first line, when it is of another day
        UiHelpers.Scroller(view).ScrollToHome();
        UiHelpers.Pump();
        c.Items.Insert(0, new UserMessageItem("days before") { Timestamp = LocalTime(2025, 10, 20, 9, 0) });
        UiHelpers.Pump();
        UiHelpers.Scroller(view).ScrollToHome(); // the list kept what was on screen in place: the new one is above it
        UiHelpers.Pump();
        Assert.Equal(new[] { "seg, 20 de out", "sáb, 25 de out", "dom, 26 de out" }, DateLines(view).Take(3));
        window.Close();
    }

    [AvaloniaFact]
    public void A_date_outside_this_year_adds_the_year()
    {
        var c = new ChatConversation { Clock = () => LocalTime(2026, 10, 28, 12, 0) };
        c.Items.Add(new UserMessageItem("last year") { Timestamp = LocalTime(2025, 10, 25, 15, 45) });
        c.Items.Add(new UserMessageItem("this year") { Timestamp = LocalTime(2026, 1, 2, 9, 0) });
        var (view, window) = Open(c);
        Assert.Equal(new[] { "Sat, Oct 25, 2025", "Fri, Jan 2" }, DateLines(view));
        view.Strings = ChatStrings.PortugueseBrazil;
        UiHelpers.Pump();
        Assert.Equal(new[] { "sáb, 25 de out de 2025", "sex, 2 de jan" }, DateLines(view));
        window.Close();
    }

    [AvaloniaFact]
    public void Show_timestamps_off_hides_times_and_date_lines()
    {
        var sent = LocalTime(2026, 10, 25, 15, 45).AddSeconds(12);
        var c = new ChatConversation { Clock = () => sent };
        var prompt = new UserMessageItem("a question") { Timestamp = sent };
        var answer = new AssistantTextItem { Text = "an answer", Timestamp = sent.AddMinutes(1) };
        c.Items.Add(prompt);
        c.Items.Add(answer);
        var (view, window) = Open(c);
        Assert.True(view.ShowTimestamps); // the default
        TextBlock[] Stamps() => view.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.Classes.Contains("ClaudeChatStamp") && t.IsEffectivelyVisible).ToArray();
        var row = view.GetVisualDescendants().OfType<ChatTimelineRow>().Single(r => ReferenceEquals(r.DataContext, answer));

        Assert.Equal(new[] { "3:45 PM", "3:46 PM" }, Stamps().Select(t => t.Text));
        Assert.Equal(new[] { "Sun, Oct 25" }, DateLines(view));
        // the time's tooltip: the whole date and time, and its offset from UTC
        Assert.Equal(ChatStrings.FormatFullTime(sent), ToolTip.GetTip(Stamps()[0]));
        Assert.Equal(ChatStrings.FormatFullTime(sent.AddMinutes(1)), ToolTip.GetTip(Stamps()[1]));
        Assert.StartsWith("2026-10-25 15:45:12 GMT", ChatStrings.FormatFullTime(sent));
        Assert.Contains(":stamped", row.Classes);

        view.ShowTimestamps = false;
        UiHelpers.Pump();
        Assert.Empty(Stamps());
        Assert.Empty(DateLines(view));
        Assert.DoesNotContain(":stamped", row.Classes); // the answer takes the room of an answer without a time

        view.ShowTimestamps = true;
        UiHelpers.Pump();
        Assert.Equal(2, Stamps().Length);
        Assert.Equal(new[] { "Sun, Oct 25" }, DateLines(view));
        window.Close();

        // in the touch layout too, where the date line is all there is of the times
        var touch = UiHelpers.TouchView(c);
        window = UiHelpers.ShowPhone(touch);
        Assert.Equal(new[] { "dom, 25 de out" }, DateLines(touch));
        touch.ShowTimestamps = false;
        UiHelpers.Pump();
        Assert.Empty(DateLines(touch));
        window.Close();
    }

    [AvaloniaFact]
    public void A_subagents_steps_have_no_date_line()
    {
        var c = new ChatConversation { Clock = () => LocalTime(2026, 10, 25, 12, 0) };
        c.Items.Add(new UserMessageItem("look around") { Timestamp = LocalTime(2026, 10, 25, 11, 0) });
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("toolu_a", "Agent", J("{\"description\":\"Look around\",\"prompt\":\"go\"}"))));
        var agent = c.Items.OfType<ToolCallItem>().Single();
        // the agent's own first message and step, of another day even: the first of their list, yet not the conversation's
        var asked = new UserMessageItem("asked of the agent") { Timestamp = LocalTime(2026, 10, 26, 0, 5) };
        var step = new AssistantTextItem { Text = "a step of the agent", Timestamp = LocalTime(2026, 10, 26, 0, 6) };
        agent.Children.Add(asked);
        agent.Children.Add(step);
        agent.IsExpanded = true;
        var (view, window) = Open(c);
        Assert.Contains("asked of the agent", UiHelpers.Texts(view)); // the steps are drawn
        Assert.Contains(view.GetVisualDescendants().OfType<DateSeparator>(), d => ReferenceEquals(d.DataContext, step));
        Assert.Equal(new[] { "Sun, Oct 25" }, DateLines(view)); // the prompt's only
        window.Close();
    }

    // ---- the prompt pinned at the top ----

    /// <summary>Two turns, each a prompt and a long reply in 40 paragraphs, so the rows above the screen are not drawn.</summary>
    private static (ChatConversation C, UserMessageItem First, UserMessageItem Second) TwoLongTurns(string? firstText = null)
    {
        var c = new ChatConversation();
        var first = new UserMessageItem(firstText ?? "First question");
        var second = new UserMessageItem("Second question");
        foreach (var prompt in new[] { first, second })
        {
            c.Items.Add(prompt);
            for (var i = 0; i < 40; i++) c.Items.Add(new AssistantTextItem { Text = "Paragraph " + i + " of the answer to " + prompt.Text });
            c.Items.Add(new TurnFooterItem(1000, 1, 1, null, false));
        }
        return (c, first, second);
    }

    private static StickyPrompt Sticky(ClaudeChatView view) => view.GetVisualDescendants().OfType<StickyPrompt>().Single();

    /// <summary>The top of <paramref name="item"/>'s row in the view, bringing it into the list's drawn rows first.</summary>
    private static double RowTop(ClaudeChatView view, ChatItem item)
    {
        var list = UiHelpers.ItemList(view);
        var row = list.ContainerFromItem(item)!;
        return row.TranslatePoint(default, view)!.Value.Y;
    }

    /// <summary>Scrolls the list so the top of <paramref name="item"/>'s row is <paramref name="y"/> px under the top of the view.</summary>
    private static void ScrollRowTo(ClaudeChatView view, ChatItem item, double y)
    {
        var list = UiHelpers.ItemList(view);
        var scroller = UiHelpers.Scroller(view);
        list.ScrollIntoView(item);
        UiHelpers.Pump();
        var inList = list.ContainerFromItem(item)!.TranslatePoint(default, list)!.Value.Y;
        scroller.Offset = new Vector(0, inList - y);
        UiHelpers.Pump();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Scrolling_a_long_reply_pins_its_prompt_at_the_top(bool touch)
    {
        var (c, first, _) = TwoLongTurns();
        var view = touch ? UiHelpers.TouchView(c) : new ClaudeChatView { Conversation = c };
        var window = touch ? UiHelpers.ShowPhone(view) : UiHelpers.Show(view);
        var scroller = UiHelpers.Scroller(view);
        var sticky = Sticky(view);

        ScrollRowTo(view, c.Items[20], 0);                                // in the middle of the first reply
        Assert.True(sticky.IsVisible);
        Assert.Same(first, sticky.Item);
        Assert.Contains("First question", UiHelpers.Texts(sticky));
        Assert.Null(UiHelpers.ItemList(view).ContainerFromItem(first));   // its own row is far above, not drawn
        // a band of the side bar's color from the top of the view: 14 px over the bubble and 12 px under it, the last 12 px fading out
        var band = sticky.Band;
        Assert.Equal(0, band.TranslatePoint(default, view)!.Value.Y, 0.5);
        var bubble = sticky.Bubble;
        Assert.Contains("ClaudeChatStickyBubble", bubble.Classes);
        Assert.Equal(14, bubble.TranslatePoint(default, band)!.Value.Y, 0.5);
        Assert.Equal(band.Bounds.Height - 12, bubble.TranslatePoint(new Point(0, bubble.Bounds.Height), band)!.Value.Y, 0.5);
        Assert.Same(view.FindResource("ClaudeChat.Background"), sticky.Fill.Fill);
        Assert.Equal(12, sticky.Fade.Bounds.Height, 0.5);
        Assert.Equal(band.Bounds.Height, sticky.Fade.TranslatePoint(new Point(0, 12), band)!.Value.Y, 0.5);
        Assert.IsType<LinearGradientBrush>(sticky.Fade.OpacityMask);
        // as wide as the list's text, the bubble across all of it (in the touch layout too, where a row's bubble is at the right)
        var list = UiHelpers.ItemList(view);
        var text = list.TranslatePoint(new Point(list.Padding.Left, 0), view)!.Value.X;
        Assert.Equal(text, bubble.TranslatePoint(default, view)!.Value.X, 0.5);
        Assert.Equal(list.Bounds.Width - list.Padding.Left - list.Padding.Right, bubble.Bounds.Width, 0.5);
        Assert.NotNull(bubble.Cursor);                                    // a hand: a click goes to the turn

        scroller.Offset = new Vector(0, 0);                               // its row is on screen again: nothing pinned
        UiHelpers.Pump();
        Assert.False(sticky.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void The_next_prompt_pushes_the_pinned_one_out()
    {
        var (c, first, second) = TwoLongTurns();
        var (view, window) = Open(c);
        var sticky = Sticky(view);

        ScrollRowTo(view, second, 30);                                    // the next prompt's row 30 px under the top
        Assert.True(sticky.IsVisible);
        Assert.Same(first, sticky.Item);
        var band = sticky.Band;
        Assert.True(band.Bounds.Height > 30);
        var bottom = band.TranslatePoint(new Point(0, band.Bounds.Height), view)!.Value.Y;
        Assert.Equal(RowTop(view, second), bottom, 0.5);                  // pushed up by it: its bottom on the next row's top
        Assert.True(band.TranslatePoint(default, view)!.Value.Y < 0);

        ScrollRowTo(view, second, 120);                                   // further down, nothing pushes it
        Assert.Same(first, sticky.Item);
        Assert.Equal(0, band.TranslatePoint(default, view)!.Value.Y, 0.5);

        ScrollRowTo(view, c.Items[c.Items.IndexOf(second) + 20], 0);      // into the second reply: its prompt
        Assert.True(sticky.IsVisible);
        Assert.Same(second, sticky.Item);
        Assert.Contains("Second question", UiHelpers.Texts(sticky));
        window.Close();
    }

    [AvaloniaFact]
    public void Prompts_without_text_and_turns_before_any_prompt_pin_nothing()
    {
        var c = new ChatConversation();
        for (var i = 0; i < 40; i++) c.Items.Add(new AssistantTextItem { Text = "Resumed paragraph " + i });
        var image = new UserMessageItem("", new[] { ChatImage.FromBytes(Png(8, 8), "image/png") });
        c.Items.Add(image);
        for (var i = 0; i < 40; i++) c.Items.Add(new AssistantTextItem { Text = "About the image, paragraph " + i });
        var (view, window) = Open(c);
        var sticky = Sticky(view);

        ScrollRowTo(view, c.Items[20], 0);                                // a reply with no prompt before it
        Assert.False(sticky.IsVisible);
        ScrollRowTo(view, c.Items[c.Items.IndexOf(image) + 20], 0);       // the reply to an image sent without text
        Assert.False(sticky.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void The_pinned_prompt_is_at_most_forty_percent_of_the_view()
    {
        var (c, first, _) = TwoLongTurns(string.Join("\n", Enumerable.Range(1, 60).Select(i => "requirement " + i)));
        first.IsTextExpanded = true;                                      // opened: the whole 60 lines
        var (view, window) = Open(c);
        var sticky = Sticky(view);

        ScrollRowTo(view, c.Items[20], 0);
        Assert.Same(first, sticky.Item);
        var bubble = sticky.Bubble;
        Assert.Equal(0.4 * view.Bounds.Height, bubble.Bounds.Height, 0.5);  // 40 % of the view's height
        var inner = bubble.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Content is not null && s is not MessageAttachments);
        Assert.True(inner.Extent.Height > inner.Viewport.Height + 100);   // and it scrolls inside
        inner.Offset = new Vector(0, 200);
        UiHelpers.Pump();
        Assert.Equal(200, inner.Offset.Y, 0.5);

        window.Height = 500;                                              // a shorter view, a shorter cap
        UiHelpers.Pump();
        Assert.Equal(0.4 * view.Bounds.Height, sticky.Bubble.Bounds.Height, 0.5);
        window.Close();
    }

    [AvaloniaFact]
    public void A_click_on_the_pinned_prompt_scrolls_to_the_start_of_its_turn()
    {
        var (c, first, _) = TwoLongTurns();
        var (view, window) = Open(c);
        var scroller = UiHelpers.Scroller(view);
        var sticky = Sticky(view);
        ScrollRowTo(view, c.Items[20], 0);
        Assert.Same(first, sticky.Item);
        var before = scroller.Offset.Y;
        var text = sticky.Bubble.GetVisualDescendants().OfType<SelectableTextBlock>().Single(t => t.Text == "First question");

        UiHelpers.Click(window, text);
        Assert.True(sticky.IsWaiting);                                    // it waits 300 ms (a double click selects a word instead)
        Assert.Equal(TimeSpan.FromMilliseconds(300), StickyPrompt.ClickDelay);
        Assert.Equal(before, scroller.Offset.Y);
        sticky.Tick();                                                    // the timer's tick
        UiHelpers.Pump();
        Assert.False(sticky.IsWaiting);
        Assert.Equal(0, RowTop(view, first), 1);                          // the start of the turn at the top
        Assert.False(sticky.IsVisible);                                   // and its row is there, so nothing is pinned

        ScrollRowTo(view, c.Items[20], 0);                                // with text selected, a click does not scroll
        Assert.True(sticky.IsVisible);
        text = sticky.Bubble.GetVisualDescendants().OfType<SelectableTextBlock>().Single(t => t.Text == "First question");
        UiHelpers.Click(window, text);
        Assert.True(sticky.IsWaiting);
        text.SelectAll();                                                 // a word picked by a double click, before the 300 ms are up
        before = scroller.Offset.Y;
        sticky.Tick();
        UiHelpers.Pump();
        Assert.Equal(before, scroller.Offset.Y);
        Assert.True(sticky.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void Show_more_in_the_pinned_prompt_opens_the_prompt_and_waits_for_no_scroll()
    {
        var (c, first, _) = TwoLongTurns(string.Join("\n", Enumerable.Range(1, 10).Select(i => "requirement " + i)));
        var (view, window) = Open(c);
        var sticky = Sticky(view);
        ScrollRowTo(view, c.Items[20], 0);
        Assert.Same(first, sticky.Item);
        var fold = sticky.Bubble.GetVisualDescendants().OfType<FoldedText>().Single();
        Assert.True(fold.IsFolded);                                       // folded as its row is
        Assert.Equal(60, fold.Bounds.Height, 0.5);

        UiHelpers.Click(window, fold.ShowMoreButton);
        Assert.False(sticky.IsWaiting);                                   // a button, not a click on the prompt
        Assert.True(first.IsTextExpanded);                                // the prompt's state: its row opens too
        Assert.True(fold.IsExpanded);
        window.Close();
    }

    [AvaloniaFact]
    public void A_drag_on_the_pinned_prompt_is_no_click()
    {
        var (c, first, _) = TwoLongTurns();
        var (view, window) = Open(c);
        var sticky = Sticky(view);
        ScrollRowTo(view, c.Items[20], 0);
        Assert.Same(first, sticky.Item);
        var bubble = sticky.Bubble;

        // pressed on the bubble's padding, moved 30 px, let go on the bubble: a drag, nothing waits to scroll
        var from = bubble.TranslatePoint(new Point(3, 2), window)!.Value;
        var to = from + new Vector(30, 0);
        window.MouseDown(from, MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        window.MouseUp(to, MouseButton.Left, RawInputModifiers.None);
        UiHelpers.Pump();
        Assert.False(sticky.IsWaiting);

        // the same press let go where it was (or a pixel or two off) is a click
        window.MouseDown(from, MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(from + new Vector(2, 1), RawInputModifiers.LeftMouseButton);
        window.MouseUp(from + new Vector(2, 1), MouseButton.Left, RawInputModifiers.None);
        UiHelpers.Pump();
        Assert.True(sticky.IsWaiting);
        window.Close();
    }

    [AvaloniaFact]
    public void A_finger_dragged_over_the_pinned_prompt_scrolls_the_list_once_its_own_text_does_not()
    {
        var (c, first, _) = TwoLongTurns(string.Join("\n", Enumerable.Range(1, 60).Select(i => "requirement " + i)));
        first.IsTextExpanded = true;                                      // a bubble that scrolls inside
        var (view, window) = Open(c);
        var scroller = UiHelpers.Scroller(view);
        var sticky = Sticky(view);
        ScrollRowTo(view, c.Items[20], 0);
        Assert.Same(first, sticky.Item);
        var inner = sticky.Bubble.GetVisualDescendants().OfType<ScrollViewer>().First(s => s is not MessageAttachments);
        var before = scroller.Offset.Y;

        sticky.TouchPressed();                                            // (the headless window has no fingers: the band's own steps)
        inner.Offset = new Vector(0, 50);                                 // the bubble's text took this move
        sticky.TouchMoved(-30);
        UiHelpers.Pump();
        Assert.Equal(before, scroller.Offset.Y);
        sticky.TouchMoved(-30);                                           // this one it did not take: the list scrolls with the finger
        UiHelpers.Pump();
        Assert.Equal(before + 30, scroller.Offset.Y, 0.5);
        sticky.TouchMoved(10);                                            // and back down
        UiHelpers.Pump();
        Assert.Equal(before + 20, scroller.Offset.Y, 0.5);

        view.ScrollListBy(-1e6);                                          // never past its ends
        Assert.Equal(0, scroller.Offset.Y);
        view.ScrollListBy(1e7);
        Assert.Equal(scroller.Extent.Height - scroller.Viewport.Height, scroller.Offset.Y, 0.5);
        window.Close();
    }

    [AvaloniaFact]
    public void Show_more_in_the_pinned_prompt_is_a_44_px_target_always_shown_in_the_touch_layout()
    {
        var (c, first, _) = TwoLongTurns(string.Join("\n", Enumerable.Range(1, 10).Select(i => "requirement " + i)));
        var view = UiHelpers.TouchView(c);
        var window = UiHelpers.ShowPhone(view);
        var sticky = Sticky(view);
        ScrollRowTo(view, c.Items[20], 0);
        Assert.Same(first, sticky.Item);
        var fold = sticky.Bubble.GetVisualDescendants().OfType<FoldedText>().Single();
        var more = fold.ShowMoreButton;
        Assert.True(more.IsEffectivelyVisible);
        Assert.Equal(1, more.Opacity);
        Assert.True(more.Bounds.Width >= 44 && more.Bounds.Height >= 44, more.Bounds.ToString());
        var corner = more.TranslatePoint(new Point(2, 2), window)!.Value;  // a tap near the corner of its target
        window.MouseDown(corner, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(corner, MouseButton.Left, RawInputModifiers.None);
        UiHelpers.Pump();
        Assert.True(first.IsTextExpanded);
        Assert.False(sticky.IsWaiting);
        window.Close();
    }

    [AvaloniaFact]
    public void The_wheel_over_the_pinned_prompt_scrolls_the_list()
    {
        var (c, first, _) = TwoLongTurns();
        var (view, window) = Open(c);
        var scroller = UiHelpers.Scroller(view);
        var sticky = Sticky(view);
        ScrollRowTo(view, c.Items[20], 0);
        Assert.Same(first, sticky.Item);
        var before = scroller.Offset.Y;
        var at = sticky.Bubble.TranslatePoint(new Point(20, 10), window)!.Value;
        window.MouseWheel(at, new Vector(0, -1), RawInputModifiers.None);  // down
        UiHelpers.Pump();
        Assert.True(scroller.Offset.Y > before, $"{scroller.Offset.Y} after {before}");
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

        // above the text, as the extension's message attachments (0.4.0), 6 px over it; as the extension's 2.1.309
        // thumbnails, the picture 48 px high and in proportion up to 120 px wide, in a 1 px frame; 4 px apart, in one row
        var text = boxes[0].GetVisualDescendants().OfType<SelectableTextBlock>().Single();
        Assert.Equal("look at these", text.Text);
        var first = thumbs[0].TranslatePoint(default, boxes[0])!.Value;
        var row = boxes[0].GetVisualDescendants().OfType<MessageAttachments>().Single();
        Assert.Equal(6, text.FindAncestorOfType<FoldedText>()!.Bounds.Top - row.Bounds.Bottom, 1); // the text's box, which folds a long one
        Assert.Equal(50, row.Bounds.Height, 1); // 48 and the frame
        Assert.Equal(first.Y, thumbs[1].TranslatePoint(default, boxes[0])!.Value.Y, 1);
        Assert.True(first.Y + 50 <= text.TranslatePoint(default, boxes[0])!.Value.Y);
        Assert.Equal(new Size(98, 50), thumbs[0].Bounds.Size);
        Assert.Equal(new Size(98, 50), thumbs[1].Bounds.Size);
        Assert.Equal(first.X + 98 + 4, thumbs[1].TranslatePoint(default, boxes[0])!.Value.X, 1);
        var frame = thumbs[0].GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_ChatButton");
        Assert.Equal(new CornerRadius(8), frame.CornerRadius);
        Assert.Equal(new Thickness(1), frame.BorderThickness);
        Assert.Equal(new CornerRadius(7), thumbs[0].GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatMessageImageClip")).CornerRadius);
        Assert.Equal("image.png", Avalonia.Automation.AutomationProperties.GetName(thumbs[0]));
        Assert.Equal("image-2.jpg", Avalonia.Automation.AutomationProperties.GetName(thumbs[1]));

        // a message with only an image: no empty line of text over it
        Assert.False(boxes[1].GetVisualDescendants().OfType<FoldedText>().Single().IsVisible); // the text's box (0.6.0)
        Assert.Equal(new Size(34, 50), Thumbnails(boxes[1])[0].Bounds.Size); // a small image is drawn 48 px high too

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
        // twice the 48 px it is drawn at; the long one, cut at its sides, held to four times twice its 120 px
        Assert.Equal(new Size(96, 48), images[0].Bounds.Size);
        Assert.Equal(new PixelSize(192, 96), ((Avalonia.Media.Imaging.Bitmap)images[0].Source!).PixelSize);
        Assert.Equal(new Size(120, 48), images[1].Bounds.Size);
        Assert.Equal(new PixelSize(960, 24), ((Avalonia.Media.Imaging.Bitmap)images[1].Source!).PixelSize);

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
            Assert.Equal(new Size(98, 50), Thumbnails(view).Single().Bounds.Size); // its place is kept
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

    // ---- /copy: the meta line ----

    private static ChatConversation WithOneReply(string reply)
    {
        var c = new ChatConversation();
        c.Items.Add(new UserMessageItem("question"));
        c.Items.Add(new AssistantTextItem { Text = reply });
        c.Items.Add(new TurnFooterItem(null, null, null, null, false));
        return c;
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Copy_reports_characters_and_lines_in_a_meta_line(bool touch)
    {
        var copied = new List<string>();
        ChatClipboard.Override = text => { copied.Add(text); return System.Threading.Tasks.Task.CompletedTask; };
        try
        {
            var c = WithOneReply("line one\nline two");
            var view = touch ? UiHelpers.TouchView(c) : new ClaudeChatView { Conversation = c };
            var window = touch ? UiHelpers.ShowPhone(view) : UiHelpers.Show(view);

            c.Submit("/copy");
            UiHelpers.Pump();

            Assert.Equal(new[] { "line one\nline two" }, copied);
            var item = Assert.IsType<MetaLineItem>(c.Items.Last());
            Assert.Equal(touch ? "Copiado para a área de transferência (17 caracteres, 2 linhas)" : "Copied to clipboard (17 characters, 2 lines)", item.Text);

            // the line: italic, the secondary color at 80%, 8 px of padding, 4 px over and under, and selectable
            var line = view.GetVisualDescendants().OfType<SelectableTextBlock>().Single(t => t.Classes.Contains("ClaudeChatMetaLine"));
            Assert.Equal(item.Text, line.Text);
            Assert.Equal(FontStyle.Italic, line.FontStyle);
            Assert.Equal(0.8, line.Opacity);
            Assert.Equal(new Thickness(8), line.Padding);
            Assert.Equal(new Thickness(0, 4), line.Margin);
            Assert.Equal(Assert.IsAssignableFrom<ISolidColorBrush>(Resource(view, "ClaudeChat.ForegroundMuted")).Color,
                Assert.IsAssignableFrom<ISolidColorBrush>(line.Foreground).Color);

            // not in the timeline: no row with a dot and a line, and it breaks no run
            Assert.Null(line.FindAncestorOfType<ChatTimelineRow>());
            Assert.False(ChatTimelineRow.IsTimeline(item));
            var answer = view.GetVisualDescendants().OfType<ChatTimelineRow>().Single(r => r.DataContext is AssistantTextItem);
            Assert.True(line.TranslatePoint(default, view)!.Value.Y > answer.TranslatePoint(default, view)!.Value.Y, "it comes after the answer");
            window.Close();
        }
        finally { ChatClipboard.Override = null; }
    }

    [AvaloniaFact]
    public void Copy_says_so_when_the_clipboard_refuses_and_the_line_is_text_of_the_list()
    {
        ChatClipboard.Override = _ => throw new InvalidOperationException("refused");
        try
        {
            var c = WithOneReply("hello");
            var (view, window) = Open(c);
            c.Submit("/copy");
            UiHelpers.Pump();
            Assert.Equal("Could not copy to clipboard", Assert.IsType<MetaLineItem>(c.Items.Last()).Text);
            Assert.Contains(view.GetVisualDescendants().OfType<SelectableTextBlock>(),
                t => t.Classes.Contains("ClaudeChatMetaLine") && t.Text == "Could not copy to clipboard" && t.IsEffectivelyVisible);
            window.Close();
        }
        finally { ChatClipboard.Override = null; }
    }

    [AvaloniaFact]
    public void Copy_lines_follow_the_language_of_the_view_when_it_changes()
    {
        var copied = new List<string>();
        ChatClipboard.Override = text => { copied.Add(text); return System.Threading.Tasks.Task.CompletedTask; };
        try
        {
            var c = WithOneReply("hello");
            var (view, window) = Open(c);
            c.Submit("/copy 2");
            Assert.Equal("Only 1 assistant messages available to copy", Assert.IsType<MetaLineItem>(c.Items.Last()).Text);
            view.Strings = ChatStrings.PortugueseBrazil;
            c.Submit("/copy");
            UiHelpers.Pump();
            Assert.Equal("Copiado para a área de transferência (5 caracteres, 1 linha)", Assert.IsType<MetaLineItem>(c.Items.Last()).Text);
            Assert.Single(copied);
            window.Close();
        }
        finally { ChatClipboard.Override = null; }
    }

    // ---- Bash and PowerShell: the copy button on the command's row ----

    private const string LongCommand = "cd build\nmake -j8 && echo 'built'\necho done # a long comment to cut the line";

    private static (ChatConversation C, ToolCallItem Tool) Command(string tool, string command, string? output = null)
    {
        var c = new ChatConversation();
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("t1", tool, J(JsonSerializer.Serialize(new { command, description = "Builds it" })))));
        if (output != null) c.Apply(new UserBlock(null, new ToolResultBlock("t1", output, false), null, false, false));
        return (c, c.Items.OfType<ToolCallItem>().Single());
    }

    private static Button CopyButton(Visual root, ToolCallItem tool) =>
        ViewOf(root, tool).GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("ClaudeChatCopyCode"));

    private static Path CopyIcon(Button button) => button.GetVisualDescendants().OfType<Path>().Single();

    /// <summary>Runs frames until <paramref name="done"/>, for what a transition of 0.15 s brings (5 s at most).</summary>
    private static void FadeUntil(Func<bool> done)
    {
        var clock = Stopwatch.StartNew();
        while (!done() && clock.Elapsed < TimeSpan.FromSeconds(5))
        {
            System.Threading.Thread.Sleep(20);
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            UiHelpers.Pump();
        }
        Assert.True(done());
    }

    private static void MoveTo(Window window, Control control, double x, double y)
    {
        window.MouseMove(control.TranslatePoint(new Point(x, y), window)!.Value, RawInputModifiers.None);
        UiHelpers.Pump();
    }

    [AvaloniaTheory]
    [InlineData("Bash")]
    [InlineData("PowerShell")]
    public void The_copy_button_copies_the_whole_command_and_turns_into_a_check(string name)
    {
        var copied = new List<string>();
        ChatClipboard.Override = text => { copied.Add(text); return System.Threading.Tasks.Task.CompletedTask; };
        try
        {
            var (c, tool) = Command(name, LongCommand, "built");
            var (view, window) = Open(c, ChatStrings.English);
            var button = CopyButton(view, tool);
            var icon = CopyIcon(button);

            // like the extension's: a 14 px icon, 4 px inside, the editor's background, a 1 px border, 4 px corners
            Assert.Equal(new Thickness(4), button.Padding);
            Assert.Equal(new Thickness(1), button.BorderThickness);
            Assert.Equal(new CornerRadius(4), button.CornerRadius);
            Assert.Same(Resource(view, "ClaudeChat.ToolBackground"), button.Background);
            Assert.Same(Resource(view, "ClaudeChat.BorderStrong"), button.BorderBrush);
            Assert.Same(Resource(view, "ClaudeChat.ForegroundMuted"), button.Foreground);
            var box = Assert.IsType<Viewbox>(button.Content);
            Assert.Equal(new Size(14, 14), box.Bounds.Size);
            Assert.Equal("Copy code", ToolTip.GetTip(button));
            Assert.Equal("Copy code to clipboard", AutomationProperties.GetName(button));
            Assert.Equal(icon.FindResource("ClaudeChat.Icon.Copy"), icon.Data);

            // at the top right of the IN row, 4 px in
            var row = button.GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("ClaudeChatToolRow"));
            var corner = button.TranslatePoint(new Point(button.Bounds.Width, 0), row)!.Value;
            Assert.Equal(row.Bounds.Width - 4, corner.X, 1);
            Assert.Equal(4, corner.Y, 1);

            UiHelpers.Click(window, button);
            Assert.Equal(new[] { LongCommand }, copied); // the whole command, not the 60 px that show
            Assert.True(ViewOf(view, tool).IsCopied);
            Assert.Equal(icon.FindResource("ClaudeChat.Icon.Check"), icon.Data);
            Assert.Equal("Copied", AutomationProperties.GetName(button));
            Assert.True(ViewOf(view, tool).IsCopyTicking); // a timer of 2 s takes the check away

            ViewOf(view, tool).EndCopied();                // 2 s later
            UiHelpers.Pump();
            Assert.False(ViewOf(view, tool).IsCopyTicking);
            Assert.Equal(icon.FindResource("ClaudeChat.Icon.Copy"), icon.Data);
            Assert.Equal("Copy code to clipboard", AutomationProperties.GetName(button));

            view.Strings = ChatStrings.PortugueseBrazil;   // the texts follow the language
            UiHelpers.Pump();
            button = CopyButton(view, tool);
            Assert.Equal("Copiar código", ToolTip.GetTip(button));
            Assert.Equal("Copiar código para a área de transferência", AutomationProperties.GetName(button));
            window.Close();
        }
        finally { ChatClipboard.Override = null; }
    }

    [AvaloniaFact]
    public void The_check_survives_a_redraw_of_the_row_and_a_refused_copy_confirms_nothing()
    {
        var accept = true;
        ChatClipboard.Override = _ => accept ? System.Threading.Tasks.Task.CompletedTask : throw new InvalidOperationException("refused");
        try
        {
            var (c, tool) = Command("Bash", "ls");
            var (view, window) = Open(c);
            accept = false;
            UiHelpers.Click(window, CopyButton(view, tool));
            Assert.False(ViewOf(view, tool).IsCopied);       // the clipboard said no: no check, and nothing thrown
            Assert.False(ViewOf(view, tool).IsCopyTicking);

            accept = true;
            UiHelpers.Click(window, CopyButton(view, tool));
            Assert.True(ViewOf(view, tool).IsCopied);
            c.Apply(new UserBlock(null, new ToolResultBlock("t1", "a\nb", false), null, false, false)); // the result redraws the box
            UiHelpers.Pump();
            var icon = CopyIcon(CopyButton(view, tool));
            Assert.Equal(icon.FindResource("ClaudeChat.Icon.Check"), icon.Data); // and the new button still shows the check
            Assert.Equal("Copied", AutomationProperties.GetName(CopyButton(view, tool)));
            window.Close();
        }
        finally { ChatClipboard.Override = null; }
    }

    [AvaloniaFact]
    public void The_copy_button_shows_only_while_the_pointer_is_over_the_in_row()
    {
        var (c, tool) = Command("Bash", "ls -la", "a\nb");
        var (view, window) = Open(c);
        var button = CopyButton(view, tool);
        var rows = ViewOf(view, tool).GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("ClaudeChatToolRow")).ToList();
        Assert.Equal(2, rows.Count);                          // IN and OUT
        Assert.Equal(0, button.Opacity);                      // clear until the pointer comes
        Assert.Contains(button.Transitions!, t => t is Avalonia.Animation.DoubleTransition d
            && d.Property == Visual.OpacityProperty && d.Duration == TimeSpan.FromSeconds(0.15));

        MoveTo(window, rows[0], rows[0].Bounds.Width / 2, 3);  // over the IN row, away from the button
        FadeUntil(() => button.Opacity == 1);

        MoveTo(window, rows[1], rows[1].Bounds.Width / 2, 3);  // over the OUT row
        FadeUntil(() => button.Opacity == 0);

        MoveTo(window, rows[0], 20, 3);                        // and back over IN
        FadeUntil(() => button.Opacity == 1);
        window.MouseMove(new Point(window.Width - 2, window.Height - 2), RawInputModifiers.None); // away from the whole call
        FadeUntil(() => button.Opacity == 0);
        window.Close();
    }

    [AvaloniaFact]
    public void The_copy_button_has_a_hover_background_and_the_keyboard_shows_it()
    {
        var (c, tool) = Command("Bash", "ls");
        var (view, window) = Open(c);
        var button = CopyButton(view, tool);
        Assert.Equal(0, button.Opacity);
        button.Focus(NavigationMethod.Tab);                    // reached with Tab: it shows, without the pointer
        FadeUntil(() => button.Opacity == 1);
        var face = button.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_ChatButton");
        MoveTo(window, button, 5, 5);
        Assert.Same(Resource(view, "ClaudeChat.HoverBackground"), face.Background); // its own hover: toolbar.hoverBackground
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_copy_button_is_a_target_that_copies_in_both_layouts(bool touch)
    {
        var copied = new List<string>();
        var command = "echo " + new string('x', 200);
        ChatClipboard.Override = text => { copied.Add(text); return System.Threading.Tasks.Task.CompletedTask; };
        try
        {
            var (c, tool) = Command("Bash", command, "ok");
            var view = touch ? UiHelpers.TouchView(c) : new ClaudeChatView { Conversation = c };
            var window = touch ? UiHelpers.ShowPhone(view) : UiHelpers.Show(view);
            var button = CopyButton(view, tool);
            if (touch)
            {
                Assert.Equal(1, button.Opacity);               // no pointer to hover with: always shown
                Assert.True(button.Bounds.Width >= 44 && button.Bounds.Height >= 44, button.Bounds.ToString());
                var content = ViewOf(view, tool).GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("ClaudeChatToolContent"));
                Assert.True(content.TranslatePoint(new Point(content.Bounds.Width, 0), view)!.Value.X
                    <= button.TranslatePoint(default, view)!.Value.X + 0.5, "the command stops short of the button");
                var face = button.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_ChatButton");
                Assert.Equal(new Size(24, 24), face.Bounds.Size); // the face is the icon's (14, 4 inside, a 1 px border), the target a finger's
            }
            else
            {
                Assert.Equal(0, button.Opacity);
                Assert.True(button.Bounds.Height < 44);
            }
            // a tap on the middle of its target, and (touch) near its corners, which the face does not reach
            var taps = touch
                ? new[] { (22.0, 22.0), (2.0, 2.0), (42.0, 2.0), (2.0, 42.0), (42.0, 42.0) }
                : new[] { (button.Bounds.Width / 2, button.Bounds.Height / 2) };
            foreach (var (x, y) in taps)
            {
                var before = copied.Count;
                var at = button.TranslatePoint(new Point(x, y), window)!.Value;
                window.MouseMove(at, RawInputModifiers.None);
                window.MouseDown(at, MouseButton.Left, RawInputModifiers.None);
                window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
                UiHelpers.Pump();
                Assert.True(copied.Count == before + 1, "a tap " + x + ", " + y);
            }
            Assert.All(copied, t => Assert.Equal(command, t));
            window.Close();
        }
        finally { ChatClipboard.Override = null; }
    }

    [AvaloniaFact]
    public void Only_a_command_has_the_copy_button_and_the_title_reads_the_tool_and_its_description()
    {
        var c = new ChatConversation();
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("t1", "PowerShell", J("{\"command\":\"Get-Date\",\"description\":\"Shows the date\"}"))));
        c.Apply(new AssistantBlock("m2", null, new ToolUseBlock("t2", "Read", J("{\"file_path\":\"a.txt\"}"))));
        c.Apply(new AssistantBlock("m3", null, new ToolUseBlock("t3", "Grep", J("{\"pattern\":\"x\"}"))));
        var (view, window) = Open(c);
        var tools = c.Items.OfType<ToolCallItem>().ToList();
        Assert.Single(view.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("ClaudeChatCopyCode"));
        Assert.DoesNotContain(ViewOf(view, tools[1]).GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("ClaudeChatCopyCode"));
        var power = ViewOf(view, tools[0]);
        var title = power.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "ToolName");
        var detail = power.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "DetailText");
        Assert.Equal("PowerShell", title.Text);
        Assert.Equal(FontWeight.Bold, title.FontWeight);
        Assert.Equal("Shows the date", detail.Text);
        Assert.Contains("plain", detail.Classes);             // the body font, not the monospace one
        Assert.NotEqual(FontWeight.Bold, detail.FontWeight);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("Bash")]
    [InlineData("PowerShell")]
    public void Bash_output_shows_without_ansi_codes(string name)
    {
        var (c, tool) = Command(name, "ls", "\u001b[32mgreen\u001b[0m \u001b]0;title\u0007plain \u009b1;31mred\u001b(B end");
        var (view, window) = Open(c);
        Assert.Equal("green plain red end", ResultOf(view, tool));
        Assert.Equal("green plain red end", tool.FullResultText);
        window.Close();
    }

    // ---- Glob, Grep, Web Fetch and Web Search ----

    private static (ChatConversation C, ToolCallItem Tool) SearchCall(string name, object input, string? result, bool isError = false)
    {
        var c = new ChatConversation();
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("t1", name, J(JsonSerializer.Serialize(input)))));
        if (result != null) c.Apply(new UserBlock(null, new ToolResultBlock("t1", result, isError), null, false, false));
        return (c, c.Items.OfType<ToolCallItem>().Single());
    }

    private static TextBlock ToolTitle(Visual root, ToolCallItem tool, string name) =>
        ViewOf(root, tool).GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == name);

    private static bool HasBox(Visual root, ToolCallItem tool) =>
        ViewOf(root, tool).GetVisualDescendants().OfType<Border>().Any(b => b.Classes.Contains("ClaudeChatToolBody"));

    private static Border OutputLine(Visual root, ToolCallItem tool) =>
        ViewOf(root, tool).GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatOutputLine"));

    private static string LineText(Border line) => Assert.IsType<TextBlock>(line.Child).Text!;

    [AvaloniaFact]
    public void Glob_counts_the_files_found()
    {
        var cases = new[]
        {
            ("src/a.cs\nsrc/b.cs\n\nsrc/c.cs\n", "Found 3 files"),
            ("src/a.cs", "Found 1 file"),
            ("No files found", "No files found"),
            ("", "No files found"),
        };
        foreach (var (output, expected) in cases)
        {
            var (c, tool) = SearchCall("Glob", new { pattern = "**/*.cs", path = @"C:\demo\app" }, output);
            var (view, window) = Open(c);
            var title = ToolTitle(view, tool, "ToolName");
            var detail = ToolTitle(view, tool, "DetailText");
            Assert.Equal("Glob", title.Text);
            Assert.Equal("pattern: \"**/*.cs\"", detail.Text); // no folder
            Assert.DoesNotContain("plain", detail.Classes);     // the editor's font
            Assert.DoesNotContain("file", detail.Classes);
            Assert.False(HasBox(view, tool), output);          // only the secondary line, no box
            var texts = UiHelpers.Texts(ViewOf(view, tool));
            Assert.Contains(expected, texts);
            Assert.DoesNotContain("OUT", texts);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Grep_counts_the_lines_of_output()
    {
        var cases = new[]
        {
            ("Found 2 files\nsrc/a.cs\nsrc/b.cs\n", "3 lines of output"),
            ("src/a.cs:4:Foo", "1 line of output"),
            ("", "No matches found"),
        };
        foreach (var (output, expected) in cases)
        {
            var (c, tool) = SearchCall("Grep", new { pattern = "Foo", path = "src", glob = "*.cs", type = "cs", output_mode = "content" }, output);
            var (view, window) = Open(c);
            Assert.Equal("Grep", ToolTitle(view, tool, "ToolName").Text);
            Assert.Equal("\"Foo\" (in src, glob: *.cs, type: cs)", ToolTitle(view, tool, "DetailText").Text);
            Assert.False(HasBox(view, tool));
            Assert.Contains(expected, UiHelpers.Texts(ViewOf(view, tool)));
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("Glob")]
    [InlineData("Grep")]
    public void The_line_of_a_search_opens_and_cuts_its_whole_output_in_place(string tool)
    {
        var output = string.Join("\n", Enumerable.Range(1, 30).Select(i => "src/file" + i + ".cs"));
        var (c, call) = SearchCall(tool, new { pattern = "x" }, output);
        var (view, window) = Open(c);
        Assert.False(HasBox(view, call));
        Assert.False(call.IsOutputExpanded);
        Assert.True(OutputLine(view, call).Focusable);

        UiHelpers.Click(window, OutputLine(view, call));      // a click opens it
        Assert.True(call.IsOutputExpanded);
        Assert.True(HasBox(view, call));
        var texts = UiHelpers.Texts(ViewOf(view, call));
        Assert.Contains(texts, t => t.Contains("src/file30.cs", StringComparison.Ordinal)); // the whole output, not its first lines
        var box = ViewOf(view, call).GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("ClaudeChatToolContent"));
        Assert.Contains("whole", box.Classes);
        Assert.DoesNotContain("expandable", box.Classes);     // the line cuts it, not the box

        System.Threading.Thread.Sleep(600);                   // past the double-click time
        UiHelpers.DrawFrames();
        OutputLine(view, call).BringIntoView();                // the list stayed at the end, below the line
        UiHelpers.DrawFrames();
        UiHelpers.Click(window, OutputLine(view, call));      // and another cuts it
        Assert.False(call.IsOutputExpanded);
        Assert.False(HasBox(view, call));

        OutputLine(view, call).Focus(NavigationMethod.Tab);   // the keyboard too
        UiHelpers.Pump();
        UiHelpers.Press(window, Key.Enter);
        Assert.True(call.IsOutputExpanded);
        Assert.True(OutputLine(view, call).IsFocused);        // the redraw keeps the focus
        UiHelpers.Press(window, Key.Space);
        Assert.False(call.IsOutputExpanded);
        window.Close();
    }

    [AvaloniaFact]
    public void A_search_without_output_has_a_plain_line_and_an_error_shows_its_text()
    {
        var (c, none) = SearchCall("Glob", new { pattern = "*.xyz" }, "No files found");
        var (view, window) = Open(c);
        Assert.Contains("No files found", UiHelpers.Texts(ViewOf(view, none)));
        UiHelpers.Click(window, OutputLine(view, none));      // there is output (the tool's own sentence): it opens
        Assert.True(none.IsOutputExpanded);
        window.Close();

        var (empty, grep) = SearchCall("Grep", new { pattern = "x" }, "");
        (view, window) = Open(empty);
        Assert.DoesNotContain(ViewOf(view, grep).GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("ClaudeChatOutputLine"));
        Assert.Contains("No matches found", UiHelpers.Texts(ViewOf(view, grep)));
        window.Close();

        var (failed, glob) = SearchCall("Glob", new { pattern = "[" }, "Invalid pattern", isError: true);
        (view, window) = Open(failed);
        var texts = UiHelpers.Texts(ViewOf(view, glob));
        Assert.Contains("OUT", texts);                         // an error is the raw text in OUT
        Assert.Contains("Invalid pattern", texts);
        Assert.DoesNotContain(texts, t => t.StartsWith("Found", StringComparison.Ordinal) || t.StartsWith("No files", StringComparison.Ordinal));
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_line_of_a_search_is_a_finger_tall_in_the_touch_layout(bool touch)
    {
        var (c, tool) = SearchCall("Glob", new { pattern = "*.cs" }, "a.cs\nb.cs");
        var view = touch ? UiHelpers.TouchView(c) : new ClaudeChatView { Conversation = c };
        var window = touch ? UiHelpers.ShowPhone(view) : UiHelpers.Show(view);
        var line = OutputLine(view, tool);
        if (touch) Assert.True(line.Bounds.Height >= 44, line.Bounds.ToString());
        else Assert.True(line.Bounds.Height < 44, line.Bounds.ToString());
        UiHelpers.Click(window, line);
        Assert.True(tool.IsOutputExpanded);                    // a tap opens it in both
        window.Close();
    }

    [AvaloniaFact]
    public void Web_fetch_shows_fetched_from_the_url()
    {
        const string url = "https://example.com/docs/intro";
        var (c, tool) = SearchCall("WebFetch", new { url, prompt = "Summarize" }, "A long page about intro that must not show.");
        var (view, window) = Open(c);
        Assert.Equal("Web Fetch", ToolTitle(view, tool, "ToolName").Text);
        Assert.Equal(url, ToolTitle(view, tool, "DetailText").Text);
        var texts = UiHelpers.Texts(ViewOf(view, tool));
        Assert.DoesNotContain("IN", texts);                    // no IN row
        Assert.DoesNotContain("OUT", texts);
        Assert.DoesNotContain(texts, t => t.Contains("must not show", StringComparison.Ordinal)); // what it fetched stays out
        Assert.DoesNotContain("Summarize", texts);
        var note = ViewOf(view, tool).GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Fetched from " + url);
        Assert.Equal(0.5, note.Opacity);
        Assert.Equal(new Thickness(8), note.Margin);
        Assert.True(HasBox(view, tool));
        window.Close();
    }

    [AvaloniaFact]
    public void Web_fetch_that_failed_shows_the_error_in_out_and_one_that_runs_shows_nothing()
    {
        var (c, failed) = SearchCall("WebFetch", new { url = "https://nope.invalid" }, "getaddrinfo ENOTFOUND nope.invalid", isError: true);
        var (view, window) = Open(c);
        var texts = UiHelpers.Texts(ViewOf(view, failed));
        Assert.Contains("OUT", texts);
        Assert.Contains("getaddrinfo ENOTFOUND nope.invalid", texts);
        Assert.DoesNotContain(texts, t => t.StartsWith("Fetched from", StringComparison.Ordinal));
        window.Close();

        var (waiting, running) = SearchCall("WebFetch", new { url = "https://example.com" }, null);
        (view, window) = Open(waiting);
        Assert.False(HasBox(view, running));
        window.Close();
    }

    [AvaloniaFact]
    public void Web_search_lists_allowed_and_blocked_domains()
    {
        var cases = new (object Input, string? Line)[]
        {
            (new { query = "avalonia headless", allowed_domains = new[] { "a.com", "b.com" }, blocked_domains = new[] { "c.com" } },
                "Allowed: a.com, b.com · Blocked: c.com"),
            (new { query = "avalonia headless", allowed_domains = new[] { "a.com" }, blocked_domains = new string[0] }, "Allowed: a.com"),
            (new { query = "avalonia headless", blocked_domains = new[] { "c.com", "d.com" } }, "Blocked: c.com, d.com"),
            (new { query = "avalonia headless", allowed_domains = new string[0] }, null),
        };
        foreach (var (input, line) in cases)
        {
            var (c, tool) = SearchCall("WebSearch", input, "Links:\n- Avalonia docs");
            var (view, window) = Open(c);
            Assert.Equal("Web Search", ToolTitle(view, tool, "ToolName").Text);
            Assert.Equal("avalonia headless", ToolTitle(view, tool, "DetailText").Text);
            var texts = UiHelpers.Texts(ViewOf(view, tool));
            Assert.DoesNotContain("IN", texts);
            Assert.Contains("OUT", texts);                     // the result is an OUT row
            Assert.Contains(texts, t => t.Contains("Avalonia docs", StringComparison.Ordinal));
            if (line != null) Assert.Contains(line, texts);
            else Assert.DoesNotContain(texts, t => t.StartsWith("Allowed", StringComparison.Ordinal) || t.StartsWith("Blocked", StringComparison.Ordinal));
            window.Close();
        }
    }

    // ---- TaskOutput, Skill and the call that is still arriving ----

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Task_output_reads_its_task_id_and_has_no_in_row(bool touch)
    {
        const string colored = "\u001b[32mbuild ok\u001b[0m\nsecond line";
        var (c, tool) = SearchCall("TaskOutput", new { task_id = "bq1x7", block = true, timeout = 30000 }, colored);
        var view = OpenIn(c, touch);
        Assert.Equal("TaskOutput", ToolTitle(view, tool, "ToolName").Text);          // the raw name
        var detail = ToolTitle(view, tool, "DetailText");
        Assert.Equal("task: \"bq1x7\"", detail.Text);                                // in mono, like the other patterns
        Assert.DoesNotContain("plain", detail.Classes);
        Assert.DoesNotContain("file", detail.Classes);
        var texts = UiHelpers.Texts(ViewOf(view, tool));
        Assert.DoesNotContain("IN", texts);
        Assert.Contains("OUT", texts);
        Assert.Contains(texts, t => t.Contains("build ok", StringComparison.Ordinal));
        Assert.DoesNotContain(texts, t => t.IndexOf('\u001b') >= 0);                 // no ANSI
        Assert.DoesNotContain(texts, t => t.Contains("bq1x7", StringComparison.Ordinal) && t != detail.Text); // the id is only on the header
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_skill_row_reads_name_skill_and_has_no_body(bool touch)
    {
        var (c, tool) = SearchCall("Skill", new { skill = "/commit", args = "-m fix" }, "Launching skill: commit");
        var view = OpenIn(c, touch);
        var name = ToolTitle(view, tool, "ToolName");
        Assert.Equal("commit", name.Text);                                           // without the leading "/"
        Assert.Equal(FontWeight.Bold, name.FontWeight);
        var detail = ToolTitle(view, tool, "DetailText");
        Assert.Equal("skill", detail.Text);                                          // mono, .85em, after the name's gap
        Assert.Equal(name.FontSize * 0.85, detail.FontSize, 1);
        Assert.NotEqual(name.FontFamily, detail.FontFamily);
        Assert.False(HasBox(view, tool));                                            // no arguments, no output
        var texts = UiHelpers.Texts(ViewOf(view, tool));
        Assert.DoesNotContain("IN", texts);
        Assert.DoesNotContain("OUT", texts);
        Assert.DoesNotContain(texts, t => t.Contains("Launching", StringComparison.Ordinal) || t.Contains("-m fix", StringComparison.Ordinal));
        Assert.True(detail.TranslatePoint(default, view)!.Value.X > name.TranslatePoint(default, view)!.Value.X); // it follows the name

        // a skill that failed has no body either
        var (c2, failed) = SearchCall("Skill", new { skill = "gone" }, "Unknown skill: gone", isError: true);
        var view2 = OpenIn(c2, touch);
        Assert.Equal("gone", ToolTitle(view2, failed, "ToolName").Text);
        Assert.False(HasBox(view2, failed));
        Assert.DoesNotContain("OUT", UiHelpers.Texts(ViewOf(view2, failed)));
    }

    // ---- MCP and Claude in Chrome ----

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_mcp_row_reads_server_and_tool_in_brackets(bool touch)
    {
        var (c, tool) = SearchCall("mcp__claude_ai_Gmail__search_threads", new { query = "from:boss", maxResults = 5 }, "3 threads");
        var view = OpenIn(c, touch);
        var name = ToolTitle(view, tool, "ToolName");
        Assert.Equal("Claude AI Gmail [search_threads]", name.Text);                  // the server made readable, the tool raw
        Assert.Equal(FontWeight.Bold, name.FontWeight);
        Assert.Equal("from:boss", ToolTitle(view, tool, "DetailText").Text);          // the first known field
        var texts = UiHelpers.Texts(ViewOf(view, tool));
        Assert.DoesNotContain("IN", texts);                                           // no IN row, an OUT row with the result
        Assert.Contains("OUT", texts);
        Assert.Contains("3 threads", texts);

        // a channel reads "#name", a long url its host, and a field of no known name is left out
        var (c2, slack) = SearchCall("mcp__github__post", new { owner = "x", channel = "dev", url = "https://example.com/a" }, "ok");
        var view2 = OpenIn(c2, touch);
        Assert.Equal("GitHub [post]", ToolTitle(view2, slack, "ToolName").Text);
        Assert.Equal("#dev", ToolTitle(view2, slack, "DetailText").Text);
        var (c3, bare) = SearchCall("mcp__my-server__ping", new { owner = "x" }, "pong");
        var view3 = OpenIn(c3, touch);
        Assert.Equal("My-server [ping]", ToolTitle(view3, bare, "ToolName").Text);
        Assert.False(ToolTitle(view3, bare, "DetailText").IsVisible);
    }

    /// <summary>The view in the desktop or touch layout (which starts in Portuguese), reading English.</summary>
    private static ClaudeChatView EnglishIn(ChatConversation c, bool touch)
    {
        var view = OpenIn(c, touch);
        view.Strings = ChatStrings.English;
        UiHelpers.Pump();
        return view;
    }

    private static (ChatConversation C, ToolCallItem Tool) ChromeCall(string action, object input, string? result, bool isError = false,
        params (int W, int H)[] images)
    {
        var c = new ChatConversation();
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("t1", "mcp__claude-in-chrome__" + action, J(JsonSerializer.Serialize(input)))));
        if (result != null)
        {
            var block = new ToolResultBlock("t1", result, isError)
            {
                Images = images.Select(i => new ClaudeVisualChat.Protocol.ImageBlock("image/png", Convert.ToBase64String(Png(i.W, i.H)))).ToList(),
            };
            c.Apply(new UserBlock(null, block, null, false, false));
        }
        return (c, c.Items.OfType<ToolCallItem>().Single());
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Chrome_navigate_reads_navigation_completed(bool touch)
    {
        var (c, tool) = ChromeCall("navigate", new { url = "https://www.example.com/page?q=1", tabId = 7 },
            "Navigated to https://www.example.com/page?q=1\n\nTab Context:\n- tabId 7: Example");
        var view = EnglishIn(c, touch);
        var name = ToolTitle(view, tool, "ToolName");
        Assert.Equal("Claude in Chrome [navigate]", name.Text);
        Assert.Equal(FontWeight.Bold, name.FontWeight);
        var detail = ToolTitle(view, tool, "DetailText");
        Assert.Equal("www.example.com", detail.Text);                                // the host, in mono
        Assert.DoesNotContain("plain", detail.Classes);
        var texts = UiHelpers.Texts(ViewOf(view, tool));
        Assert.Contains("Navigation completed", texts);                              // only the table's line
        Assert.DoesNotContain("OUT", texts);
        Assert.DoesNotContain("IN", texts);
        Assert.DoesNotContain(texts, t => t.Contains("Tab Context", StringComparison.Ordinal) || t.Contains("Show Tab", StringComparison.Ordinal));
        Assert.False(HasBox(view, tool));

        // another language, another line
        view.Strings = ChatStrings.PortugueseBrazil;
        UiHelpers.Pump();
        Assert.Contains("Navegação concluída", UiHelpers.Texts(ViewOf(view, tool)));
        Assert.Equal("Claude in Chrome [navigate]", ToolTitle(view, tool, "ToolName").Text); // a product's name stays
    }

    [AvaloniaFact]
    public void Chrome_actions_with_an_out_box_drop_the_tab_context_and_the_others_show_one_line()
    {
        // the page text goes in OUT, without the section the tool appends
        var (c, read) = ChromeCall("get_page_text", new { tabId = 1 }, "Hello world\nsecond line\n\nTab Context:\n- tabId 1: Home");
        var (view, window) = Open(c);
        var texts = UiHelpers.Texts(ViewOf(view, read));
        Assert.Contains("OUT", texts);
        Assert.Contains(texts, t => t.Contains("Hello world", StringComparison.Ordinal));
        Assert.DoesNotContain(texts, t => t.Contains("Tab Context", StringComparison.Ordinal));
        Assert.DoesNotContain("Text retrieved", texts);                               // the line is for an empty result
        window.Close();

        // nothing but the section: the summary line takes its place
        var (c2, empty) = ChromeCall("read_page", new { filter = "all" }, "Tab Context:\n- tabId 1: Home");
        (view, window) = Open(c2);
        texts = UiHelpers.Texts(ViewOf(view, empty));
        Assert.Contains("Page read", texts);
        Assert.DoesNotContain("OUT", texts);
        window.Close();

        // a find: its pattern on the header, and an OUT box
        var (c3, find) = ChromeCall("find", new { query = "the login button" }, "ref_3 button \"Log in\"");
        (view, window) = Open(c3);
        Assert.Equal("pattern: the login button", ToolTitle(view, find, "DetailText").Text);
        Assert.Contains("OUT", UiHelpers.Texts(ViewOf(view, find)));
        window.Close();

        // the other actions: only their line; the technical "_mcp" of the tab tools is not shown
        var cases = new (string Action, string Shown, string Line)[]
        {
            ("tabs_create_mcp", "tabs_create", "Tab created"),
            ("tabs_context_mcp", "tabs_context", "Tabs read"),
            ("form_input", "form_input", "Input completed"),
            ("resize_window", "resize_window", "Window resized"),
            ("upload_image", "upload_image", "Image uploaded"),
        };
        foreach (var (action, shown, line) in cases)
        {
            var (cc, call) = ChromeCall(action, new { width = 800, height = 600 }, "done\n\nTab Context:\n- tabId 1");
            (view, window) = Open(cc);
            Assert.Equal("Claude in Chrome [" + shown + "]", ToolTitle(view, call, "ToolName").Text);
            texts = UiHelpers.Texts(ViewOf(view, call));
            Assert.Contains(line, texts);
            Assert.DoesNotContain("OUT", texts);
            Assert.DoesNotContain("done", texts);
            window.Close();
        }
        var (c4, resize) = ChromeCall("resize_window", new { width = 800, height = 600 }, "ok");
        (view, window) = Open(c4);
        Assert.Equal("800x600", ToolTitle(view, resize, "DetailText").Text);
        window.Close();
    }

    [AvaloniaFact]
    public void A_chrome_error_and_a_script_show_the_generic_body_and_a_call_without_result_shows_nothing()
    {
        var (c, failed) = ChromeCall("navigate", new { url = "https://nope.invalid" }, "net::ERR_NAME_NOT_RESOLVED", isError: true);
        var (view, window) = Open(c);
        var texts = UiHelpers.Texts(ViewOf(view, failed));
        Assert.Contains("IN", texts);                                                // the input as JSON
        Assert.Contains(texts, t => t.Contains("\"url\": \"https://nope.invalid\"", StringComparison.Ordinal));
        Assert.Contains("OUT", texts);
        Assert.Contains("net::ERR_NAME_NOT_RESOLVED", texts);                         // the raw text
        Assert.DoesNotContain("Navigation completed", texts);
        window.Close();

        var (c2, script) = ChromeCall("javascript_tool", new { action = "javascript_exec", text = "1+1" }, "2\n\nTab Context:\n- x");
        (view, window) = Open(c2);
        Assert.Equal("execute", ToolTitle(view, script, "DetailText").Text);
        texts = UiHelpers.Texts(ViewOf(view, script));
        Assert.Contains("IN", texts);
        Assert.Contains("OUT", texts);
        Assert.DoesNotContain("Script executed", texts);
        window.Close();

        var (c3, running) = ChromeCall("computer", new { action = "screenshot" }, null);
        (view, window) = Open(c3);
        Assert.False(HasBox(view, running));
        Assert.DoesNotContain("Action completed", UiHelpers.Texts(ViewOf(view, running)));
        Assert.Equal("screenshot", ToolTitle(view, running, "DetailText").Text);
        window.Close();
    }

    private static IReadOnlyList<Button> Screenshots(Visual root, ToolCallItem tool) =>
        ViewOf(root, tool).GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("ClaudeChatMessageImage")).ToList();

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_chrome_screenshot_shows_a_thumbnail_that_opens_the_preview(bool touch)
    {
        var (c, tool) = ChromeCall("computer", new { action = "left_click", coordinate = new[] { 120, 340 } }, "Clicked at (120, 340)", false, (400, 100));
        var view = EnglishIn(c, touch);
        var window = (Window)view.GetVisualRoot()!;
        Assert.Equal("left_click at (120, 340)", ToolTitle(view, tool, "DetailText").Text);
        var line = ViewOf(view, tool).GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Action completed");
        var thumb = Assert.Single(Screenshots(view, tool));
        Assert.Equal("Screenshot", AutomationProperties.GetName(thumb));

        // 80 px high and at most 160 wide (a long image is cut at its sides), 1 px frame and 8 px corners; the frame adds 2
        Assert.Equal(new Size(162, 82), thumb.Bounds.Size);
        var frame = thumb.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_ChatButton");
        Assert.Equal(new CornerRadius(8), frame.CornerRadius);
        Assert.Equal(new Thickness(1), frame.BorderThickness);
        Assert.Same(Resource(view, "ClaudeChat.InputBorder"), frame.BorderBrush);
        var below = thumb.TranslatePoint(default, view)!.Value.Y - (line.TranslatePoint(default, view)!.Value.Y + line.Bounds.Height);
        Assert.InRange(below, 8, 8 + 8);                                              // 8 px under the line (the line's own margin may add)
        UiHelpers.DrawFrames();
        Assert.NotNull(thumb.GetVisualDescendants().OfType<Image>().Single().Source ?? WaitForPicture(thumb));

        // a click shows the image over the view; Esc closes it
        var preview = view.GetVisualDescendants().OfType<ImagePreview>().Single();
        UiHelpers.Click(window, thumb);
        Assert.True(preview.IsEffectivelyVisible);
        Assert.Equal("Screenshot", AutomationProperties.GetName(preview));
        Assert.Equal(400, ((Avalonia.Media.Imaging.Bitmap)preview.GetVisualDescendants().OfType<Image>().Single().Source!).PixelSize.Width);
        UiHelpers.Press(window, Key.Escape);
        Assert.False(preview.IsVisible);

        // a narrow image keeps its proportion: 40 x 80 is 40 wide
        var (c2, narrow) = ChromeCall("computer", new { action = "screenshot" }, "ok", false, (40, 80));
        var view2 = EnglishIn(c2, touch);
        Assert.Equal(new Size(42, 82), Assert.Single(Screenshots(view2, narrow)).Bounds.Size);
    }

    private static Avalonia.Media.IImage? WaitForPicture(Button thumb)
    {
        var image = thumb.GetVisualDescendants().OfType<Image>().Single();
        UiHelpers.PumpUntil(() => image.Source != null);
        return image.Source;
    }

    [AvaloniaFact]
    public void A_chrome_computer_action_without_an_image_has_only_its_line()
    {
        var (c, tool) = ChromeCall("computer", new { action = "key", text = "ctrl+a" }, "Pressed ctrl+a");
        var (view, window) = Open(c);
        Assert.Equal("key ctrl+a", ToolTitle(view, tool, "DetailText").Text);
        Assert.Contains("Action completed", UiHelpers.Texts(ViewOf(view, tool)));
        Assert.Empty(Screenshots(view, tool));
        Assert.DoesNotContain("OUT", UiHelpers.Texts(ViewOf(view, tool)));
        window.Close();
    }

    [AvaloniaFact]
    public void A_call_still_arriving_has_no_row_and_the_working_line_shows()
    {
        var c = new ChatConversation();
        var (view, window) = Open(c);
        c.Submit("go");
        c.AppendLine("{\"type\":\"stream_event\",\"parent_tool_use_id\":null,\"event\":{\"type\":\"message_start\",\"message\":{\"id\":\"m1\"}}}");
        c.AppendLine("{\"type\":\"stream_event\",\"parent_tool_use_id\":null,\"event\":{\"type\":\"content_block_start\",\"index\":0,\"content_block\":{\"type\":\"tool_use\",\"id\":\"t1\",\"name\":\"Bash\",\"input\":{}}}}");
        c.AppendLine("{\"type\":\"stream_event\",\"parent_tool_use_id\":null,\"event\":{\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":\"{\\\"comm\"}}}");
        UiHelpers.Pump();
        Assert.Empty(view.GetVisualDescendants().OfType<ToolCallView>()); // no row until the input is whole
        Assert.True(WorkingRow(view).IsEffectivelyVisible);              // the working line is all that shows the work meanwhile

        c.AppendLine("{\"type\":\"stream_event\",\"parent_tool_use_id\":null,\"event\":{\"type\":\"content_block_stop\",\"index\":0}}");
        UiHelpers.Pump();
        Assert.Single(view.GetVisualDescendants().OfType<ToolCallView>());
        window.Close();
    }

    [AvaloniaFact]
    public void The_new_lines_read_in_portuguese()
    {
        var pt = ChatStrings.PortugueseBrazil;
        var (c, glob) = SearchCall("Glob", new { pattern = "*.cs" }, "a.cs\nb.cs");
        var (view, window) = Open(c, pt);
        Assert.Contains("2 arquivos encontrados", UiHelpers.Texts(ViewOf(view, glob)));
        window.Close();

        // the other counts of Glob and the lines of Grep
        foreach (var (name, output, expected) in new[]
        {
            ("Glob", "a.cs", "1 arquivo encontrado"), ("Glob", "", "Nenhum arquivo encontrado"),
            ("Grep", "a.cs:4:x\nb.cs:2:x\nc.cs:9:x", "3 linhas de saída"), ("Grep", "a.cs:4:x", "1 linha de saída"),
            ("Grep", "", "Nenhuma correspondência encontrada"),
        })
        {
            var (searched, call) = SearchCall(name, new { pattern = "x" }, output);
            (view, window) = Open(searched, pt);
            Assert.Contains(expected, UiHelpers.Texts(ViewOf(view, call)));
            window.Close();
        }

        (c, var web) = SearchCall("WebSearch", new { query = "q", allowed_domains = new[] { "a.com" }, blocked_domains = new[] { "c.com" } }, "ok");
        (view, window) = Open(c, pt);
        Assert.Contains("Permitidos: a.com · Bloqueados: c.com", UiHelpers.Texts(ViewOf(view, web)));
        window.Close();

        (c, var fetch) = SearchCall("WebFetch", new { url = "https://x.y" }, "ok");
        (view, window) = Open(c, pt);
        Assert.Contains("Buscado em https://x.y", UiHelpers.Texts(ViewOf(view, fetch)));
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

    // ---- the queue (0.6.0) ----

    private const string Requesting =
        "{\"type\":\"system\",\"subtype\":\"status\",\"status\":\"requesting\",\"session_id\":\"s\"}";

    /// <summary>A message sent from idle and Claude at work on it, with the time the messages get.</summary>
    private static ChatConversation Working()
    {
        var c = new ChatConversation { Clock = () => new DateTimeOffset(2026, 10, 28, 9, 41, 0, TimeSpan.Zero) };
        c.Submit("first");
        c.Apply(new AssistantBlock("m1", null, new ToolUseBlock("t1", "Bash", JsonDocument.Parse("{\"command\":\"sleep 5\"}").RootElement.Clone())));
        return c;
    }

    private static StackPanel FooterPanel(ClaudeChatView view) =>
        view.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "FooterPanel");

    private static QueuedMessageList QueuedList(ClaudeChatView view) =>
        view.GetVisualDescendants().OfType<QueuedMessageList>().Single();

    [AvaloniaFact]
    public void Queued_messages_are_drawn_below_the_working_row_without_a_time()
    {
        var c = Working();
        var (view, window) = Open(c);
        c.Submit("a queued question");
        c.Submit("and another");
        UiHelpers.Pump();

        var queued = QueuedList(view);
        var working = WorkingRow(view);
        Assert.True(working.IsEffectivelyVisible);
        Assert.True(queued.IsEffectivelyVisible);
        Assert.Equal(new[] { "a queued question", "and another" }, UiHelpers.Texts(queued));          // in the order sent
        Assert.True(queued.TranslatePoint(default, view)!.Value.Y >= working.TranslatePoint(default, view)!.Value.Y + working.Bounds.Height - 1);
        // the bubble of a message, without its time and without a date line; not in the list (so never pinned at the top)
        Assert.Equal(2, queued.GetVisualDescendants().OfType<Border>().Count(b => b.Classes.Contains("ClaudeChatUser")));
        Assert.DoesNotContain(queued.GetVisualDescendants().OfType<TextBlock>(), t => t.Classes.Contains("ClaudeChatStamp"));
        Assert.Empty(queued.GetVisualDescendants().OfType<DateSeparator>());
        Assert.DoesNotContain("a queued question", UiHelpers.Texts(view.GetVisualDescendants().OfType<VirtualizingStackPanel>().First()));
        // what a screen reader hears
        Assert.All(queued.Children, row => Assert.Equal("You, queued", AutomationProperties.GetName(row)));
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(queued));
        Assert.Equal("Message queued.", AutomationProperties.GetName(queued));

        // the first request to the API after them takes them into the list, in the order sent
        c.AppendLine(Requesting);
        UiHelpers.Pump();
        Assert.False(queued.IsVisible);
        Assert.Empty(queued.Children);
        var list = UiHelpers.Texts(view.GetVisualDescendants().OfType<VirtualizingStackPanel>().First());
        Assert.True(list.IndexOf("a queued question") > list.IndexOf("first"));
        Assert.True(list.IndexOf("and another") > list.IndexOf("a queued question"));
        window.Close();
    }

    [AvaloniaFact]
    public void A_queued_message_says_when_it_ran_at_once()
    {
        var sent = new List<MessageSubmittedEventArgs>();
        var c = Working();
        c.MessageSubmitted += (_, e) => sent.Add(e);
        var (view, window) = Open(c, ChatStrings.PortugueseBrazil);
        c.Submit("/status");
        c.AppendLine("{\"type\":\"command_lifecycle\",\"command_uuid\":\"" + sent[0].Uuid + "\",\"state\":\"queued\",\"ran_ahead\":true}");
        UiHelpers.Pump();

        var queued = QueuedList(view);
        var meta = queued.GetVisualDescendants().OfType<SelectableTextBlock>().Single(t => t.Classes.Contains("ClaudeChatMetaLine"));
        Assert.True(meta.IsEffectivelyVisible);
        Assert.Equal("Rodou na hora, enquanto o Claude trabalhava.", meta.Text);
        Assert.Equal("Você, na fila", AutomationProperties.GetName(Assert.Single(queued.Children)));
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(ChatLayout.Desktop)]
    [InlineData(ChatLayout.Touch)]
    public void Queued_messages_keep_their_place_between_the_working_row_and_the_agents_chip_when_the_layout_changes(ChatLayout start)
    {
        var c = Working();
        c.Apply(new AssistantBlock("m2", null, new ToolUseBlock("toolu_a", "Agent", JsonDocument.Parse("{\"description\":\"Look around\"}").RootElement.Clone())));
        c.Submit("held");
        var view = new ClaudeChatView { Conversation = c, Layout = start };
        var window = start == ChatLayout.Touch ? UiHelpers.ShowPhone(view) : UiHelpers.Show(view);
        var queued = QueuedList(view);

        void Check(ChatLayout layout)
        {
            UiHelpers.Pump();
            var footer = FooterPanel(view).Children;
            Assert.Same(WorkingRow(view), footer[0]);
            Assert.Same(queued, footer[1]);
            if (layout == ChatLayout.Touch)
            {
                Assert.Equal(3, footer.Count);
                Assert.IsType<AgentsChip>(footer[2]);
            }
            else
            {
                Assert.Equal(2, footer.Count);
            }
            Assert.Equal(new[] { "held" }, UiHelpers.Texts(queued));
            Assert.True(queued.IsEffectivelyVisible);
        }

        Check(start);
        var other = start == ChatLayout.Touch ? ChatLayout.Desktop : ChatLayout.Touch;
        view.Layout = other;
        Check(other);
        view.Layout = start;
        Check(start);
        c.Submit("held too");                                            // and what comes after goes in the same place
        UiHelpers.Pump();
        Assert.Same(queued, FooterPanel(view).Children[1]);
        Assert.Equal(new[] { "held", "held too" }, UiHelpers.Texts(queued));
        window.Close();
    }

    // ---- the thinking line with its tokens, and the compaction card (0.6.0) ----

    [AvaloniaFact]
    public void The_thinking_row_reads_thinking_dots_and_the_token_estimate()
    {
        long now = 5000;
        TokenCountUp.ClockOverride = () => now;
        try
        {
            var c = new ChatConversation();
            c.Apply(new ClaudeVisualChat.Protocol.BlockStarted("m1", 0, null, new ClaudeVisualChat.Protocol.ThinkingBlock("")));
            var (view, window) = Open(c);
            var row = view.GetVisualDescendants().OfType<ChatTimelineRow>().Single();
            var label = row.GetVisualDescendants().OfType<ThinkingLabel>().Single();
            Assert.Equal(new[] { "Thinking..." }, UiHelpers.Texts(row));       // no count yet: no tokens

            c.Apply(new ClaudeVisualChat.Protocol.ThinkingTokens(1234));
            UiHelpers.Pump();
            Assert.Equal(new[] { "Thinking..." }, UiHelpers.Texts(row));       // it starts at 0 and climbs
            Assert.True(label.IsTicking);

            now += 600;                                                        // half of the first 1200 ms
            label.Tick();
            Assert.Equal(new[] { "Thinking... · 617 tokens" }, UiHelpers.Texts(row));
            Assert.True(label.IsTicking);

            now += 700;                                                        // settled: 1.2k, and the timer rests
            label.Tick();
            Assert.Equal(new[] { "Thinking... · 1.2k tokens" }, UiHelpers.Texts(row));
            Assert.False(label.IsTicking);

            view.Strings = ChatStrings.PortugueseBrazil;
            UiHelpers.Pump();
            Assert.Equal(new[] { "Pensando... · 1,2k tokens" }, UiHelpers.Texts(row));

            // a lower count shows at once; the whole thought says how long it took, and no tokens
            c.Apply(new ClaudeVisualChat.Protocol.ThinkingTokens(900));
            UiHelpers.Pump();
            Assert.Equal(new[] { "Pensando... · 900 tokens" }, UiHelpers.Texts(row));
            c.Apply(new AssistantBlock("m1", null, new ClaudeVisualChat.Protocol.ThinkingBlock("hm")) { ThinkingDurationMs = 2600 });
            UiHelpers.Pump();
            Assert.Equal(new[] { "Pensou por 3 s" }, UiHelpers.Texts(row));
            Assert.False(label.IsTicking);
            window.Close();
        }
        finally { TokenCountUp.ClockOverride = null; }
    }

    [AvaloniaFact]
    public void A_reused_thinking_row_shows_the_count_of_the_item_it_is_given()
    {
        long now = 0;
        TokenCountUp.ClockOverride = () => now;
        try
        {
            var first = new ThinkingItem { IsStreaming = true };
            var second = new ThinkingItem { IsStreaming = true };
            var c = new ChatConversation();
            c.Items.Add(first);
            c.Items.Add(second);
            first.EstimatedTokens = 340;
            now = 5000;
            second.EstimatedTokens = 48_700;
            now = 10_000;
            var (view, window) = Open(c);
            var label = UiHelpers.ItemList(view).ContainerFromIndex(0)!.GetVisualDescendants().OfType<ThinkingLabel>().Single();
            Assert.Equal("Thinking... · 340 tokens", label.Text);
            label.DataContext = second;                                        // the list reuses the row for another item
            UiHelpers.Pump();
            Assert.Equal("Thinking... · 48.7k tokens", label.Text);
            Assert.Equal("Thinking...", ThinkingLabel.TextOf(new ThinkingItem { IsStreaming = true }, ChatStrings.English, now));
            window.Close();
        }
        finally { TokenCountUp.ClockOverride = null; }
    }

    private static CompactionItem Compaction(string? summary = "**What was done**\n\nthe summary text") =>
        new CompactionItem { Trigger = "manual", TokensBefore = 36326, Summary = summary };

    private static CompactionCard CardOf(ClaudeChatView view, CompactionItem item) =>
        view.GetVisualDescendants().OfType<CompactionCard>().Single(card => ReferenceEquals(card.DataContext, item));

    [AvaloniaFact]
    public void The_compaction_card_opens_to_the_summary()
    {
        var item = Compaction();
        var c = new ChatConversation();
        c.Items.Add(item);
        var (view, window) = Open(c);
        var card = CardOf(view, item);

        Assert.Equal("Compacted chat · manual · 36k tokens freed", card.Label.Text);
        Assert.False(item.IsExpanded);                                         // closed to begin with
        Assert.False(card.Summary.IsVisible);
        Assert.DoesNotContain("the summary text", UiHelpers.MarkdownText(card));

        UiHelpers.Click(window, card.Head);                                    // opened to the summary, in Markdown
        Assert.True(item.IsExpanded);
        Assert.True(card.Summary.IsVisible);
        Assert.Contains("the summary text", UiHelpers.MarkdownText(card));
        Assert.DoesNotContain("**", UiHelpers.MarkdownText(card));             // rendered, not as typed
        Assert.True(card.Summary.TranslatePoint(default, card)!.Value.Y >= card.Head.Bounds.Bottom + 8 - 1);

        UiHelpers.Click(window, card.Head);
        Assert.False(item.IsExpanded);
        Assert.False(card.Summary.IsVisible);

        item.IsExpanded = true;                                                // the item says it: the card follows
        UiHelpers.Pump();
        Assert.True(card.Head.IsChecked);
        Assert.True(card.Summary.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void A_compaction_without_a_summary_says_so_when_opened()
    {
        var item = Compaction(summary: null);
        var c = new ChatConversation();
        c.Items.Add(item);
        var (view, window) = Open(c);
        var card = CardOf(view, item);
        item.IsExpanded = true;
        UiHelpers.Pump();
        Assert.Contains("Conversation was compacted to free up context.", UiHelpers.MarkdownText(card));

        view.Strings = ChatStrings.PortugueseBrazil;
        UiHelpers.Pump();
        Assert.Equal("Conversa compactada · manual · 36k tokens liberados", card.Label.Text);
        Assert.Contains("A conversa foi compactada para liberar contexto.", UiHelpers.MarkdownText(card));
        window.Close();
    }

    [AvaloniaFact]
    public void The_compaction_card_is_drawn_as_measured()
    {
        var item = Compaction();
        var c = new ChatConversation();
        c.Items.Add(item);
        var (view, window) = Open(c);
        var card = CardOf(view, item);
        Assert.Equal(new Thickness(0, 4), card.Margin);                        // 4 px over and under
        Assert.Equal(new Thickness(8), card.Padding);
        Assert.Equal(FontStyle.Italic, card.Label.FontStyle);
        Assert.Same(Resource(view, "ClaudeChat.ForegroundMuted"), card.Label.Foreground);
        var line = card.Head.GetVisualDescendants().OfType<StackPanel>().First();
        Assert.Equal(0.8, line.Opacity);
        var chevron = card.Head.GetVisualDescendants().OfType<Path>().Single();
        Assert.Equal(12, chevron.Width);
        Assert.Equal(12, chevron.Height);
        Assert.Equal(8, card.Summary.Margin.Top);
        window.Close();
    }

    [AvaloniaFact]
    public void The_compaction_head_is_a_finger_tall_in_the_touch_layout_only()
    {
        var item = Compaction();
        var desktopConversation = new ChatConversation();
        desktopConversation.Items.Add(item);
        var (desktop, desktopWindow) = Open(desktopConversation);
        var desktopCard = CardOf(desktop, item);
        Assert.True(desktopCard.Head.Bounds.Height < 44);
        desktopWindow.Close();

        var other = Compaction();
        var touchConversation = new ChatConversation();
        touchConversation.Items.Add(other);
        var touch = UiHelpers.TouchView(touchConversation);
        var touchWindow = UiHelpers.ShowPhone(touch);
        var touchCard = CardOf(touch, other);
        Assert.True(touchCard.Head.Bounds.Height >= 44);
        Assert.Equal("Conversa compactada · manual · 36k tokens liberados", touchCard.Label.Text);
        UiHelpers.Click(touchWindow, touchCard.Head);                          // and a tap opens it
        Assert.True(other.IsExpanded);
        Assert.True(touchCard.Summary.IsVisible);
        touchWindow.Close();
    }

    [AvaloniaFact]
    public void A_reused_compaction_card_shows_the_state_of_the_item_it_is_given()
    {
        var open = Compaction();
        open.IsExpanded = true;
        var closed = new CompactionItem { Trigger = "auto", TokensBefore = 160_000, Summary = "other summary" };
        var c = new ChatConversation();
        c.Items.Add(open);
        c.Items.Add(closed);
        var (view, window) = Open(c);
        var card = CardOf(view, open);
        Assert.True(card.Summary.IsVisible);
        card.DataContext = closed;                                             // the list reuses the card for another item
        UiHelpers.Pump();
        Assert.Equal("Compacted chat · auto · 160k tokens freed", card.Label.Text);
        Assert.False(card.Summary.IsVisible);
        Assert.False(card.Head.IsChecked);
        card.DataContext = open;
        UiHelpers.Pump();
        Assert.True(card.Summary.IsVisible);
        window.Close();
    }
}
