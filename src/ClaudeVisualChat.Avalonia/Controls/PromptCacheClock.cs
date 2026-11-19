using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Threading;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The prompt cache clock of the reply box's bar, like the extension's: a clock icon and the minutes the cache has
/// left ("59m", rounded up); once it has likely expired, or after a compaction, only the icon, in the error color
/// (class <c>cold</c>). The tooltip says the same in words. Hidden, taking no room, until the claude reports cache
/// use. It is redone every second while it shows (the timer keeps running while the clock is in a window with data,
/// and a second with the clock hidden, under a hidden ancestor, does nothing); the minutes never go below 1 while warm
/// nor above the cache's lifetime, whatever the clock says. In the bar it keeps room for the widest it can be
/// (<see cref="ReservedWidth"/>), so that the minutes going by, or the cache going cold, never move the bar's line breaks.
/// </summary>
internal sealed class PromptCacheClock : Border, IBarPieceWidth
{
    private static readonly StyledProperty<ChatStrings> StringsProperty =
        ClaudeChatView.StringsProperty.AddOwner<PromptCacheClock>();

    private readonly TextBlock _minutes = new TextBlock();
    private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };

    private ChatConversation? _conversation;
    private ChatConversation? _tracked;
    private bool _attached;

    private readonly Viewbox _iconBox;

    /// <summary>The padding while warm (the theme's 8 px after the minutes); the cold clock has none, and may be warm again.</summary>
    private Thickness _warmPadding = new Thickness(0, 0, 8, 0);

    /// <summary>
    /// A text block like the minutes, never on screen: each text the minutes can show is measured with it, by the text block's own
    /// measure, unrounded, and then rounded as the minutes are.
    /// </summary>
    private readonly TextBlock _probe = new TextBlock { UseLayoutRounding = false };

    /// <summary>The widest minutes, rounded, for the font, the texts, the lifetime and the scale they were measured with.</summary>
    private (string Key, double Width)? _widest;

    /// <summary>The widest the clock has been for that same key: room it has needed once, it keeps.</summary>
    private (string Key, double Width)? _widestSeen;

    public PromptCacheClock()
    {
        var icon = new Path();
        icon.Classes.Add("ClaudeChatCacheClockIcon");
        var iconBox = _iconBox = new Viewbox { Child = icon };
        iconBox.Classes.Add("ClaudeChatCacheClockIcon");
        _minutes.Classes.Add("ClaudeChatCacheClockMinutes");
        Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { iconBox, _minutes } };
        _timer.Tick += (_, _) => Tick();
        IsVisible = false;
    }

    /// <summary>The conversation whose cache it shows.</summary>
    internal ChatConversation? Conversation
    {
        get => _conversation;
        set
        {
            _conversation = value;
            if (_attached) Track(value);
        }
    }

    /// <summary>True while the timer redoes the clock every second.</summary>
    internal bool IsTicking => _timer.IsEnabled;

    /// <summary>
    /// The timer's second: redoes the clock with the conversation's time while it can be seen; hidden (the reply box,
    /// or an ancestor of the view, hidden without leaving the window), nothing, and the next second after it shows
    /// again catches up.
    /// </summary>
    internal void Tick()
    {
        if (IsEffectivelyVisible) Update();
    }

    private ChatStrings Strings => GetValue(StringsProperty) ?? ChatStrings.English;

    /// <summary>
    /// The widest the clock can be in the bar, whatever it shows now: warm with the widest of the minutes it can show (from 1 to the
    /// cache's lifetime, an hour when it is not known), each measured by a text block in the minutes' own font and rounded as the
    /// minutes are, beside the icon as it is and with the warm padding; and never less than the widest it has been for the same
    /// font, texts, lifetime and scale. It does not depend on the minutes shown, so the minutes going by, or the cache going cold,
    /// leave it as it is.
    /// </summary>
    public double ReservedWidth
    {
        get
        {
            if (_minutes.IsVisible) _warmPadding = Padding; // the cold clock's padding is not the one it has warm
            var scale = LayoutHelper.GetLayoutScale(this);
            var most = MostMinutes();
            var strings = Strings;
            var key = string.Join("|", most, scale, _minutes.FontFamily, _minutes.FontSize, _minutes.FontWeight, _minutes.FontStyle,
                _minutes.FontStretch, _minutes.LetterSpacing, strings.FormatCacheMinutes(88), _iconBox.DesiredSize.Width, _warmPadding);
            if (_widest is not { } widest || widest.Key != key)
            {
                double text = 0;
                for (var minutes = 1; minutes <= most; minutes++) text = Math.Max(text, MinutesWidth(strings.FormatCacheMinutes(minutes), scale));
                var warm = _iconBox.DesiredSize.Width + text + _warmPadding.Left + _warmPadding.Right + BorderThickness.Left + BorderThickness.Right;
                widest = (key, LayoutHelper.RoundLayoutValueUp(warm, scale));
                _widest = widest;
            }
            var seen = _widestSeen is { } before && before.Key == key ? Math.Max(before.Width, DesiredSize.Width) : DesiredSize.Width;
            _widestSeen = (key, seen);
            return Math.Max(widest.Width, seen);
        }
    }

    /// <summary>The most minutes the clock can show: the cache's lifetime, an hour when it is not known.</summary>
    private int MostMinutes()
    {
        var ttl = _tracked?.PromptCacheTtl;
        var minutes = ttl is TimeSpan lifetime && lifetime > TimeSpan.Zero ? Math.Ceiling(lifetime.TotalMinutes) : 60;
        return (int)Math.Min(999, Math.Max(1, minutes));
    }

    /// <summary>How wide the minutes are with <paramref name="text"/>: the text block's measure, rounded up as the layout rounds it.</summary>
    private double MinutesWidth(string text, double scale)
    {
        _probe.FontFamily = _minutes.FontFamily;
        _probe.FontSize = _minutes.FontSize;
        _probe.FontWeight = _minutes.FontWeight;
        _probe.FontStyle = _minutes.FontStyle;
        _probe.FontStretch = _minutes.FontStretch;
        _probe.LetterSpacing = _minutes.LetterSpacing;
        _probe.Padding = _minutes.Padding;
        _probe.Text = text;
        _probe.Measure(Size.Infinity);
        var width = _probe.DesiredSize.Width + _minutes.Margin.Left + _minutes.Margin.Right;
        return LayoutHelper.RoundLayoutValueUp(width, scale);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Track(_conversation);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        Track(null); // the conversation outlives the view: let go of its events, and stop the timer
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StringsProperty) Update();
    }

    private void Track(ChatConversation? conversation)
    {
        if (!ReferenceEquals(conversation, _tracked))
        {
            if (_tracked != null) _tracked.PropertyChanged -= OnConversationChanged;
            _tracked = conversation;
            if (_tracked != null) _tracked.PropertyChanged += OnConversationChanged;
        }
        // out of the window, only the events and the timer go: Avalonia is taking the tree apart, and coming back
        // draws it again (and starts the timer)
        if (_attached) Update();
        else _timer.Stop();
    }

    private void OnConversationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatConversation.PromptCacheExpiresAt)
            || e.PropertyName == nameof(ChatConversation.PromptCacheTtl)
            || e.PropertyName == nameof(ChatConversation.IsPromptCacheCompacted))
            Update();
    }

    private void Update()
    {
        var conversation = _tracked;
        var expiresAt = conversation?.PromptCacheExpiresAt;
        IsVisible = expiresAt != null;
        if (conversation == null || expiresAt == null)
        {
            _timer.Stop();
            return;
        }

        var strings = Strings;
        var ttl = conversation.PromptCacheTtl;
        var now = conversation.Clock();
        var left = expiresAt.Value - now;
        string tip;
        string? minutesText = null;
        if (conversation.IsPromptCacheCompacted)
        {
            tip = strings.CacheCompacted;
        }
        else if (left > TimeSpan.Zero)
        {
            var minutes = MinutesLeft(left, ttl);
            minutesText = strings.FormatCacheMinutes(minutes);
            tip = strings.FormatCacheWarm(minutes);
        }
        else
        {
            // idle since the last request: the expiry less the cache's lifetime
            tip = strings.FormatCacheExpired(now - (expiresAt.Value - (ttl ?? TimeSpan.Zero)));
        }

        Classes.Set("cold", minutesText == null);
        _minutes.Text = minutesText;
        _minutes.IsVisible = minutesText != null;
        ToolTip.SetTip(this, tip);
        AutomationProperties.SetName(this, tip);

        // while it is in a window with data; a hidden ancestor tells no one when it shows again, so the timer goes on
        if (_attached) _timer.Start();
        else _timer.Stop();
    }

    /// <summary>
    /// The minutes left, rounded up as in the extension (30 s left is "1m"); never more than the lifetime, so a clock
    /// that went back does not show an hour and a half of a one-hour cache.
    /// </summary>
    internal static int MinutesLeft(TimeSpan left, TimeSpan? ttl)
    {
        var minutes = Math.Ceiling(left.TotalMinutes);
        if (ttl is TimeSpan lifetime && lifetime > TimeSpan.Zero) minutes = Math.Min(minutes, Math.Ceiling(lifetime.TotalMinutes));
        return (int)Math.Max(1, Math.Min(minutes, int.MaxValue));
    }
}
