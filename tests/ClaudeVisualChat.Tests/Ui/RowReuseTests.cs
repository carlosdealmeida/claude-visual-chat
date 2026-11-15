using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using ClaudeVisualChat.Controls;
using ClaudeVisualChat.Protocol;
using Xunit;

namespace ClaudeVisualChat.Tests.Ui;

/// <summary>
/// In the touch layout a row scrolled off the list is kept and shows the next item of its kind, so scrolling on a phone does not build
/// the row again each time. On the desktop rows are built as they always were.
/// </summary>
public class RowReuseTests
{
    private static ChatConversation Long(int turns = 40)
    {
        var c = new ChatConversation();
        for (var i = 0; i < turns; i++)
        {
            c.Items.Add(new UserMessageItem("Question " + i));
            c.Items.Add(new ThinkingItem { Text = "thought " + i });
            c.Items.Add(new ToolCallItem
            {
                Name = "Read",
                Summary = "file" + i + ".cs",
                Status = i % 2 == 0 ? ToolStatus.Succeeded : ToolStatus.Failed,
                ToolUseId = "t" + i,
                Input = JsonDocument.Parse("{\"file_path\":\"file" + i + ".cs\"}").RootElement,
            });
            c.Items.Add(new AssistantTextItem { Text = "Answer " + i });
        }
        return c;
    }

    private static List<ContentPresenter> Containers(ClaudeChatView view) =>
        view.GetVisualDescendants().OfType<VirtualizingStackPanel>().First().Children.OfType<ContentPresenter>().ToList();

    private static List<ContentPresenter> Shown(ClaudeChatView view) =>
        Containers(view).Where(c => c.IsVisible && c.Child != null).ToList();

    /// <summary>Goes down the list and back, and counts every row control that showed.</summary>
    private static HashSet<Control> ScrollDownAndUp(ClaudeChatView view)
    {
        var scroller = UiHelpers.Scroller(view);
        var seen = new HashSet<Control>();
        void Look()
        {
            UiHelpers.Pump();
            foreach (var c in Shown(view)) seen.Add(c.Child!);
        }
        Look();
        for (var y = 0.0; y <= 3000; y += 60)
        {
            scroller.Offset = new Vector(0, y);
            Look();
        }
        for (var y = 3000.0; y >= 0; y -= 60)
        {
            scroller.Offset = new Vector(0, y);
            Look();
        }
        return seen;
    }

    private static HashSet<object> ItemsShown(ClaudeChatView view) =>
        new HashSet<object>(Shown(view).Select(c => c.Content!));

    [AvaloniaFact]
    public void Touch_reuses_the_rows_while_scrolling()
    {
        var view = UiHelpers.TouchView(Long());
        UiHelpers.ShowPhone(view);
        var built = ScrollDownAndUp(view);
        // a screen of each of the 4 kinds is enough for the whole trip down and back, past dozens of items
        Assert.InRange(built.Count, 4, 30);
    }

    [AvaloniaFact]
    public void Desktop_builds_a_row_for_each_item_that_shows()
    {
        var view = new ClaudeChatView { Conversation = Long() };
        UiHelpers.Show(view, UiHelpers.PhoneWidth, UiHelpers.PhoneHeight);
        var built = ScrollDownAndUp(view);
        Assert.True(built.Count > 30, "built " + built.Count);
    }

    [AvaloniaFact]
    public void A_reused_row_is_always_the_kind_of_its_item()
    {
        var view = UiHelpers.TouchView(Long());
        UiHelpers.ShowPhone(view);
        var scroller = UiHelpers.Scroller(view);
        for (var y = 0.0; y <= 3000; y += 60)
        {
            scroller.Offset = new Vector(0, y);
            UiHelpers.Pump();
            foreach (var c in Shown(view))
            {
                switch (c.Content)
                {
                    case ToolCallItem: Assert.IsType<ToolCallView>(c.Child); break;
                    case AssistantTextItem:
                    case ThinkingItem: Assert.IsType<ChatTimelineRow>(c.Child); break;
                    case UserMessageItem: Assert.IsType<StackPanel>(c.Child); break;
                }
            }
        }
    }

