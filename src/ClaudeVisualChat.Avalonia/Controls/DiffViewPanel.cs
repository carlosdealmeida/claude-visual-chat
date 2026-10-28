using System;
using System.ComponentModel;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The extension's Diff View: a diff of the list opened over the whole view, on an opaque backdrop (the side bar's color, 16 px
/// around), in a dialog as large as the view less 40 px (at most <see cref="MostWidth"/> by <see cref="MostHeight"/>) with a 1 px
/// border and 4 px corners. Its header has the call's whole file path (or "Diff View" without one) and a ×; under it the whole
/// change, always side by side, scrolling. Esc, the × or a click on the backdrop closes it; the keyboard goes to the × when it opens
/// and back to where it was when it closes (<see cref="GaveFocusBack"/>). It is the view's own (not the conversation's): the view
/// opens it from a diff's layer (<see cref="Show"/>) and closes it with another conversation and through its back button. It
/// follows its call while it is open and in the window: a call whose input changes is drawn again.
/// </summary>
internal sealed class DiffViewPanel : Border
{
    private static readonly StyledProperty<ChatStrings> StringsProperty =
        ClaudeChatView.StringsProperty.AddOwner<DiffViewPanel>();

    /// <summary>The widest the dialog gets.</summary>
    internal const double MostWidth = 1400;

    /// <summary>The tallest the dialog gets.</summary>
    internal const double MostHeight = 900;

    /// <summary>The dialog is the view less this, in width and in height.</summary>
    internal const double Inset = 40;

    private readonly TextBlock _title = new TextBlock();
    private readonly Button _close;
    private readonly Border _dialog;
    private readonly ScrollViewer _scroller;

    private ToolCallItem? _item;    // the call on show
    private ToolCallItem? _tracked; // the call listened to: the one on show, only in the window
    private bool _attached;

    /// <summary>What had the keyboard when the Diff View opened, and whether it showed the keyboard's focus.</summary>
    private Control? _returnFocus;
    private bool _returnByKey;

