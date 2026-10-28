using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ClaudeVisualChat.Controls;

/// <summary>What the line at the end of the list says it is doing.</summary>
internal enum WorkingState
{
    /// <summary>Claude works: a verb, which changes now and then.</summary>
    Verb,

    /// <summary>The claude compacts the conversation: "Compacting...".</summary>
    Compacting,

    /// <summary>A <c>SessionStart</c> or <c>Setup</c> hook runs: "Running start-up hooks...".</summary>
    StartupHooks,
}

/// <summary>
/// The line at the end of the list while Claude works and nothing is arriving: a star that grows and shrinks (6 glyphs forward and
/// back, 12 frames, one every 120 ms) and a verb picked at random, which changes after 2 s, after 3 s more and then every 5 s, each
/// time written from left to right, one character every 40 ms, with a cursor and "." or "_" over the next three. With
/// <see cref="ClaudeChatView.ReduceMotion"/> the star stays still and the words swap whole. Its two timers run only while the line shows
/// in the window: out of it they stop, and coming back draws the line again; the time comes from <see cref="TokenCountUp.NowMs"/>.
/// </summary>
internal sealed class WorkingIndicator : Grid
{
    /// <summary>How often the star moves.</summary>
    internal static readonly TimeSpan GlyphInterval = TimeSpan.FromMilliseconds(120);

    /// <summary>How long one character takes to be written.</summary>
    internal const long RevealStepMs = 40;

    // the six stars, small to large; the still one is the fifth
    private static readonly string[] Stars = { "·", "✢", "✳", "✶", "✻", "✽" };
    private const string StillStar = "✻";

    // forward then back, the two ends twice: 12 frames
    private const int FrameCount = 12;

    private static readonly StyledProperty<ChatStrings> StringsProperty = ClaudeChatView.StringsProperty.AddOwner<WorkingIndicator>();
    private static readonly StyledProperty<bool> ReduceMotionProperty = ClaudeChatView.ReduceMotionProperty.AddOwner<WorkingIndicator>();

