using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.VisualTree;
using ClaudeVisualChat.Controls;
using ClaudeVisualChat.Protocol;
using Xunit;

namespace ClaudeVisualChat.Tests.Ui;

/// <summary>The word <c>ultrathink</c> in the reply box, painted in a rainbow with a shine while the claude offers it.</summary>
public class UltrathinkMirrorTests
{
    private static (ClaudeChatView View, Window Window, ChatConversation Conversation) Open(bool available = true, bool reduceMotion = false)
    {
        var c = new ChatConversation();
        if (available) c.Apply(new SessionStarted("s", "claude-opus-5-5", null, null, null) { UltrathinkAvailable = true });
        var view = new ClaudeChatView { Conversation = c, ReduceMotion = reduceMotion };
        return (view, UiHelpers.Show(view), c);
    }

    private static UltrathinkMirror Mirror(ClaudeChatView view) =>
        UiHelpers.Composer(view).GetVisualDescendants().OfType<UltrathinkMirror>().Single();

    private static TextPresenter Presenter(TextBox input) => input.GetVisualDescendants().OfType<TextPresenter>().Single();

    private static void Write(TextBox input, string text)
    {
        input.Text = text;
        input.CaretIndex = text.Length;
        UiHelpers.Pump();
    }

    private static IBrush Normal(Control view) => (IBrush)view.FindResource("ClaudeChat.Foreground")!;

    private static void WithClock(long start, Action<Action<long>> body)
    {
        var now = start;
        TokenCountUp.ClockOverride = () => now;
        try { body(by => now += by); }
        finally { TokenCountUp.ClockOverride = null; }
    }

    /// <summary>Where the box draws characters <paramref name="start"/> to <paramref name="start"/>+<paramref name="length"/>, in the window.</summary>
    private static Rect Where(TextBox input, Window window, int start, int length)
    {
        var presenter = Presenter(input);
        var at = presenter.TranslatePoint(default, window)!.Value;
        var rects = presenter.TextLayout.HitTestTextRange(start, length).ToList();
        var union = rects[0];
        foreach (var r in rects.Skip(1)) union = union.Union(r);
        return union.Translate(at);
    }

    // ---- the frame, read back ----

    private sealed class Frame
    {
        private readonly byte[] _bytes;
        private readonly int _stride;
        private readonly bool _bgra;

        public Frame(Window window)
        {
            UiHelpers.DrawFrames();
            using var bitmap = window.CaptureRenderedFrame()!;
            using var buffer = bitmap.Lock();
            _stride = buffer.RowBytes;
            Width = buffer.Size.Width;
            Height = buffer.Size.Height;
            _bgra = buffer.Format == PixelFormat.Bgra8888;
            _bytes = new byte[_stride * Height];
            System.Runtime.InteropServices.Marshal.Copy(buffer.Address, _bytes, 0, _bytes.Length);
        }

        public int Width { get; }

        public int Height { get; }

        public Color this[int x, int y]
        {
            get
            {
                var i = y * _stride + x * 4;
                return _bgra
                    ? Color.FromArgb(_bytes[i + 3], _bytes[i + 2], _bytes[i + 1], _bytes[i])
                    : Color.FromArgb(_bytes[i + 3], _bytes[i], _bytes[i + 1], _bytes[i + 2]);
            }
        }

        public IEnumerable<Color> In(Rect rect)
        {
            for (var y = (int)Math.Ceiling(rect.Top); y < (int)Math.Floor(rect.Bottom); y++)
            {
                for (var x = (int)Math.Ceiling(rect.Left); x < (int)Math.Floor(rect.Right); x++)
                {
                    if (x >= 0 && y >= 0 && x < Width && y < Height) yield return this[x, y];
                }
            }
        }
    }

    // the text is drawn with subpixel antialiasing, whose fringes are colored: a color counts as the rainbow's only where one of its
    // channels goes past the normal text's (#BFBFBF, 191), which no fringe of it does

    /// <summary>The rainbow's start: red, orange, yellow.</summary>
    private static bool Warm(Color c) => c.R >= 200 && c.R - c.B >= 80;

    /// <summary>The rainbow's end: blue, violet.</summary>
    private static bool Cool(Color c) => c.B >= 196 && c.B - c.G >= 20;

    private static bool Rainbow(Color c) => Warm(c) || Cool(c);

    private static int Light(Color c) => (c.R + c.G + c.B) / 3;

    private static Rect Third(Rect r, int which) => new Rect(r.X + r.Width * which / 3, r.Y, r.Width / 3, r.Height);