    public DiffViewPanel()
    {
        _title.Classes.Add("ClaudeChatDiffViewTitle");
        var icon = new Path();
        icon.Classes.Add("ClaudeChatRemoveIcon");
        var iconBox = new Viewbox { Child = icon };
        iconBox.Classes.Add("ClaudeChatDiffViewCloseIcon");
        _close = new Button { Content = iconBox };
        _close.Classes.Add("ClaudeChatButton");
        _close.Classes.Add("ClaudeChatDiffViewClose");
        _close.Click += (_, _) => Hide();
        DockPanel.SetDock(_close, Dock.Right);
        var header = new Border { Child = new DockPanel { Children = { _close, _title } } };
        header.Classes.Add("ClaudeChatDiffViewHeader");
        DockPanel.SetDock(header, Dock.Top);

        // the lines wrap, so it scrolls only up and down
        _scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        _dialog = new Border
        {
            Child = new DockPanel { Children = { header, _scroller } },
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _dialog.Classes.Add("ClaudeChatDiffViewDialog");
        KeyboardNavigation.SetTabNavigation(_dialog, KeyboardNavigationMode.Cycle); // Tab stays in the dialog

        Child = _dialog;
        IsVisible = false;
        Label();
    }

    /// <summary>Raised when the Diff View closes: Esc, its ×, a click on the backdrop, the view's back button or another conversation.</summary>
    public event EventHandler? Closed;

    /// <summary>True while the Diff View is on screen.</summary>
    public bool IsOpen => IsVisible;

    /// <summary>The call on show; null while closed.</summary>
    internal ToolCallItem? Item => _item;

    /// <summary>The keyboard went back, when the Diff View last closed, to what had it before; false when that was gone.</summary>
    internal bool GaveFocusBack { get; private set; }

    /// <summary>Shows the diff of <paramref name="item"/>, from its top, and puts the keyboard on the ×.</summary>
    public void Show(ToolCallItem item)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));
        if (!IsVisible) RememberFocus();
        var other = !ReferenceEquals(item, _item);
        _item = item;
        Track(_attached ? item : null);
        if (other) _scroller.Offset = default;
        IsVisible = true;
        _close.Focus();
    }

    /// <summary>Closes the Diff View and gives the keyboard back to what had it, when that is still on screen.</summary>
    public void Hide()
    {
        if (!IsVisible) return;
        IsVisible = false;
        _item = null;
        Track(null);
        _scroller.Content = null; // the diff of a long file goes with it
        var back = _returnFocus;
        _returnFocus = null;
        GaveFocusBack = _attached && back is { IsEffectivelyVisible: true, Focusable: true } && back.IsAttachedToVisualTree()
            && back.Focus(_returnByKey ? NavigationMethod.Tab : NavigationMethod.Pointer);
        Closed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>What has the keyboard now, so it gets it back; with its ring when the keyboard brought it there.</summary>
    private void RememberFocus()
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
        _returnFocus = focused;
        _returnByKey = focused != null && focused.Classes.Contains(":focus-visible");
    }

    private ChatStrings Strings => GetValue(StringsProperty) ?? ChatStrings.English;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Track(_item);
        // back in the window with the Diff View open: the keyboard goes to the ×, unless the host put it somewhere of its own
        if (IsVisible && _item != null && KeyboardIsFree() && !_close.Focus())
            Dispatcher.UIThread.Post(() =>
            {
                if (_attached && IsVisible && _item != null && KeyboardIsFree()) _close.Focus();
            }, DispatcherPriority.Loaded);
    }

    /// <summary>Whether nothing has the keyboard, or something inside the view does; what is outside it is the host's, and stays.</summary>
    private bool KeyboardIsFree()
    {
        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not Visual focused) return true;
        var view = this.FindAncestorOfType<ClaudeChatView>();
        return view != null && (ReferenceEquals(view, focused) || view.IsVisualAncestorOf(focused));
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        Track(null); // the call outlives the view: let go of its events
    }

    private void Track(ToolCallItem? item)
    {
        if (!ReferenceEquals(item, _tracked))
        {
            if (_tracked != null) _tracked.PropertyChanged -= OnItemChanged;
            _tracked = item;
            if (_tracked != null) _tracked.PropertyChanged += OnItemChanged;
        }
        // out of the window, only the events go: coming back draws the call as it is now
        if (_attached && _item != null) Fill();
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ToolCallItem.Input) || e.PropertyName == nameof(ToolCallItem.Name)
            || e.PropertyName == nameof(ToolCallItem.Diff))
            Fill();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StringsProperty)
        {
            Label();
            if (_attached && _item != null) Fill();
        }
        else if (change.Property == BoundsProperty)
        {
            // the view less 40 px, 1400 by 900 at most
            var size = Bounds.Size;
            _dialog.Width = Math.Max(0, Math.Min(MostWidth, size.Width - Inset));
            _dialog.Height = Math.Max(0, Math.Min(MostHeight, size.Height - Inset));
        }
    }

    /// <summary>The title (the call's whole path, or "Diff View") and the whole change, side by side.</summary>
    private void Fill()
    {
        var item = _item;
        if (item == null) return;
        var strings = Strings;
        var path = FilePath(item.Input);
        _title.Text = string.IsNullOrEmpty(path) ? strings.DiffViewTitle : path;
        ToolTip.SetTip(_title, _title.Text);
        // the whole change, not the list's 20 lines a side; an item built without its input has only its own diff
        var diff = ToolPresentation.Diff(item.Name, item.Input, maxLinesPerSide: null) ?? item.Diff;
        _scroller.Content = diff is { Count: > 0 } ? new DiffView(diff, strings, whole: true, alwaysSideBySide: true) : null;
    }

    private static string? FilePath(JsonElement input) =>
        input.ValueKind == JsonValueKind.Object && input.TryGetProperty("file_path", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private void Label()
    {
        AutomationProperties.SetName(_close, Strings.Close);
        ToolTip.SetTip(_close, Strings.Close);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None || !IsVisible) return;
        e.Handled = true;
        Hide();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        // a click on the backdrop, not on the dialog
        if (!ReferenceEquals(e.Source, this)) return;
        e.Handled = true;
        Hide();
    }
}
