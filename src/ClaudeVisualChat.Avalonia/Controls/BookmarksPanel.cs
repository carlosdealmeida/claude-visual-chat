using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The extension's bookmarks panel: the replies the user marked with the bookmark button under them
/// (<see cref="ChatConversation.Bookmarks"/>), in a side panel that is not a modal. It sits at the right of the conversation, 30 % of the
/// view wide (240 to 420 px) behind a 1 px line, or, in a view of <see cref="ClaudeChatView.NarrowWidth"/> px or less and always in the
/// touch layout, below it, 45 % of the view high (180 to 360 px). A bar of 6 px on its inner edge resizes it: dragged, or moved by the
/// arrow keys 16 px at a time, down to 200 px (140 below) and up to 70 % of the view; a double click brings back the size it opens at.
/// Its title and its × come first; then the list, one row to a reply (its first line, and how long ago it was written), at most 35 % of
/// the panel high, or a line telling how to bookmark while there is none; then the reply picked: "Show in conversation" (the view's job,
/// <see cref="ShowInConversation"/>) or "No longer shown in the conversation", "Remove bookmark", and the whole reply. It takes the
/// keyboard when it opens and gives it back to what had it (<see cref="FocusReturn"/>), or lets the view put it back
/// (<see cref="FocusReleased"/>). The picked reply is the panel's; whether a reply is bookmarked is the reply's own
/// (<see cref="AssistantTextItem.IsBookmarked"/>).
/// </summary>
internal sealed class BookmarksPanel : Border
{
    private static readonly StyledProperty<ChatStrings> StringsProperty =
        ClaudeChatView.StringsProperty.AddOwner<BookmarksPanel>();

    private static readonly StyledProperty<ChatLayout> LayoutProperty =
        ClaudeChatView.LayoutProperty.AddOwner<BookmarksPanel>();

    /// <summary>How much of the view the panel takes beside the conversation, and between which widths.</summary>
    internal const double SideShare = 0.30, SideMin = 240, SideMax = 420;

    /// <summary>The same below the conversation, as a share of the view's height.</summary>
    internal const double StackedShare = 0.45, StackedMin = 180, StackedMax = 360;

    /// <summary>The smallest the resize bar leaves it (beside the conversation, below it), and the share of the view it never passes.</summary>
    internal const double SideLeast = 200, StackedLeast = 140, MostShare = 0.70;

    /// <summary>The resize bar's width, and what an arrow key moves it by.</summary>
    internal const double HandleThickness = 6, KeyStep = 16;

    /// <summary>The list never takes more than this share of the panel's height.</summary>
    internal const double ListShare = 0.35;

    private readonly TextBlock _title = new TextBlock();
    private readonly Button _close;
    private readonly Border _handle = new Border();
    private readonly Border _handleLine = new Border();
    private readonly StackPanel _rows = new StackPanel();
    private readonly ScrollViewer _listScroller;
    private readonly TextBlock _empty = new TextBlock();
    private readonly TextBlock _hint = new TextBlock();
    private readonly Border _selection = new Border();
    private readonly Button _show;
    private readonly TextBlock _notShown = new TextBlock();
    private readonly Button _remove;
    private readonly MarkdownBlock _reply = new MarkdownBlock();
    private readonly List<(Button Row, AssistantTextItem Item)> _built = new List<(Button, AssistantTextItem)>();
    private readonly FocusReturn _return = new FocusReturn();

    private ChatConversation? _conversation;
    private ChatConversation? _tracked;
    private INotifyCollectionChanged? _list;
    private AssistantTextItem? _selected;
    private bool _attached;
    private bool _stacked;
    private double _extent;
    private double? _chosen;

    // a drag of the resize bar: where the pointer was in the window, and how big the panel was
    private bool _dragging;
    private double _dragFrom;
    private double _dragSize;

