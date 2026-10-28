using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Media;
using ColorTextBlock.Avalonia;

namespace ClaudeVisualChat.Controls;

/// <summary>A passage of a block of the plan that carries a comment: its place in the block's text, its number and the comment.</summary>
internal readonly struct PlanMark
{
    public PlanMark(int start, int end, int number, string comment)
    {
        Start = start;
        End = end;
        Number = number;
        Comment = comment;
    }

    public int Start { get; }

    public int End { get; }

    public int Number { get; }

    public string Comment { get; }
}

/// <summary>
/// A paragraph or heading of the plan's document, drawn so that the user can select a passage of it to comment on. The Markdown draws
/// its blocks with a text control that selects nothing (<see cref="CTextBlock"/>); the document puts one of these in each one's place,
/// with the same words, the same classes (<c>Paragraph</c>, <c>Heading1</c>…) and the same bold, italic, code and links, the last two
/// as runs of the classes <c>ClaudeChatPlanCode</c> and <c>ClaudeChatPlanLink</c>. A passage that carries a comment is a run of the class <c>ClaudeChatPlanMarked</c>, followed by
/// a numbered circle whose tooltip is the comment (<see cref="SetMarks"/>). The block's words (<see cref="Words"/>) and the places in it
/// leave the circles out: a selection is told in those places, whatever circles stand in the line.
/// </summary>
internal sealed class PlanTextBlock : SelectableTextBlock
{
    private readonly List<Piece> _pieces = new List<Piece>();
    private readonly string _text;
    private IReadOnlyList<PlanMark> _marks = Array.Empty<PlanMark>();

    /// <summary>For each place of the drawn text (the circles take one each), the place in <see cref="Words"/>; one more for the end.</summary>
    private int[] _toText = { 0 };

    public PlanTextBlock(CTextBlock source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        foreach (var name in source.Classes) Classes.Add(name);
        Classes.Add("ClaudeChatPlanText");
        TextAlignment = source.TextAlignment;
        TextWrapping = TextWrapping.Wrap;
        Grid.SetRow(this, Grid.GetRow(source));
        Grid.SetColumn(this, Grid.GetColumn(source));
        var text = new StringBuilder();
        Read(source.Content, default, text);
        _text = text.ToString();
        CopyingToClipboard += OnCopying;
        Draw();
    }

    protected override Type StyleKeyOverride => typeof(SelectableTextBlock);

    /// <summary>The block's words, without the circles of its comments.</summary>
    internal string Words => _text;

    /// <summary>A heading (<c>Heading1</c> to <c>Heading6</c>), which is the heading a comment under it is filed under.</summary>
    internal bool IsHeading => Classes.Any(c => c.StartsWith("Heading", StringComparison.Ordinal));

    /// <summary>The comments' passages in this block, numbered as the plan's comments are.</summary>
    internal IReadOnlyList<PlanMark> Marks => _marks;

    /// <summary>
    /// The selected passage, in places of <see cref="Words"/>, without the blanks around it; null when nothing but blanks is
    /// selected.
    /// </summary>
    internal (int Start, int End)? SelectedRange
    {
        get
        {
            var start = ToText(Math.Min(SelectionStart, SelectionEnd));
            var end = ToText(Math.Max(SelectionStart, SelectionEnd));
            while (start < end && char.IsWhiteSpace(_text[start])) start++;
            while (end > start && char.IsWhiteSpace(_text[end - 1])) end--;
            return end > start ? (start, end) : null;
        }
    }

    /// <summary>
    /// The rectangle of the selection, in the block's own coordinates (the union of its lines), or null when nothing is selected.
    /// </summary>
    internal Rect? SelectionBounds
    {
        get
        {
            var from = Math.Min(SelectionStart, SelectionEnd);
            var length = Math.Abs(SelectionEnd - SelectionStart);
            if (length == 0) return null;
            Rect? union = null;
            foreach (var rect in TextLayout.HitTestTextRange(from, length))
                union = union == null ? rect : union.Value.Union(rect);
            return union?.Translate(new Vector(Padding.Left, Padding.Top));
        }
    }

    /// <summary>Draws the comments' passages and their circles; the selection, whose places moved, is cleared.</summary>
    internal void SetMarks(IReadOnlyList<PlanMark> marks)
    {
        var next = marks ?? Array.Empty<PlanMark>();
        if (_marks.SequenceEqual(next)) return;
        _marks = next;
        ClearSelection();
        Draw();
    }

    /// <summary>The place in <see cref="Words"/> of a place of the drawn text.</summary>
    private int ToText(int drawn) => drawn <= 0 ? 0 : drawn < _toText.Length ? _toText[drawn] : _text.Length;

