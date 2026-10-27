using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The base of the sheets of the touch layout, in the style of Anthropic's Claude app: a layer over the whole view that
/// dims it with a veil and holds a panel risen from the bottom, 92 % of the view's height at most. The panel has a
/// handle, a header (a ✕ at the left, the title in the middle, an optional action at the right, each a target of 44 px)
/// and a body that scrolls, laid out in groups: blocks of rows with a line between them, a label over a block. A sheet
/// may have pages: on one other than the first the ✕ is a back arrow, and <see cref="Back"/> goes to the first.
/// <para>
/// A closed sheet is an empty hidden layer that draws nothing and takes no input: in the desktop layout, where it never opens,
/// that is all the tree has of it. The user closes
/// it with the ✕, with a tap on the veil, with Esc, or with the host's back button (<see cref="Back"/>); all of these ask
/// <see cref="Dismiss"/>, which closes the sheet unless a subclass does otherwise (a sheet that shows a state of the
/// conversation closes that state). What takes the reason of the sheet away closes it by itself, without a dismissal: another
/// conversation, the layout going back to the desktop's, and the view when it becomes read-only (except the usage sheet, which is
/// information only and shows in a read-only view too). A permission request is a
/// dismissal, since the sheet would cover it, and while one waits a sheet does not open (the rule of
/// <see cref="ChatMenuPopup"/>'s menus). The keyboard goes into the sheet when it opens and back to where it was when it closes.
/// </para>
/// <para>
/// The sheets are opened by the user, through <see cref="Open"/>, except one that <see cref="FollowsState"/>: that one lives
/// from a state of the conversation, and opens by itself whenever the conversation is in it (and closes when it is not), so
/// that its dismissal is to end the state.
/// </para>
/// </summary>
internal abstract class ChatSheet : Border
{
    /// <summary>How much of the view's height the panel takes at most.</summary>
    private const double MaxHeightShare = 0.92;

    private static readonly StyledProperty<ChatStrings> StringsProperty =
        ClaudeChatView.StringsProperty.AddOwner<ChatSheet>();

    private static readonly StyledProperty<ChatLayout> LayoutProperty =
        ClaudeChatView.LayoutProperty.AddOwner<ChatSheet>();

    private readonly Border _panel = new Border();
    private readonly Button _back;
    private readonly Path _closeIcon = new Path();
    private readonly Path _backIcon = new Path();
    private readonly TextBlock _title = new TextBlock();
    private readonly Border _action = new Border();
    private readonly ScrollViewer _scroller;
    private readonly StackPanel _stack = new StackPanel();

    private ChatConversation? _conversation;
    private ChatConversation? _tracked;
    private bool _attached;
    private string? _page;

    /// <summary>What had the keyboard when the sheet opened; it gets it back when the sheet closes.</summary>
    private IInputElement? _returnFocus;

    protected ChatSheet()
    {
        Classes.Add("ClaudeChatSheet");
        _closeIcon.Classes.Add("ClaudeChatSheetCloseIcon");
        _backIcon.Classes.Add("ClaudeChatSheetBackIcon");
        _back = HeaderButton(new Panel { Children = { _closeIcon, _backIcon } }, "", () => Back());
        _title.Classes.Add("ClaudeChatSheetTitle");
        Grid.SetColumn(_title, 1);
        Grid.SetColumn(_action, 2);
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("44,*,44"),
            Children = { _back, _title, _action },
        };
        header.Classes.Add("ClaudeChatSheetHeader");
        DockPanel.SetDock(header, Dock.Top);

        var handle = new Border();
        handle.Classes.Add("ClaudeChatSheetHandle");
        DockPanel.SetDock(handle, Dock.Top);

