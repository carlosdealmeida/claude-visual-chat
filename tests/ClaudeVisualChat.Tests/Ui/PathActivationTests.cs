using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using ClaudeVisualChat.Controls;
using ColorTextBlock.Avalonia;
using Xunit;

namespace ClaudeVisualChat.Tests.Ui;

/// <summary>
/// <see cref="ChatConversation.PathActivated"/>: a click on the path in a tool call's header, on a code span that looks like a path
/// or on a Markdown link that is not a web address, in both layouts. Everything is off while the host does not listen.
/// </summary>
public class PathActivationTests
{
    private sealed class Opened
    {
        public Opened(string markdown, bool listen = true, ChatLayout layout = ChatLayout.Desktop)
        {
            Conversation = new ChatConversation();
            if (listen) Conversation.PathActivated += (_, e) => Paths.Add((e.Path, e.Line));
            Conversation.Items.Add(new AssistantTextItem { Text = markdown });
            View = new ClaudeChatView { Conversation = Conversation, Layout = layout };
            Window = layout == ChatLayout.Touch ? UiHelpers.ShowPhone(View) : UiHelpers.Show(View);
        }

        public ChatConversation Conversation { get; }

        public ClaudeChatView View { get; }

        public Window Window { get; }

        public List<(string Path, int? Line)> Paths { get; } = new List<(string Path, int? Line)>();

        public MarkdownBlock Block => View.GetVisualDescendants().OfType<MarkdownBlock>().First();

        public CTextBlock Paragraph => Block.GetVisualDescendants().OfType<CTextBlock>().First();

        /// <summary>A click near the start of the first paragraph, where its first words are.</summary>
        public void ClickStartOfText()
        {
            var p = Paragraph;
            var point = p.TranslatePoint(new Point(8, Math.Min(p.Bounds.Height / 2, 10)), Window)!.Value;
            Window.MouseMove(point, RawInputModifiers.None);
            Window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
            Window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
            UiHelpers.Pump();
        }
    }

    // ---- code spans ----

