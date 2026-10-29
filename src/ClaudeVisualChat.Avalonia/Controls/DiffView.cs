using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// A diff drawn like the extension's diff editor: side by side (the old text on the left, the new on the right, the
/// shorter side hatched) when wider than <see cref="SideBySideWidth"/>, else in one column (the old lines over the new
/// ones); once side by side, it goes back to one column only at <see cref="NarrowBackWidth"/> or less. Each changed line has
/// its sign in the gutter, the changed characters of a pair of lines are marked, the indentation shows its guides and a long
/// line wraps under its own indentation. In the list it is as tall as its lines and <see cref="BottomSpace"/> px more, at most
/// <see cref="MostHeight"/> px, with a fade when its lines are taller than that, and a line longer than
/// <see cref="ToolCallView.MaxLineChars"/> characters is cut; a layer covers it there (a click, Enter or Space,
/// <see cref="ExpandRequested"/>), which darkens under the pointer and shows "Click to expand" at its bottom right while the
/// pointer or the keyboard is on it. Whole (in the permission request and in the Diff View, which scroll) it shows every line and
/// every character, building the rows as they scroll into view, and has no layer. Colors, font and sizes come from the
/// <c>ClaudeChat.Diff*</c> and editor resources.
/// </summary>
internal sealed class DiffView : Panel
{
    /// <summary>Above this width the diff goes side by side, like the extension.</summary>
    internal const double SideBySideWidth = 900;

    /// <summary>Side by side, the diff goes back to one column only at this width or less (the extension's step back).</summary>
    internal const double NarrowBackWidth = 876;

    /// <summary>The tallest a diff gets in the list; its lines past this are faded out.</summary>
    internal const double MostHeight = 200;

    /// <summary>Room under the last line.</summary>
    internal const double BottomSpace = 20;

    /// <summary>The gutters: the left column of a side-by-side diff has a wider one.</summary>
    private const double WideGutter = 29;

    private const double NarrowGutter = 10;

    /// <summary>Room at the right that the editor keeps out of the wrapping.</summary>
    private const double WrapReserve = 14;

    /// <summary>The tab stops of the indentation guides, in spaces.</summary>
    private const int TabSize = 4;

    private static WriteableBitmap? s_hatchMask;

    private readonly IReadOnlyList<DiffLine> _diff;
    private readonly ChatStrings _strings;
    private readonly bool _whole;
    private readonly bool _alwaysSideBySide;
    private readonly bool _touch;
    private readonly Rectangle _fade = new Rectangle { IsHitTestVisible = false, Height = 30, VerticalAlignment = VerticalAlignment.Bottom };
    private readonly Border? _expand;
    private Control? _content;
    private bool? _sideBySide;
    private double _charWidth;

    /// <param name="diff">The lines to draw.</param>
    /// <param name="strings">The texts ("+N lines", "Click to expand").</param>
    /// <param name="whole">Every line, never cut at <see cref="MostHeight"/> and with no layer (for a place that scrolls).</param>
    /// <param name="alwaysSideBySide">Side by side at any width (the Diff View's).</param>
    /// <param name="touch">The touch layout: the pill says "Tap to expand".</param>
    public DiffView(IReadOnlyList<DiffLine> diff, ChatStrings strings, bool whole = false, bool alwaysSideBySide = false, bool touch = false)
    {
        _touch = touch;
        _diff = diff ?? throw new ArgumentNullException(nameof(diff));
        _strings = strings ?? ChatStrings.English;
        _whole = whole;
        _alwaysSideBySide = alwaysSideBySide;
        ClipToBounds = true;
        Classes.Add("ClaudeChatDiff");
        _fade.Classes.Add("ClaudeChatDiffFade");
        Children.Add(_fade);
        if (!whole)
        {
            _expand = ExpandLayer();
            Children.Add(_expand);
        }
    }

    /// <summary>Raised by a click, Enter or Space on the layer over a diff in the list: the view opens the Diff View.</summary>
    public event EventHandler? ExpandRequested;