    /// <summary>A copy takes the words without the circles.</summary>
    private async void OnCopying(object? sender, RoutedEventArgs e)
    {
        var start = ToText(Math.Min(SelectionStart, SelectionEnd));
        var end = ToText(Math.Max(SelectionStart, SelectionEnd));
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null || end <= start) return;
        e.Handled = true;
        try
        {
            await clipboard.SetTextAsync(_text.Substring(start, end - start));
        }
        catch (Exception)
        {
            // the clipboard refused it: nothing was copied, and a refusal is not an unobserved task exception
        }
    }

    private void Read(IEnumerable<CInline> inlines, Look style, StringBuilder text)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case CLineBreak:
                    Add(new Piece(Environment.NewLine, style, lineBreak: true), text);
                    break;
                case CRun run:
                    if (!string.IsNullOrEmpty(run.Text)) Add(new Piece(run.Text, style), text);
                    break;
                case CSpan span:
                    var inner = style;
                    if (span is CBold) inner.Bold = true;
                    else if (span is CItalic) inner.Italic = true;
                    else if (span is CCode) inner.Code = true;
                    else if (span is CHyperlink) inner.Link = true;
                    else if (span is CStrikethrough) inner.Strike = true;
                    else if (span is CUnderline) inner.Underline = true;
                    Read(span.Content, inner, text);
                    break;
                // images are never loaded, and nothing else carries words
            }
        }
    }

    private void Add(Piece piece, StringBuilder text)
    {
        piece.Start = text.Length;
        _pieces.Add(piece);
        text.Append(piece.Text);
    }

    /// <summary>The runs of the pieces, cut where a comment's passage begins or ends, and a circle after each passage.</summary>
    private void Draw()
    {
        var inlines = Inlines ??= new InlineCollection();
        inlines.Clear();
        var toText = new List<int>(_text.Length + 1);
        foreach (var piece in _pieces)
        {
            var end = piece.Start + piece.Text.Length;
            if (piece.LineBreak)
            {
                inlines.Add(new LineBreak());
                for (var i = 0; i < piece.Text.Length; i++) toText.Add(piece.Start + i);
                AddCircles(inlines, toText, m => m.End > piece.Start && m.End <= end, end);
                continue;
            }
            var cuts = new SortedSet<int> { piece.Start, end };
            foreach (var mark in _marks)
            {
                if (mark.Start > piece.Start && mark.Start < end) cuts.Add(mark.Start);
                if (mark.End > piece.Start && mark.End < end) cuts.Add(mark.End);
            }
            var points = cuts.ToList();
            for (var i = 0; i + 1 < points.Count; i++)
            {
                int from = points[i], to = points[i + 1];
                var run = new Run(_text.Substring(from, to - from));
                piece.Style.Apply(run);
                if (_marks.Any(m => m.Start <= from && to <= m.End)) run.Classes.Add("ClaudeChatPlanMarked");
                inlines.Add(run);
                for (var at = from; at < to; at++) toText.Add(at);
                AddCircles(inlines, toText, m => m.End == to, to);
            }
        }
        toText.Add(_text.Length);
        _toText = toText.ToArray();
    }

    private void AddCircles(InlineCollection inlines, List<int> toText, Func<PlanMark, bool> endsHere, int at)
    {
        foreach (var mark in _marks.Where(endsHere).OrderBy(m => m.Number))
        {
            var number = new TextBlock { Text = mark.Number.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            number.Classes.Add("ClaudeChatPlanCommentNumberText");
            var circle = new Border { Child = number };
            circle.Classes.Add("ClaudeChatPlanCommentNumber");
            ToolTip.SetTip(circle, mark.Comment);
            inlines.Add(new InlineUIContainer(circle));
            toText.Add(at);
        }
    }

    private struct Look
    {
        public bool Bold;
        public bool Italic;
        public bool Code;
        public bool Link;
        public bool Strike;
        public bool Underline;

        public void Apply(Run run)
        {
            if (Bold) run.FontWeight = FontWeight.Bold;
            if (Italic) run.FontStyle = FontStyle.Italic;
            if (Code) run.Classes.Add("ClaudeChatPlanCode");
            if (Link) run.Classes.Add("ClaudeChatPlanLink");
            if (Strike) run.TextDecorations = Avalonia.Media.TextDecorations.Strikethrough;
            else if (Underline) run.TextDecorations = Avalonia.Media.TextDecorations.Underline;
        }
    }

    private sealed class Piece
    {
        public Piece(string text, Look style, bool lineBreak = false)
        {
            Text = text;
            Style = style;
            LineBreak = lineBreak;
        }

        public string Text { get; }

        public Look Style { get; }

        public bool LineBreak { get; }

        public int Start { get; set; }
    }
}
