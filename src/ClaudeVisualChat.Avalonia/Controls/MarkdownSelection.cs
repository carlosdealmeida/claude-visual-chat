using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using ColorTextBlock.Avalonia;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// Selecting the words of a <see cref="MarkdownBlock"/> with the mouse, as a page selects its text: a drag over its paragraphs,
/// headings, list items, quotes and table cells selects the words between the press and the pointer, across those blocks, and the
/// platform's copy keys (Ctrl+C) copy them, one line per block, a list item with its bullet or number when it is selected from
/// its start. The engine's text control (<see cref="CTextBlock"/>) draws a selection and finds the place under a point, but only
/// the engine's own viewer moves it with the pointer, and the block draws its text without that viewer.
/// <para>
/// A press that does not move selects nothing and leaves the keyboard where it was (a link under it still works). A selection
/// takes the keyboard, so the copy keys reach it; Esc, a press anywhere else, the keyboard going to another control or a new
/// drawing of the text (the answer still arriving) ends it, and a block that held the keyboard gives it back to the reply box.
/// Code blocks keep their Copy button and take no part in a selection, and the finger selects nothing: on a touch screen it
/// scrolls the list.
/// </para>
/// </summary>
internal sealed class MarkdownSelection
{
    /// <summary>How far the pointer goes, in px, before a press becomes a selection.</summary>
    private const double DragThreshold = 3;

    /// <summary>The class the engine gives the bullet or the number of a list item, a block of text of its own.</summary>
    private const string ListMarkerClass = "ListMarker";

    private readonly MarkdownBlock _block;

    /// <summary>The blocks of text in reading order, without the list markers, while a press or a selection lasts.</summary>
    private List<CTextBlock>? _texts;

    /// <summary>The bullet or number of each block of text that opens a list item.</summary>
    private readonly Dictionary<CTextBlock, string> _markers = new Dictionary<CTextBlock, string>();

    /// <summary>The blocks of text selected from their first letter: a list item among them is copied with its marker.</summary>
    private readonly HashSet<CTextBlock> _fromStart = new HashSet<CTextBlock>();

    private Place? _anchor;
    private Point _pressedAt;
    private bool _pressing;
    private bool _dragging;
    private bool _selected;

    /// <summary>The window whose presses end the selection, while there is one.</summary>
    private TopLevel? _watched;

