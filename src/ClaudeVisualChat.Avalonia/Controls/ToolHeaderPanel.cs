using System;
using Avalonia;
using Avalonia.Controls;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The line of a tool call: its pieces one after the other, each as wide as its text really is (not rounded up, as
/// the layout rounds a text block, so a piece starts where the browser would start it), except the second (the path
/// or the description), which gives up width, and is trimmed, when the line is too short. Hidden pieces take no room.
/// </summary>
internal sealed class ToolHeaderPanel : Panel
{
    private const int Shrinking = 1;

    protected override Size MeasureOverride(Size availableSize)
    {
        var fixedWidth = 0.0;
        var height = 0.0;
        var infinite = new Size(double.PositiveInfinity, availableSize.Height);
        for (var i = 0; i < Children.Count; i++)
        {
            if (i == Shrinking) continue;
            var child = Children[i];
            child.Measure(infinite);
            fixedWidth += RoomOf(child);
            height = Math.Max(height, child.DesiredSize.Height);
        }
        var total = fixedWidth;
        if (Children.Count > Shrinking)
        {
            var room = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : Math.Max(0, availableSize.Width - fixedWidth);
            var shrinking = Children[Shrinking];
            shrinking.Measure(new Size(room, availableSize.Height));
            total += RoomOf(shrinking);
            height = Math.Max(height, shrinking.DesiredSize.Height);
        }
        return new Size(Math.Ceiling(total), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var fixedWidth = 0.0;
        for (var i = 0; i < Children.Count; i++)
        {
            if (i != Shrinking) fixedWidth += RoomOf(Children[i]);
        }
        var x = 0.0;
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            var width = RoomOf(child);
            if (i == Shrinking) width = Math.Min(width, Math.Max(0, finalSize.Width - fixedWidth));
            // the block keeps its rounded-up size; only where the next one starts follows the text
            child.Arrange(new Rect(x, 0, Math.Max(width, Math.Min(child.DesiredSize.Width, finalSize.Width - x)), finalSize.Height));
            x += width;
        }
        return finalSize;
    }

    /// <summary>The room a piece takes: a text's own width and its margins; anything else, its size.</summary>
    private static double RoomOf(Control child)
    {
        if (!child.IsVisible) return 0;
        if (child is TextBlock text && text.TextLayout is { } layout)
            return layout.WidthIncludingTrailingWhitespace + text.Padding.Left + text.Padding.Right + text.Margin.Left + text.Margin.Right;
        return child.DesiredSize.Width;
    }
}
