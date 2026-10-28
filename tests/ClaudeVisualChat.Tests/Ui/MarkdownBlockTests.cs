using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using ClaudeVisualChat.Controls;
using ColorTextBlock.Avalonia;
using Xunit;

namespace ClaudeVisualChat.Tests.Ui;

public class MarkdownBlockTests
{
    /// <summary>A clock that only moves when the test says, running what was scheduled on the way.</summary>
    private sealed class FakeClock : IRenderClock
    {
        private readonly List<Entry> _queue = new List<Entry>();

        public TimeSpan Now { get; private set; }

        public IDisposable Schedule(TimeSpan delay, Action action)
        {
            var entry = new Entry(Now + delay, action, _queue);
            _queue.Add(entry);
            return entry;
        }

        public void Advance(TimeSpan by)
        {
            var end = Now + by;
            while (true)
            {
                var next = _queue.Where(e => e.Due <= end).OrderBy(e => e.Due).FirstOrDefault();
                if (next == null) break;
                _queue.Remove(next);
                Now = next.Due;
                next.Action();
            }
            Now = end;
        }

        private sealed class Entry : IDisposable
        {
            private readonly List<Entry> _queue;

            public Entry(TimeSpan due, Action action, List<Entry> queue)
            {
                Due = due;
                Action = action;
                _queue = queue;
            }

            public TimeSpan Due { get; }

            public Action Action { get; }

            public void Dispose() => _queue.Remove(this);
        }
    }

    private static string Shown(MarkdownBlock block) =>
        string.Join(" ", block.GetVisualDescendants().OfType<CTextBlock>().Select(t => t.Text));

    [AvaloniaFact]
    public void Renders_at_most_every_interval()
    {
        var clock = new FakeClock();
        var block = new MarkdownBlock { Clock = clock };
        Assert.Equal(TimeSpan.FromMilliseconds(100), block.RenderInterval);
        var window = UiHelpers.Show(block);
        var words = Enumerable.Range(0, 300).Select(i => "word" + i).ToArray();
        var step = TimeSpan.FromTicks(TimeSpan.FromSeconds(1).Ticks / 300);

        for (var i = 0; i < words.Length; i++)
        {
            block.Markdown = string.Join(" ", words.Take(i + 1)); // text arriving in pieces, 300 times in 1 s
            clock.Advance(step);
        }
        clock.Advance(block.RenderInterval);
        UiHelpers.Pump();

        Assert.InRange(block.RenderCount, 2, 11);
        Assert.Equal(string.Join(" ", words), Shown(block));

        // after a quiet moment the next piece shows at once
        clock.Advance(TimeSpan.FromSeconds(1));
        var before = block.RenderCount;
        block.Markdown += " tail";
        Assert.Equal(before + 1, block.RenderCount);
        window.Close();
    }

    [AvaloniaFact]
    public void Replaced_text_renders_at_once()
    {
        var clock = new FakeClock();
        var block = new MarkdownBlock { Clock = clock };
        var window = UiHelpers.Show(block);

        block.Markdown = "first message";
        block.Markdown = "first message, growing"; // waits for the interval
        Assert.Equal(1, block.RenderCount);

        block.Markdown = "another message"; // a recycled block shows its new item without waiting
        UiHelpers.Pump();
        Assert.Equal(2, block.RenderCount);
        Assert.Equal("another message", Shown(block));

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(2, block.RenderCount); // nothing left to draw
        window.Close();
    }

    [AvaloniaFact]
    public void Empty_and_odd_markdown_never_throw()
    {
        var block = new MarkdownBlock { Clock = new FakeClock() };
        var window = UiHelpers.Show(block);
        foreach (var text in new[] { null, "", "```", "| a |\n|", "![image](https://example.com/x.png)", "<b>\u0000</b>", "# \n#\n- \n1. " })
        {
            block.Markdown = text;
            UiHelpers.Pump();
        }
        block.Markdown = null;
        Assert.Null(block.Content);
        window.Close();
    }

    [AvaloniaFact]
    public void Tables_settle_with_or_without_the_theme()
    {
        const string table = "| Item | Description |\n|------|-------------|\n| Title | Top-level heading |\n| Code | One-line Python snippet |";

        // the engine's tables used to resize their columns after every layout pass: "Infinite layout loop detected"
        var alone = new MarkdownBlock { Markdown = table };
        UiHelpers.Show(alone).Close();

        var c = new ChatConversation();
        c.Items.Add(new AssistantTextItem { Text = table });
        var view = new ClaudeChatView { Conversation = c };
        var window = UiHelpers.Show(view);
        Assert.Contains(view.GetVisualDescendants().OfType<CTextBlock>(), t => t.Text == "Top-level heading");
        window.Close();
    }

