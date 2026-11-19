using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The pieces of the reply box's desktop bar. Where they all fit, one line, as the dock panel the bar was drew it: the pieces at
/// the left one after the other in their order, 2 px apart, and the ones marked <see cref="RightProperty"/> at the right, side by
/// side, as docked at the right: the first of them at the right edge (so the children keep the order, and the Tab order, the dock
/// panel had). Where they do not, nothing is drawn over anything else: the pieces leave the first line in the order of
/// <see cref="KeepProperty"/>, the highest number first, and go to the lines under it, in the order they are seen in from left to
/// right, from the left, 2 px apart and 2 px under the line before. A piece with <see cref="KeepProperty"/> 0 never leaves the first line while those
/// pieces fit on it; in a bar narrower than that, every piece follows the one before it from the left, line after line. A piece
/// wider than a whole line has one to itself and is measured with its width, so that one that trims its text does so, and the bar
/// clips what is still too wide (only then: otherwise nothing is clipped, as before). Whether a piece fits is decided with the
/// width it may come to take by itself (<see cref="IBarPieceWidth"/>), so that a clock that ticks cannot move the line breaks.
/// </summary>
internal sealed class ComposerBarPanel : Panel
{
    /// <summary>How long a piece stays on the first line: 0 always (while those fit), else the lower the longer.</summary>
    public static readonly AttachedProperty<int> KeepProperty =
        AvaloniaProperty.RegisterAttached<ComposerBarPanel, Control, int>("Keep");

    /// <summary>On the first line, the piece is at the right, with the others marked so.</summary>
    public static readonly AttachedProperty<bool> RightProperty =
        AvaloniaProperty.RegisterAttached<ComposerBarPanel, Control, bool>("Right");

    /// <summary>Between two pieces of a line, and between two lines.</summary>
    private const double Spacing = 2;

    /// <summary>The width of each piece as it would like it, taken before any is measured with less.</summary>
    private readonly Dictionary<Control, double> _natural = new Dictionary<Control, double>();

    /// <summary>The width each piece is given room for when the lines are decided: its own, or more while it may grow by itself.</summary>
    private readonly Dictionary<Control, double> _fit = new Dictionary<Control, double>();

    static ComposerBarPanel()
    {
        AffectsParentMeasure<ComposerBarPanel>(KeepProperty, RightProperty);
    }

    public static int GetKeep(Control control) => control.GetValue(KeepProperty);

    public static void SetKeep(Control control, int value) => control.SetValue(KeepProperty, value);

    public static bool GetRight(Control control) => control.GetValue(RightProperty);

    public static void SetRight(Control control, bool value) => control.SetValue(RightProperty, value);

    protected override Size MeasureOverride(Size availableSize)
    {
        _natural.Clear();
        _fit.Clear();
        foreach (var child in Children)
        {
            child.Measure(Size.Infinity);
            if (!child.IsVisible) continue;
            _natural[child] = child.DesiredSize.Width;
            _fit[child] = child is IBarPieceWidth grows ? Math.Max(child.DesiredSize.Width, grows.ReservedWidth) : child.DesiredSize.Width;
        }
        var plan = Plan(availableSize.Width);
        foreach (var piece in plan.Pieces)
        {
            if (piece.Squeezed) piece.Control.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        }
        return plan.Size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var squeezed = false;
        foreach (var piece in Plan(finalSize.Width).Pieces)
        {
            piece.Control.Arrange(piece.Rect);
            squeezed |= piece.Squeezed;
        }
        ClipToBounds = squeezed; // only a piece wider than its line can reach past the bar
        return finalSize;
    }

