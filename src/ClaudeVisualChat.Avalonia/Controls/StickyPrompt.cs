using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The extension's sticky prompt, over row 0 of the view: the prompt of the turn on screen, pinned to the top of the list while its own
/// row is above it, in a band of the side bar's color (14 px over the bubble, 12 px under it, the last 12 px fading out). The view says
/// which prompt (<see cref="Show"/>) and where the next prompt's row begins, which pushes the band up and out. The bubble spans the list's
/// width, folds a long text as the row does (the same item's state), and is at most <see cref="CapHeight"/> tall, scrolling inside. A click
/// on it with no text selected waits <see cref="ClickDelay"/> (a double click selects a word instead), then asks the view to bring the start
/// of the turn to the top (<see cref="TurnRequested"/>); a press that moves more than <see cref="ClickSlop"/> px (<see cref="TouchClickSlop"/>
/// in the touch layout), or scrolls the bubble's text, is a drag and no click. A finger dragged over the band that the bubble's text does
/// not take goes to the list
/// (<see cref="ListDragged"/>), as the wheel does through the view. It follows the 0.3.1 rule: out of the window it lets go of its prompt and stops the
/// click's timer; back, it is drawn as the prompt is then.
/// </summary>
internal sealed class StickyPrompt : Border
{
    /// <summary>The band over and under the bubble; the lower part fades out.</summary>
    internal const double TopSpace = 14, BottomSpace = 12;

    /// <summary>How long a click waits before it scrolls, so a double click that selects a word does not.</summary>
    internal static readonly TimeSpan ClickDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>A press that moves farther than this before it is let go is a drag, not a click.</summary>
    internal const double ClickSlop = 4;

    /// <summary><see cref="ClickSlop"/> in the touch layout: a finger rolls a little as it taps.</summary>
    internal const double TouchClickSlop = 10;

    private static readonly StyledProperty<ChatLayout> LayoutProperty =
        ClaudeChatView.LayoutProperty.AddOwner<StickyPrompt>();

