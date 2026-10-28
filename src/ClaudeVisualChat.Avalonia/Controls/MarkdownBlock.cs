using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Threading;
using Markdown.Avalonia;
using Markdown.Avalonia.Controls;
using Markdown.Avalonia.Utils;
using MarkdownEngine = Markdown.Avalonia.Markdown;

namespace ClaudeVisualChat.Controls;

/// <summary>Time source of <see cref="MarkdownBlock"/>; a test swaps it for a fake one.</summary>
internal interface IRenderClock
{
    TimeSpan Now { get; }

    /// <summary>Runs <paramref name="action"/> once, after <paramref name="delay"/>; disposing cancels it.</summary>
    IDisposable Schedule(TimeSpan delay, Action action);
}

/// <summary>
/// Markdown drawn in place, without a scroll bar of its own, so it can sit in a scrolling list. While the text grows
/// (Claude's answer arriving in pieces) it is drawn at most once per <see cref="RenderInterval"/>, and the last text
/// is always drawn. Images are not loaded and links open nothing: the block never touches the network, the disk or
/// other processes.
/// </summary>
public class MarkdownBlock : ContentControl
{
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownBlock, string?>(nameof(Markdown));

    public static readonly StyledProperty<TimeSpan> RenderIntervalProperty =
        AvaloniaProperty.Register<MarkdownBlock, TimeSpan>(nameof(RenderInterval), TimeSpan.FromMilliseconds(100));

    /// <summary>The view's layout, which the touch styles of the "Copy code" button of a code block follow (<c>:touch</c>).</summary>
    private static readonly StyledProperty<ChatLayout> LayoutProperty =
        ClaudeChatView.LayoutProperty.AddOwner<MarkdownBlock>();

    [ThreadStatic]
    private static MarkdownEngine? t_engine;

    private string? _rendered;
    private bool _hasRendered;
    private TimeSpan _lastRender;
    private IDisposable? _pending;

    /// <summary>The drawn markdown has tables, whose columns depend on the width.</summary>
    private bool _hasTables;

    /// <summary>The width the tables were sized for (0 when hidden); null until the layout after drawing.</summary>
    private double? _tableWidth;

    static MarkdownBlock()
    {
        // a template of its own, so the block shows with any theme (or none)
        TemplateProperty.OverrideDefaultValue<MarkdownBlock>(new FuncControlTemplate<MarkdownBlock>((block, scope) =>
            new ContentPresenter
            {
                Name = "PART_ContentPresenter",
                [~ContentPresenter.ContentProperty] = block[~ContentProperty],
                [~ContentPresenter.PaddingProperty] = block[~PaddingProperty],
            }.RegisterInNameScope(scope)));
    }

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    /// <summary>Shortest time between two drawings while the text grows. 100 ms by default.</summary>
    public TimeSpan RenderInterval
    {
        get => GetValue(RenderIntervalProperty);
        set => SetValue(RenderIntervalProperty, value);
    }

    internal IRenderClock Clock { get; set; } = DispatcherClock.Instance;

