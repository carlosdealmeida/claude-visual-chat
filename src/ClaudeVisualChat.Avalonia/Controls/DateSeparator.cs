using System;
using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The line that marks where the day changes in the conversation's list, like the extension's: the date in the middle ("Sat, Oct 25",
/// and the year outside the current one), cut with "…" when it does not fit, and a wavy rule on each side. It sits at the top of the
/// row of the first message of each day, whatever row that is (the user's message or an entry of the timeline), and shows only there:
/// the row's item has a time, and the nearest item before it with a time is from another day, or there is none (the first one). Only
/// the conversation's own list has them, not a subagent's steps, and none while the view hides the times
/// (<see cref="ClaudeChatView.ShowTimestamps"/>). The row's place comes from the list (<see cref="ChatItemList.IndexProperty"/>) and only
/// the items just before it are read, so a long list costs a new row no more than a short one.
/// </summary>
internal sealed class DateSeparator : Control
{
    /// <summary>A rule's height, and one wave of it: its line goes up and down across this width.</summary>
    internal const double WaveHeight = 7;
    internal const double WavePeriod = 14;

    /// <summary>The narrowest a rule gets: the date is cut before.</summary>
    internal const double MinRuleWidth = 32;

    /// <summary>The room between each rule and the date.</summary>
    internal const double Gap = 10;

    /// <summary>The room over and under the line.</summary>
    internal const double VerticalPadding = 8;

    /// <summary>The color of the date and of the rules.</summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<DateSeparator>();

    private static readonly StyledProperty<ChatStrings> StringsProperty = ClaudeChatView.StringsProperty.AddOwner<DateSeparator>();

    private static readonly StyledProperty<bool> ShowTimestampsProperty = ClaudeChatView.ShowTimestampsProperty.AddOwner<DateSeparator>();

    private readonly TextBlock _label = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };

    private bool _attached;
    private ChatItemList? _owner;
    private INotifyCollectionChanged? _items;
    private INotifyPropertyChanged? _item;
    private ClaudeChatView? _view;
    private Rect _leftRule;
    private Rect _rightRule;

    public DateSeparator()
    {
        _label.Classes.Add("ClaudeChatDateLabel");
        LogicalChildren.Add(_label);
        VisualChildren.Add(_label);
        IsVisible = false;
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>The date shown; null while the line is hidden.</summary>
    internal string? Date => IsVisible ? _label.Text : null;

    /// <summary>Whether it follows a list or an item (only while in the window).</summary>
    internal bool IsTracking => _items != null || _item != null;

    /// <summary>The date's text block.</summary>
    internal TextBlock Label => _label;

    /// <summary>Where the rules are drawn, in the line's own space.</summary>
    internal Rect LeftRule => _leftRule;

    internal Rect RightRule => _rightRule;

    /// <summary>
    /// The extension's wavy rule, in <paramref name="brush"/>, across <paramref name="area"/>: a line of 1 px going up and down around the
    /// area's middle, one wave every <see cref="WavePeriod"/> px from its left, cut at its right. Anything that draws such a rule (the
    /// date line, a divider) draws it with this.
    /// </summary>
    internal static void DrawWave(DrawingContext context, IBrush brush, Rect area)
    {
        if (area.Width <= 0 || area.Height <= 0) return;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            // each half wave is a quadratic curve whose control point is at the area's top, then at its bottom
            var middle = area.Top + area.Height / 2;
            var half = WavePeriod / 2;
            g.BeginFigure(new Point(area.Left, middle), false);
            var up = true;
            for (var x = area.Left; x < area.Right; x += half)
            {
                g.QuadraticBezierTo(new Point(x + half / 2, up ? area.Top : area.Bottom), new Point(x + half, middle));
                up = !up;
            }
            g.EndFigure(false);
        }
        using (context.PushClip(area))
            context.DrawGeometry(null, new Pen(brush, 1), geometry);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Track();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        Track();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_attached) Track();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ChatItemList.IndexProperty || change.Property == StringsProperty || change.Property == ShowTimestampsProperty)
        {
            if (_attached) Update();
        }
        else if (change.Property == ForegroundProperty)
        {
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Follows the list the row is in (only the conversation's own) and the row's item; out of the window, lets go of both and draws
    /// nothing: coming back draws the line as it is then.
    /// </summary>
    private void Track()
    {
        // a row whose item never has a time (a tool, a thought) has no line to draw, and follows nothing
        var owner = _attached && DataContext is UserMessageItem or AssistantTextItem ? this.FindAncestorOfType<ItemsControl>() as ChatItemList : null;
        var items = owner?.Items as INotifyCollectionChanged;
        if (!ReferenceEquals(items, _items))
        {
            if (_items != null) _items.CollectionChanged -= OnItemsChanged;
            _items = items;
            if (_items != null) _items.CollectionChanged += OnItemsChanged;
        }
        _owner = owner;
        var item = owner != null ? DataContext as INotifyPropertyChanged : null;
        if (!ReferenceEquals(item, _item))
        {
            if (_item != null) _item.PropertyChanged -= OnItemChanged;
            _item = item;
            if (_item != null) _item.PropertyChanged += OnItemChanged;
        }
        _view = owner != null ? this.FindAncestorOfType<ClaudeChatView>() : null;
        if (_attached) Update();
    }

    /// <summary>
    /// Rows added or taken out before this one move its place, which the list tells by itself; an item put in the place of another, or
    /// moved, or the whole list changed, may change the item before it.
    /// </summary>
    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Replace || e.Action == NotifyCollectionChangedAction.Move
            || e.Action == NotifyCollectionChangedAction.Reset)
            Update();
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UserMessageItem.Timestamp)) Update();
    }

    private void Update()
    {
        var date = DateOf();
        if (date != null) _label.Text = date;
        IsVisible = date != null;
    }

    /// <summary>The date line of this row, or null when it has none.</summary>
    private string? DateOf()
    {
        if (!GetValue(ShowTimestampsProperty)) return null;
        var items = _owner?.Items as IList;
        var index = GetValue(ChatItemList.IndexProperty);
        if (items == null || index < 0 || index >= items.Count || !ReferenceEquals(items[index], DataContext)) return null;
        if (!(TimeOf(DataContext) is DateTimeOffset time)) return null;
        var day = time.ToLocalTime().Date;
        // the nearest item before it with a time: the rows between (tools, thoughts) have none
        for (var i = index - 1; i >= 0; i--)
        {
            if (!(TimeOf(items[i]) is DateTimeOffset before)) continue;
            if (before.ToLocalTime().Date == day) return null;
            break;
        }
        var now = _view?.Conversation?.Clock() ?? DateTimeOffset.Now;
        return GetValue(StringsProperty).FormatDate(time, now);
    }

    /// <summary>When the item was sent or written; null for the items that have no time.</summary>
    internal static DateTimeOffset? TimeOf(object? item) => item switch
    {
        UserMessageItem user => user.Timestamp,
        AssistantTextItem answer => answer.Timestamp,
        _ => null,
    };

    protected override Size MeasureOverride(Size availableSize)
    {
        var rules = 2 * (MinRuleWidth + Gap);
        _label.Measure(new Size(Math.Max(0, availableSize.Width - rules), double.PositiveInfinity));
        var height = Math.Max(_label.DesiredSize.Height, WaveHeight) + 2 * VerticalPadding;
        var width = double.IsInfinity(availableSize.Width) ? _label.DesiredSize.Width + rules : availableSize.Width;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // the date takes the width of its text, less what the rules need at least; the rules share the rest
        var labelWidth = Math.Min(_label.DesiredSize.Width, Math.Max(0, finalSize.Width - 2 * (MinRuleWidth + Gap)));
        var labelHeight = _label.DesiredSize.Height;
        var middle = finalSize.Height / 2;
        var labelLeft = (finalSize.Width - labelWidth) / 2;
        _label.Arrange(new Rect(labelLeft, middle - labelHeight / 2, labelWidth, labelHeight));
        var ruleWidth = Math.Max(0, labelLeft - Gap);
        _leftRule = new Rect(0, middle - WaveHeight / 2, ruleWidth, WaveHeight);
        _rightRule = new Rect(finalSize.Width - ruleWidth, middle - WaveHeight / 2, ruleWidth, WaveHeight);
        return finalSize;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Foreground is not { } brush) return;
        DrawWave(context, brush, _leftRule);
        DrawWave(context, brush, _rightRule);
    }
}