    [AvaloniaFact]
    public void A_reused_row_shows_its_new_item_and_nothing_of_the_old()
    {
        var view = UiHelpers.TouchView(Long());
        UiHelpers.ShowPhone(view);
        var scroller = UiHelpers.Scroller(view);
        for (var y = 0.0; y <= 3000; y += 120)
        {
            scroller.Offset = new Vector(0, y);
            UiHelpers.Pump();
            foreach (var c in Shown(view))
            {
                Assert.Same(c.Content, c.Child!.DataContext);
                var texts = string.Join(" | ", UiHelpers.Texts(c.Child));
                var markdown = UiHelpers.MarkdownText(c.Child);
                switch (c.Content)
                {
                    case UserMessageItem user: Assert.Contains(user.Text, texts); break;
                    case ToolCallItem tool:
                        Assert.Contains(tool.Summary, texts);
                        Assert.Equal(tool.Status == ToolStatus.Failed,
                            c.Child.GetVisualDescendants().OfType<Control>().Any(x => x.IsEffectivelyVisible && x.Classes.Contains("failed")));
                        break;
                    case AssistantTextItem answer: Assert.Equal(answer.Text, markdown); break;
                }
            }
        }
    }

    [AvaloniaFact]
    public void A_row_opened_stays_with_its_item_and_not_with_the_reused_control()
    {
        var c = Long();
        var opened = c.Items.OfType<ThinkingItem>().First();
        opened.IsExpanded = true;
        var view = UiHelpers.TouchView(c);
        UiHelpers.ShowPhone(view);
        var scroller = UiHelpers.Scroller(view);
        for (var y = 0.0; y <= 3000; y += 60)
        {
            scroller.Offset = new Vector(0, y);
            UiHelpers.Pump();
            foreach (var shown in Shown(view).Where(s => s.Content is ThinkingItem))
            {
                var item = (ThinkingItem)shown.Content!;
                var thought = shown.Child!.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("ClaudeChatThought"));
                Assert.Equal(item.IsExpanded, thought.IsVisible);
            }
        }
    }

    [AvaloniaFact]
    public void An_item_removed_leaves_the_list_and_the_rest_keep_their_rows_right()
    {
        var c = Long(10);
        var view = UiHelpers.TouchView(c);
        UiHelpers.ShowPhone(view);
        var first = c.Items[0];
        c.Items.RemoveAt(0);
        UiHelpers.Pump();
        Assert.DoesNotContain(first, ItemsShown(view));
        foreach (var s in Shown(view)) Assert.Same(s.Content, s.Child!.DataContext);
    }

    [AvaloniaFact]
    public void Leaving_the_touch_layout_does_not_show_an_old_row()
    {
        var view = UiHelpers.TouchView(Long());
        UiHelpers.ShowPhone(view);
        ScrollDownAndUp(view);
        view.Layout = ChatLayout.Desktop;
        UiHelpers.Pump();
        var scroller = UiHelpers.Scroller(view);
        scroller.Offset = new Vector(0, 1500);
        UiHelpers.Pump();
        foreach (var s in Shown(view)) Assert.Same(s.Content, s.Child!.DataContext);
    }

    [AvaloniaFact]
    public void Another_conversation_shows_only_its_own_items()
    {
        var view = UiHelpers.TouchView(Long());
        UiHelpers.ShowPhone(view);
        ScrollDownAndUp(view);
        var other = Long(5);
        view.Conversation = other;
        UiHelpers.Pump();
        Assert.NotEmpty(Shown(view));
        foreach (var s in Shown(view))
        {
            Assert.Contains(s.Content!, other.Items.Cast<object>());
            Assert.Same(s.Content, s.Child!.DataContext);
        }
    }

    // ---- the row of a running command (0.8.0) ----

    private static JsonElement J(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    /// <summary>A Bash call <paramref name="toolUseId"/> that the claude runs in the foreground as the task <paramref name="taskId"/>.</summary>
    private static ToolCallItem RunningBash(ChatConversation c, string toolUseId, string taskId)
    {
        c.Apply(new AssistantBlock("m_" + toolUseId, null, new ToolUseBlock(toolUseId, "Bash", J("{\"command\":\"npm test\"}"))));
        c.Apply(new AgentTaskStarted(taskId, toolUseId, "", null, false, 0, "local_bash"));
        return c.Items.OfType<ToolCallItem>().Single(t => t.ToolUseId == toolUseId);
    }

    /// <summary><paramref name="count"/> replies of several paragraphs each: enough to take a row far off a phone's screen.</summary>
    private static void LongReplies(ChatConversation c, int count)
    {
        for (var i = 0; i < count; i++)
            c.Apply(new AssistantBlock("r" + i, null, new TextContentBlock("Reply " + i + "\n\n" + string.Join("\n\n", Enumerable.Repeat("A paragraph of text.", 6)))));
    }

    /// <summary>A host that asks for the output of each command (ids r1, r2…); the claude answers on the next UI job with <see cref="Output"/>.</summary>
    private sealed class OutputHost
    {
        public readonly List<string> Asked = new List<string>(); // the task ids, in order
        public readonly Dictionary<string, string> Output = new Dictionary<string, string>();

        public OutputHost(ChatConversation c)
        {
            c.TaskOutputRequested += (_, e) =>
            {
                Asked.Add(e.TaskId);
                var id = e.RequestId = "r" + Asked.Count;
                var output = Output.TryGetValue(e.TaskId, out var text) ? text : "";
                Avalonia.Threading.Dispatcher.UIThread.Post(() => c.AppendLine(JsonSerializer.Serialize(new
                {
                    type = "control_response",
                    response = new { subtype = "success", request_id = id, response = new { output, total_bytes = output.Length, truncated = false } },
                })));
            };
        }
    }

    private static List<ToolCallView> RowsOf(ClaudeChatView view, ToolCallItem tool) =>
        view.GetVisualDescendants().OfType<ToolCallView>().Where(v => ReferenceEquals(v.DataContext, tool)).ToList();

    private static ScrollViewer LiveScroller(ToolCallView row) =>
        row.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatLiveOutput"))
            .GetVisualDescendants().OfType<ScrollViewer>().Single();

    /// <summary>Runs the dispatcher's loop, timers and all, for <paramref name="time"/> on the real clock.</summary>
    private static void RunLoop(System.TimeSpan time)
    {
        using var stop = new System.Threading.CancellationTokenSource(time);
        Avalonia.Threading.Dispatcher.UIThread.MainLoop(stop.Token);
    }

    /// <summary>Runs the dispatcher on the real clock until <paramref name="done"/> (60 s at most: the machine may be slow).</summary>
    private static void WaitFor(System.Func<bool> done)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && clock.Elapsed < System.TimeSpan.FromSeconds(60)) RunLoop(System.TimeSpan.FromMilliseconds(100));
        UiHelpers.Pump();
        Assert.True(done());
    }

    /// <summary>
    /// Closes the test's window, so the row of its running command leaves the tree and stops its timer: the dispatcher is the one every
    /// test shares, and a row left in a window would go on asking once a second until the end of the run.
    /// </summary>
    private static void CloseAndStopAsking(Window window, ToolCallView row)
    {
        window.Close();
        UiHelpers.Pump();
        Assert.Null(row.GetVisualRoot());
    }

    [AvaloniaFact]
    public void A_running_command_kept_off_screen_is_not_asked_about()
    {
        var c = new ChatConversation();
        var tool = RunningBash(c, "t1", "k1");
        LongReplies(c, 40);
        var host = new OutputHost(c);
        var view = UiHelpers.TouchView(c);
        var window = UiHelpers.ShowPhone(view);
        var scroller = UiHelpers.Scroller(view);
        scroller.Offset = new Vector(0, 0);
        UiHelpers.Pump();
        WaitFor(() => host.Asked.Count >= 1); // on screen it asks

        // the row scrolls away: the list keeps it, hidden, with its item
        scroller.Offset = new Vector(0, scroller.Extent.Height);
        UiHelpers.Pump();
        var row = Assert.Single(RowsOf(view, tool));
        Assert.False(row.IsEffectivelyVisible);
        var asked = host.Asked.Count;
        RunLoop(System.TimeSpan.FromSeconds(2.5));
        Assert.Equal(asked, host.Asked.Count);

        // back on screen: it asks again
        scroller.Offset = new Vector(0, 0);
        UiHelpers.Pump();
        WaitFor(() => host.Asked.Count > asked);
        CloseAndStopAsking(window, row);
    }

    [AvaloniaFact]
    public void A_row_given_to_another_running_command_asks_at_once_and_its_box_follows_the_end()
    {
        var now = new System.DateTimeOffset(2026, 11, 14, 10, 0, 0, System.TimeSpan.Zero);
        var c = new ChatConversation { Clock = () => now };
        var first = RunningBash(c, "t1", "k1");
        LongReplies(c, 40);
        var second = RunningBash(c, "t2", "k2");
        var host = new OutputHost(c);
        host.Output["k1"] = string.Join("\n", Enumerable.Range(1, 60).Select(i => "first " + i));
        host.Output["k2"] = string.Join("\n", Enumerable.Range(1, 90).Select(i => "second " + i));
        var view = UiHelpers.TouchView(c);
        var window = UiHelpers.ShowPhone(view);
        var scroller = UiHelpers.Scroller(view);
        scroller.Offset = new Vector(0, 0);
        UiHelpers.Pump();
        Assert.NotNull(first.LiveOutput);

        // the person scrolls the first command's box up: it no longer follows its end
        var row = Assert.Single(RowsOf(view, first));
        var box = LiveScroller(row);
        Assert.True(box.Offset.Y > 0, "the box follows the end of a long output");
        box.Offset = new Vector(0, 0);
        UiHelpers.Pump();

        // the second command has printed before its row shows (its row may have asked when the view opened at the end, too); a while
        // later the list gives it the first command's row
        now = now.AddSeconds(5);
        c.RequestTaskOutput(second);
        UiHelpers.Pump();
        Assert.NotNull(second.LiveOutput);
        now = now.AddSeconds(5);
        var asked = host.Asked.Count;
        scroller.Offset = new Vector(0, scroller.Extent.Height);
        UiHelpers.Pump();
        Assert.Same(row, Assert.Single(RowsOf(view, second)));

        Assert.Contains("k2", host.Asked.Skip(asked)); // asked about at once, not on the next tick of the first command's timer
        Assert.True(UiHelpers.AtEnd(LiveScroller(row)), "the new command's box follows its end");
        CloseAndStopAsking(window, row);
    }

    [AvaloniaFact]
    public void A_subagents_running_command_asks_at_once_when_it_shows_again()
    {
        var now = new System.DateTimeOffset(2026, 11, 14, 10, 0, 0, System.TimeSpan.Zero);
        var c = new ChatConversation { Clock = () => now };
        c.Apply(new AssistantBlock("m_a1", null, new ToolUseBlock("a1", "Agent", J("{\"description\":\"Look around\",\"prompt\":\"go\"}"))));
        c.Apply(new AssistantBlock("m_t1", "a1", new ToolUseBlock("t1", "Bash", J("{\"command\":\"npm test\"}"))));
        c.Apply(new AgentTaskStarted("k1", "t1", "", null, false, 0, "local_bash"));
        var agent = c.Items.OfType<ToolCallItem>().Single();
        var tool = agent.Children.OfType<ToolCallItem>().Single();
        agent.IsExpanded = true;
        LongReplies(c, 40);
        var host = new OutputHost(c);
        var view = UiHelpers.TouchView(c);
        var window = UiHelpers.ShowPhone(view);
        var scroller = UiHelpers.Scroller(view);
        scroller.Offset = new Vector(0, 0);
        UiHelpers.Pump();
        Assert.Contains("k1", host.Asked); // on screen it asks

        // the agent's row scrolls away with its steps and comes back a while later: the command is asked about at once, not on the
        // next tick of its row's timer (no timer runs here: only the jobs)
        scroller.Offset = new Vector(0, scroller.Extent.Height);
        UiHelpers.Pump();
        var row = Assert.Single(RowsOf(view, tool));
        Assert.False(row.IsEffectivelyVisible);
        now = now.AddSeconds(5);
        var asked = host.Asked.Count;
        scroller.Offset = new Vector(0, 0);
        UiHelpers.Pump();
        row = Assert.Single(RowsOf(view, tool));
        Assert.True(row.IsEffectivelyVisible);
        Assert.True(host.Asked.Count > asked, "asked about when the agent's row came back on screen");

        // the same when the person folds the agent's steps and opens them again
        agent.IsExpanded = false;
        UiHelpers.Pump();
        Assert.False(row.IsEffectivelyVisible);
        now = now.AddSeconds(5);
        asked = host.Asked.Count;
        agent.IsExpanded = true;
        UiHelpers.Pump();
        Assert.True(host.Asked.Count > asked, "asked about when the steps were opened again");
        CloseAndStopAsking(window, row);
    }
}