    /// <summary>How many times the markdown was turned into controls.</summary>
    internal int RenderCount { get; private set; }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MarkdownProperty) OnMarkdownChanged();
        else if (change.Property == LayoutProperty) PseudoClasses.Set(":touch", GetValue(LayoutProperty) == ChatLayout.Touch);
    }

    private void OnMarkdownChanged()
    {
        var text = Markdown;
        if (_hasRendered && text == _rendered) return;
        if (_pending != null && Grows(text)) return; // the scheduled drawing will take the newest text

        var wait = _lastRender + RenderInterval - Clock.Now;
        if (!_hasRendered || !Grows(text) || wait <= TimeSpan.Zero)
        {
            // a first text, a different one (a recycled block showing another item) or a quiet moment: draw now
            Render();
            return;
        }
        RenderLater(wait);
    }

    /// <summary>Draws again within the same <see cref="RenderInterval"/> limit; the drawing takes the newest text.</summary>
    private void RenderLater(TimeSpan wait)
    {
        if (_pending != null) return;
        _pending = Clock.Schedule(wait > TimeSpan.Zero ? wait : TimeSpan.Zero, () =>
        {
            _pending = null;
            Render();
        });
    }

    /// <summary>
    /// A table fixes its columns from its width at the first layout after it is drawn. Drawn while hidden (the host's
    /// tab is not on screen), that width is 0 and every column ends up 1 px wide; drawn at one width and then shown at
    /// another, the columns no longer fit. So, while there are tables, the block is drawn again once it shows at a
    /// width other than the one the tables were sized for.
    /// </summary>
    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (!_hasTables) return;
        var width = IsEffectivelyVisible ? Bounds.Width : 0;
        if (_tableWidth == null)
        {
            _tableWidth = width; // the same layout pass in which the tables sized their columns
            return;
        }
        if (width > 0 && Math.Abs(width - _tableWidth.Value) > 1)
            RenderLater(_lastRender + RenderInterval - Clock.Now);
    }

    /// <summary>True when <paramref name="text"/> continues what is on screen (streaming), not a new text.</summary>
    private bool Grows(string? text) =>
        _hasRendered && text != null && _rendered != null && text.StartsWith(_rendered, StringComparison.Ordinal);

    private void CancelPending()
    {
        _pending?.Dispose();
        _pending = null;
    }

    private void Render()
    {
        CancelPending();
        var text = Markdown;
        _rendered = text;
        _hasRendered = true;
        _lastRender = Clock.Now;
        RenderCount++;
        var tables = false;
        Content = string.IsNullOrEmpty(text) ? null : Transform(text!, out tables);

        _tableWidth = null;
        if (tables != _hasTables)
        {
            if (tables) LayoutUpdated += OnLayoutUpdated;
            else LayoutUpdated -= OnLayoutUpdated;
            _hasTables = tables;
        }
    }

    private static Control Transform(string text, out bool tables)
    {
        tables = false;
        try
        {
            var control = Engine.Transform(text);
            tables = SettleTables(control);
            return control;
        }
        catch (Exception)
        {
            // the engine choked on something: show the text as it came
            return new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        }
    }

    /// <summary>
    /// A table sizes its columns after each layout pass and, unless told it may do it only once
    /// (<see cref="AutoScaleColumnDefinitions.IsEnabledProperty"/>, which the engine's own viewer sets in its styles),
    /// starts over every time: "Infinite layout loop detected". Returns true when there is a table.
    /// </summary>
    private static bool SettleTables(Control control)
    {
        var found = false;
        switch (control)
        {
            case Grid grid when grid.Classes.Contains(MarkdownEngine.TableClass):
                AutoScaleColumnDefinitions.SetIsEnabled(grid, true);
                return true;
            case Panel panel:
                foreach (var child in panel.Children) found |= SettleTables(child);
                break;
            case Decorator decorator when decorator.Child != null:
                found = SettleTables(decorator.Child);
                break;
            case ContentControl content when content.Content is Control inner:
                found = SettleTables(inner);
                break;
        }
        return found;
    }

    /// <summary>The markdown engine of this thread, set up so it never touches the network, the disk or processes.</summary>
    internal static MarkdownEngine Engine => t_engine ??= CreateEngine();

    private static MarkdownEngine CreateEngine() => new MarkdownEngine
    {
        Plugins = new MdAvPlugins
        {
            PathResolver = new NoImages(),
            Plugins = { new PreRenderCodeBlocks(), new CodeBlockOverride.Plugin() },
        },
        HyperlinkCommand = NoLinks.Instance,
    };

    /// <summary>Never finds an image: the default resolver would download it or read it from disk.</summary>
    internal sealed class NoImages : IPathResolver
    {
        public string? AssetPathRoot { set { } }

        public IEnumerable<string>? CallerAssemblyNames { set { } }

        public Task<Stream?>? ResolveImageResource(string relativeOrAbsolutePath) => null;
    }

    /// <summary>Clicking a link does nothing: the default command would start the browser.</summary>
    internal sealed class NoLinks : ICommand
    {
        public static readonly NoLinks Instance = new NoLinks();

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
        }
    }

    /// <summary>Real time, with a <see cref="DispatcherTimer"/> for the drawing left for later.</summary>
    private sealed class DispatcherClock : IRenderClock
    {
        public static readonly DispatcherClock Instance = new DispatcherClock();

        private readonly Stopwatch _watch = Stopwatch.StartNew();

        public TimeSpan Now => _watch.Elapsed;

        public IDisposable Schedule(TimeSpan delay, Action action) =>
            DispatcherTimer.RunOnce(action, delay, DispatcherPriority.Normal);
    }
}