    public BookmarksPanel()
    {
        Classes.Add("ClaudeChatBookmarks");
        _title.Classes.Add("ClaudeChatBookmarksTitle");
        var icon = new Path();
        icon.Classes.Add("ClaudeChatRemoveIcon");
        var iconBox = new Viewbox { Child = icon };
        iconBox.Classes.Add("ClaudeChatRemoveIcon");
        _close = new Button { Content = iconBox };
        _close.Classes.Add("ClaudeChatButton");
        _close.Classes.Add("ClaudeChatPanelClose");
        _close.Classes.Add("ClaudeChatBookmarksClose");
        _close.Click += (_, _) => Close();
        DockPanel.SetDock(_close, Dock.Right);
        var header = new DockPanel { Children = { _close, _title } };
        header.Classes.Add("ClaudeChatBookmarksHeader");
        DockPanel.SetDock(header, Dock.Top);

        _handleLine.Classes.Add("ClaudeChatBookmarksHandleLine");
        _handle.Child = _handleLine;
        _handle.Classes.Add("ClaudeChatBookmarksHandle");
        _handle.Focusable = true;
        _handle.PointerPressed += OnHandlePressed;
        _handle.PointerMoved += OnHandleMoved;
        _handle.PointerReleased += OnHandleReleased;
        _handle.PointerCaptureLost += (_, _) => _dragging = false;
        _handle.DoubleTapped += (_, e) =>
        {
            ResetSize();
            e.Handled = true;
        };
        _handle.KeyDown += OnHandleKey;

        _empty.Classes.Add("ClaudeChatBookmarksNote");
        _hint.Classes.Add("ClaudeChatBookmarksNote");
        _listScroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _rows,
        };

