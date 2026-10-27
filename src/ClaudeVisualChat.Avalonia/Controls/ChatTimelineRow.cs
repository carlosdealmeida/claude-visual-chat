using System;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// One entry of the timeline, like the extension's: a dot on the left, the line that joins the dots of consecutive
/// entries, the time at the top right (an answer's) and the content. Where the line starts and ends depends on the
/// entries around this one, read from the list that holds it, so the row keeps watching that list. Pseudo-classes:
/// <c>:first</c> (first entry of its list), <c>:runStart</c>, <c>:runEnd</c> (no entry of the timeline before or
/// after) and <c>:stamped</c> (it shows a time). The dot takes its color from the classes <c>running</c>,
/// <c>succeeded</c>, <c>failed</c> and <c>rejected</c>. In the touch layout it takes <c>:touch</c> too, and the theme
/// draws no timeline then.
/// </summary>
internal sealed class ChatTimelineRow : ContentControl
{
    public static readonly StyledProperty<string?> StampProperty =
        AvaloniaProperty.Register<ChatTimelineRow, string?>(nameof(Stamp));

    private static readonly StyledProperty<ChatLayout> LayoutProperty =
        ClaudeChatView.LayoutProperty.AddOwner<ChatTimelineRow>();

    private ItemsControl? _owner;
    private INotifyCollectionChanged? _items;

    public ChatTimelineRow()
    {
        UpdatePlace();
    }

    /// <summary>The time shown at the top right; null or empty shows none.</summary>
    public string? Stamp
    {
        get => GetValue(StampProperty);
        set => SetValue(StampProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(ChatTimelineRow);

    /// <summary>
    /// Entries of the timeline: text, thinking, tool calls and what a command printed (drawn without a dot, the line
    /// passing by it); the rest (the user's message, notices) breaks a run.
    /// </summary>
    internal static bool IsTimeline(object? item) =>
        item is AssistantTextItem || item is ThinkingItem || item is ToolCallItem || item is CommandOutputItem;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StampProperty) PseudoClasses.Set(":stamped", !string.IsNullOrEmpty(Stamp));
        else if (change.Property == LayoutProperty) PseudoClasses.Set(":touch", GetValue(LayoutProperty) == ChatLayout.Touch);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _owner = this.FindAncestorOfType<ItemsControl>();
        _items = _owner?.Items;
        if (_items != null) _items.CollectionChanged += OnItemsChanged;
        UpdatePlace();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_items != null) _items.CollectionChanged -= OnItemsChanged;
        _items = null;
        _owner = null;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        UpdatePlace();
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdatePlace();

    /// <summary>Reads the entries before and after this one; outside a list, the row stands alone.</summary>
    private void UpdatePlace()
    {
        var items = _owner?.Items;
        var index = items == null || DataContext == null ? -1 : items.IndexOf(DataContext);
        var before = index > 0 && IsTimeline(items![index - 1]);
        var after = index >= 0 && index + 1 < items!.Count && IsTimeline(items[index + 1]);
        PseudoClasses.Set(":first", index == 0);
        PseudoClasses.Set(":runStart", !before);
        PseudoClasses.Set(":runEnd", !after);
    }
}