    public MarkdownSelection(MarkdownBlock block)
    {
        _block = block;
        block.AddHandler(InputElement.PointerPressedEvent, OnPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        block.AddHandler(InputElement.PointerMovedEvent, OnMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        block.AddHandler(InputElement.PointerReleasedEvent, OnReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        // the event is raised on the control that had the pointer only (it does not bubble): the drag's own capture of the block
        block.AddHandler(InputElement.PointerCaptureLostEvent, (_, _) => EndDrag(), RoutingStrategies.Direct);
        block.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
        block.LostFocus += (_, _) => Clear();
        block.DetachedFromVisualTree += (_, _) => Clear();
    }

    /// <summary>The words selected, one line per block; empty without a selection.</summary>
    internal string SelectedText
    {
        get
        {
            if (!_selected || _texts == null) return "";
            var lines = new List<string>();
            foreach (var text in _texts)
            {
                var words = text.GetSelectedText();
                if (words.Length == 0) continue;
                lines.Add(_fromStart.Contains(text) && _markers.TryGetValue(text, out var marker) ? marker + " " + words : words);
            }
            return string.Join("\n", lines);
        }
    }

    /// <summary>
    /// Ends the selection and any press: no block shows a selection any more, and a block that held the keyboard for it gives the
    /// keyboard back (to the reply box of its view, or to no one outside a view).
    /// </summary>
    internal void Clear()
    {
        if (_texts != null)
        {
            foreach (var text in _texts) text.ClearSelection();
        }
        _texts = null;
        _markers.Clear();
        _fromStart.Clear();
        _anchor = null;
        _pressing = false;
        _dragging = false;
        _selected = false;
        Unwatch();
        var held = _block.IsFocused;
        _block.Focusable = false;
        if (held) GiveTheKeyboardBack();
    }

    private void GiveTheKeyboardBack()
    {
        var view = _block.FindAncestorOfType<ClaudeChatView>();
        if (view != null && view.ReplyBox.FocusInput()) return;
        if (_block.IsFocused) TopLevel.GetTopLevel(_block)?.FocusManager?.ClearFocus();
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        Clear();
        var point = e.GetCurrentPoint(_block);
        if (!_block.SelectsText || !point.Properties.IsLeftButtonPressed || e.Pointer.Type == PointerType.Touch
            || TakenByAPart(e.Source as Visual)) return;
        Collect();
        if (_texts == null || _texts.Count == 0)
        {
            _texts = null;
            return;
        }
        _anchor = PlaceAt(point.Position);
        _pressedAt = point.Position;
        _pressing = _anchor != null;
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (!_pressing || _anchor is not { } anchor) return;
        var point = e.GetCurrentPoint(_block);
        if (!point.Properties.IsLeftButtonPressed)
        {
            // the button came up where the block did not see it (another window took the pointer): the drag ended there
            var captured = _dragging;
            EndDrag();
            if (captured && ReferenceEquals(e.Pointer.Captured, _block)) e.Pointer.Capture(null);
            return;
        }
        var at = point.Position;
        if (!_dragging)
        {
            var moved = at - _pressedAt;
            if (System.Math.Abs(moved.X) < DragThreshold && System.Math.Abs(moved.Y) < DragThreshold) return;
            _dragging = true;
            e.Pointer.Capture(_block); // the rest of the drag is the block's: a link under the press does not open on the release
        }
        if (PlaceAt(at) is { } end) Select(anchor, end);
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_pressing) return;
        var captured = _dragging;
        EndDrag();
        if (captured && ReferenceEquals(e.Pointer.Captured, _block)) e.Pointer.Capture(null);
    }

    /// <summary>
    /// The press is over, by the release or without one: what it selected takes the keyboard, so the copy keys reach it, and a
    /// press that selected nothing leaves no trace.
    /// </summary>
    private void EndDrag()
    {
        if (!_pressing) return;
        _pressing = false;
        _dragging = false;
        if (!_selected)
        {
            Clear();
            return;
        }
        _block.Focusable = true;
        if (!_block.Focus(NavigationMethod.Pointer)) _block.Focusable = false; // hidden or disabled meanwhile: it stays out of the keyboard's way
        Watch();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || ImeKeys.IsComposing(e)) return;
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && (_selected || _block.IsFocused))
        {
            e.Handled = true;
            Clear();
            return;
        }
        if (!_selected || !IsCopy(e)) return;
        var text = SelectedText;
        if (text.Length == 0) return;
        e.Handled = true;
        _ = ChatClipboard.SetTextAsync(_block, text); // a refusal of the clipboard copies nothing, and says so to no one
    }

    /// <summary>The platform's copy keys (Ctrl+C or Ctrl+Insert; Cmd+C on a Mac).</summary>
    private bool IsCopy(KeyEventArgs e)
    {
        var gestures = TopLevel.GetTopLevel(_block)?.PlatformSettings?.HotkeyConfiguration.Copy;
        if (gestures != null && gestures.Count > 0) return gestures.Any(g => g.Matches(e));
        return e.KeyModifiers == KeyModifiers.Control && (e.Key == Key.C || e.Key == Key.Insert);
    }

    /// <summary>A press on a part with a job of its own (the Copy button and the scrolling of a code block, a field) selects nothing.</summary>
    private bool TakenByAPart(Visual? source)
    {
        for (var visual = source; visual != null && !ReferenceEquals(visual, _block); visual = visual.GetVisualParent())
        {
            if (visual is Button or ScrollBar or ScrollViewer or TextBox) return true;
            if (visual is StyledElement styled && styled.Classes.Contains("CodeBlock")) return true;
        }
        return false;
    }