    private static Grid Table(MarkdownBlock block) =>
        block.GetLogicalDescendants().OfType<Grid>().Single(g => g.Classes.Contains("Table"));

    [AvaloniaFact]
    public void A_table_drawn_while_hidden_gets_its_columns_when_shown()
    {
        const string table = "| Item | Description |\n|------|-------------|\n| Title | Top-level heading |";
        var clock = new FakeClock();
        var block = new MarkdownBlock { Clock = clock, Markdown = "Let me check." };
        var tab = new Border { Child = block }; // a tab of the host
        var window = UiHelpers.Show(tab);

        tab.IsVisible = false; // the user went to another tab
        UiHelpers.Pump();
        block.Markdown = table; // and the answer arrived meanwhile
        UiHelpers.Pump();
        Assert.All(Table(block).ColumnDefinitions, c => Assert.Equal(0, c.Width.Value)); // the engine sized it at 0

        tab.IsVisible = true;
        UiHelpers.Pump();
        clock.Advance(block.RenderInterval);
        UiHelpers.Pump();

        // sized while hidden, the columns are "0*": 1 px wide once shown, one letter per line
        var grid = Table(block);
        Assert.All(grid.ColumnDefinitions, c => Assert.True(c.ActualWidth > 10, $"column of {c.ActualWidth} px"));
        Assert.All(grid.Children, cell => Assert.True(cell.Bounds.Width > 10, $"cell of {cell.Bounds.Width} px"));
        window.Close();
    }

    [AvaloniaFact]
    public void A_table_is_redrawn_when_the_width_changes_but_not_otherwise()
    {
        var clock = new FakeClock();
        var block = new MarkdownBlock { Clock = clock, Markdown = "| a | b |\n|---|---|\n| one | two |" };
        var plain = new MarkdownBlock { Clock = clock, Markdown = "no table here" };
        var panel = new StackPanel { Children = { block, plain } };
        var window = UiHelpers.Show(panel);
        clock.Advance(TimeSpan.FromSeconds(1));
        UiHelpers.Pump();
        var (tableRenders, plainRenders) = (block.RenderCount, plain.RenderCount);

        clock.Advance(TimeSpan.FromSeconds(1)); // nothing changed: nothing to draw
        UiHelpers.Pump();
        Assert.Equal(tableRenders, block.RenderCount);

        window.Width = 500;
        UiHelpers.Pump();
        clock.Advance(block.RenderInterval);
        UiHelpers.Pump();
        Assert.Equal(tableRenders + 1, block.RenderCount);
        Assert.Equal(plainRenders, plain.RenderCount); // only a table depends on the width
        Assert.All(Table(block).ColumnDefinitions, c => Assert.True(c.ActualWidth > 10, $"column of {c.ActualWidth} px"));

        clock.Advance(TimeSpan.FromSeconds(1));
        UiHelpers.Pump();
        Assert.Equal(tableRenders + 1, block.RenderCount); // it settles at the new width
        window.Close();
    }

    [AvaloniaFact]
    public void The_engine_never_downloads_reads_files_or_starts_processes()
    {
        // the engine's defaults would: DefaultPathResolver (HttpClient, File.OpenRead), DefaultHyperlinkCommand (Process.Start)
        var engine = MarkdownBlock.Engine;
        Assert.Same(MarkdownBlock.NoLinks.Instance, engine.HyperlinkCommand);
        Assert.IsType<MarkdownBlock.NoImages>(engine.Plugins.PathResolver);

        var block = new MarkdownBlock { Clock = new FakeClock(), Markdown = "![x](https://example.invalid/a.png) and [docs](https://example.invalid)" };
        var window = UiHelpers.Show(block);

        var paragraph = Assert.Single(block.GetVisualDescendants().OfType<CTextBlock>()); // not the raw-text fallback
        var image = Assert.Single(paragraph.Content.OfType<CImage>());
        Assert.NotNull(image.Task);
        // done at once, with nothing: the default resolver would still be fetching it, or would have failed on the network
        Assert.Equal(TaskStatus.RanToCompletion, image.Task!.Status);
        Assert.Null(image.Task.Result);

        var link = Assert.Single(paragraph.Content.OfType<CHyperlink>());
        link.Command!("https://example.invalid"); // safe: the engine's command is NoLinks (checked above)
        window.Close();
    }

