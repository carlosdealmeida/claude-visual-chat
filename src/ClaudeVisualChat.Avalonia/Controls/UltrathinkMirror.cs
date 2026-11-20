using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.Utilities;
using Avalonia.VisualTree;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The text of the reply box drawn again under it, as the extension draws it, with each <c>ultrathink</c> (<see cref="Words"/>) in a
/// rainbow (<see cref="Rainbow"/>, one gradient from the start to the end of each piece of a line the word has) and, for 1.4 s twice
/// after it is written, a shine that sweeps it from left to right (<see cref="Shine"/>; none with <see cref="ClaudeChatView.ReduceMotion"/>).
/// While it paints, the box's own text is transparent and its caret, its selection and its selected text stay as they are, drawn over
/// it (a theme that gives the selected text no color of its own has it in the box's normal color, as before). It lays the text out as the box's text presenter does (the same text, font, size, wrapping, line height and width) and puts it
/// where the presenter is, scrolled with it and cut to the part of the box that shows text. With nothing to paint (no word, or text being
/// composed by an input method) it draws nothing and the box draws its own text. Not hit-testable, and not in the accessibility tree.
/// </summary>
internal sealed class UltrathinkMirror : Control
{
    /// <summary>How long one sweep of the shine takes.</summary>
    internal const long ShineMs = 1400;

    /// <summary>How many sweeps, after which the shine rests, out of the word.</summary>
    internal const int ShineRuns = 2;

    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(16);
    private static readonly IReadOnlyList<(int Start, int Length)> NoWords = Array.Empty<(int, int)>();

    /// <summary>The color of the text that is not the word: the box's own.</summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<UltrathinkMirror, IBrush?>(nameof(Foreground));

    /// <summary>The word's gradient, from the start to the end of each of its pieces (<c>ClaudeChat.UltrathinkGradient</c>).</summary>
    public static readonly StyledProperty<IBrush?> RainbowProperty =
        AvaloniaProperty.Register<UltrathinkMirror, IBrush?>(nameof(Rainbow));

    /// <summary>The shine's color at its middle (<c>ClaudeChat.UltrathinkShine</c>).</summary>
    public static readonly StyledProperty<Color> ShineProperty =
        AvaloniaProperty.Register<UltrathinkMirror, Color>(nameof(Shine));

    private static readonly StyledProperty<bool> ReduceMotionProperty = ClaudeChatView.ReduceMotionProperty.AddOwner<UltrathinkMirror>();