    /// <summary>A press anywhere outside the block ends its selection, as a click elsewhere on a page does.</summary>
    private void Watch()
    {
        var top = TopLevel.GetTopLevel(_block);
        if (ReferenceEquals(top, _watched)) return;
        Unwatch();
        _watched = top;
        _watched?.AddHandler(InputElement.PointerPressedEvent, OnAnyPress, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void Unwatch()
    {
        _watched?.RemoveHandler(InputElement.PointerPressedEvent, OnAnyPress);
        _watched = null;
    }

    private void OnAnyPress(object? sender, PointerPressedEventArgs e)
    {
        // a press on the block itself ends the selection there, and may start the next one
        if (e.Source is Visual source && (ReferenceEquals(source, _block) || _block.IsVisualAncestorOf(source))) return;
        Clear();
    }

    /// <summary>
    /// Reads the blocks of text of the block, in reading order: the engine's text controls, not those inside another one. The
    /// bullet or number of a list item is a text control of its own, which is no words to select: it goes with the block of text
    /// that follows it.
    /// </summary>
    private void Collect()
    {
        var texts = new List<CTextBlock>();
        _markers.Clear();
        string? marker = null;
        foreach (var text in _block.GetVisualDescendants().OfType<CTextBlock>())
        {
            if (!text.IsEffectivelyVisible
                || text.GetVisualAncestors().TakeWhile(a => !ReferenceEquals(a, _block)).Any(a => a is CTextBlock)) continue;
            if (text.Classes.Contains(ListMarkerClass))
            {
                marker = text.Text?.Trim();
                continue;
            }
            if (!string.IsNullOrEmpty(marker)) _markers[text] = marker!;
            marker = null;
            texts.Add(text);
        }
        _texts = texts;
    }

    /// <summary>
    /// The place in the text nearest to <paramref name="at"/> (in the block's coordinates): in the block of text under it, or else
    /// in the nearest one, first by height and then sideways, at its start when the point is above it and at its end when below.
    /// </summary>
    private Place? PlaceAt(Point at)
    {
        if (_texts == null) return null;
        var best = -1;
        var bestScore = double.MaxValue;
        var bestOrigin = default(Point);
        for (var i = 0; i < _texts.Count; i++)
        {
            if (_texts[i].TranslatePoint(default, _block) is not { } origin) continue;
            var size = _texts[i].Bounds.Size;
            var dy = at.Y < origin.Y ? origin.Y - at.Y : at.Y > origin.Y + size.Height ? at.Y - origin.Y - size.Height : 0;
            var dx = at.X < origin.X ? origin.X - at.X : at.X > origin.X + size.Width ? at.X - origin.X - size.Width : 0;
            var score = dy * 100000 + dx;
            if (score < bestScore)
            {
                bestScore = score;
                best = i;
                bestOrigin = origin;
            }
        }
        if (best < 0) return null;
        var text = _texts[best];
        var local = at - bestOrigin;
        var pointers = (ITextPointerHandleable)text;
        if (local.Y < 0) return new Place(best, pointers.GetBegin().Index);
        if (local.Y > text.Bounds.Height) return new Place(best, pointers.GetEnd().Index);
        var x = System.Math.Max(0, System.Math.Min(text.Bounds.Width, local.X));
        return new Place(best, text.CalcuatePointerFrom(x, local.Y).Index);
    }

    /// <summary>Selects from <paramref name="anchor"/> to <paramref name="end"/>, in whichever order they come, across the blocks between.</summary>
    private void Select(Place anchor, Place end)
    {
        if (_texts == null) return;
        var (from, to) = anchor.CompareTo(end) <= 0 ? (anchor, end) : (end, anchor);
        var brush = SelectionBrush();
        var any = false;
        _fromStart.Clear();
        for (var i = 0; i < _texts.Count; i++)
        {
            var text = _texts[i];
            if (i < from.Text || i > to.Text)
            {
                text.ClearSelection();
                continue;
            }
            var pointers = (ITextPointerHandleable)text;
            var first = pointers.GetBegin().Index;
            var start = i == from.Text ? from.At : first;
            var stop = i == to.Text ? to.At : pointers.GetEnd().Index;
            if (stop <= start)
            {
                text.ClearSelection();
                continue;
            }
            if (brush != null) text.SelectionBrush = brush;
            text.Select(start, stop);
            if (start <= first) _fromStart.Add(text);
            any = true;
        }
        _selected = any;
    }

    /// <summary>
    /// The color the theme paints a selected text with: Fluent's (<c>TextControlSelectionHighlightColor</c>) or Simple's
    /// (<c>HighlightBrush</c>), as the other selectable texts of the view take it; null leaves the engine's own.
    /// </summary>
    private IBrush? SelectionBrush()
    {
        foreach (var key in new[] { "TextControlSelectionHighlightColor", "HighlightBrush" })
        {
            if (_block.TryFindResource(key, _block.ActualThemeVariant, out var value) && value is IBrush brush) return brush;
        }
        return null;
    }

    /// <summary>A place in the text: the block of text, in reading order, and the place in it.</summary>
    private readonly struct Place
    {
        public Place(int text, int at)
        {
            Text = text;
            At = at;
        }

        public int Text { get; }

        public int At { get; }

        public int CompareTo(Place other) => Text != other.Text ? Text.CompareTo(other.Text) : At.CompareTo(other.At);
    }
}
