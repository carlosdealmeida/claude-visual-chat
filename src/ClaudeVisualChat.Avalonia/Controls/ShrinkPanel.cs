using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// A column whose parts give way when there is not room for all of them, as the extension's flex columns with their
/// <c>flex-shrink</c> and <c>min-height</c>. Each part is measured with the whole height; where they come to more than there is,
/// the parts of the highest <see cref="GivesWayProperty"/> give height first, each down to its <see cref="MinShareProperty"/> (or
/// to what it wants, when that is less: a part never grows), then those of the next level, and so on; a part of 0 never gives
/// way. Parts of the same level share the cut by what each can give. Where even the minimums do not fit, the parts that give way
/// go on giving, in the same order, down to their <see cref="FloorProperty"/>, so that the parts of 0 are never cut. A part that
/// gave way is measured again with the height it was left, so that it scrolls (or clips) what it has more of. Then they are laid
/// out from the top down. A hidden part takes no room.
/// <para>
/// Inside a scroll, which measures it with no limit, the panel still wants all its parts' heights, but gives way to
/// <see cref="Room"/> (the scroll's viewport, which its owner hands it) and is laid out as high as its parts were left, so that
/// only what their floors leave over scrolls.
/// </para>
/// </summary>
internal sealed class ShrinkPanel : Panel
{
    /// <summary>When a part gives way: 0 (the default) never; else the higher, the sooner.</summary>
    public static readonly AttachedProperty<int> GivesWayProperty =
        AvaloniaProperty.RegisterAttached<ShrinkPanel, Control, int>("GivesWay");

    /// <summary>The least height a part that gives way keeps while the minimums fit (0 by default).</summary>
    public static readonly AttachedProperty<double> MinShareProperty =
        AvaloniaProperty.RegisterAttached<ShrinkPanel, Control, double>("MinShare");

    /// <summary>The least height a part that gives way keeps when even the minimums do not fit (0 by default).</summary>
    public static readonly AttachedProperty<double> FloorProperty =
        AvaloniaProperty.RegisterAttached<ShrinkPanel, Control, double>("Floor");

    /// <summary>The height to give way to inside a scroll; infinite (the default) is the height the panel is given.</summary>
    public static readonly StyledProperty<double> RoomProperty =
        AvaloniaProperty.Register<ShrinkPanel, double>(nameof(Room), double.PositiveInfinity);

    /// <summary>The height each part wants, and the one it was left, from the last measure.</summary>
    private double[] _natural = Array.Empty<double>(), _shares = Array.Empty<double>();

    /// <summary>How high the panel was laid out in a scroll the last time (see <see cref="ArrangeOverride"/>).</summary>
    private double _laidHeight = double.NaN;

    static ShrinkPanel()
    {
        AffectsParentMeasure<ShrinkPanel>(GivesWayProperty, MinShareProperty, FloorProperty);
        AffectsMeasure<ShrinkPanel>(RoomProperty);
    }

    /// <summary>See <see cref="RoomProperty"/>.</summary>
    public double Room
    {
        get => GetValue(RoomProperty);
        set => SetValue(RoomProperty, value);
    }

    public static int GetGivesWay(Control control) => control.GetValue(GivesWayProperty);

    public static void SetGivesWay(Control control, int value) => control.SetValue(GivesWayProperty, value);

    public static double GetMinShare(Control control) => control.GetValue(MinShareProperty);

    public static void SetMinShare(Control control, double value) => control.SetValue(MinShareProperty, value);

    public static double GetFloor(Control control) => control.GetValue(FloorProperty);

    public static void SetFloor(Control control, double value) => control.SetValue(FloorProperty, value);

    protected override Size MeasureOverride(Size availableSize)
    {
        var count = Children.Count;
        _natural = new double[count];
        double width = 0, total = 0;
        for (var i = 0; i < count; i++)
        {
            var child = Children[i];
            child.Measure(availableSize); // the whole height: each part says how much it would take
            if (!child.IsVisible) continue;
            _natural[i] = child.DesiredSize.Height;
            total += _natural[i];
        }
        _shares = Shares(Math.Min(availableSize.Height, Room));
        for (var i = 0; i < count; i++)
        {
            var child = Children[i];
            if (!child.IsVisible) continue;
            // a part that gave way is measured with what it was left: a scroll then scrolls, instead of keeping its whole height
            if (_shares[i] < _natural[i] - 0.01) child.Measure(new Size(availableSize.Width, _shares[i]));
            width = Math.Max(width, child.DesiredSize.Width);
        }
        // what the parts want: the one who lays the panel out may have more room for it than this measure was given
        return new Size(width, Math.Min(total, availableSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double y = 0;
        for (var i = 0; i < Children.Count && i < _shares.Length; i++)
        {
            Children[i].Arrange(new Rect(0, y, finalSize.Width, _shares[i]));
            y += _shares[i];
        }
        if (double.IsPositiveInfinity(Room)) return finalSize;
        // in a scroll, as high as the parts were left: the scroll goes no further than they do. The scroll reads that height when it
        // lays the panel out, so when the panel alone was laid out again (its room changed) the scroll must lay it out once more
        if (!y.Equals(_laidHeight)) (this.GetVisualParent() as Layoutable)?.InvalidateArrange();
        _laidHeight = y;
        return new Size(finalSize.Width, y);
    }

    /// <summary>The height each part is left with in <paramref name="room"/>, from what each wants.</summary>
    private double[] Shares(double room)
    {
        var count = Children.Count;
        var heights = (double[])_natural.Clone();
        var levels = new SortedSet<int>();
        double total = 0;
        for (var i = 0; i < count; i++)
        {
            total += heights[i];
            if (Children[i].IsVisible && GetGivesWay(Children[i]) > 0) levels.Add(GetGivesWay(Children[i]));
        }
        var excess = total - room;
        foreach (var least in new[] { MinShareProperty, FloorProperty }) // down to the minimums, then down to the floors
        {
            foreach (var level in levels.Reverse())
            {
                if (excess <= 0) return heights;
                var gives = new double[count];
                double canGive = 0;
                for (var i = 0; i < count; i++)
                {
                    var child = Children[i];
                    if (!child.IsVisible || GetGivesWay(child) != level) continue;
                    gives[i] = Math.Max(0, heights[i] - Math.Max(0, child.GetValue(least)));
                    canGive += gives[i];
                }
                if (canGive <= 0) continue;
                var taken = Math.Min(excess, canGive);
                for (var i = 0; i < count; i++) heights[i] -= gives[i] * taken / canGive;
                excess -= taken;
            }
        }
        return heights;
    }
}