    private readonly TextBlock _glyph = new TextBlock { Text = StillStar };
    private readonly TextBlock _words = new TextBlock();
    private readonly TextBlock _room = new TextBlock(); // the whole text, unseen, so the line keeps its width while it is written
    private readonly DispatcherTimer _glyphTimer = new DispatcherTimer { Interval = GlyphInterval };
    private readonly DispatcherTimer _wordTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RevealStepMs) };
    private readonly Random _random = new Random();

    /// <summary>Styled as a Grid: the theme's <c>Grid.ClaudeChatWorking</c>.</summary>
    protected override Type StyleKeyOverride => typeof(Grid);

    private WorkingState _state;
    private IReadOnlyList<string>? _custom;
    private bool _replace;
    private bool _attached;
    private string _verb = "";
    private string _full = "";
    private int _frame;
    private int _changes;
    private long _verbAt;
    private long _writtenAt;

    public WorkingIndicator()
    {
        ColumnDefinitions = new ColumnDefinitions("Auto,*");
        _glyph.Classes.Add("ClaudeChatWorkingGlyph");
        _words.Classes.Add("ClaudeChatWorkingText");
        _room.Classes.Add("ClaudeChatWorkingText");
        _room.Opacity = 0;
        var wordsBox = new Grid { VerticalAlignment = VerticalAlignment.Center };
        wordsBox.Classes.Add("ClaudeChatWorkingWords");
        wordsBox.Children.Add(_room);
        wordsBox.Children.Add(_words);
        SetColumn(wordsBox, 1);
        Children.Add(_glyph);
        Children.Add(wordsBox);
        // a screen reader reads the state once, not every verb
        AutomationProperties.SetAccessibilityView(_glyph, AccessibilityView.Raw);
        AutomationProperties.SetAccessibilityView(wordsBox, AccessibilityView.Raw);
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
        _glyphTimer.Tick += (_, _) => StepGlyph();
        _wordTimer.Tick += (_, _) => Tick();
    }

    /// <summary>What the line says; set by the view.</summary>
    internal WorkingState State
    {
        get => _state;
        set
        {
            if (_state == value) return;
            _state = value;
            if (_attached) Refresh(true);
        }
    }

    /// <summary>The host's verbs and whether they replace ours or come after them (see <see cref="ClaudeChatView.SpinnerVerbs"/>).</summary>
    internal void SetVerbs(IReadOnlyList<string>? custom, bool replace)
    {
        _custom = custom;
        _replace = replace;
        if (_attached && !Pool().Contains(_verb)) Refresh(false);
    }

    /// <summary>True while the star's timer waits for the next frame.</summary>
    internal bool IsGlyphMoving => _glyphTimer.IsEnabled;

    /// <summary>True while the words' timer waits (to write the next character, or to change the verb).</summary>
    internal bool IsTicking => _wordTimer.IsEnabled;

    /// <summary>What the line says now, as drawn (a verb half written shows half).</summary>
    internal string Shown => _words.Text ?? "";

    /// <summary>The whole text of the line, as it will be once written.</summary>
    internal string FullText => _full;

    /// <summary>The verb now, without its dots.</summary>
    internal string Verb => _verb;

    /// <summary>The star now.</summary>
    internal string Glyph => _glyph.Text ?? "";

    private ChatStrings Strings => GetValue(StringsProperty) ?? ChatStrings.English;

    private bool ReduceMotion => GetValue(ReduceMotionProperty);

    private List<string> Pool() => PoolOf(_custom, _replace, Strings);

    /// <summary>The verbs to pick from: <paramref name="custom"/> alone when it <paramref name="replace"/>s ours and has any; else ours and then it.</summary>
    internal static List<string> PoolOf(IReadOnlyList<string>? custom, bool replace, ChatStrings strings)
    {
        var pool = new List<string>();
        if (custom != null)
        {
            foreach (var verb in custom)
            {
                var trimmed = verb?.Trim();
                if (!string.IsNullOrEmpty(trimmed) && !pool.Contains(trimmed!)) pool.Add(trimmed!);
            }
        }
        if (replace && pool.Count > 0) return pool;
        var own = new List<string>(strings.WorkingVerbs);
        if (own.Count == 0) own.Add(strings.Working.TrimEnd('…', '.'));
        foreach (var verb in pool)
            if (!own.Contains(verb)) own.Add(verb);
        return own;
    }

    /// <summary>How long a verb stays before the one after it: 2 s, then 3 s, then 5 s.</summary>
    internal static long DelayMs(int changes) => changes == 0 ? 2000 : changes == 1 ? 3000 : 5000;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Refresh(_verb.Length == 0); // back in the window it goes on with the verb it had
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        _glyphTimer.Stop(); // Avalonia is taking the tree apart: nothing runs, and coming back draws the line again
        _wordTimer.Stop();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (!_attached) return;
        if (change.Property == IsVisibleProperty) Refresh(IsVisible);
        else if (change.Property == StringsProperty) Refresh(false);
        else if (change.Property == ReduceMotionProperty) Refresh(false);
    }

    /// <summary>The star moves to its next frame (the timer's tick).</summary>
    internal void StepGlyph()
    {
        if (!_attached || !IsVisible) return; // a late tick, out of the window or hidden, has nothing to draw
        _frame = (_frame + 1) % FrameCount;
        _glyph.Text = ReduceMotion ? StillStar : Stars[FrameStar(_frame)];
    }

    // 0..5 going up, then 5..0 coming back
    private static int FrameStar(int frame) => frame < 6 ? frame : 11 - frame;

    /// <summary>
    /// Draws the line again: the verb after its time, the characters written so far. The words' timer calls it; it also schedules the
    /// timer's next call.
    /// </summary>
    internal void Tick()
    {
        if (!_attached || !IsVisible) return;
        var now = TokenCountUp.NowMs();
        if (_state == WorkingState.Verb && now - _verbAt >= DelayMs(_changes))
        {
            _changes++;
            _verbAt = now;
            _verb = Pick(_verb);
            Start(now);
        }
        Draw(now);
    }

    private string TextFor() => _state switch
    {
        WorkingState.Compacting => Strings.CompactingLine,
        WorkingState.StartupHooks => Strings.StartupHooksLine,
        _ => _verb + "...",
    };

    // a new text to write, from now
    private void Start(long now)
    {
        _full = TextFor();
        _writtenAt = now;
        _room.Text = _full;
    }

    /// <param name="restart">True to begin again: a state that changed, or the line that came into view.</param>
    private void Refresh(bool restart)
    {
        if (!IsVisible || !_attached)
        {
            _glyphTimer.Stop();
            _wordTimer.Stop();
            return;
        }
        var now = TokenCountUp.NowMs();
        if (restart)
        {
            _changes = 0;
            _verbAt = now;
            _verb = Pick(_verb);
            Start(now);
        }
        else
        {
            if (!Pool().Contains(_verb)) _verb = Pick(_verb); // the host's verbs changed, or the language did
            if (TextFor() != _full) Start(now);
        }
        var still = ReduceMotion;
        if (still)
        {
            _glyphTimer.Stop();
            _glyph.Text = StillStar;
        }
        else if (!_glyphTimer.IsEnabled)
        {
            _glyph.Text = Stars[FrameStar(_frame)];
            _glyphTimer.Start();
        }
        AutomationProperties.SetName(this, _state == WorkingState.Compacting ? Strings.CompactingAnnouncement : _state == WorkingState.StartupHooks ? Strings.StartupHooksLine : Strings.WorkingAnnouncement);
        Draw(now);
    }

    private string Pick(string current)
    {
        var pool = Pool();
        if (pool.Count == 1) return pool[0];
        var at = pool.IndexOf(current);
        var next = _random.Next(at < 0 ? pool.Count : pool.Count - 1);
        return pool[at >= 0 && next >= at ? next + 1 : next];
    }

    // the text as it stands at `now`, and the timer for what comes next
    private void Draw(long now)
    {
        var length = _full.Length;
        var written = ReduceMotion ? length : (int)Math.Min(length, Math.Max(0, now - _writtenAt) / RevealStepMs);
        if (written >= length)
        {
            _words.Text = _full;
            // all written: the timer waits for the next verb (a state with one text has nothing to wait for)
            if (_state != WorkingState.Verb)
            {
                _wordTimer.Stop();
                return;
            }
            var wait = DelayMs(_changes) - (now - _verbAt);
            _wordTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(RevealStepMs, wait));
        }
        else
        {
            var cursor = new System.Text.StringBuilder(_full.Substring(0, written)).Append('|');
            for (var i = written + 1; i < Math.Min(length, written + 4); i++)
                cursor.Append(_random.Next(2) == 0 ? '.' : '_');
            _words.Text = cursor.ToString();
            _wordTimer.Interval = TimeSpan.FromMilliseconds(RevealStepMs);
        }
        if (!_wordTimer.IsEnabled) _wordTimer.Start();
    }
}