        _show = new Button();
        _show.Classes.Add("ClaudeChatButton");
        _show.Classes.Add("ClaudeChatBookmarkLink");
        _show.Click += (_, _) => ShowSelected();
        _notShown.Classes.Add("ClaudeChatBookmarkNotShown");
        _remove = new Button();
        _remove.Classes.Add("ClaudeChatButton");
        _remove.Classes.Add("ClaudeChatBookmarkRemove");
        _remove.Click += (_, _) => RemoveSelected();
        DockPanel.SetDock(_remove, Dock.Right);
        var actions = new DockPanel { Children = { _remove, _show, _notShown } };
        actions.Classes.Add("ClaudeChatBookmarkActions");
        DockPanel.SetDock(actions, Dock.Top);
        var replyScroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _reply,
        };
        _selection.Classes.Add("ClaudeChatBookmarkSelection");
        _selection.Child = new DockPanel { Children = { actions, replyScroller } };

        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        var top = new StackPanel { Children = { _empty, _listScroller } };
        Grid.SetRow(_selection, 1);
        Grid.SetRow(_hint, 1);
        body.Children.Add(top);
        body.Children.Add(_hint);
        body.Children.Add(_selection);

        DockPanel.SetDock(_handle, Dock.Left);
        Child = new DockPanel { Children = { _handle, header, body } };
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        IsVisible = false;
        Orient();
    }

    /// <summary>The panel closed while the keyboard was in it, and there was nothing to give it back to.</summary>
    internal event EventHandler? FocusReleased;

    /// <summary>The panel closed (its ×, <see cref="Close"/>).</summary>
    internal event EventHandler? Closed;

    /// <summary>Whether the reply is in the conversation's list now (the view's list, whole or as the Focus view folds it).</summary>
    internal Func<AssistantTextItem, bool>? IsInConversation { get; set; }

    /// <summary>Brings the reply into view in the conversation's list; false when it could not (it is not there).</summary>
    internal Func<AssistantTextItem, bool>? ShowInConversation { get; set; }

    /// <summary>The conversation whose bookmarks it shows; another one clears the picked reply.</summary>
    internal ChatConversation? Conversation
    {
        get => _conversation;
        set
        {
            if (ReferenceEquals(value, _conversation)) return;
            _conversation = value;
            _selected = null;
            if (_attached) Track(value);
        }
    }

    /// <summary>True while the panel is on screen.</summary>
    internal bool IsOpen => IsVisible;

    /// <summary>The reply picked in the list; null when none.</summary>
    internal AssistantTextItem? Selected => _selected;

    /// <summary>The bar that resizes the panel.</summary>
    internal Border Handle => _handle;

    /// <summary>The × in its header.</summary>
    internal Button CloseButton => _close;

    /// <summary>Whether it sits below the conversation (the view says so, from its width and layout).</summary>
    internal bool Stacked
    {
        get => _stacked;
        set
        {
            if (_stacked == value) return;
            _stacked = value;
            Orient();
        }
    }

    /// <summary>The view's width (beside the conversation) or height (below it), which the sizes are shares of; 0 before the first layout.</summary>
    internal double ViewExtent
    {
        get => _extent;
        set
        {
            if (_extent == value) return;
            _extent = value;
            ApplySize();
        }
    }

    /// <summary>The size it opens at: 30 % of the view's width within 240 to 420 px, or 45 % of its height within 180 to 360 px below.</summary>
    internal double DefaultSize => _stacked
        ? Math.Min(StackedMax, Math.Max(StackedMin, StackedShare * _extent))
        : Math.Min(SideMax, Math.Max(SideMin, SideShare * _extent));

    /// <summary>How wide (beside) or high (below) the panel is: what the user chose within the limits, else <see cref="DefaultSize"/>.</summary>
    internal double Size
    {
        get
        {
            if (_chosen is not double chosen) return DefaultSize;
            var least = _stacked ? StackedLeast : SideLeast;
            return Math.Max(least, Math.Min(Math.Max(least, MostShare * _extent), chosen));
        }
    }

    /// <summary>Brings back the size it opens at (the resize bar's double click).</summary>
    internal void ResetSize()
    {
        _chosen = null;
        ApplySize();
    }

    /// <summary>Moves the resize bar: a positive amount makes the panel larger.</summary>
    internal void Grow(double amount)
    {
        _chosen = Size + amount;
        ApplySize();
    }

    private ChatStrings Strings => GetValue(StringsProperty) ?? ChatStrings.English;

    /// <summary>Styled as a Border: the theme's <c>Border.ClaudeChatBookmarks</c>.</summary>
    protected override Type StyleKeyOverride => typeof(Border);

    // ---- opening and closing ----

    /// <summary>Opens the panel (or, open, takes the keyboard into it) with the × focused.</summary>
    internal void Open()
    {
        if (!IsVisible)
        {
            _return.Remember(this);
            IsVisible = true;
            Update();
        }
        _close.Focus();
    }

    /// <summary>Closes the panel; the keyboard goes back to what had it, or to the view's rule.</summary>
    internal void Close()
    {
        if (!IsVisible) return;
        var hadFocus = IsKeyboardFocusWithin;
        IsVisible = false;
        _dragging = false;
        if (!hadFocus) _return.Forget();
        else if (!(GetValue(LayoutProperty) != ChatLayout.Touch && _tracked?.CurrentPermission == null && _return.Restore()))
            FocusReleased?.Invoke(this, EventArgs.Empty);
        Closed?.Invoke(this, EventArgs.Empty);
    }

    // ---- the detach rule: out of the window only the events go ----

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Track(_conversation);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        _dragging = false;
        Track(null); // the conversation outlives the view: let go of its events
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StringsProperty || change.Property == LayoutProperty) Update();
        else if (change.Property == BoundsProperty) CapList();
    }

    private void Track(ChatConversation? conversation)
    {
        var list = conversation?.Bookmarks;
        if (!ReferenceEquals(list, _list))
        {
            if (_list != null) _list.CollectionChanged -= OnBookmarksChanged;
            _list = list;
            if (_list != null) _list.CollectionChanged += OnBookmarksChanged;
        }
        _tracked = conversation;
        // out of the window, only the events go: Avalonia is taking the tree apart, and coming back draws it again
        if (_attached) Update();
        else _return.Forget();
    }

    private void OnBookmarksChanged(object? sender, NotifyCollectionChangedEventArgs e) => Update();

    // ---- what it draws ----

    private void Update()
    {
        if (!_attached || !IsVisible) return;
        var strings = Strings;
        _title.Text = strings.BookmarksTitle;
        _empty.Text = strings.BookmarksEmpty;
        _hint.Text = strings.BookmarksSelectHint;
        _show.Content = strings.BookmarkShowInConversation;
        _notShown.Text = strings.BookmarkNotShown;
        _remove.Content = strings.RemoveBookmark;
        AutomationProperties.SetName(_close, strings.Close);
        ToolTip.SetTip(_close, strings.Close);
        AutomationProperties.SetName(_handle, strings.BookmarkResize);

        var bookmarks = _tracked?.Bookmarks;
        if (_selected != null && (bookmarks == null || !bookmarks.Contains(_selected))) _selected = null;
        _rows.Children.Clear();
        _built.Clear();
        var now = _tracked?.Clock();
        if (bookmarks != null)
        {
            foreach (var item in bookmarks)
            {
                var row = Row(item, strings, now);
                _built.Add((row, item));
                _rows.Children.Add(row);
            }
        }
        var any = _built.Count > 0;
        _empty.IsVisible = !any;
        _listScroller.IsVisible = any;
        CapList();
        UpdateSelection();
    }

    private Button Row(AssistantTextItem item, ChatStrings strings, DateTimeOffset? now)
    {
        var first = new TextBlock { Text = FirstLine(item.Text) };
        first.Classes.Add("ClaudeChatBookmarkFirstLine");
        var stack = new StackPanel { Children = { first } };
        string? age = null;
        if (item.Timestamp is DateTimeOffset written && now is DateTimeOffset current)
        {
            age = strings.FormatAgo(current - written);
            var line = new TextBlock { Text = age };
            line.Classes.Add("ClaudeChatBookmarkAge");
            stack.Children.Add(line);
        }
        var row = new Button { Content = stack };
        row.Classes.Add("ClaudeChatButton");
        row.Classes.Add("ClaudeChatBookmarkRow");
        AutomationProperties.SetName(row, age == null ? first.Text : first.Text + ", " + age);
        row.Click += (_, _) =>
        {
            _selected = item;
            UpdateSelection();
        };
        return row;
    }

    /// <summary>The first line of the reply that has a text, trimmed.</summary>
    internal static string FirstLine(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        foreach (var line in text!.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0) return trimmed;
        }
        return "";
    }

    /// <summary>The rows say which one is picked, and the box under the list shows it, or tells to pick one.</summary>
    private void UpdateSelection()
    {
        foreach (var (row, item) in _built) row.Classes.Set("selected", ReferenceEquals(item, _selected));
        var any = _built.Count > 0;
        var picked = _selected;
        _hint.IsVisible = any && picked == null;
        _selection.IsVisible = picked != null;
        if (picked == null)
        {
            _reply.Markdown = null;
            return;
        }
        var shown = IsInConversation?.Invoke(picked) ?? false;
        _show.IsVisible = shown;
        _notShown.IsVisible = !shown;
        _reply.Markdown = picked.Text;
    }

    /// <summary>The list takes 35 % of the panel's height at most, and scrolls past that.</summary>
    private void CapList()
    {
        var height = Bounds.Height;
        _listScroller.MaxHeight = height > 0 ? ListShare * height : double.PositiveInfinity;
    }

    private void ShowSelected()
    {
        var picked = _selected;
        if (picked == null) return;
        if (!(ShowInConversation?.Invoke(picked) ?? false)) UpdateSelection(); // it went from the list meanwhile
    }

    private void RemoveSelected()
    {
        var picked = _selected;
        if (picked == null || _tracked == null) return;
        _tracked.ToggleBookmark(picked); // the list tells: the row goes and the box with it
    }

    // ---- size and place ----

    /// <summary>The resize bar goes on the edge the conversation is on, and the line on the other side of the panel's border changes.</summary>
    private void Orient()
    {
        DockPanel.SetDock(_handle, _stacked ? Dock.Top : Dock.Left);
        BorderThickness = _stacked ? new Thickness(0, 1, 0, 0) : new Thickness(1, 0, 0, 0);
        _handle.Classes.Set("stacked", _stacked);
        _handle.Cursor = new Cursor(_stacked ? StandardCursorType.SizeNorthSouth : StandardCursorType.SizeWestEast);
        if (_stacked)
        {
            _handle.Width = double.NaN;
            _handle.Height = HandleThickness;
            _handleLine.Width = double.NaN;
            _handleLine.Height = HandleThickness;
        }
        else
        {
            _handle.Height = double.NaN;
            _handle.Width = HandleThickness;
            _handleLine.Height = double.NaN;
            _handleLine.Width = HandleThickness;
        }
        ApplySize();
    }

    private void ApplySize()
    {
        if (_extent <= 0) return;
        var size = Size;
        if (_stacked)
        {
            Width = double.NaN;
            Height = size;
        }
        else
        {
            Height = double.NaN;
            Width = size;
        }
    }

    // ---- the resize bar ----

    private double Along(PointerEventArgs e)
    {
        var point = TopLevel.GetTopLevel(this) is { } top ? e.GetPosition(top) : e.GetPosition(null);
        return _stacked ? point.Y : point.X;
    }

    private void OnHandlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_handle).Properties.IsLeftButtonPressed) return;
        _handle.Focus();
        _dragging = true;
        _dragFrom = Along(e);
        _dragSize = Size;
        e.Pointer.Capture(_handle);
        e.Handled = true;
    }

    private void OnHandleMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging) return;
        // the bar is the panel's inner edge: dragged toward the conversation (left, up) the panel grows
        _chosen = _dragSize - (Along(e) - _dragFrom);
        ApplySize();
        e.Handled = true;
    }

    private void OnHandleReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnHandleKey(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None) return;
        var grow = _stacked ? Key.Up : Key.Left;
        var shrink = _stacked ? Key.Down : Key.Right;
        if (e.Key == grow) Grow(KeyStep);
        else if (e.Key == shrink) Grow(-KeyStep);
        else return;
        e.Handled = true;
    }
}