    [AvaloniaTheory]
    [InlineData(ChatLayout.Desktop)]
    [InlineData(ChatLayout.Touch)]
    public void A_code_span_that_looks_like_a_path_raises_the_event_with_its_line(ChatLayout layout)
    {
        var ui = new Opened("`src/app/main.css:42` is long", layout: layout);
        var link = Assert.Single(ui.Paragraph.Content.OfType<CHyperlink>());
        Assert.IsType<CCode>(Assert.Single(link.Content));

        ui.ClickStartOfText();
        Assert.Equal(new[] { ("src/app/main.css", (int?)42) }, ui.Paths);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_code_span_without_a_line_gives_a_null_line()
    {
        var ui = new Opened("`src/app/main.css` is long");
        ui.ClickStartOfText();
        Assert.Equal(new[] { ("src/app/main.css", (int?)null) }, ui.Paths);
        ui.Window.Close();
    }

    [AvaloniaTheory]
    [InlineData("`dotnet test` runs them")]
    [InlineData("`console.log` is a call")]
    [InlineData("`https://example.com/a.html` is a page")]
    [InlineData("plain text with src/app/main.css:42 in it")]
    public void Other_text_stays_plain_and_does_not_raise(string markdown)
    {
        var ui = new Opened(markdown);
        Assert.Empty(ui.Paragraph.Content.OfType<CHyperlink>());
        ui.ClickStartOfText();
        Assert.Empty(ui.Paths);
        ui.Window.Close();
    }

    // ---- links ----

    [AvaloniaTheory]
    [InlineData("[main](src/app/main.css#L42) here", "src/app/main.css", 42)]
    [InlineData("[main](src/app/main.css#L42-L51) here", "src/app/main.css", 42)]
    [InlineData("[main](src/app/main.css:42) here", "src/app/main.css", 42)]
    [InlineData("[main](src/app/main.css) here", "src/app/main.css", null)]
    [InlineData("[main](./notes.md) here", "./notes.md", null)]
    public void A_link_to_a_path_raises_the_event(string markdown, string path, int? line)
    {
        var ui = new Opened(markdown);
        ui.ClickStartOfText();
        Assert.Equal(new[] { (path, line) }, ui.Paths);
        ui.Window.Close();
    }

    [AvaloniaTheory]
    [InlineData("[docs](https://example.com/a.html) here")]
    [InlineData("[mail](mailto:a@b.example) here")]
    [InlineData("[top](#top) here")]
    public void A_link_to_a_url_or_an_anchor_raises_nothing(string markdown)
    {
        var ui = new Opened(markdown);
        ui.ClickStartOfText();
        Assert.Empty(ui.Paths);
        ui.Window.Close();
    }

    // ---- while the host does not listen ----

    [AvaloniaFact]
    public void Without_a_listener_the_text_is_drawn_and_works_as_before()
    {
        var ui = new Opened("`src/app/main.css:42` and [main](src/app/main.css#L42)", listen: false);
        Assert.Equal(1, ui.Block.RenderCount);
        var inlines = ui.Paragraph.Content.ToList();
        Assert.Single(inlines.OfType<CCode>());                 // not wrapped
        var link = Assert.Single(inlines.OfType<CHyperlink>()); // only the Markdown link
        ui.ClickStartOfText();
        link.Command!("src/app/main.css#L42");                  // the engine's own command: nothing
        Assert.Empty(ui.Paths);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void The_text_follows_whether_the_host_listens()
    {
        var ui = new Opened("`src/app/main.css:42` here", listen: false);
        Assert.Empty(ui.Paragraph.Content.OfType<CHyperlink>());

        var paths = new List<string>();
        EventHandler<PathActivatedEventArgs> handler = (_, e) => paths.Add(e.Path);
        ui.Conversation.PathActivated += handler;
        UiHelpers.Pump();
        Assert.Single(ui.Paragraph.Content.OfType<CHyperlink>());
        ui.ClickStartOfText();
        Assert.Equal(new[] { "src/app/main.css" }, paths);

        ui.Conversation.PathActivated -= handler;
        UiHelpers.Pump();
        Assert.Empty(ui.Paragraph.Content.OfType<CHyperlink>());
        ui.ClickStartOfText();
        Assert.Single(paths);
        ui.Window.Close();
    }

    // ---- the header of a tool call ----

    private static ToolCallItem Call(string name, string json) => new ToolCallItem
    {
        Name = name, Summary = "main.css", Status = ToolStatus.Succeeded,
        Input = JsonDocument.Parse(json).RootElement.Clone(),
    };

    private static TextBlock DetailOf(ClaudeChatView view, ToolCallItem item) =>
        view.GetVisualDescendants().OfType<ToolCallView>().Single(v => ReferenceEquals(v.DataContext, item))
            .GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("ClaudeChatToolDetail"));

    private static (ClaudeChatView View, Window Window, List<(string Path, int? Line)> Paths) OpenCall(
        ToolCallItem item, bool listen = true, ChatLayout layout = ChatLayout.Desktop)
    {
        var c = new ChatConversation();
        var paths = new List<(string Path, int? Line)>();
        if (listen) c.PathActivated += (_, e) => paths.Add((e.Path, e.Line));
        c.Items.Add(item);
        var view = new ClaudeChatView { Conversation = c, Layout = layout };
        return (view, layout == ChatLayout.Touch ? UiHelpers.ShowPhone(view) : UiHelpers.Show(view), paths);
    }

    [AvaloniaTheory]
    [InlineData("Edit", ChatLayout.Desktop)]
    [InlineData("Write", ChatLayout.Desktop)]
    [InlineData("Read", ChatLayout.Desktop)]
    [InlineData("MultiEdit", ChatLayout.Desktop)]
    [InlineData("Read", ChatLayout.Touch)]
    [InlineData("Edit", ChatLayout.Touch)]
    public void Clicking_the_path_of_a_file_tool_raises_the_event_with_the_file_path(string tool, ChatLayout layout)
    {
        var item = Call(tool, "{\"file_path\":\"/work/app/src/main.css\"}");
        var (view, window, paths) = OpenCall(item, layout: layout);

        var detail = DetailOf(view, item);
        Assert.Contains("pathlink", detail.Classes);
        UiHelpers.DrawFrames();
        UiHelpers.Click(window, detail);
        Assert.Equal(new[] { ("/work/app/src/main.css", (int?)null) }, paths);
        Assert.False(item.IsExpanded);                  // the click is not the row's toggle
        window.Close();
    }

    [AvaloniaFact]
    public void Clicking_elsewhere_in_the_header_raises_nothing_and_does_not_toggle()
    {
        var item = Call("Read", "{\"file_path\":\"src/main.css\"}");
        var (view, window, paths) = OpenCall(item);
        var name = view.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("ClaudeChatToolName"));
        UiHelpers.Click(window, name);
        Assert.Empty(paths);
        Assert.False(item.IsExpanded);
        window.Close();
    }