    // ---- the tests ----

    [AvaloniaFact]
    public void Nothing_is_painted_while_ultrathink_is_unavailable()
    {
        var (view, window, c) = Open(available: false);
        var input = UiHelpers.Input(view);
        var mirror = Mirror(view);
        Write(input, "please ultrathink about it");
        Assert.False(mirror.IsPainting);
        Assert.Same(Normal(view), input.Foreground);
        var frame = new Frame(window);
        Assert.DoesNotContain(frame.In(Where(input, window, 7, 10)), Rainbow); // the box's own text, in its own color

        // the claude offers it: painted
        c.Apply(new SessionStarted("s", "claude-opus-5-5", null, null, null) { UltrathinkAvailable = true });
        UiHelpers.Pump();
        Assert.True(mirror.IsPainting);

        // a turn that changes the effort turns it off again: the box's own color comes back
        c.Apply(new PerTurnEffortChanged());
        UiHelpers.Pump();
        Assert.False(mirror.IsPainting);
        Assert.Same(Normal(view), input.Foreground);

        // available, but no word to paint: nothing either
        c.Apply(new SettingsApplied(null, null, false) { UltrathinkAvailable = true });
        Write(input, "please think about it");
        Assert.False(mirror.IsPainting);
        Assert.Same(Normal(view), input.Foreground);
        // nor in a command
        Write(input, "/ultrathink");
        Assert.False(mirror.IsPainting);
        window.Close();
    }

    [AvaloniaFact]
    public void The_word_is_painted_and_the_box_text_goes_transparent_keeping_the_caret()
    {
        var (view, window, _) = Open();
        var input = UiHelpers.Input(view);
        var mirror = Mirror(view);
        input.Focus();
        UiHelpers.Type(window, "please ultrathink about it");
        Assert.True(mirror.IsPainting);
        Assert.Equal(new[] { (7, 10) }, mirror.Words.ToArray());
        Assert.Equal(0, ((ISolidColorBrush)input.Foreground!).Color.A); // the box draws no text
        Assert.Same(Normal(view), input.CaretBrush);                   // but its caret, in the normal color
        Assert.False(mirror.IsHitTestVisible);

        // the mirror draws the text: the word's left in red, its right in violet, the words around it in the normal grey
        var frame = new Frame(window);
        var word = Where(input, window, 7, 10);
        Assert.Contains(frame.In(Third(word, 0)), Warm);
        Assert.DoesNotContain(frame.In(Third(word, 0)), Cool);
        Assert.Contains(frame.In(Third(word, 2)), Cool);
        Assert.DoesNotContain(frame.In(Third(word, 2)), Warm);
        var please = Where(input, window, 0, 6);
        Assert.Contains(frame.In(please), p => Light(p) > 120);   // drawn, though the box draws none
        Assert.DoesNotContain(frame.In(please), Rainbow);
        var about = Where(input, window, 18, 5);
        Assert.Contains(frame.In(about), p => Light(p) > 120);
        Assert.DoesNotContain(frame.In(about), Rainbow);

        // two on a line: each its own rainbow, and the word between them in the normal grey
        Write(input, "ultrathink and ultrathink");
        frame = new Frame(window);
        foreach (var start in new[] { 0, 15 })
        {
            var each = Where(input, window, start, 10);
            Assert.Contains(frame.In(Third(each, 0)), Warm);
            Assert.DoesNotContain(frame.In(Third(each, 0)), Cool);
            Assert.Contains(frame.In(Third(each, 2)), Cool);
            Assert.DoesNotContain(frame.In(Third(each, 2)), Warm);
        }
        var and = Where(input, window, 11, 3);
        Assert.Contains(frame.In(and), p => Light(p) > 120);
        Assert.DoesNotContain(frame.In(and), Rainbow);

        // the word taken away: the box's own text again
        Write(input, "please about it");
        Assert.False(mirror.IsPainting);
        Assert.Same(Normal(view), input.Foreground);
        window.Close();
    }