    private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = FrameInterval };

    /// <summary>When each occurrence (by its order in the text) was first painted: its shine starts then.</summary>
    private readonly List<long> _shineStarts = new List<long>();

    private IReadOnlyList<(int Start, int Length)> _words = NoWords;
    private TextBox? _box;
    private TextPresenter? _presenter;
    private bool _attached;
    private bool _painting;

    /// <summary>True while the box's selection text color is the mirror's: the theme gave it none.</summary>
    private bool _selectionForegroundSet;

    /// <summary>True while the box's caret color is the mirror's: the theme gave it none, and the caret would take the clear text color.</summary>
    private bool _caretBrushSet;

    // the layouts and what they were made from: the presenter's own layout (a new one for each change of its text, width or selection)
    private TextLayout? _source;
    private TextLayout? _layout;
    private TextLayout? _shineLayout;
    private List<Fragment> _fragments = new List<Fragment>();

    // where the last frame was drawn, to tell when the box moved its text (scrolled, resized)
    private Point _drawnAt;
    private Rect _drawnIn;

    /// <summary>A piece of an occurrence on one line, and where the layout puts it.</summary>
    private readonly struct Fragment
    {
        public Fragment(int occurrence, Rect rect)
        {
            Occurrence = occurrence;
            Rect = rect;
        }

        public int Occurrence { get; }

        public Rect Rect { get; }
    }

    public UltrathinkMirror()
    {
        IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        _timer.Tick += (_, _) => Tick();
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public IBrush? Rainbow
    {
        get => GetValue(RainbowProperty);
        set => SetValue(RainbowProperty, value);
    }

    public Color Shine
    {
        get => GetValue(ShineProperty);
        set => SetValue(ShineProperty, value);
    }

    /// <summary>The text box drawn again: the composer's.</summary>
    internal TextBox? Box
    {
        get => _box;
        set
        {
            if (ReferenceEquals(_box, value)) return;
            if (_box != null) _box.TemplateApplied -= OnBoxTemplateApplied;
            Restore();
            _box = value;
            if (_box != null) _box.TemplateApplied += OnBoxTemplateApplied;
            UsePresenter(null);
            Update();
        }
    }

    /// <summary>
    /// Where the word is in the box's text (<see cref="UltrathinkKeyword.Find"/>), empty when nothing is to be painted (the claude does
    /// not offer it). An occurrence that was not there before (by its order) starts its shine now.
    /// </summary>
    internal IReadOnlyList<(int Start, int Length)> Words
    {
        get => _words;
        set
        {
            _words = value ?? NoWords;
            if (_shineStarts.Count > _words.Count) _shineStarts.RemoveRange(_words.Count, _shineStarts.Count - _words.Count);
            var now = TokenCountUp.NowMs();
            while (_shineStarts.Count < _words.Count) _shineStarts.Add(now);
            _source = null; // the occurrences changed: lay the text out again
            Update();
        }
    }

    /// <summary>True while the mirror draws the text and the box's own is transparent.</summary>
    internal bool IsPainting => _painting;

    /// <summary>True while the shine's timer runs: some occurrence is still sweeping.</summary>
    internal bool IsShining => _timer.IsEnabled;

    /// <summary>The mirror's layout of the text (made from the presenter's), null while it does not paint.</summary>
    internal TextLayout? Layout => _painting && EnsureLayout() ? _layout : null;

    /// <summary>Where the text's top left is drawn, in the mirror: the presenter's, scrolled with it.</summary>
    internal Point TextOrigin => _presenter?.TranslatePoint(default, this) ?? default;

    /// <summary>The part of the mirror where the box shows text (its scroll viewer's viewport): the text is cut to it.</summary>
    internal Rect Viewport
    {
        get
        {
            if (_presenter == null) return default;
            var shown = (Visual?)_presenter.FindAncestorOfType<ScrollContentPresenter>() ?? _presenter;
            var at = shown.TranslatePoint(default, this);
            return at == null ? default : new Rect(at.Value, shown.Bounds.Size);
        }
    }

    private bool ReduceMotion => GetValue(ReduceMotionProperty);

    /// <summary>
    /// Where the shine of occurrence <paramref name="occurrence"/> is: from 1 (100 %, left of the word) to 0 (right of it) in each sweep;
    /// null when it does not shine (its two sweeps are over, the motion is reduced, or nothing is painted).
    /// </summary>
    internal double? ShinePosition(int occurrence)
    {
        if (!_painting || ReduceMotion || occurrence < 0 || occurrence >= _shineStarts.Count) return null;
        var elapsed = TokenCountUp.NowMs() - _shineStarts[occurrence];
        if (elapsed < 0 || elapsed >= ShineMs * ShineRuns) return null;
        return 1 - (elapsed % ShineMs) / (double)ShineMs;
    }

    /// <summary>A frame of the shine (the timer's tick): draws again, and stops the timer once no occurrence shines.</summary>
    internal void Tick()
    {
        UpdateShine();
        InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        LayoutUpdated += OnLayoutUpdated;
        Update();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        LayoutUpdated -= OnLayoutUpdated;
        Update(); // nothing is painted out of the window: the box gets its color back, and the timer stops
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ReduceMotionProperty)
        {
            UpdateShine();
            InvalidateVisual();
        }
        else if (change.Property == ForegroundProperty || change.Property == RainbowProperty || change.Property == ShineProperty)
        {
            if (change.Property == ForegroundProperty && _selectionForegroundSet) _box!.SelectionForegroundBrush = Foreground;
            if (change.Property == ForegroundProperty && _caretBrushSet) _box!.CaretBrush = Foreground;
            _source = null;
            InvalidateVisual();
        }
    }

    private void OnBoxTemplateApplied(object? sender, Avalonia.Controls.Primitives.TemplateAppliedEventArgs e)
    {
        UsePresenter(null); // a new template, a new presenter: found again when needed
        Update();
    }

    private void UsePresenter(TextPresenter? presenter)
    {
        if (ReferenceEquals(_presenter, presenter)) return;
        if (_presenter != null) _presenter.PropertyChanged -= OnPresenterChanged;
        _presenter = presenter;
        if (_presenter != null) _presenter.PropertyChanged += OnPresenterChanged;
        _source = null;
    }

    private void OnPresenterChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextPresenter.PreeditTextProperty) Update();
        else if (_painting) InvalidateVisual(); // its text, selection or font: the next frame compares its layout with the one used
    }

    /// <summary>After each layout pass: the box may have scrolled, wrapped again or changed size.</summary>
    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (!_painting || _presenter == null) return;
        if (!ReferenceEquals(_presenter.TextLayout, _source) || TextOrigin != _drawnAt || Viewport != _drawnIn) InvalidateVisual();
    }

    /// <summary>Whether to paint, and the box's color and the shine's timer to go with it.</summary>
    private void Update()
    {
        if (_attached && _box != null && _presenter == null)
            UsePresenter(_box.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault());
        var painting = _attached && _box != null && _presenter != null && _words.Count > 0
                       && string.IsNullOrEmpty(_presenter.PreeditText); // text being composed is the box's to draw
        if (painting != _painting)
        {
            _painting = painting;
            if (painting)
            {
                _box!.Foreground = Brushes.Transparent; // its caret has its own brush, and stays
                if (_box.CaretBrush == null)
                {
                    _box.CaretBrush = Foreground;
                    _caretBrushSet = true;
                }
                // its selected text is drawn in the selection's text color; a theme with none draws it in the box's color, now clear
                if (_box.SelectionForegroundBrush == null)
                {
                    _box.SelectionForegroundBrush = Foreground;
                    _selectionForegroundSet = true;
                }
            }
            else Restore();
        }
        if (!painting)
        {
            _source = null;
            _layout = null;
            _shineLayout = null;
        }
        UpdateShine();
        InvalidateVisual();
    }

    /// <summary>The box draws its own text again.</summary>
    private void Restore()
    {
        _painting = false;
        _box?.ClearValue(TextBox.ForegroundProperty);
        if (_selectionForegroundSet) _box?.ClearValue(TextBox.SelectionForegroundBrushProperty);
        if (_caretBrushSet) _box?.ClearValue(TextBox.CaretBrushProperty);
        _selectionForegroundSet = false;
        _caretBrushSet = false;
    }

    private void UpdateShine()
    {
        var shining = false;
        if (_painting && !ReduceMotion)
        {
            for (var i = 0; i < _shineStarts.Count && !shining; i++) shining = ShinePosition(i) != null;
        }
        if (shining && !_timer.IsEnabled) _timer.Start();
        else if (!shining) _timer.Stop();
    }

    /// <summary>Lays the text out again when the presenter has a new layout (or the words or brushes changed); false when it cannot.</summary>
    private bool EnsureLayout()
    {
        if (_presenter == null) return false;
        var source = _presenter.TextLayout;
        if (ReferenceEquals(source, _source) && _layout != null) return true;
        _source = source;
        var text = _presenter.Text ?? "";
        var typeface = new Typeface(_presenter.FontFamily, _presenter.FontStyle, _presenter.FontWeight, _presenter.FontStretch);
        var size = _presenter.FontSize;

        // each occurrence cut at the presenter's line breaks: one gradient per piece
        var pieces = new List<(int Occurrence, int Start, int Length)>();
        for (var n = 0; n < _words.Count; n++)
        {
            var (start, length) = _words[n];
            var end = Math.Min(text.Length, start + length);
            foreach (var line in source.TextLines)
            {
                var from = Math.Max(start, line.FirstTextSourceIndex);
                var to = Math.Min(end, line.FirstTextSourceIndex + line.Length);
                if (to > from) pieces.Add((n, from, to - from));
            }
        }

        TextLayout Make(IBrush? rest, IBrush? word)
        {
            // the spans cover the whole text, the gaps with the text's own color: Avalonia 11.0 runs the properties of a span on to the
            // next one when there is a gap between them (two words on a line painted with all that is between them)
            var spans = new List<ValueSpan<TextRunProperties>>();
            var at = 0;
            foreach (var piece in pieces)
            {
                if (piece.Start > at) spans.Add(new ValueSpan<TextRunProperties>(at, piece.Start - at, new GenericTextRunProperties(typeface, size, null, rest)));
                spans.Add(new ValueSpan<TextRunProperties>(piece.Start, piece.Length, new GenericTextRunProperties(typeface, size, null, word)));
                at = piece.Start + piece.Length;
            }
            if (at < text.Length) spans.Add(new ValueSpan<TextRunProperties>(at, text.Length - at, new GenericTextRunProperties(typeface, size, null, rest)));
            return new TextLayout(
                text, typeface, size, rest, _presenter.TextAlignment, _presenter.TextWrapping, TextTrimming.None, null,
                _presenter.FlowDirection, source.MaxWidth, source.MaxHeight, _presenter.LineHeight, _presenter.LetterSpacing, source.MaxLines,
                spans);
        }

        _layout = Make(Foreground, Rainbow);
        _shineLayout = Make(Brushes.Transparent, new ImmutableSolidColorBrush(Shine));
        _fragments = pieces
            .Select(p => new Fragment(p.Occurrence, _layout.HitTestTextRange(p.Start, p.Length).Aggregate((a, b) => a.Union(b))))
            .ToList();
        return true;
    }

    public override void Render(DrawingContext context)
    {
        if (!_painting || !EnsureLayout()) return;
        var origin = TextOrigin;
        var viewport = Viewport;
        _drawnAt = origin;
        _drawnIn = viewport;
        using (context.PushClip(viewport))
        {
            _layout!.Draw(context, origin);
            foreach (var fragment in _fragments)
            {
                if (ShinePosition(fragment.Occurrence) is not { } position) continue;
                var piece = fragment.Rect.Translate((Vector)origin);
                var image = new Rect(piece.X - 2 * piece.Width * position, piece.Y, 3 * piece.Width, piece.Height);
                using (context.PushClip(piece))
                using (context.PushOpacityMask(ShineMask(image), image))
                    _shineLayout!.Draw(context, origin);
            }
        }
    }

    /// <summary>
    /// The shine over <paramref name="image"/> (three times the piece's width): CSS's <c>linear-gradient(100deg, transparent 40%,
    /// shine 50%, transparent 60%)</c>, as an opacity mask whose line runs at 100° (right and a little down) through the image's
    /// center, as long as the image seen along it.
    /// </summary>
    private static IBrush ShineMask(Rect image)
    {
        const double angle = 100 * Math.PI / 180;
        var direction = new Vector(Math.Sin(angle), -Math.Cos(angle));
        var length = Math.Abs(image.Width * Math.Sin(angle)) + Math.Abs(image.Height * Math.Cos(angle));
        var center = image.Center;
        var half = direction * (length / 2);
        var stops = new[]
        {
            new ImmutableGradientStop(0.4, Color.FromArgb(0, 255, 255, 255)),
            new ImmutableGradientStop(0.5, Color.FromArgb(255, 255, 255, 255)),
            new ImmutableGradientStop(0.6, Color.FromArgb(0, 255, 255, 255)),
        };
        return new ImmutableLinearGradientBrush(
            stops, 1, null, null, GradientSpreadMethod.Pad,
            new RelativePoint(center - half, RelativeUnit.Absolute), new RelativePoint(center + half, RelativeUnit.Absolute));
    }
}