    [AvaloniaFact]
    public void Without_a_listener_the_header_path_is_not_a_link()
    {
        var item = Call("Edit", "{\"file_path\":\"src/main.css\"}");
        var (view, window, paths) = OpenCall(item, listen: false);
        var detail = DetailOf(view, item);
        Assert.DoesNotContain("pathlink", detail.Classes);
        UiHelpers.DrawFrames();
        UiHelpers.Click(window, detail);
        Assert.Empty(paths);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("Bash", "{\"command\":\"ls src/main.css\",\"description\":\"List src/main.css\"}")]
    [InlineData("Read", "{}")]
    public void Other_headers_are_not_links(string tool, string json)
    {
        var item = Call(tool, json);
        var (view, window, paths) = OpenCall(item);
        var detail = DetailOf(view, item);
        Assert.DoesNotContain("pathlink", detail.Classes);
        UiHelpers.DrawFrames();
        UiHelpers.Click(window, detail);
        Assert.Empty(paths);
        window.Close();
    }

    [AvaloniaFact]
    public void The_header_follows_whether_the_host_listens()
    {
        var item = Call("Write", "{\"file_path\":\"src/main.css\"}");
        var (view, window, _) = OpenCall(item, listen: false);
        var detail = DetailOf(view, item);
        Assert.DoesNotContain("pathlink", detail.Classes);

        var paths = new List<string>();
        EventHandler<PathActivatedEventArgs> handler = (_, e) => paths.Add(e.Path);
        view.Conversation!.PathActivated += handler;
        UiHelpers.Pump();
        Assert.Contains("pathlink", detail.Classes);
        UiHelpers.DrawFrames();
        UiHelpers.Click(window, detail);
        Assert.Equal(new[] { "src/main.css" }, paths);

        view.Conversation.PathActivated -= handler;
        UiHelpers.Pump();
        Assert.DoesNotContain("pathlink", detail.Classes);
        window.Close();
    }

    // ---- fix round 1 ----