    [AvaloniaFact]
    public void A_long_paste_is_not_painted()
    {
        var (view, window, _) = Open();
        var input = UiHelpers.Input(view);
        var mirror = Mirror(view);
        ClaudeVisualChat.Imaging.ClipboardImages.HasTextOverride = () => true;
        ClaudeVisualChat.Imaging.ClipboardImages.HasImageOverride = () => false;
        try
        {
            input.Focus();
            UiHelpers.Type(window, "ultrathink ");
            // a paste of more than two lines is remembered for the message: its words are the paste's, not the user's
            window.Clipboard!.SetTextAsync("notes\nultrathink in a pasted log\nmore\nlines").Wait();
            UiHelpers.Press(window, Avalonia.Input.Key.V, Avalonia.Input.RawInputModifiers.Control);
            UiHelpers.PumpUntil(() => (input.Text ?? "").Length > 11);
            UiHelpers.Pump();
            Assert.Equal(new[] { (0, 10) }, mirror.Words.ToArray());
            // what the user writes after it is painted
            UiHelpers.Type(window, " ultrathink");
            Assert.Equal(2, mirror.Words.Count);
        }
        finally
        {
            try { window.Clipboard?.ClearAsync().Wait(); } catch (Exception) { /* nothing left to clear */ }
            ClaudeVisualChat.Imaging.ClipboardImages.HasTextOverride = null;
            ClaudeVisualChat.Imaging.ClipboardImages.HasImageOverride = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void The_touch_layout_lines_up_too()
    {
        var c = new ChatConversation();
        c.Apply(new SessionStarted("s", "claude-opus-5-5", null, null, null) { UltrathinkAvailable = true });
        var view = UiHelpers.TouchView(c);
        var window = UiHelpers.ShowPhone(view);
        var input = UiHelpers.Input(view);
        var mirror = Mirror(view);
        Write(input, "and then ultrathink about the whole plan before you write any of it down");
        Assert.True(mirror.IsPainting);
        var frame = new Frame(window);
        var drawnAt = mirror.TranslatePoint(mirror.TextOrigin, window)!.Value;
        var boxAt = Presenter(input).TranslatePoint(default, window)!.Value;
        Assert.Equal(boxAt.X, drawnAt.X, 1);
        Assert.Equal(boxAt.Y, drawnAt.Y, 1);
        Assert.Equal(Presenter(input).TextLayout.TextLines.Count, mirror.Layout!.TextLines.Count);
        Assert.Contains(frame.In(Third(Where(input, window, 9, 10), 0)), Warm);
        window.Close();
    }

    [AvaloniaFact]
    public void Composition_text_paints_nothing()
    {
        var (view, window, _) = Open();
        var input = UiHelpers.Input(view);
        var mirror = Mirror(view);
        input.Focus();
        Write(input, "ultrathink ");
        Assert.True(mirror.IsPainting);
        Presenter(input).PreeditText = "かな";
        UiHelpers.Pump();
        Assert.False(mirror.IsPainting);
        Assert.Same(Normal(view), input.Foreground);
        Presenter(input).PreeditText = null;
        UiHelpers.Pump();
        Assert.True(mirror.IsPainting);
        window.Close();
    }

    [AvaloniaFact]
    public void The_mirror_lines_up_with_the_box_after_it_scrolls()
    {
        var (view, window, _) = Open(reduceMotion: true); // the rainbow alone: a shine passing would whiten the colors read
        var input = UiHelpers.Input(view);
        var mirror = Mirror(view);
        input.Focus();
        // the word, a paragraph that wraps, then lines enough to go past the box's 200 px, and the word at the end
        var text = "ultrathink first, " + string.Join(" ", Enumerable.Repeat("a long sentence that wraps across the width of the reply box", 6)) + "\n"
                   + string.Join("\n", Enumerable.Range(1, 14).Select(i => "line " + i)) + "\nnow ultrathink";
        Write(input, text);
        var presenter = Presenter(input);
        var scroller = input.GetVisualDescendants().OfType<ScrollViewer>().Single();
        Assert.True(input.Bounds.Height <= 200.5);
        Assert.True(scroller.Offset.Y > 0, "the box scrolled to the caret");
        Assert.True(mirror.IsPainting);

        // in a frame drawn now, the word where the box has it: red at its start, violet at its end
        void PaintedAt(int start, string when)
        {
            var frame = new Frame(window);
            var word = Where(input, window, start, 10);
            Assert.True(Viewport(input, window).Contains(word), when + ": the word is in sight");
            Assert.True(frame.In(Third(word, 0)).Any(Warm), when + ": warm at the start");
            Assert.False(frame.In(Third(word, 0)).Any(Cool), when + ": no cool at the start");
            Assert.True(frame.In(Third(word, 2)).Any(Cool), when + ": cool at the end");
            Assert.False(frame.In(Third(word, 2)).Any(Warm), when + ": no warm at the end");
        }

        void SameLines()
        {
            var mine = mirror.Layout!.TextLines;
            var its = presenter.TextLayout.TextLines;
            Assert.Equal(its.Count, mine.Count);
            for (var i = 0; i < its.Count; i++)
            {
                Assert.Equal(its[i].FirstTextSourceIndex, mine[i].FirstTextSourceIndex);
                Assert.Equal(its[i].Length, mine[i].Length);
                Assert.Equal(its[i].WidthIncludingTrailingWhitespace, mine[i].WidthIncludingTrailingWhitespace, 2);
                Assert.Equal(its[i].Height, mine[i].Height, 2);
            }
        }

        scroller.Offset = new Vector(0, scroller.Extent.Height - scroller.Viewport.Height); // the last line whole
        UiHelpers.Pump();
        PaintedAt(text.Length - 10, "scrolled to the end");
        SameLines();
        // the lines scrolled above the text's area are not drawn over the box's top padding
        var frame = new Frame(window);
        var box = input.TranslatePoint(default, window)!.Value;
        var top = scroller.TranslatePoint(default, window)!.Value;
        Assert.True(top.Y - box.Y >= 8);
        Assert.DoesNotContain(frame.In(new Rect(top.X, box.Y + 1, input.Bounds.Width - 50, top.Y - box.Y - 2)), p => Light(p) > 90);

        // back to the top: the first word, where the box has it now
        scroller.Offset = default;
        UiHelpers.Pump();
        PaintedAt(0, "scrolled back");

        // a narrower window wraps it again
        window.Width = 520;
        UiHelpers.Pump();
        Assert.Equal(0, scroller.Offset.Y);
        PaintedAt(0, "narrower");
        SameLines();
        window.Close();
    }

    /// <summary>The part of the box that shows text, in the window.</summary>
    private static Rect Viewport(TextBox input, Window window)
    {
        var viewport = input.GetVisualDescendants().OfType<ScrollContentPresenter>().First();
        return new Rect(viewport.TranslatePoint(default, window)!.Value, viewport.Bounds.Size);
    }

    /// <summary>
    /// The mirror, its word in the box's own color, against the box drawing its own text: the two frames of the text's area match but
    /// for antialiasing (a pixel off by more than 48 in a channel, on at most 1 in 20 of the inked ones).
    /// </summary>
    private static void DrawnLikeTheBox(ChatConversation c, UltrathinkMirror mirror, TextBox input, Window window, string when)
    {
        var area = Viewport(input, window);
        Assert.True(mirror.IsPainting, when);
        var painted = new Frame(window).In(area).ToList();
        c.Apply(new PerTurnEffortChanged()); // the box draws its own text
        UiHelpers.Pump();
        Assert.False(mirror.IsPainting, when);
        Assert.NotSame(Brushes.Transparent, input.Foreground);
        var own = new Frame(window).In(area).ToList();
        c.Apply(new SettingsApplied(null, null, false) { UltrathinkAvailable = true });
        UiHelpers.Pump();

        var ink = own.Count(p => Light(p) > 60);
        var off = painted.Zip(own, (a, b) => Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)))).Count(d => d > 48);
        Assert.True(ink > 200, when + ": text in the area (" + ink + ")");
        Assert.True(off * 20 <= ink, when + ": " + off + " pixels off of " + ink + " inked");
    }

    [AvaloniaFact]
    public void The_mirror_draws_the_text_where_the_box_does()
    {
        var (view, window, c) = Open(reduceMotion: true); // no shine: the word in the plain color
        var input = UiHelpers.Input(view);
        var mirror = Mirror(view);
        mirror.Rainbow = Normal(view);
        var text = "ultrathink first, " + string.Join(" ", Enumerable.Repeat("a long sentence that wraps across the width of the reply box", 6)) + "\n"
                   + string.Join("\n", Enumerable.Range(1, 14).Select(i => "line " + i)) + "\nnow ultrathink";
        Write(input, text);
        var scroller = input.GetVisualDescendants().OfType<ScrollViewer>().Single();
        DrawnLikeTheBox(c, mirror, input, window, "at the top");

        scroller.Offset = new Vector(0, scroller.Extent.Height - scroller.Viewport.Height);
        UiHelpers.Pump();
        Assert.True(scroller.Offset.Y > 100);
        DrawnLikeTheBox(c, mirror, input, window, "scrolled to the end");

        scroller.Offset = new Vector(0, 37);
        UiHelpers.Pump();
        DrawnLikeTheBox(c, mirror, input, window, "scrolled part of a line");

        window.Width = 520;
        UiHelpers.Pump();
        DrawnLikeTheBox(c, mirror, input, window, "narrower");
        window.Close();
    }

    [AvaloniaFact]
    public void A_selected_word_shows_the_normal_selection()
    {
        var (view, window, _) = Open();
        var input = UiHelpers.Input(view);
        var mirror = Mirror(view);
        input.Focus();
        Write(input, "please ultrathink about it");
        input.SelectionStart = 7;
        input.SelectionEnd = 17;
        UiHelpers.Pump();
        Assert.True(mirror.IsPainting);

        var frame = new Frame(window);
        var word = Where(input, window, 7, 10);
        var selection = ((ISolidColorBrush)Presenter(input).SelectionBrush!).Color;
        Assert.Equal(255, selection.A); // opaque: it covers the paint under it
        Assert.DoesNotContain(frame.In(word), Warm);
        Assert.Contains(frame.In(word), p => p == selection);
        // the selected text shows, in the selection's own text color
        Assert.Contains(frame.In(word), p => Light(p) > Light(selection) + 60);
        // the rest of the word's line is still the mirror's
        Assert.Contains(frame.In(Where(input, window, 0, 6)), p => Light(p) > 120);
        window.Close();
    }

    [AvaloniaFact]
    public void Reduced_motion_paints_no_shine()
    {
        WithClock(10_000, advance =>
        {
            var (view, window, _) = Open(reduceMotion: true);
            var input = UiHelpers.Input(view);
            var mirror = Mirror(view);
            Write(input, "ultrathink");
            Assert.True(mirror.IsPainting);
            Assert.False(mirror.IsShining);
            Assert.Null(mirror.ShinePosition(0));
            var word = Where(input, window, 0, 10);
            var still = new Frame(window).In(word).ToList();
            advance(700);
            mirror.Tick();
            Assert.Equal(still, new Frame(window).In(word).ToList()); // nothing moves
            Assert.Contains(still, Rainbow);                           // the rainbow stays

            // turned on later: the word written again shines
            view.ReduceMotion = false;
            Write(input, "");
            Write(input, "ultrathink");
            Assert.True(mirror.IsShining);
            // and turned off while it shines: it stops
            view.ReduceMotion = true;
            UiHelpers.Pump();
            Assert.False(mirror.IsShining);
            Assert.Null(mirror.ShinePosition(0));
            window.Close();
        });
    }

    [AvaloniaFact]
    public void The_shine_runs_twice_and_stops()
    {
        WithClock(10_000, advance =>
        {
            var (view, window, _) = Open();
            var input = UiHelpers.Input(view);
            var mirror = Mirror(view);
            Write(input, "ultrathink");
            var word = Where(input, window, 0, 10);
            Assert.True(mirror.IsShining);
            Assert.Equal(1.0, mirror.ShinePosition(0)!.Value, 3); // from 100 %: the shine left of the word

            int Brightest() => new Frame(window).In(new Rect(word.Center.X - 4, word.Y, 8, word.Height)).Max(Light);
            var atStart = Brightest();
            advance(700);
            mirror.Tick();
            Assert.Equal(0.5, mirror.ShinePosition(0)!.Value, 3);  // half way: over the word's middle
            var halfWay = Brightest();
            Assert.True(halfWay > atStart + 15, atStart + " -> " + halfWay);
            advance(700);
            mirror.Tick();
            Assert.Equal(1.0, mirror.ShinePosition(0)!.Value, 3);  // the second time
            advance(700);
            mirror.Tick();
            Assert.Equal(0.5, mirror.ShinePosition(0)!.Value, 3);
            advance(700);                                          // 2.8 s: done
            mirror.Tick();
            Assert.False(mirror.IsShining);
            Assert.Null(mirror.ShinePosition(0));
            Assert.True(Brightest() < halfWay - 15);
            Assert.True(mirror.IsPainting);                        // the rainbow stays

            // a second word shines alone; the first stays at rest
            Write(input, "ultrathink and ultrathink");
            Assert.True(mirror.IsShining);
            Assert.Null(mirror.ShinePosition(0));
            Assert.Equal(1.0, mirror.ShinePosition(1)!.Value, 3);
            advance(2800);
            mirror.Tick();
            Assert.False(mirror.IsShining);

            // the word written again shines again
            Write(input, "");
            Write(input, "ultrathink");
            Assert.True(mirror.IsShining);
            Assert.Equal(1.0, mirror.ShinePosition(0)!.Value, 3);

            // out of the window, its timer stops
            window.Close();
            Assert.False(mirror.IsShining);
        });
    }
}
