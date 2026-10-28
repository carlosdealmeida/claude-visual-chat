using System;
using Avalonia;
using Avalonia.Controls;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The conversation's list, with a footer under its items (the "Working…" line), inside its own padding. The list
/// stays the scroll viewer's content, with its padding inside: its virtualization needs both.
/// </summary>
internal sealed class ChatItemList : ItemsControl
{
    public static readonly StyledProperty<object?> FooterProperty =
        AvaloniaProperty.Register<ChatItemList, object?>(nameof(Footer));

    /// <summary>
    /// The list's width (<c>Bounds.Width</c>) as a property of its own, for the touch layout's user bubble to take its share
    /// of (<c>$parent[ChatItemList].ListWidth</c>). A binding through <c>Bounds.Width</c> holds the boxed <c>Rect</c> only
    /// by a weak reference, which a collection drops while the bubble's style is off (the layout going back and forth): the
    /// binding then starts again with a "Null value" error.
    /// </summary>
    public static readonly StyledProperty<double> ListWidthProperty =
        AvaloniaProperty.Register<ChatItemList, double>(nameof(ListWidth));

    /// <summary>
    /// Where the item a row of this list holds is in the list: set on the row's container as the list prepares it and moved with it,
    /// and inherited by what the row holds, so a row reads its place (to compare itself with the item before it) without looking for
    /// its item in the list. -1 outside a row.
    /// </summary>
    public static readonly AttachedProperty<int> IndexProperty =
        AvaloniaProperty.RegisterAttached<ChatItemList, Control, int>("Index", -1, inherits: true);

    /// <summary>What shows under the items, shown by the template's <c>PART_Footer</c>.</summary>
    public object? Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }

    /// <summary>The list's width, 0 before its first layout.</summary>
    public double ListWidth => GetValue(ListWidthProperty);

    protected override Type StyleKeyOverride => typeof(ChatItemList);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty) SetValue(ListWidthProperty, Bounds.Width);
    }

    protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
    {
        container.SetValue(IndexProperty, index); // before the base hands the item to the row: the row reads its new place with it
        base.PrepareContainerForItemOverride(container, item, index);
    }

    protected override void ContainerIndexChangedOverride(Control container, int oldIndex, int newIndex)
    {
        base.ContainerIndexChangedOverride(container, oldIndex, newIndex);
        container.SetValue(IndexProperty, newIndex);
    }

    protected override void ClearContainerForItemOverride(Control container)
    {
        base.ClearContainerForItemOverride(container);
        container.ClearValue(IndexProperty);
    }
}

/// <summary>
/// The items (first child) over the footer (second child), the footer right under the items. The items keep all the
/// room the list gets, less the footer's: the virtualizing panel sees only what lies inside its own bounds, so a
/// panel ending right at its last item would never show a last item without height, or settle at its end.
/// </summary>
internal sealed class ChatListBody : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count < 2) return base.MeasureOverride(availableSize);
        var items = Children[0];
        var footer = Children[1];
        items.Measure(availableSize);
        footer.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        return new Size(Math.Max(items.DesiredSize.Width, footer.DesiredSize.Width),
            items.DesiredSize.Height + footer.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count < 2) return base.ArrangeOverride(finalSize);
        var items = Children[0];
        var footer = Children[1];
        var footerHeight = footer.DesiredSize.Height;
        items.Arrange(new Rect(0, 0, finalSize.Width, Math.Max(items.DesiredSize.Height, finalSize.Height - footerHeight)));
        footer.Arrange(new Rect(0, items.DesiredSize.Height, finalSize.Width, footerHeight));
        return finalSize;
    }
}