    [AvaloniaFact]
    public void A_listening_host_gets_one_drawing_on_first_show()
    {
        var ui = new Opened("`src/app/main.css:42` is long", listen: true);
        UiHelpers.DrawFrames();
        Assert.Equal(1, ui.Block.RenderCount);
        Assert.Single(ui.Paragraph.Content.OfType<CHyperlink>());
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_code_span_path_is_underlined_only_under_the_pointer()
    {
        var ui = new Opened("`src/app/main.css:42` is long");
        var link = Assert.Single(ui.Paragraph.Content.OfType<CHyperlink>());
        Assert.False(link.IsUnderline);
        var p = ui.Paragraph;
        var point = p.TranslatePoint(new Point(8, Math.Min(p.Bounds.Height / 2, 10)), ui.Window)!.Value;
        ui.Window.MouseMove(point, RawInputModifiers.None);
        UiHelpers.Pump();
        Assert.True(link.IsUnderline);
        ui.Window.MouseMove(new Point(point.X + 600, point.Y + 200), RawInputModifiers.None);
        UiHelpers.Pump();
        Assert.False(link.IsUnderline);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_code_span_as_the_text_of_a_link_acts_as_a_code_span()
    {
        // the engine drops the link around a code span (no CHyperlink of its own is left), so the span's own path is what is clicked
        var ui = new Opened("[`src/a.cs`](src/b.cs#L42) here");
        Assert.Single(ui.Paragraph.Content.OfType<CHyperlink>());
        ui.ClickStartOfText();
        Assert.Equal(new[] { ("src/a.cs", (int?)null) }, ui.Paths);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_notebook_header_raises_the_notebook_path()
    {
        var item = Call("NotebookEdit", "{\"notebook_path\":\"/work/a.ipynb\",\"cell_id\":\"c1\"}");
        var (view, window, paths) = OpenCall(item);
        var detail = DetailOf(view, item);
        Assert.Contains("pathlink", detail.Classes);
        UiHelpers.DrawFrames();
        UiHelpers.Click(window, detail);
        Assert.Equal(new[] { ("/work/a.ipynb", (int?)null) }, paths);
        window.Close();
    }

    [AvaloniaFact]
    public void In_the_touch_layout_the_whole_header_row_takes_the_tap()
    {
        var item = Call("Read", "{\"file_path\":\"src/main.css\"}");
        var (view, window, paths) = OpenCall(item, layout: ChatLayout.Touch);
        var header = view.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().First(b => b.Classes.Contains("ClaudeChatDisclosure"));
        var detail = DetailOf(view, item);
        UiHelpers.DrawFrames();
        // well right of the file name, and above it: still the header row
        var right = header.TranslatePoint(new Point(header.Bounds.Width - 20, header.Bounds.Height / 2), window)!.Value;
        window.MouseMove(right, RawInputModifiers.None);
        window.MouseDown(right, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(right, MouseButton.Left, RawInputModifiers.None);
        UiHelpers.Pump();
        Assert.Equal(new[] { ("src/main.css", (int?)null) }, paths);
        Assert.False(item.IsExpanded);
        window.Close();
    }

    [AvaloniaFact]
    public void On_the_desktop_only_the_path_takes_the_click()
    {
        var item = Call("Read", "{\"file_path\":\"src/main.css\"}");
        var (view, window, paths) = OpenCall(item);
        var header = view.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().First(b => b.Classes.Contains("ClaudeChatDisclosure"));
        UiHelpers.DrawFrames();
        var right = header.TranslatePoint(new Point(header.Bounds.Width - 20, header.Bounds.Height / 2), window)!.Value;
        window.MouseMove(right, RawInputModifiers.None);
        window.MouseDown(right, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(right, MouseButton.Left, RawInputModifiers.None);
        UiHelpers.Pump();
        Assert.Empty(paths);
        Assert.False(item.IsExpanded);
        window.Close();
    }

    [AvaloniaFact]
    public void A_press_on_the_path_that_never_ends_there_does_not_pair_with_a_later_release()
    {
        var item = Call("Read", "{\"file_path\":\"src/main.css\"}");
        var (view, window, paths) = OpenCall(item);
        var detail = DetailOf(view, item);
        var name = view.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("ClaudeChatToolName"));
        UiHelpers.DrawFrames();
        Point Center(Control c) => c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), window)!.Value;
        window.MouseMove(Center(detail), RawInputModifiers.None);
        window.MouseDown(Center(detail), MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(new Point(900, 600), RawInputModifiers.None);
        window.MouseUp(new Point(900, 600), MouseButton.Left, RawInputModifiers.None); // released far away: the header never sees it
        UiHelpers.Pump();
        Assert.Empty(paths);
        window.MouseMove(Center(name), RawInputModifiers.None);
        window.MouseDown(Center(name), MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(Center(detail), RawInputModifiers.None);
        window.MouseUp(Center(detail), MouseButton.Left, RawInputModifiers.None);
        UiHelpers.Pump();
        Assert.Empty(paths);
        window.Close();
    }

    // ---- fix round 2: a reply shaped like the real ones ----

    private const string Bullets =
        "Created:\n\n- `aaa-primeiro.css` the style\n- `arquivo-15.txt:1` the list\n- `body { color: #fff; }` the rule\n";

    [AvaloniaFact]
    public void A_bare_file_name_in_a_bullet_is_clickable_by_a_press_and_release_alone()
    {
        var ui = new Opened(Bullets);
        UiHelpers.DrawFrames();
        var links = ui.Block.GetVisualDescendants().OfType<CTextBlock>().SelectMany(t => t.Content.OfType<CHyperlink>()).ToList();
        Assert.Equal(2, links.Count); // the two files; the rule is plain code
        var second = ui.Block.GetVisualDescendants().OfType<CTextBlock>().First(t => t.Content.OfType<CHyperlink>().Any(l => l.CommandParameter == "arquivo-15.txt"));
        // no pointer move before: as with a finger, where there is no hover
        var point = second.TranslatePoint(new Point(8, Math.Min(second.Bounds.Height / 2, 10)), ui.Window)!.Value;
        ui.Window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        ui.Window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        UiHelpers.Pump();
        Assert.Equal(new[] { ("arquivo-15.txt", (int?)1) }, ui.Paths);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_streamed_reply_ends_with_the_same_clickable_spans()
    {
        var c = new ChatConversation();
        var paths = new List<string>();
        c.PathActivated += (_, e) => paths.Add(e.Path);
        var item = new AssistantTextItem { Text = "Created:\n\n- `aaa-pri" };
        c.Items.Add(item);
        var view = new ClaudeChatView { Conversation = c };
        var window = UiHelpers.Show(view);
        System.Threading.Thread.Sleep(150); // past the block's render interval: the longer text draws at once
        item.Text = Bullets;
        UiHelpers.DrawFrames();
        var block = view.GetVisualDescendants().OfType<MarkdownBlock>().First();
        List<CHyperlink> Links() => block.GetVisualDescendants().OfType<CTextBlock>().SelectMany(t => t.Content.OfType<CHyperlink>()).ToList();
        UiHelpers.PumpUntil(() => Links().Count == 2);
        var links = Links();
        links[1].Command!(links[1].CommandParameter!);
        Assert.Equal(new[] { "arquivo-15.txt" }, paths);
        window.Close();
    }

    [AvaloniaFact]
    public void A_code_span_path_and_a_link_end_in_the_same_activation()
    {
        var ui = new Opened("`src/a.cs:7` and [b](src/b.cs#L9)");
        var inlines = ui.Paragraph.Content.OfType<CHyperlink>().ToList();
        Assert.Equal(2, inlines.Count);
        Assert.All(inlines, l => Assert.NotNull(l.Command));
        inlines[0].Command!(inlines[0].CommandParameter!);
        inlines[1].Command!(inlines[1].CommandParameter!);
        Assert.Equal(new[] { ("src/a.cs", (int?)7), ("src/b.cs", (int?)9) }, ui.Paths);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void In_the_touch_layout_a_code_span_path_is_underlined_at_rest()
    {
        var ui = new Opened("`src/a.cs:7` here", layout: ChatLayout.Touch);
        UiHelpers.DrawFrames();
        Assert.True(Assert.Single(ui.Paragraph.Content.OfType<CHyperlink>()).IsUnderline);
        ui.Window.Close();
    }

    // ---- fix round 3 ----

    [AvaloniaFact]
    public void Unsubscribing_while_attached_redraws_later_not_in_the_call()
    {
        var ui = new Opened("`src/app/main.css:42` here", listen: false);
        EventHandler<PathActivatedEventArgs> handler = (_, _) => { };
        ui.Conversation.PathActivated += handler;
        UiHelpers.Pump();
        var drawn = ui.Block.RenderCount;
        ui.Conversation.PathActivated -= handler;
        Assert.Equal(drawn, ui.Block.RenderCount); // not synchronously: the view may be about to go
        UiHelpers.Pump();
        Assert.Equal(drawn + 1, ui.Block.RenderCount);
        Assert.Empty(ui.Paragraph.Content.OfType<CHyperlink>());
        ui.Window.Close();
    }

    // ---- a touch that stops the list's fling ----

    /// <summary>
    /// A step of a finger's scroll gesture <paramref name="id"/> in the list (a drag sends them, and so does the fling after it, each
    /// time the dispatcher is free), at <paramref name="ms"/> on the view's clock.
    /// </summary>
    private static void ScrollStep(Control inside, int id, ref long clock, long ms)
    {
        clock = ms;
        inside.RaiseEvent(new ScrollGestureEventArgs(id, new Vector(0, 0)));
    }

    /// <summary>The gesture ends: its fling ran out, or a touch stopped it (the recognizer ends it on the press).</summary>
    private static void ScrollEnd(Control inside, int id) => inside.RaiseEvent(new ScrollGestureEndedEventArgs(id));

    [AvaloniaFact]
    public void A_press_that_stops_the_fling_does_not_open_the_path_in_a_header()
    {
        long clock = 1000;
        ClaudeChatView.ClockOverride = () => clock;
        try
        {
            var item = Call("Read", "{\"file_path\":\"src/main.css\"}");
            var (view, window, paths) = OpenCall(item, layout: ChatLayout.Touch);
            var detail = DetailOf(view, item);
            UiHelpers.DrawFrames();

            var id = ScrollGestureEventArgs.GetNextFreeId();
            ScrollStep(detail, id, ref clock, 1000);
            clock = 1400; // a step can come this late from a phone busy drawing: the list still moves, and the press stops it
            UiHelpers.Click(window, detail);
            Assert.Empty(paths);
            Assert.False(item.IsExpanded);

            ScrollEnd(detail, id); // the press ended the fling
            UiHelpers.Click(window, detail);
            Assert.Equal(new[] { ("src/main.css", (int?)null) }, paths);
            window.Close();
        }
        finally
        {
            ClaudeChatView.ClockOverride = null;
        }
    }

    [AvaloniaFact]
    public void A_press_that_stops_the_fling_does_not_open_a_code_span_path()
    {
        long clock = 1000;
        ClaudeChatView.ClockOverride = () => clock;
        try
        {
            var ui = new Opened("`src/app/main.css:42` is long", layout: ChatLayout.Touch);
            var id = ScrollGestureEventArgs.GetNextFreeId();
            ScrollStep(ui.Paragraph, id, ref clock, 1000);
            clock = 1050;
            ui.ClickStartOfText();
            Assert.Empty(ui.Paths);

            ScrollEnd(ui.Paragraph, id);
            ui.ClickStartOfText();
            Assert.Equal(new[] { ("src/app/main.css", (int?)42) }, ui.Paths);
            ui.Window.Close();
        }
        finally
        {
            ClaudeChatView.ClockOverride = null;
        }
    }

    [AvaloniaFact]
    public void A_gesture_whose_end_never_came_is_over_once_it_stood_still()
    {
        long clock = 1000;
        ClaudeChatView.ClockOverride = () => clock;
        try
        {
            var ui = new Opened("`src/app/main.css:42` is long", layout: ChatLayout.Touch);
            ScrollStep(ui.Paragraph, ScrollGestureEventArgs.GetNextFreeId(), ref clock, 1000);
            clock = 1000 + ClaudeChatView.StaleScrollGestureMs + 1;
            ui.ClickStartOfText();
            Assert.Equal(new[] { ("src/app/main.css", (int?)42) }, ui.Paths);
            ui.Window.Close();
        }
        finally
        {
            ClaudeChatView.ClockOverride = null;
        }
    }

    [AvaloniaFact]
    public void A_tap_right_after_a_drag_that_ended_opens_the_path()
    {
        long clock = 1000;
        ClaudeChatView.ClockOverride = () => clock;
        try
        {
            var ui = new Opened("`src/app/main.css:42` is long", layout: ChatLayout.Touch);
            var id = ScrollGestureEventArgs.GetNextFreeId();
            ScrollStep(ui.Paragraph, id, ref clock, 1000);
            ScrollEnd(ui.Paragraph, id); // a slow drag: no fling after it
            clock = 1010;
            ui.ClickStartOfText();
            Assert.Equal(new[] { ("src/app/main.css", (int?)42) }, ui.Paths);
            ui.Window.Close();
        }
        finally
        {
            ClaudeChatView.ClockOverride = null;
        }
    }

    [AvaloniaFact]
    public void A_press_outside_the_list_does_not_count_as_stopping_it()
    {
        long clock = 1000;
        ClaudeChatView.ClockOverride = () => clock;
        try
        {
            var ui = new Opened("`src/app/main.css:42` is long", layout: ChatLayout.Touch);
            ScrollStep(ui.Paragraph, ScrollGestureEventArgs.GetNextFreeId(), ref clock, 1000);
            clock = 1010;
            ui.Conversation.PressStoppedFling = true; // left by an earlier press
            ui.View.FindControl<Control>("Composer")!.RaiseEvent(new PointerPressedEventArgs(ui.View, new Pointer(Pointer.GetNextFreeId(), PointerType.Touch, true),
                ui.Window, new Point(0, 0), 0, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None));
            Assert.False(ui.Conversation.PressStoppedFling);
            ui.Window.Close();
        }
        finally
        {
            ClaudeChatView.ClockOverride = null;
        }
    }

    [AvaloniaFact]
    public void A_redraw_waiting_for_the_dispatcher_is_dropped_when_the_block_left()
    {
        var ui = new Opened("`src/app/main.css:42` here", listen: false);
        var block = ui.Block;
        ui.Conversation.PathActivated += (_, _) => { };
        ui.View.Conversation = new ChatConversation(); // the view shows another conversation; the old blocks are taken apart
        var drawn = block.RenderCount;
        UiHelpers.Pump();
        Assert.Equal(drawn, block.RenderCount);
        ui.Window.Close();
    }
}
