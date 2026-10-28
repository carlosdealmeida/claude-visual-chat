using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using ClaudeVisualChat.Controls;
using Xunit;

namespace ClaudeVisualChat.Tests.Ui;

/// <summary>The Focus view in the view: the folds in the list, their head and steps, and the two ways to turn it on and off.</summary>
public class FocusViewTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public FocusViewTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        _output = output;
    }

    private static string Ms(System.TimeSpan time) => time.TotalMilliseconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " ms";
    /// <summary>A round: a prompt, three calls (one failed), the answer and the footer.</summary>
    private static ChatConversation Round()
    {
        var c = new ChatConversation();
        c.Items.Add(new UserMessageItem("fix it"));
        c.Items.Add(new ToolCallItem { Name = "Read", Status = ToolStatus.Succeeded, ToolUseId = "t1" });
        c.Items.Add(new ToolCallItem { Name = "Bash", Status = ToolStatus.Failed, ToolUseId = "t2" });
        c.Items.Add(new ToolCallItem { Name = "Edit", Status = ToolStatus.Succeeded, ToolUseId = "t3" });
        c.Items.Add(new AssistantTextItem { Text = "Done." });
        c.Items.Add(new TurnFooterItem(4200, 1000, 200, 0.01, false));
        return c;
    }

    private static List<ToolCallView> Calls(ClaudeChatView view) =>
        view.GetVisualDescendants().OfType<ToolCallView>().Where(t => t.IsEffectivelyVisible).ToList();

    private static ToggleButton Head(ClaudeChatView view) =>
        view.GetVisualDescendants().OfType<ToggleButton>().Single(b => b.IsEffectivelyVisible && b.Classes.Contains("ClaudeChatFocusHead"));

    private static ChatTimelineRow RowOf(ClaudeChatView view, object item) =>
        view.GetVisualDescendants().OfType<ChatTimelineRow>().First(r => ReferenceEquals(r.DataContext, item) && r.IsEffectivelyVisible);

    private static object? Resource(Control view, string key) => view.TryFindResource(key, out var value) ? value : null;

    [AvaloniaTheory]
    [InlineData(ChatLayout.Desktop)]
    [InlineData(ChatLayout.Touch)]
    public void Focus_view_on_shows_folds_and_off_shows_every_row(ChatLayout layout)
    {
        var c = Round();
        var touch = layout == ChatLayout.Touch;
        var view = new ClaudeChatView { Conversation = c, Layout = layout, FocusView = true };
        var window = touch ? UiHelpers.ShowPhone(view) : UiHelpers.Show(view);
        var list = UiHelpers.ItemList(view);
        var strings = view.Strings;

        // the three calls are one row; the prompt, the answer and the footer stay
        Assert.Empty(Calls(view));
        var fold = Assert.IsType<FocusFoldItem>(Assert.IsType<FocusProjection>(list.ItemsSource)[1]);
        var head = Head(view);
        Assert.Contains("3 tool calls · 1 failed", UiHelpers.Texts(head));
        Assert.Contains("fix it", UiHelpers.Texts(view));
        Assert.Contains("Done.", UiHelpers.MarkdownText(view));

        if (touch)
        {
            // the row a thought had in the 0.5.0, with a target of 44 px; the copy of the turn still reads the conversation's items
            Assert.True(head.Bounds.Height >= 43.5, head.Bounds.ToString());
            Assert.True(view.GetVisualDescendants().OfType<CopyTurnButton>().Single().IsVisible);
        }
        else
        {
            // a timeline row: its dot red after a failure, the line going on to the answer; the chevron 1em
            var row = RowOf(view, fold);
            var dot = row.GetVisualDescendants().OfType<Ellipse>().Single(e => e.Name == "PART_Dot");
            Assert.Same(Resource(view, "ClaudeChat.DotFailed"), dot.Fill);
            Assert.DoesNotContain(":runEnd", row.Classes);
            Assert.DoesNotContain(":runStart", RowOf(view, c.Items[4]).Classes);
            var chevron = head.GetVisualDescendants().OfType<Viewbox>().Single(v => v.Classes.Contains("ClaudeChatFocusChevron"));
            Assert.Equal(13, chevron.Bounds.Width, 1);
        }

        // open, the steps themselves and "Collapse"; the state is the item's. The head is at .8 under the pointer, .65 pressed
        var center = head.TranslatePoint(new Point(head.Bounds.Width / 2, head.Bounds.Height / 2), window)!.Value;
        window.MouseMove(center, RawInputModifiers.None);
        UiHelpers.Pump();
        if (!touch) Assert.Equal(0.8, head.Opacity);
        window.MouseDown(center, MouseButton.Left, RawInputModifiers.None);
        UiHelpers.Pump();
        if (!touch) Assert.Equal(0.65, head.Opacity);
        window.MouseUp(center, MouseButton.Left, RawInputModifiers.None);
        UiHelpers.Pump();
        Assert.True(fold.IsExpanded);
        Assert.Equal(3, Calls(view).Count);
        var collapse = view.GetVisualDescendants().OfType<ToggleButton>().Single(b => b.IsEffectivelyVisible && b.Classes.Contains("ClaudeChatFocusCollapse"));
        Assert.Contains(strings.FocusCollapse, UiHelpers.Texts(collapse));
        UiHelpers.Click(window, collapse);
        Assert.False(fold.IsExpanded);
        Assert.Empty(Calls(view));

        // a call that comes runs under the fold's live label, its count after it in bold at half
        var running = new ToolCallItem { Name = "Grep", Status = ToolStatus.Running, ToolUseId = "t4" };
        c.Items.Insert(4, running);
        UiHelpers.Pump();
        Assert.Contains("Running Grep…", UiHelpers.Texts(Head(view)));
        var count = Head(view).GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("ClaudeChatFocusCount"));
        Assert.True(count.IsEffectivelyVisible);
        Assert.Equal("4 tool calls · 1 failed", count.Text);
        Assert.Equal(FontWeight.Bold, count.FontWeight);
        Assert.Equal(0.5, count.Opacity);
        Assert.Contains("running", RowOf(view, fold).Classes);
        running.Status = ToolStatus.Succeeded;
        UiHelpers.Pump();
        Assert.Contains("4 tool calls · 1 failed", UiHelpers.Texts(Head(view)));

        // off: every row, from the conversation's own list
        view.FocusView = false;
        UiHelpers.Pump();
        Assert.Same(c.Items, list.ItemsSource);
        Assert.Equal(4, Calls(view).Count);
        Assert.DoesNotContain(view.GetVisualDescendants().OfType<ToggleButton>(), b => b.IsEffectivelyVisible && b.Classes.Contains("ClaudeChatFocusHead"));
        window.Close();
    }

    [AvaloniaFact]
    public void Ctrl_alt_f_toggles_focus_view()
    {
        var c = Round();
        var view = new ClaudeChatView { Conversation = c };
        var window = UiHelpers.Show(view);
        var list = UiHelpers.ItemList(view);
        UiHelpers.Input(view).Focus();

        UiHelpers.Press(window, Key.F, RawInputModifiers.Control | RawInputModifiers.Alt);
        Assert.True(view.FocusView);
        Assert.IsType<FocusProjection>(list.ItemsSource);
        Assert.Empty(Calls(view));

        UiHelpers.Press(window, Key.F, RawInputModifiers.Control | RawInputModifiers.Alt);
        Assert.False(view.FocusView);
        Assert.Same(c.Items, list.ItemsSource);
        Assert.Equal(3, Calls(view).Count);
        window.Close();
    }

    [AvaloniaFact]
    public void The_palette_switch_toggles_focus_view()
    {
        var view = new ClaudeChatView { Conversation = Round() };
        var window = UiHelpers.Show(view);
        var composer = UiHelpers.Composer(view);
        var palette = composer.GetLogicalDescendants().OfType<CommandPalette>().Single();
        UiHelpers.Click(window, UiHelpers.ButtonWithText(composer, "Show command menu (/)"));
        Assert.True(palette.IsOpen);

        // under "Settings", a switch with its tooltip
        var menu = palette.Child!;
        Assert.Contains("Settings", menu.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && t.Classes.Contains("ClaudeChatMenuHeader")).Select(t => t.Text));
        var row = menu.GetVisualDescendants().OfType<Border>().Single(b => b.IsEffectivelyVisible && b.Classes.Contains("ClaudeChatMenuItem")
            && b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Classes.Contains("ClaudeChatMenuLabel") && t.Text == "Focus view"));
        Assert.Equal("Show only your prompts and Claude's responses", ToolTip.GetTip(row));
        var track = row.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatSwitch"));
        Assert.DoesNotContain("on", track.Classes);

        // a press turns it on, the palette stays open and the thumb slides; another turns it off
        UiHelpers.Click(window, row);
        Assert.True(view.FocusView);
        Assert.True(palette.IsOpen);
        Assert.Contains("on", track.Classes);
        Assert.Empty(Calls(view));
        UiHelpers.Click(window, row);
        Assert.False(view.FocusView);
        Assert.DoesNotContain("on", track.Classes);
        Assert.True(palette.IsOpen);

        // the host's own value shows when the palette opens, in Portuguese too
        palette.IsOpen = false;
        view.FocusView = true;
        view.Strings = ChatStrings.PortugueseBrazil;
        UiHelpers.Pump();
        UiHelpers.Click(window, UiHelpers.ButtonWithText(composer, "Mostrar o menu de comandos (/)"));
        row = palette.Child!.GetVisualDescendants().OfType<Border>().Single(b => b.IsEffectivelyVisible && b.Classes.Contains("ClaudeChatMenuItem")
            && b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Classes.Contains("ClaudeChatMenuLabel") && t.Text == "Modo foco"));
        Assert.Contains("on", row.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("ClaudeChatSwitch")).Classes);
        Assert.Equal("Mostrar só os seus prompts e as respostas do Claude", ToolTip.GetTip(row));
        window.Close();
    }

    [AvaloniaFact]
    public void A_long_conversation_takes_rows_at_its_end_and_in_its_middle_without_reading_the_list_again()
    {
        // 3,000 items over two days (date lines), the Focus view on, the list at its end
        var c = new ChatConversation();
        var day = new System.DateTimeOffset(2026, 10, 27, 9, 0, 0, System.TimeSpan.Zero);
        var n = 0;
        void Round()
        {
            var at = day.AddMinutes(n++ * 3);
            c.Items.Add(new UserMessageItem("question " + n) { Timestamp = at });
            for (var i = 0; i < 7; i++) c.Items.Add(new ToolCallItem { Name = "Read", Status = ToolStatus.Succeeded });
            c.Items.Add(new AssistantTextItem { Text = "answer " + n, Timestamp = at.AddSeconds(30) });
            c.Items.Add(new TurnFooterItem(1000, null, null, null, false));
        }
        while (c.Items.Count < 3000) Round();
        var view = new ClaudeChatView { Conversation = c, FocusView = true };
        var window = UiHelpers.Show(view);
        var list = UiHelpers.ItemList(view);
        var projection = Assert.IsType<FocusProjection>(list.ItemsSource);
        var resets = 0;
        projection.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset) resets++;
        };
        UiHelpers.Pump();

        // 100 rounds as they come, each drawn: the cost of a round does not grow with the list
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (var r = 0; r < 100; r++)
        {
            Round();
            UiHelpers.Pump();
        }
        clock.Stop();
        var appended = clock.Elapsed;
        Assert.Equal(0, resets);
        Assert.True(UiHelpers.AtEnd(UiHelpers.Scroller(view)));
        Assert.Contains("answer " + n, UiHelpers.MarkdownText(view));

        // a line put in the middle cuts a fold in two: the rows on screen move their places, no list is read again
        var middle = c.Items.IndexOf(c.Items.OfType<ToolCallItem>().Skip(1500).First());
        clock.Restart();
        c.Items.Insert(middle, new NoticeItem(NoticeKind.ApiRetry));
        UiHelpers.Pump();
        clock.Stop();
        var inserted = clock.Elapsed;
        clock.Restart();
        c.Items.RemoveAt(middle);
        UiHelpers.Pump();
        clock.Stop();
        Assert.Equal(0, resets);
        _output.WriteLine("1,100 items (100 rounds) appended to 3,000 and drawn: " + Ms(appended) + "; one insert in the middle: " + Ms(inserted)
                          + "; its removal: " + Ms(clock.Elapsed));
        // budgets for a busy machine; the numbers are in the output
        Assert.True(appended.TotalMilliseconds < 20_000, Ms(appended));
        Assert.True(inserted.TotalMilliseconds < 2_000, Ms(inserted));
        window.Close();
    }
}