    private readonly Panel _band = new Panel();
    private readonly TranslateTransform _push = new TranslateTransform();
    private readonly ScrollViewer _scroller = new ScrollViewer
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };
    private readonly MessageAttachments _attachments = new MessageAttachments();
    private readonly SelectableTextBlock _text = new SelectableTextBlock();
    private readonly FoldedText _fold = new FoldedText();
    private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = ClickDelay };

    private bool _attached;
    private UserMessageItem? _item;     // the prompt shown
    private UserMessageItem? _tracked;  // the prompt listened to: the one shown, only in the window
    private double _nextTop = double.PositiveInfinity;

    // the press on the bubble a release may be the click of: where it was (from the top level) and where the bubble's own text had
    // scrolled to then
    private Point? _pressedAt;
    private Vector _pressedScroll;

    // a finger dragging over the band: the pointer, where it was last, and where the bubble's own text had scrolled to then
    private IPointer? _touch;
    private Point _touchAt;
    private Vector _touchScroll;

    public StickyPrompt()
    {
        Fill = new Rectangle { Margin = new Thickness(0, 0, 0, BottomSpace) };
        Fill.Classes.Add("ClaudeChatStickyFill");
        Fade = new Rectangle { Height = BottomSpace, VerticalAlignment = VerticalAlignment.Bottom };
        Fade.Classes.Add("ClaudeChatStickyFade");

        _text.Classes.Add("ClaudeChatUserText");
        _fold.Content = _text;
        _fold.PropertyChanged += OnFoldChanged;
        _scroller.Content = new StackPanel { Children = { _attachments, _fold } };
        Bubble = new Border { Margin = new Thickness(0, TopSpace, 0, BottomSpace), Cursor = new Cursor(StandardCursorType.Hand), Child = _scroller };
        Bubble.Classes.Add("ClaudeChatStickyBubble"); // the look of a prompt's bubble (Border.ClaudeChatUser), under a class of its own
        Bubble.AddHandler(PointerPressedEvent, OnBubblePressed, RoutingStrategies.Bubble, handledEventsToo: true);
        Bubble.AddHandler(PointerReleasedEvent, OnBubbleReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        // a finger over the band (the bubble's text takes it first, when it can still scroll)
        AddHandler(PointerPressedEvent, OnPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, (_, _) => _touch = null, RoutingStrategies.Bubble, handledEventsToo: true);

        _band.Children.Add(Fill);
        _band.Children.Add(Fade);
        _band.Children.Add(Bubble);
        _band.RenderTransform = _push;
        _band.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty) Place();
        };
        Child = _band;
        ClipToBounds = true; // pushed up, the part above the list is not drawn
        IsVisible = false;
        _timer.Tick += (_, _) => OnTick();
    }

    /// <summary>The view brings the start of <see cref="Item"/>'s turn to the top: a click on the bubble asked for it.</summary>
    internal event EventHandler<UserMessageItem>? TurnRequested;

    /// <summary>
    /// A finger dragged over the band by this many px (down is positive) that the bubble's own text did not take: the view scrolls the
    /// list by it, as the finger would have on the list.
    /// </summary>
    internal event EventHandler<double>? ListDragged;

    /// <summary>The prompt shown; null while nothing is pinned.</summary>
    internal UserMessageItem? Item => _item;

    /// <summary>The band, which the next prompt pushes up.</summary>
    internal Panel Band => _band;

    /// <summary>The prompt's bubble.</summary>
    internal Border Bubble { get; }

    /// <summary>The band's color, down to where it fades.</summary>
    internal Rectangle Fill { get; }

    /// <summary>The band's last 12 px, fading out.</summary>
    internal Rectangle Fade { get; }

    /// <summary>True from a click until it scrolls (or does not, a word being selected).</summary>
    internal bool IsWaiting => _timer.IsEnabled;

    /// <summary>The bubble's greatest height: 40 % of the view's (set by the view).</summary>
    internal double CapHeight
    {
        get => Bubble.MaxHeight;
        set => Bubble.MaxHeight = value;
    }

    /// <summary>
    /// Pins <paramref name="item"/>; the next prompt's row begins <paramref name="nextTop"/> px under the top of the list (infinity when
    /// none is drawn), and the band never reaches past it.
    /// </summary>
    internal void Show(UserMessageItem item, double nextTop)
    {
        if (!ReferenceEquals(item, _item))
        {
            _timer.Stop(); // a click on another prompt
            _item = item;
            _text.Text = ChatText.Clip(item.Text, ChatConverters.MaxTextChars);
            _attachments.Images = item.Images;
            _attachments.Files = item.Files;
            _fold.SetCurrentValue(FoldedText.IsExpandedProperty, item.IsTextExpanded);
            _scroller.Offset = default;
            if (_attached) Track(item);
        }
        _nextTop = nextTop;
        IsVisible = true;
        Place();
    }

    /// <summary>Nothing is pinned.</summary>
    internal void Hide()
    {
        _timer.Stop();
        Track(null);
        IsVisible = false;
        if (_item == null) return;
        _item = null; // nothing of it stays behind, hidden
        _text.Text = null;
        _attachments.Images = null;
        _attachments.Files = null;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        if (_item == null) return;
        Track(_item);
        _fold.SetCurrentValue(FoldedText.IsExpandedProperty, _item.IsTextExpanded); // opened or folded while out
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        _timer.Stop(); // a click does not scroll a view that is out of the window
        _pressedAt = null;
        _touch = null;
        Track(null);   // the prompt outlives the view: let go of its events
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LayoutProperty) PseudoClasses.Set(":touch", change.GetNewValue<ChatLayout>() == ChatLayout.Touch);
    }

    private void Track(UserMessageItem? item)
    {
        if (ReferenceEquals(item, _tracked)) return;
        if (_tracked != null) _tracked.PropertyChanged -= OnItemChanged;
        _tracked = item;
        if (_tracked != null) _tracked.PropertyChanged += OnItemChanged;
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UserMessageItem.IsTextExpanded) && sender is UserMessageItem item)
            _fold.SetCurrentValue(FoldedText.IsExpandedProperty, item.IsTextExpanded);
    }

    /// <summary>"Show more" or "Show less" in the pinned bubble: the prompt's state, which its row follows too.</summary>
    private void OnFoldChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == FoldedText.IsExpandedProperty && _attached && _item != null) _item.IsTextExpanded = _fold.IsExpanded;
    }

    /// <summary>The band at the top, or pushed up by the next prompt's row so that it ends where that row begins.</summary>
    private void Place()
    {
        var height = _band.Bounds.Height;
        var y = height > 0 ? Math.Min(0, _nextTop - height) : 0;
        if (!_push.Y.Equals(y)) _push.Y = y;
    }

    private void OnBubblePressed(object? sender, PointerPressedEventArgs e)
    {
        _pressedAt = e.GetPosition(null);
        _pressedScroll = _scroller.Offset;
    }

    /// <summary>
    /// A click on the bubble, on its text or around it, but not on its buttons or its scroll bar, with nothing selected; not the end of a
    /// drag (the pointer moved, or the bubble's text scrolled under it).
    /// </summary>
    private void OnBubbleReleased(object? sender, PointerReleasedEventArgs e)
    {
        var pressedAt = _pressedAt;
        _pressedAt = null;
        if (e.InitialPressMouseButton != MouseButton.Left || _item == null || pressedAt == null) return;
        var moved = e.GetPosition(null) - pressedAt.Value;
        var slop = GetValue(LayoutProperty) == ChatLayout.Touch ? TouchClickSlop : ClickSlop;
        if (Math.Abs(moved.X) > slop || Math.Abs(moved.Y) > slop || _scroller.Offset != _pressedScroll) return;
        if (e.Source is Visual source && (source.FindAncestorOfType<Button>(includeSelf: true) != null
                                          || source.FindAncestorOfType<ScrollBar>(includeSelf: true) != null))
            return;
        if (!string.IsNullOrEmpty(_text.SelectedText)) return;
        _timer.Stop();
        _timer.Start();
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Pointer.Type != PointerType.Touch) return;
        _touch = e.Pointer;
        _touchAt = e.GetPosition(null);
        TouchPressed();
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (!ReferenceEquals(e.Pointer, _touch)) return;
        var at = e.GetPosition(null);
        var dy = at.Y - _touchAt.Y;
        _touchAt = at;
        TouchMoved(dy);
    }

    /// <summary>A finger came down on the band: where the bubble's text has scrolled to, to tell later whether it took the drag.</summary>
    internal void TouchPressed() => _touchScroll = _scroller.Offset;

    /// <summary>
    /// The finger moved by <paramref name="dy"/> px: what the bubble's text took (it scrolled in this move) stays with it; anything else
    /// goes to the list (<see cref="ListDragged"/>).
    /// </summary>
    internal void TouchMoved(double dy)
    {
        var scroll = _scroller.Offset;
        var taken = scroll != _touchScroll;
        _touchScroll = scroll;
        if (!taken && dy != 0 && _attached) ListDragged?.Invoke(this, dy);
    }

    /// <summary>The timer's tick, 300 ms after a click: the view scrolls to the turn, unless a word was selected meanwhile.</summary>
    internal void Tick() => OnTick();

    private void OnTick()
    {
        _timer.Stop();
        if (!_attached || _item == null || !string.IsNullOrEmpty(_text.SelectedText)) return; // a word was selected meanwhile
        TurnRequested?.Invoke(this, _item);
    }
}
