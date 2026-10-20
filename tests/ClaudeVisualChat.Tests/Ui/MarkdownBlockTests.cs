using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
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
}
