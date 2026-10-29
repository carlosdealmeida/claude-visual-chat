using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The second line of a live subagent's row in a fold of the Focus view (its <see cref="FocusAgentRow"/> is its data): "1.2k tokens · 3
/// tools · 1m 5s", the time from the agent's start by the conversation's clock, drawn again every second, like the extension's. The clock
/// runs only while the line is in the window and has a time to show; out of it, it stops, and coming back draws the time as it is then.
/// </summary>
internal sealed class FocusAgentStats : TextBlock
{
    /// <summary>How often the time is drawn again.</summary>
    internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private static readonly StyledProperty<ChatStrings> StringsProperty = ClaudeChatView.StringsProperty.AddOwner<FocusAgentStats>();

    private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = Interval };
    private bool _attached;
    private ClaudeChatView? _view;

    public FocusAgentStats()
    {
        _timer.Tick += (_, _) => Tick();
    }

    /// <summary>True while the clock runs.</summary>
    internal bool IsTicking => _timer.IsEnabled;

    /// <summary>The clock's tick: the time is drawn again.</summary>
    internal void Tick() => Update();

    /// <summary>Styled as a TextBlock: the theme's <c>TextBlock.ClaudeChatFocusAgentStats</c>.</summary>
    protected override Type StyleKeyOverride => typeof(TextBlock);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        _view = this.FindAncestorOfType<ClaudeChatView>();
        Update();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        _view = null;
        _timer.Stop(); // out of the window, only the timer goes
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_attached) Update();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StringsProperty && _attached) Update();
    }

    /// <summary>The line as it is now; the clock runs while there is a time in it.</summary>
    private void Update()
    {
        var row = DataContext as FocusAgentRow;
        var now = _view?.Conversation?.Clock() ?? DateTimeOffset.Now;
        var text = row == null ? "" : (GetValue(StringsProperty) ?? ChatStrings.English).FormatFocusAgentStats(row, now);
        Text = text;
        IsVisible = text.Length > 0;
        var ticks = _attached && row is { Starts.Count: > 0 };
        if (ticks && !_timer.IsEnabled) _timer.Start();
        else if (!ticks) _timer.Stop();
    }
}
