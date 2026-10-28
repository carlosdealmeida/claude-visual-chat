using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The words of the line of a thought: "Thinking... · 1.2k tokens" while it arrives (the tokens once the claude counts them, climbing
/// to the count over a few seconds, see <see cref="TokenCountUp"/>), "Thought for 3s" once it is whole, and "Thinking" when no length
/// is known. While the count climbs a timer redraws the line every 100 ms; the timer runs only while the label is in the window, and
/// the count it shows is the item's, so a row of the list reused for another thought shows that thought's.
/// </summary>
internal sealed class ThinkingLabel : TextBlock
{
    /// <summary>How often the line is redrawn while the count climbs.</summary>
    internal static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);

    private static readonly StyledProperty<ChatStrings> StringsProperty = ClaudeChatView.StringsProperty.AddOwner<ThinkingLabel>();

    private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = Interval };

    private ThinkingItem? _tracked;
    private bool _attached;

    public ThinkingLabel()
    {
        _timer.Tick += (_, _) => Tick();
    }

    /// <summary>True while the timer waits to redraw the climbing count.</summary>
    internal bool IsTicking => _timer.IsEnabled;

    /// <summary>True while the label follows its item (only while in the window).</summary>
    internal bool IsTracking => _tracked != null;

    private ChatStrings Strings => GetValue(StringsProperty) ?? ChatStrings.English;

    /// <summary>Styled as a TextBlock: the theme's <c>TextBlock.ClaudeChatThinkingLabel</c>.</summary>
    protected override Type StyleKeyOverride => typeof(TextBlock);

    /// <summary>The words of the line for <paramref name="item"/> at <paramref name="nowMs"/> of the clock of <see cref="TokenCountUp.NowMs"/>.</summary>
    internal static string TextOf(ThinkingItem item, ChatStrings strings, long nowMs)
    {
        if (item.IsStreaming)
        {
            var shown = item.EstimatedTokens == null ? 0 : item.DisplayedTokens(nowMs);
            return shown > 0 ? strings.ThinkingNow + strings.FormatThinkingTokens(shown) : strings.ThinkingNow;
        }
        return item.DurationMs is long ms ? strings.FormatThoughtFor(ms) : strings.Thinking;
    }

    /// <summary>The timer's tick: the line is drawn again at the time it is now.</summary>
    internal void Tick() => Update();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Track(DataContext as ThinkingItem);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        Track(null); // the item outlives the view: let go of its events, and stop the timer
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_attached) Track(DataContext as ThinkingItem);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StringsProperty && _attached) Update();
    }

    private void Track(ThinkingItem? item)
    {
        if (!ReferenceEquals(item, _tracked))
        {
            if (_tracked != null) _tracked.PropertyChanged -= OnItemChanged;
            _tracked = item;
            if (_tracked != null) _tracked.PropertyChanged += OnItemChanged;
        }
        // out of the window, only the events and the timer go: Avalonia is taking the tree apart, and coming back draws it again
        if (_attached) Update();
        else _timer.Stop();
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e) => Update();

    private void Update()
    {
        var item = _tracked;
        if (item == null)
        {
            _timer.Stop();
            return;
        }
        var now = TokenCountUp.NowMs();
        Text = TextOf(item, Strings, now);
        var climbing = item.IsStreaming && item.EstimatedTokens != null && item.IsCountingUp(now);
        if (climbing) _timer.Start();
        else _timer.Stop();
    }
}