    [AvaloniaFact]
    public void Code_uses_the_mono_font_on_the_code_backgrounds()
    {
        var c = new ChatConversation();
        c.Items.Add(new AssistantTextItem { Text = "Run `dotnet test`:\n\n```\ndotnet test\n```" });
        var view = new ClaudeChatView { Conversation = c };
        var window = UiHelpers.Show(view);
        view.TryFindResource("ClaudeChat.MonoFontFamily", out var mono);
        view.TryFindResource("ClaudeChat.CodeBlockBackground", out var block);
        view.TryFindResource("ClaudeChat.CodeBackground", out var chip);

        var code = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("CodeBlock"));
        Assert.Equal("dotnet test", code.Text);
        Assert.Equal(mono, code.FontFamily);
        var box = view.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("CodeBlock"));
        Assert.Same(block, box.Background); // textCodeBlock.background
        var scroller = view.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Classes.Contains("CodeBlock"));
        Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, scroller.VerticalScrollBarVisibility);

        var inline = view.GetVisualDescendants().OfType<CTextBlock>().SelectMany(t => t.Content).OfType<CCode>().Single();
        Assert.Equal(mono, inline.MonospaceFontFamily);
        Assert.Same(chip, inline.Background); // textPreformat.background
        window.Close();
    }

    // ---- code blocks: "Copy code", streaming, no colors ----

    private const string Answer = "Run it:\n\n```cs\nvar x = 1;\nvar y = 2;\n```\n\nDone.";

    private static (ClaudeChatView View, Window Window, AssistantTextItem Item) OpenAnswer(string text, bool touch = false)
    {
        var c = new ChatConversation();
        var item = new AssistantTextItem { Text = text };
        c.Items.Add(item);
        var view = touch ? UiHelpers.TouchView(c) : new ClaudeChatView { Conversation = c };
        return (view, touch ? UiHelpers.ShowPhone(view) : UiHelpers.Show(view), item);
    }

    private static Border CodeBox(Visual root) =>
        root.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("CodeBlock"));

    private static CodeCopyButton CopyButtonOf(Visual root) => root.GetVisualDescendants().OfType<CodeCopyButton>().Single();

    private static Path IconOf(Button button) => button.GetVisualDescendants().OfType<Path>().Single();

    /// <summary>Runs frames until <paramref name="done"/>, for what a transition of 0.15 s brings (5 s at most).</summary>
    private static void FadeUntil(Func<bool> done)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && clock.Elapsed < TimeSpan.FromSeconds(5))
        {
            System.Threading.Thread.Sleep(20);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            UiHelpers.Pump();
        }
        Assert.True(done());
    }

    private static void MoveTo(Window window, Control control, double x, double y)
    {
        window.MouseMove(control.TranslatePoint(new Point(x, y), window)!.Value, RawInputModifiers.None);
        UiHelpers.Pump();
    }

    [AvaloniaFact]
    public void A_code_block_has_a_copy_button_that_copies_its_text_and_shows_a_check()
    {
        var copied = new List<string>();
        var accept = true;
        ChatClipboard.Override = text => { copied.Add(text); return accept ? Task.CompletedTask : throw new InvalidOperationException("refused"); };
        try
        {
            var (view, window, _) = OpenAnswer(Answer);
            var button = CopyButtonOf(view);

            // like the extension's: a 14 px icon, 4 px inside, the editor's background, a 1 px border, 4 px corners
            Assert.Equal(new Thickness(4), button.Padding);
            Assert.Equal(new Thickness(1), button.BorderThickness);
            Assert.Equal(new CornerRadius(4), button.CornerRadius);
            Assert.Equal(new Size(14, 14), Assert.IsType<Viewbox>(button.Content).Bounds.Size);
            view.TryFindResource("ClaudeChat.ToolBackground", out var editor);
            view.TryFindResource("ClaudeChat.ForegroundMuted", out var muted);
            Assert.Same(editor, button.Background);
            Assert.Same(muted, button.Foreground);
            Assert.Equal("Copy code", ToolTip.GetTip(button));
            Assert.Equal("Copy code to clipboard", AutomationProperties.GetName(button));
            var icon = IconOf(button);
            Assert.Equal(icon.FindResource("ClaudeChat.Icon.Copy"), icon.Data);

            accept = false;                                    // a copy that failed shows nothing
            UiHelpers.Click(window, button);
            Assert.False(button.IsCopied);
            Assert.False(button.IsTicking);

            accept = true;
            UiHelpers.Click(window, button);
            Assert.Equal(new[] { "var x = 1;\nvar y = 2;", "var x = 1;\nvar y = 2;" }, copied);   // the code, not the fences or the text around
            Assert.True(button.IsCopied);
            Assert.True(button.IsTicking);                     // a timer of 2 s takes the check away
            Assert.Equal(icon.FindResource("ClaudeChat.Icon.Check"), icon.Data);
            Assert.Equal("Copied", AutomationProperties.GetName(button));

            button.EndCopied();                                // 2 s later
            UiHelpers.Pump();
            Assert.False(button.IsTicking);
            Assert.Equal(icon.FindResource("ClaudeChat.Icon.Copy"), icon.Data);
            Assert.Equal("Copy code to clipboard", AutomationProperties.GetName(button));

            view.Strings = ChatStrings.PortugueseBrazil;       // the texts follow the language
            UiHelpers.Pump();
            Assert.Equal("Copiar código", ToolTip.GetTip(button));
            Assert.Equal("Copiar código para a área de transferência", AutomationProperties.GetName(button));
            window.Close();
        }
        finally { ChatClipboard.Override = null; }
    }

    [AvaloniaFact]
    public void Every_kind_of_code_block_gets_a_button_with_its_own_text()
    {
        var copied = new List<string>();
        ChatClipboard.Override = text => { copied.Add(text); return Task.CompletedTask; };
        try
        {
            // fenced with a language, fenced without one, and indented (its four spaces come off)
            var (view, window, _) = OpenAnswer("```js\nlet a;\n```\n\nA text.\n\n```\nplain\n```\n\nMore text.\n\n    indented\n      deeper\n");
            var buttons = view.GetVisualDescendants().OfType<CodeCopyButton>().ToList();
            Assert.Equal(3, buttons.Count);
            foreach (var button in buttons) UiHelpers.Click(window, button);
            Assert.Equal(new[] { "let a;", "plain", "indented\n  deeper" }, copied);
            Assert.Equal(new[] { "let a;", "plain", "indented\n  deeper" },
                view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("CodeBlock")).Select(t => t.Text));
            window.Close();
        }
        finally { ChatClipboard.Override = null; }
    }

    [AvaloniaFact]
    public void The_copy_button_stays_at_the_corner_while_the_code_scrolls_sideways()
    {
        var (view, window, _) = OpenAnswer("```\n" + new string('x', 600) + "\n```");
        var box = CodeBox(view);
        var button = CopyButtonOf(view);
        var corner = button.TranslatePoint(new Point(button.Bounds.Width, 0), box)!.Value;
        Assert.Equal(box.Bounds.Width - 4, corner.X, 1);       // 4 px from the top right corner
        Assert.Equal(4, corner.Y, 1);
        Assert.InRange(button.Bounds.Width, 23, 25);           // about 24 px square
        Assert.InRange(button.Bounds.Height, 23, 25);

        var scroller = view.GetVisualDescendants().OfType<ScrollViewer>().Single(x => x.Classes.Contains("CodeBlock"));
        Assert.True(scroller.Extent.Width > scroller.Viewport.Width);
        scroller.Offset = new Vector(100, 0);
        UiHelpers.Pump();
        Assert.Equal(100, scroller.Offset.X);
        Assert.Equal(corner, button.TranslatePoint(new Point(button.Bounds.Width, 0), box)!.Value); // the code moved, the button did not
        window.Close();
    }

    [AvaloniaFact]
    public void The_copy_button_shows_only_while_the_pointer_is_over_the_block()
    {
        ChatClipboard.Override = _ => Task.CompletedTask;      // the press below is a click
        var (view, window, _) = OpenAnswer(Answer);
        var box = CodeBox(view);
        var button = CopyButtonOf(view);
        Assert.Equal(0, button.Opacity);                       // clear until the pointer comes
        Assert.Contains(button.Transitions!, t => t is Avalonia.Animation.DoubleTransition d
            && d.Property == Visual.OpacityProperty && d.Duration == TimeSpan.FromSeconds(0.15));

        button.Focus(NavigationMethod.Tab);                    // reached with Tab: it shows, without the pointer
        FadeUntil(() => button.Opacity == 1);
        TopLevel.GetTopLevel(button)!.FocusManager!.ClearFocus();
        FadeUntil(() => button.Opacity == 0);

        MoveTo(window, box, 12, box.Bounds.Height / 2);        // over the block, away from the button
        FadeUntil(() => button.Opacity == 1);

        var face = button.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_ChatButton");
        MoveTo(window, button, 12, 12);
        view.TryFindResource("ClaudeChat.HoverBackground", out var hover);
        Assert.Same(hover, face.Background);                   // its own hover: toolbar.hoverBackground
        var on = button.TranslatePoint(new Point(12, 12), window)!.Value;
        window.MouseDown(on, MouseButton.Left, RawInputModifiers.None);
        FadeUntil(() => Math.Abs(button.Opacity - 0.7) < 0.001); // pressed
        window.MouseUp(on, MouseButton.Left, RawInputModifiers.None);

        window.MouseMove(new Point(window.Width - 2, window.Height - 2), RawInputModifiers.None); // away from the block
        FadeUntil(() => button.Opacity == 0);
        ChatClipboard.Override = null;
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_copy_button_is_a_target_that_copies_in_both_layouts(bool touch)
    {
        var copied = new List<string>();
        ChatClipboard.Override = text => { copied.Add(text); return Task.CompletedTask; };
        try
        {
            var (view, window, _) = OpenAnswer(Answer, touch);
            var button = CopyButtonOf(view);
            if (touch)
            {
                Assert.Equal(1, button.Opacity);               // no pointer to hover with: always shown
                Assert.True(button.Bounds.Width >= 44 && button.Bounds.Height >= 44, button.Bounds.ToString());
                var face = button.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_ChatButton");
                Assert.Equal(new Size(24, 24), face.Bounds.Size); // the face is the icon's, the target a finger's
            }
            else
            {
                Assert.Equal(0, button.Opacity);
                Assert.True(button.Bounds.Height < 44);
            }
            // a tap on the middle of its target and (touch) near its corners, which the face does not reach
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
            Assert.All(copied, t => Assert.Equal("var x = 1;\nvar y = 2;", t));
            window.Close();
        }
        finally { ChatClipboard.Override = null; }
    }

    [AvaloniaFact]
    public void An_unclosed_fence_while_streaming_is_drawn_as_code()
    {
        var (view, window, _) = OpenAnswer("Text\n```cs\nvar x = 1;");
        var code = Assert.Single(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Classes.Contains("CodeBlock"));
        Assert.Equal("var x = 1;", code.Text!.TrimEnd('\n'));
        CodeBox(view);                                         // a CodeBlock border exists, with its button
        CopyButtonOf(view);
        Assert.DoesNotContain(view.GetVisualDescendants().OfType<CTextBlock>(), t => t.Text.Contains("```"));  // the fence is not shown as text

        window.Close();

        // and the closing fence arrives, in a block drawn on a clock of the test's
        var clock = new FakeClock();
        var block = new MarkdownBlock { Clock = clock, Markdown = "Text\n```cs\nvar x = 1;" };
        window = UiHelpers.Show(block);
        Assert.Single(block.GetVisualDescendants().OfType<CodeCopyButton>());
        block.Markdown = "Text\n```cs\nvar x = 1;\nvar y = 2;\n```\n\nDone.";
        clock.Advance(TimeSpan.FromSeconds(1));
        UiHelpers.Pump();
        code = Assert.Single(block.GetVisualDescendants().OfType<TextBlock>(), t => t.Classes.Contains("CodeBlock"));
        Assert.Equal("var x = 1;\nvar y = 2;", code.Text);
        Assert.Contains("Done.", Shown(block));
        window.Close();
    }

    [AvaloniaFact]
    public void The_engine_pre_renders_open_blocks_and_changes_nothing_else_in_its_setup()
    {
        var info = MarkdownBlock.Engine.Plugins.Info;
        Assert.True(info.EnablePreRenderingCodeBlock);
        // only that was switched on: the package's plugin for chat answers would also turn off notes, rules and lists
        Assert.True(info.EnableRuleExt);
        Assert.True(info.EnableListMarkerExt);
        var block = new MarkdownBlock { Clock = new FakeClock(), Markdown = "```\nopen" };
        var window = UiHelpers.Show(block);
        Assert.Single(block.GetVisualDescendants().OfType<CodeCopyButton>());
        window.Close();
    }

    [AvaloniaFact]
    public void Code_stays_in_the_text_color()
    {
        var (view, window, _) = OpenAnswer("Some text.\n\n```cs\nvar x = 1;\n```");
        var code = view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("CodeBlock"));
        var paragraph = view.GetVisualDescendants().OfType<CTextBlock>().Single();
        Assert.True(code.Inlines == null || code.Inlines.Count == 0); // no colored runs
        Assert.Equal("var x = 1;", code.Text);                 // no language label
        Assert.IsType<SolidColorBrush>(code.Foreground);
        Assert.Equal(((ISolidColorBrush)paragraph.Foreground!).Color, ((ISolidColorBrush)code.Foreground!).Color);
        window.Close();
    }
}