        _stack.Classes.Add("ClaudeChatSheetStack");
        var body = new Border { Child = _stack };
        body.Classes.Add("ClaudeChatSheetBody");
        _scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = body,
        };

        _panel.Classes.Add("ClaudeChatSheetPanel");
        _panel.Child = new DockPanel { Children = { handle, header, _scroller } };
        KeyboardNavigation.SetTabNavigation(_panel, KeyboardNavigationMode.Cycle); // Tab never goes out to what the veil covers
        IsVisible = false;
    }

    /// <summary>The sheet closed, whatever closed it.</summary>
    internal event EventHandler? Closed;

    /// <summary>The conversation whose state the sheet shows; another one closes it.</summary>
    internal ChatConversation? Conversation
    {
        get => _conversation;
        set
        {
            if (!ReferenceEquals(value, _conversation)) Close();
            _conversation = value;
            if (_attached) Track(value);
        }
    }

    /// <summary>True while the sheet is on screen.</summary>
    internal bool IsOpen => IsVisible;

    /// <summary>The page shown; null on the first.</summary>
    protected string? Page => _page;

    protected ChatStrings Strings => GetValue(StringsProperty) ?? ChatStrings.English;

    /// <summary>Styled as a Border: the theme's <c>Border.ClaudeChatSheet</c>.</summary>
    protected override Type StyleKeyOverride => typeof(Border);

    /// <summary>
    /// Opens the sheet on its first page, with the keyboard in it. Nothing opens while the claude waits on a permission
    /// request, without a conversation, or when <see cref="CanOpen"/> says there is nothing to show.
    /// </summary>
    internal void Open()
    {
        var conversation = _tracked;
        if (IsOpen || conversation == null || conversation.CurrentPermission != null || !CanOpen(conversation)) return;
        _page = null;
        _returnFocus = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        IsVisible = true;
        Child = _panel;
        Rebuild();
        _back.Focus();
    }

    /// <summary>Hides the sheet and gives the keyboard back, if it was in it. Nothing else happens: <see cref="Dismiss"/> is the user's way out.</summary>
    internal void Close()
    {
        if (!IsOpen) return;
        var hadFocus = IsKeyboardFocusWithin;
        var returnTo = _returnFocus;
        _returnFocus = null;
        _page = null;
        IsVisible = false;
        Child = null; // an empty layer, as it was before the first opening
        _stack.Children.Clear(); // the rows and the action hold the conversation they were drawn for
        _action.Child = null;
        if (hadFocus && returnTo is InputElement element && element.GetVisualRoot() != null) element.Focus();
        Closed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// What the host's back button asks: from a page to the first, else the sheet's <see cref="Dismiss"/>. False when the
    /// sheet is closed and there was nothing to go back from.
    /// </summary>
    internal bool Back()
    {
        if (!IsOpen) return false;
        if (_page != null) ShowPage(null);
        else Dismiss();
        return true;
    }

    /// <summary>What a tap on the veil does, for the host's own veil: <see cref="Dismiss"/>. False when the sheet is closed.</summary>
    internal bool DismissFromVeil()
    {
        if (!IsOpen) return false;
        Dismiss();
        return true;
    }

    /// <summary>
    /// How the sheet closes when the user asks (the ✕, the veil, Esc, the back button on the first page) or a permission
    /// request comes: it closes. A sheet that lives from a state of the conversation changes that state instead, and
    /// follows it: it must not hide itself while the state stays, or it would open again at the next change.
    /// </summary>
    protected virtual void Dismiss() => Close();

    /// <summary>
    /// True for a sheet that lives from a state of the conversation (the usage sheet, from <see cref="ChatConversation.ShownPanel"/>),
    /// where <see cref="CanOpen"/> says whether the conversation is in it. Such a sheet opens by itself, in the touch layout, whenever
    /// the conversation is in the state and nothing waits on the user: when the state begins, when the sheet comes into the window or
    /// gets another conversation, when the layout becomes the touch layout, and when the permission request that kept it
    /// from opening is gone. It closes when the state is over, and its <see cref="Dismiss"/> is what ends the state. False (the
    /// default) for a sheet the user opens.
    /// </summary>
    protected virtual bool FollowsState => false;

    /// <summary>
    /// True when there is something to show. A sheet that has nothing to show does not open, and one open when it loses
    /// everything closes.
    /// </summary>
    protected virtual bool CanOpen(ChatConversation conversation) => true;

    /// <summary>True when the page can be shown for the conversation as it is; if not, the sheet goes back to the first.</summary>
    protected virtual bool HasPage(ChatConversation conversation, string page) => true;

    /// <summary>True when the conversation's property is one the body shows: the sheet is drawn again when it changes.</summary>
    protected abstract bool Shows(string? propertyName);

    /// <summary>The title of the page being shown.</summary>
    protected abstract string Title(ChatStrings strings);

    /// <summary>Puts the body of the page being shown in <paramref name="body"/>, which is empty.</summary>
    protected abstract void Fill(Panel body, ChatConversation conversation, ChatStrings strings);

    /// <summary>The action at the right of the header (see <see cref="HeaderButton"/>), or null for none.</summary>
    protected virtual Button? HeaderAction(ChatConversation conversation, ChatStrings strings) => null;

    /// <summary>Shows another page (null: the first) and draws it from the top.</summary>
    protected void ShowPage(string? page)
    {
        if (_page == page) return;
        _page = page;
        Rebuild();
        _scroller.SetCurrentValue(ScrollViewer.OffsetProperty, default(Vector));
    }

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
        Track(null); // the conversation outlives the view: let go of its events
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StringsProperty)
        {
            Rebuild();
        }
        else if (change.Property == LayoutProperty)
        {
            if (GetValue(LayoutProperty) != ChatLayout.Touch) Close(); // the sheets are the touch layout's
            else Follow();
        }
        else if (change.Property == BoundsProperty && Bounds.Height > 0)
        {
            _panel.MaxHeight = Bounds.Height * MaxHeightShare;
        }
    }

    /// <summary>A press on the veil, not on the panel.</summary>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!ReferenceEquals(e.Source, this)) return;
        e.Handled = true;
        DismissFromVeil();
    }

    /// <summary>Esc goes back a page, or closes the sheet; it is not the app's.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None || !IsOpen) return;
        e.Handled = true;
        Back();
    }

    private void Track(ChatConversation? conversation)
    {
        if (!ReferenceEquals(conversation, _tracked))
        {
            if (_tracked != null) _tracked.PropertyChanged -= OnConversationChanged;
            _tracked = conversation;
            if (_tracked != null) _tracked.PropertyChanged += OnConversationChanged;
        }
        // out of the window, only the events go: Avalonia is taking the tree apart, and coming back draws it again
        if (_attached) Refresh();
    }

    /// <summary>An open sheet that comes back to the window follows the conversation as it is now.</summary>
    private void Refresh()
    {
        if (!IsOpen)
        {
            Follow();
            return;
        }
        var conversation = _tracked;
        if (conversation == null) Close();
        else if (conversation.CurrentPermission != null) Dismiss();
        else Rebuild();
    }

    /// <summary>
    /// A sheet that <see cref="FollowsState"/> opens when the conversation is in its state, in the touch layout; <see cref="Open"/>
    /// has the rest of the say (a conversation, no request waiting, something to show).
    /// </summary>
    private void Follow()
    {
        if (FollowsState && !IsOpen && GetValue(LayoutProperty) == ChatLayout.Touch) Open();
    }

    private void OnConversationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!IsOpen)
        {
            // what began the state it lives from, or took away what kept it from opening
            if (FollowsState && (e.PropertyName == nameof(ChatConversation.CurrentPermission) || Shows(e.PropertyName))) Follow();
            return;
        }
        if (e.PropertyName == nameof(ChatConversation.CurrentPermission))
        {
            if (_tracked?.CurrentPermission != null) Dismiss(); // the request comes first: the sheet would cover it
        }
        else if (Shows(e.PropertyName))
        {
            Rebuild();
        }
    }

    /// <summary>
    /// Draws the header and the body for the page and the conversation as they are; the keyboard stays in the sheet. The sheet
    /// does it by itself when the conversation changes in a way <see cref="Shows"/> says it shows, and when the texts change;
    /// a subclass calls it when something else it shows changed. Nothing happens while the sheet is closed.
    /// </summary>
    protected void Rebuild()
    {
        var conversation = _tracked;
        if (!IsOpen || conversation == null) return;
        if (!CanOpen(conversation))
        {
            Close();
            return;
        }
        if (_page != null && !HasPage(conversation, _page)) _page = null;
        var strings = Strings;
        var hadFocus = IsKeyboardFocusWithin;
        var inner = _page != null;
        _title.Text = Title(strings);
        _closeIcon.IsVisible = !inner;
        _backIcon.IsVisible = inner;
        var name = inner ? strings.Back : strings.Close;
        AutomationProperties.SetName(_back, name);
        ToolTip.SetTip(_back, name);
        AutomationProperties.SetName(_panel, _title.Text);
        _action.Child = HeaderAction(conversation, strings);
        _stack.Children.Clear();
        Fill(_stack, conversation, strings);
        if (hadFocus && !IsKeyboardFocusWithin) _back.Focus(); // the row that had it is gone
    }

    // ---- pieces of the sheets ----

    /// <summary>A button of the header: a target of 44 px around the icon, in the text's color.</summary>
    protected static Button HeaderButton(Control icon, string name, Action click)
    {
        var button = new Button { Content = icon };
        button.Classes.Add("ClaudeChatButton");
        button.Classes.Add("ClaudeChatSheetButton");
        button.Click += (_, _) => click();
        AutomationProperties.SetName(button, name);
        ToolTip.SetTip(button, name);
        return button;
    }

    /// <summary>A block of rows with a line of the sheet's color between them.</summary>
    protected static Border Group(IEnumerable<Control> rows)
    {
        var stack = new StackPanel();
        foreach (var row in rows)
        {
            if (stack.Children.Count > 0)
            {
                var line = new Border();
                line.Classes.Add("ClaudeChatSheetSeparator");
                stack.Children.Add(line);
            }
            stack.Children.Add(row);
        }
        var group = new Border { Child = stack };
        group.Classes.Add("ClaudeChatSheetGroup");
        return group;
    }

    /// <summary>The label over a block.</summary>
    protected static TextBlock Label(string text)
    {
        var label = new TextBlock { Text = text };
        label.Classes.Add("ClaudeChatSheetLabel");
        return label;
    }

    /// <summary>
    /// A row of a group: a button across the block, at least <paramref name="height"/> high, with the icon (when there is one)
    /// at the left of the name and, under the name, the description, in the secondary color; at the right, the value in the
    /// secondary color, a "›" after it when the row <paramref name="opens"/> another page, and the check. The check's column
    /// is there when <paramref name="check"/> is not null, and the check shows when it is true. The icon is the drawing
    /// with the key <paramref name="icon"/> among the resources (<c>ClaudeChat.Icon.…</c>).
    /// </summary>
    protected static Button Row(string name, Action tap, double height = 52, string? description = null, string? value = null,
        bool opens = false, bool? check = null, string? icon = null)
    {
        var label = new TextBlock { Text = name };
        label.Classes.Add("ClaudeChatSheetName");
        var text = new StackPanel { Children = { label } };
        text.Classes.Add("ClaudeChatSheetRowText");
        Grid.SetColumn(text, 1);
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto") };
        content.Children.Add(text);
        if (icon != null)
        {
            var drawing = new Path();
            drawing.Classes.Add("ClaudeChatSheetIcon");
            drawing.Bind(Path.DataProperty, drawing.GetResourceObservable(icon));
            content.Children.Add(drawing);
        }
        var spoken = name;
        if (!string.IsNullOrEmpty(description))
        {
            var detail = new TextBlock { Text = description };
            detail.Classes.Add("ClaudeChatSheetDescription");
            text.Children.Add(detail);
            spoken += ", " + description;
        }
        if (value != null || opens)
        {
            var amount = new TextBlock { Text = opens ? (value + " ›").TrimStart() : value };
            amount.Classes.Add("ClaudeChatSheetValue");
            Grid.SetColumn(amount, 2);
            content.Children.Add(amount);
            if (value != null) spoken += ", " + value;
        }
        if (check != null)
        {
            var mark = new Path { IsVisible = check == true };
            mark.Classes.Add("ClaudeChatMenuCheck");
            var box = new Viewbox { Child = mark };
            box.Classes.Add("ClaudeChatSheetCheck");
            Grid.SetColumn(box, 3);
            content.Children.Add(box);
        }
        return Tappable(content, tap, spoken, height);
    }

    /// <summary>
    /// What a row is made of: a button across the block, at least <paramref name="height"/> high, around any
    /// <paramref name="content"/>, which a screen reader hears as <paramref name="name"/>.
    /// </summary>
    protected static Button Tappable(Control content, Action tap, string name, double height = 52)
    {
        var row = new Button { Content = content, MinHeight = height };
        row.Classes.Add("ClaudeChatButton");
        row.Classes.Add("ClaudeChatSheetRow");
        row.Click += (_, _) => tap();
        AutomationProperties.SetName(row, name);
        return row;
    }
}