    /// <summary>Where each piece on screen goes in a bar <paramref name="width"/> wide, and the size of the bar.</summary>
    private Layout Plan(double width)
    {
        // the pieces on screen from left to right: those at the left in their order, then those at the right, the outermost last
        var shown = new List<Control>();
        var rightmostFirst = new List<Control>();
        foreach (var child in Children)
        {
            if (!child.IsVisible || !_natural.ContainsKey(child)) continue;
            (GetRight(child) ? rightmostFirst : shown).Add(child);
        }
        rightmostFirst.Reverse();
        shown.AddRange(rightmostFirst);

        var first = new HashSet<Control>();
        foreach (var piece in shown)
        {
            if (GetKeep(piece) == 0) first.Add(piece);
        }
        if (LineWidth(first, Fit) <= width)
        {
            // the others join the first line in the order they are kept, as long as each fits; the first that does not ends it
            var candidates = shown.FindAll(p => GetKeep(p) > 0);
            candidates.Sort((a, b) => GetKeep(a).CompareTo(GetKeep(b)));
            foreach (var piece in candidates)
            {
                first.Add(piece);
                if (LineWidth(first, Fit) <= width) continue;
                first.Remove(piece);
                break;
            }
        }
        else
        {
            first.Clear(); // not even those fit: every piece follows the one before it
        }

        var layout = new Layout();
        double y = 0;
        if (first.Count > 0)
        {
            var height = 0.0;
            foreach (var piece in first) height = Math.Max(height, piece.DesiredSize.Height);
            double x = 0;
            var left = true;
            foreach (var piece in shown)
            {
                if (!first.Contains(piece) || GetRight(piece)) continue;
                if (!left) x += Spacing;
                layout.Add(piece, new Rect(x, 0, Natural(piece), height), false);
                x += Natural(piece);
                left = false;
            }
            var right = double.IsInfinity(width) ? LineWidth(first, Natural) : width;
            for (var i = shown.Count - 1; i >= 0; i--)
            {
                var piece = shown[i];
                if (!first.Contains(piece) || !GetRight(piece)) continue;
                right -= Natural(piece);
                layout.Add(piece, new Rect(right, 0, Natural(piece), height), false);
            }
            y = height;
            layout.Width = LineWidth(first, Natural);
        }

        // the rest, in the bar's order, line after line
        var line = new List<Control>();
        double lineWidth = 0;
        foreach (var piece in shown)
        {
            if (first.Contains(piece)) continue;
            if (line.Count > 0 && lineWidth + Spacing + Fit(piece) > width)
            {
                y = PlaceLine(layout, line, y, width);
                line.Clear();
                lineWidth = 0;
            }
            lineWidth += (line.Count > 0 ? Spacing : 0) + Fit(piece);
            line.Add(piece);
        }
        if (line.Count > 0) y = PlaceLine(layout, line, y, width);
        layout.Size = new Size(layout.Width, y);
        return layout;
    }

    /// <summary>One line under the first, from the left; a piece wider than the line is alone on it, measured with the line's width.</summary>
    private double PlaceLine(Layout layout, List<Control> line, double y, double width)
    {
        if (layout.Pieces.Count > 0) y += Spacing;
        var squeezed = line.Count == 1 && Natural(line[0]) > width;
        var height = 0.0;
        foreach (var piece in line) height = Math.Max(height, piece.DesiredSize.Height);
        double x = 0;
        foreach (var piece in line)
        {
            if (x > 0) x += Spacing;
            var pieceWidth = squeezed ? Math.Min(piece.DesiredSize.Width, width) : Natural(piece);
            layout.Add(piece, new Rect(x, y, pieceWidth, height), squeezed);
            x += pieceWidth;
        }
        layout.Width = Math.Max(layout.Width, Math.Min(x, width));
        return y + height;
    }

    private double Natural(Control piece) => _natural.TryGetValue(piece, out var width) ? width : 0;

    private double Fit(Control piece) => _fit.TryGetValue(piece, out var width) ? width : 0;

    /// <summary>
    /// What the pieces take on one line, each <paramref name="widthOf"/> wide: those at the left 2 px apart, those at the right against them.
    /// </summary>
    private static double LineWidth(HashSet<Control> pieces, Func<Control, double> widthOf)
    {
        double left = 0, right = 0;
        var count = 0;
        foreach (var piece in pieces)
        {
            if (GetRight(piece))
            {
                right += widthOf(piece);
            }
            else
            {
                left += widthOf(piece);
                count++;
            }
        }
        return left + Math.Max(0, count - 1) * Spacing + right;
    }

    private sealed class Layout
    {
        public List<(Control Control, Rect Rect, bool Squeezed)> Pieces { get; } = new List<(Control, Rect, bool)>();

        public double Width { get; set; }

        public Size Size { get; set; }

        public void Add(Control control, Rect rect, bool squeezed) => Pieces.Add((control, rect, squeezed));
    }
}

/// <summary>A piece of the bar whose width changes by itself (the cache clock's minutes as they go by).</summary>
internal interface IBarPieceWidth
{
    /// <summary>The widest it may come to be without anything else changing; the bar keeps room for that when it decides its lines.</summary>
    double ReservedWidth { get; }
}