    /// <summary>True once laid out side by side.</summary>
    internal bool IsSideBySide => _sideBySide == true;

    /// <summary>True when the lines are taller than the diff may get, so their end is faded out.</summary>
    internal bool IsCut { get; private set; }

    /// <summary>The layer over a diff in the list, which opens the Diff View; null for a whole diff.</summary>
    internal Border? ExpandControl => _expand;

    /// <summary>
    /// The layer over the whole diff: it takes the pointer, Enter and Space, and holds the pill "Click to expand" at its bottom right,
    /// which the theme shows while the pointer or the keyboard is on the layer (and always in the touch layout).
    /// </summary>
    private string ExpandText => _touch ? _strings.TapToExpand : _strings.ClickToExpand;

    private Border ExpandLayer()
    {
        var text = new TextBlock { Text = ExpandText };
        text.Classes.Add("ClaudeChatDiffExpandText");
        var pill = new Border { Child = text };
        pill.Classes.Add("ClaudeChatDiffExpandPill");
        var layer = new Border { Child = pill, Focusable = true, Cursor = new Cursor(StandardCursorType.Hand) };
        layer.Classes.Add("ClaudeChatDiffExpand");
        AutomationProperties.SetName(layer, ExpandText);
        layer.Tapped += (_, e) =>
        {
            e.Handled = true;
            ExpandRequested?.Invoke(this, EventArgs.Empty);
        };
        // a click soon after the last one is a double tap, which raises no Tapped: it opens all the same
        layer.DoubleTapped += (_, e) =>
        {
            e.Handled = true;
            ExpandRequested?.Invoke(this, EventArgs.Empty);
        };
        layer.KeyDown += (_, e) =>
        {
            if ((e.Key != Key.Enter && e.Key != Key.Space) || e.KeyModifiers != KeyModifiers.None) return;
            e.Handled = true;
            ExpandRequested?.Invoke(this, EventArgs.Empty);
        };
        return layer;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // above 900 px side by side; once so, back in one column only at 876 px or less
        var width = availableSize.Width;
        var wide = _alwaysSideBySide
            || (!double.IsInfinity(width) && width > (_sideBySide == true ? NarrowBackWidth : SideBySideWidth));
        if (_sideBySide != wide)
        {
            // built on the first measure, and again only when the width crosses the line
            _sideBySide = wide;
            if (_content != null) Children.Remove(_content);
            _content = wide ? SideBySide() : OneColumn();
            Children.Insert(0, _content);
        }

        _content!.Measure(new Size(width, double.PositiveInfinity));
        _fade.Measure(availableSize);
        // the extension's: the lines and 20 px, 200 px at most, faded only when the lines alone are taller than that
        var lines = _content.DesiredSize.Height;
        IsCut = !_whole && lines > MostHeight;
        PseudoClasses.Set(":cut", IsCut);
        var height = _whole ? lines + BottomSpace : Math.Min(MostHeight, lines + BottomSpace);
        _expand?.Measure(new Size(width, height));
        return new Size(_content.DesiredSize.Width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _content?.Arrange(new Rect(0, 0, finalSize.Width, _content.DesiredSize.Height));
        _fade.Arrange(new Rect(0, finalSize.Height - 30, finalSize.Width, 30));
        _expand?.Arrange(new Rect(finalSize));
        return finalSize;
    }

    // ---- the two layouts ----

    private Control SideBySide()
    {
        var rows = new List<Func<Control>>();
        var firstOld = true;
        var firstNew = true;
        foreach (var block in Blocks(_diff, _whole))
        {
            if (block.Gap != null)
            {
                var gap = block.Gap;
                rows.Add(() => GapRow(gap));
                continue;
            }
            var b = block;
            var oldCurrent = firstOld && b.Old.Count > 0;
            var newCurrent = firstNew && b.New.Count > 0;
            firstOld &= !oldCurrent;
            firstNew &= !newCurrent;
            rows.Add(() =>
            {
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
                var left = Side(b.Old, b.New, b.IsChange ? "removed" : "context", WideGutter, 21, oldCurrent);
                var right = Side(b.New, b.Old, b.IsChange ? "added" : "context", NarrowGutter, 2, newCurrent);
                Grid.SetColumn(right, 1);
                grid.Children.Add(left);
                grid.Children.Add(right);
                // the old side throws a soft shadow on the new one, like the extension's
                var shadow = new Rectangle { Width = 7, HorizontalAlignment = HorizontalAlignment.Right, IsHitTestVisible = false };
                shadow.Classes.Add("ClaudeChatDiffShadow");
                grid.Children.Add(shadow);
                return grid;
            });
        }
        return Rows(rows);
    }

    private Control OneColumn()
    {
        var rows = new List<Func<Control>>();
        var first = true;
        foreach (var block in Blocks(_diff, _whole))
        {
            if (block.Gap != null)
            {
                var gap = block.Gap;
                rows.Add(() => GapRow(gap));
                continue;
            }
            if (!block.IsChange)
            {
                foreach (var line in block.Old)
                {
                    var current = first;
                    first = false;
                    rows.Add(() => LineRow(line, null, "context", NarrowGutter, 2, current));
                }
                continue;
            }
            // the cursor sits on the new text, not on the old lines shown above it
            for (var i = 0; i < block.Old.Count; i++)
            {
                var line = block.Old[i];
                var partner = Partner(block.New, i, block.Old.Count);
                rows.Add(() => LineRow(line, partner, "removed", NarrowGutter, 2, false));
            }
            for (var i = 0; i < block.New.Count; i++)
            {
                var line = block.New[i];
                var partner = Partner(block.Old, i, block.New.Count);
                var current = first;
                first = false;
                rows.Add(() => LineRow(line, partner, "added", NarrowGutter, 2, current));
            }
        }
        return Rows(rows);
    }

    /// <summary>
    /// The rows, one under the other. A whole diff (the permission request's, which can be a file of thousands of
    /// lines) builds only the rows its scrolling area shows; the list's, cut at 200 px, builds them all.
    /// </summary>
    private Control Rows(List<Func<Control>> rows)
    {
        if (!_whole)
        {
            var stack = new StackPanel();
            foreach (var row in rows) stack.Children.Add(row());
            return stack;
        }
        return new ItemsControl
        {
            ItemsSource = rows,
            ItemTemplate = new FuncDataTemplate<Func<Control>>((make, _) => make?.Invoke()),
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
            Template = new FuncControlTemplate<ItemsControl>((items, scope) => new ItemsPresenter
            {
                Name = "PART_ItemsPresenter",
                [~ItemsPresenter.ItemsPanelProperty] = items[~ItemsControl.ItemsPanelProperty],
            }.RegisterInNameScope(scope)),
        };
    }

    /// <summary>One side of a block: its lines, then the hatch filling what the other side has more.</summary>
    private Control Side(IReadOnlyList<string> lines, IReadOnlyList<string> other, string kind, double gutter, double signLeft, bool current)
    {
        var side = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        var stack = new StackPanel();
        for (var i = 0; i < lines.Count; i++)
            stack.Children.Add(LineRow(lines[i], kind == "context" ? null : Partner(other, i, lines.Count), kind, gutter, signLeft, current && i == 0));
        side.Children.Add(stack);

        var hatch = new Border { Margin = new Thickness(gutter, 0, 0, 0), OpacityMask = HatchMask() };
        hatch.Classes.Add("ClaudeChatDiffHatch");
        Grid.SetRow(hatch, 1);
        side.Children.Add(hatch);
        return side;
    }

    /// <summary>
    /// A line: the gutter (with its sign when changed), then the guides of its indentation, the color of its kind,
    /// the changed characters and the text, wrapped under its indentation.
    /// </summary>
    private Control LineRow(string text, string? partner, string kind, double gutter, double signLeft, bool current)
    {
        var expanded = text.Replace("\t", new string(' ', TabSize));
        var indent = 0;
        while (indent < expanded.Length && expanded[indent] == ' ') indent++;
        var body = expanded.Substring(indent);
        var charWidth = CharWidth();

        var cell = new Panel();
        if (current)
        {
            // the cursor's line, as the editor shows it: a frame around the first line
            var frame = new Border { VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
            frame.Classes.Add("ClaudeChatDiffCurrentLine");
            cell.Children.Add(frame);
        }
        for (var column = 0; column < indent; column += TabSize)
        {
            var guide = new Rectangle { Width = 1, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(Math.Round(column * charWidth), 0, 0, 0) };
            guide.Classes.Add("ClaudeChatDiffGuide");
            cell.Children.Add(guide);
        }
        var tint = new Border();
        tint.Classes.Add("ClaudeChatDiffLine");
        tint.Classes.Add(kind);
        cell.Children.Add(tint);

        // the editor keeps 14 px at the right (its scroll bar's) out of the wrapping
        var line = new TextBlock { Margin = new Thickness(indent * charWidth, 0, WrapReserve, 0) };
        line.Classes.Add("ClaudeChatDiffText");
        // the list cuts a long line (Task 5); the request shows all of it, wrapped like the extension's editor
        var clipped = _whole ? body : ChatText.Clip(body, ToolCallView.MaxLineChars);
        var (from, to) = partner == null ? (0, 0) : Changed(expanded, partner.Replace("\t", new string(' ', TabSize)));
        from = Math.Max(0, from - indent);
        to = Math.Min(clipped.Length, Math.Max(0, to - indent));
        if (to > from)
        {
            if (from > 0) line.Inlines!.Add(new Run(clipped.Substring(0, from)));
            var changed = new Run(clipped.Substring(from, to - from));
            changed.Classes.Add("ClaudeChatDiffChanged");
            changed.Classes.Add(kind);
            line.Inlines!.Add(changed);
            if (to < clipped.Length) line.Inlines.Add(new Run(clipped.Substring(to)));
        }
        else
        {
            line.Text = clipped;
        }
        cell.Children.Add(line);

        // a plain panel, the line after the gutter: a grid's star column would measure the text twice
        var row = new Panel();
        row.Classes.Add("ClaudeChatDiffRow");
        if (kind != "context")
        {
            var sign = new Border { Margin = new Thickness(signLeft, 7, 0, 0) };
            sign.Classes.Add("ClaudeChatDiffSign");
            row.Children.Add(sign);
        }
        cell.Margin = new Thickness(gutter, 0, 0, 0);
        row.Children.Add(cell);
        return row;
    }

    /// <summary>"+N lines" for unchanged lines left out, "⋯" between the edits of a MultiEdit.</summary>
    private Control GapRow(string gap)
    {
        var text = new TextBlock { Text = GapText(gap, _strings) };
        text.Classes.Add("ClaudeChatDiffGap");
        var row = new Border { Child = text, Padding = new Thickness(WideGutter, 0, 0, 0) };
        row.Classes.Add("ClaudeChatDiffGapRow");
        return row;
    }

    internal static string GapText(string text, ChatStrings strings)
    {
        if (text.Length == 0) return "⋯";
        if (text[0] == '+' && int.TryParse(text.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out var lines))
            return strings.FormatMoreLines(lines);
        return text;
    }

    /// <summary>The width of a character of the editor's font, for the indentation.</summary>
    private double CharWidth()
    {
        if (_charWidth > 0) return _charWidth;
        var family = this.TryFindResource("ClaudeChat.MonoFontFamily", out var f) && f is FontFamily ff ? ff : new FontFamily("Consolas");
        var size = this.TryFindResource("ClaudeChat.EditorFontSize", out var s) && s is double d ? d : 14;
        using (var layout = new TextLayout("          ", new Typeface(family), size, null))
            _charWidth = layout.WidthIncludingTrailingWhitespace / 10;
        return _charWidth;
    }

    // ---- reading the diff ----

    private sealed class Block
    {
        public List<string> Old { get; } = new List<string>();

        public List<string> New { get; } = new List<string>();

        public bool IsChange { get; set; }

        public string? Gap { get; set; }
    }

    /// <summary>Runs of unchanged lines, changes (removed lines with the added ones after them) and gaps.</summary>
    /// <param name="diff">The lines of the diff.</param>
    /// <param name="whole">Every row; else at most <see cref="ToolCallView.MaxDiffRows"/>, then "+N lines".</param>
    private static List<Block> Blocks(IReadOnlyList<DiffLine> diff, bool whole)
    {
        var blocks = new List<Block>();
        Block? current = null;
        var shown = whole ? diff.Count : Math.Min(diff.Count, ToolCallView.MaxDiffRows);
        for (var i = 0; i < shown; i++)
        {
            var line = diff[i];
            switch (line.Kind)
            {
                case DiffKind.Gap:
                    blocks.Add(new Block { Gap = line.Text });
                    current = null;
                    break;
                case DiffKind.Context:
                    if (current == null || current.IsChange) blocks.Add(current = new Block());
                    current.Old.Add(line.Text);
                    current.New.Add(line.Text);
                    break;
                case DiffKind.Removed:
                    if (current == null || !current.IsChange || current.New.Count > 0) blocks.Add(current = new Block { IsChange = true });
                    current.Old.Add(line.Text);
                    break;
                default:
                    if (current == null || !current.IsChange) blocks.Add(current = new Block { IsChange = true });
                    current.New.Add(line.Text);
                    break;
            }
        }
        if (diff.Count > shown) blocks.Add(new Block { Gap = "+" + (diff.Count - shown).ToString(CultureInfo.InvariantCulture) });
        return blocks;
    }

    /// <summary>The line paired with line <paramref name="i"/>: the same place on the other side, when both sides have the same count.</summary>
    private static string? Partner(IReadOnlyList<string> other, int i, int count) =>
        other.Count == count && i < other.Count ? other[i] : null;

    /// <summary>The changed part of <paramref name="text"/> against <paramref name="partner"/>: what is left between their common start and end.</summary>
    internal static (int From, int To) Changed(string text, string partner)
    {
        var max = Math.Min(text.Length, partner.Length);
        var start = 0;
        while (start < max && text[start] == partner[start]) start++;
        var end = 0;
        while (end < max - start && text[text.Length - 1 - end] == partner[partner.Length - 1 - end]) end++;
        return (start, text.Length - end);
    }

    /// <summary>The diagonal stripes of the extension's filler: 8 px tiles, a 2 px stripe rising to the right.</summary>
    private static ImageBrush HatchMask()
    {
        if (s_hatchMask == null)
        {
            var bitmap = new WriteableBitmap(new PixelSize(8, 8), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            using (var buffer = bitmap.Lock())
            {
                var row = new byte[buffer.RowBytes];
                for (var y = 0; y < 8; y++)
                {
                    for (var x = 0; x < 8; x++)
                    {
                        var on = (x + y) % 8 >= 6 ? (byte)255 : (byte)0;
                        row[x * 4] = on;
                        row[x * 4 + 1] = on;
                        row[x * 4 + 2] = on;
                        row[x * 4 + 3] = on;
                    }
                    System.Runtime.InteropServices.Marshal.Copy(row, 0, buffer.Address + y * buffer.RowBytes, buffer.RowBytes);
                }
            }
            s_hatchMask = bitmap;
        }
        return new ImageBrush(s_hatchMask)
        {
            TileMode = TileMode.Tile,
            Stretch = Stretch.None,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top,
            SourceRect = new RelativeRect(0, 0, 8, 8, RelativeUnit.Absolute),
            DestinationRect = new RelativeRect(0, 0, 8, 8, RelativeUnit.Absolute),
        };
    }
}
