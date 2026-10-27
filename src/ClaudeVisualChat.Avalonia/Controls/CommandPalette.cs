using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// A menu of the reply box, like the extension's (<c>menuPopup</c>): over the box, as wide as it, 8 px above it. It
/// closes when the user presses the pointer anywhere outside it but on its <see cref="Opener"/>, whose click opens
/// and closes it, and when the window goes to the background.
/// </summary>
internal abstract class ChatMenuPopup : Popup
{
    protected static readonly StyledProperty<ChatStrings> StringsProperty =
        ClaudeChatView.StringsProperty.AddOwner<ChatMenuPopup>();

    private TopLevel? _topLevel;
    private Control? _target;

    protected ChatMenuPopup()
    {
        Placement = PlacementMode.Top;
        IsLightDismissEnabled = false; // the click on the opener must close it, not close and open again
        WindowManagerAddShadowHint = false;
        Frame.Classes.Add("ClaudeChatMenu");
        Child = Frame;
        Opened += (_, _) => Listen(TopLevel.GetTopLevel(PlacementTarget ?? this));
        Closed += (_, _) => Listen(null);
    }

    /// <summary>The control whose click opens and closes the menu; pressing it does not dismiss.</summary>
    internal Control? Opener { get; set; }

    /// <summary>The menu's frame, the popup's child.</summary>
    protected Border Frame { get; } = new Border();

    protected ChatStrings Strings => GetValue(StringsProperty) ?? ChatStrings.English;

    /// <summary>
    /// The reply box is on screen and working, and the claude waits on no permission request: only then does the menu
    /// open, and only then does what is picked in it reach the conversation (a menu left open under a reply box that
    /// went away, read-only or hidden, picks nothing).
    /// </summary>
    protected bool CanAct(ChatConversation? conversation) =>
        conversation != null && conversation.CurrentPermission == null && IsEffectivelyVisible && IsEffectivelyEnabled;

    /// <summary>Opens over the reply box (<see cref="Popup.PlacementTarget"/>), as wide as it.</summary>
    protected void Show()
    {
        if (PlacementTarget is { } target) Frame.Width = target.Bounds.Width;
        IsOpen = true;
    }

    protected bool IsInside(object? source) =>
        source is Visual visual && (Contains(Frame, visual) || (Opener != null && Contains(Opener, visual)));

    private static bool Contains(Visual ancestor, Visual visual) =>
        ReferenceEquals(ancestor, visual) || ancestor.IsVisualAncestorOf(visual);

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        IsOpen = false;
    }

    private void Listen(TopLevel? topLevel)
    {
        if (_topLevel != null)
        {
            _topLevel.RemoveHandler(PointerPressedEvent, OnPointerPressedAnywhere);
            if (_topLevel is WindowBase window) window.Deactivated -= OnDeactivated;
        }
        if (_target != null) _target.PropertyChanged -= OnTargetChanged;
        _topLevel = topLevel;
        _target = topLevel == null ? null : PlacementTarget;
        if (_topLevel != null)
        {
            _topLevel.AddHandler(PointerPressedEvent, OnPointerPressedAnywhere, RoutingStrategies.Tunnel, true);
            if (_topLevel is WindowBase window) window.Deactivated += OnDeactivated;
        }
        if (_target != null) _target.PropertyChanged += OnTargetChanged;
    }

    private void OnPointerPressedAnywhere(object? sender, PointerPressedEventArgs e)
    {
        if (!IsInside(e.Source)) IsOpen = false;
    }

    private void OnDeactivated(object? sender, EventArgs e) => IsOpen = false;

    /// <summary>The reply box changed its width (the window was resized): the menu follows.</summary>
    private void OnTargetChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == BoundsProperty && _target != null) Frame.Width = _target.Bounds.Width;
    }
}

/// <summary>
/// The command palette, like the extension's: "Slash Commands" and the conversation's
/// <see cref="ChatConversation.PaletteCommands"/>, each as <c>/name</c> with its description in the tooltip. The /
/// button opens it with a filter field that takes the keyboard; a "/" typed in the empty reply box opens it without one,
/// filtered by what follows the "/" until a space or the "/" goes. A command matches by its name (exactly, then at its
/// start, then anywhere) and then by its description, in the claude's order within each. The arrows move the mark,
/// Enter runs the marked command (<see cref="ChatConversation.Submit"/> with <c>/name</c>), Tab puts <c>/name </c>
/// in the reply box, Esc closes and gives the keyboard back to the box. A press on a row runs its command; in the touch
/// layout a tap does (the pointer let go where it was pressed), so that a finger that scrolls the list runs nothing.
/// </summary>
internal sealed class CommandPalette : ChatMenuPopup
{
    private static readonly StyledProperty<ChatLayout> LayoutProperty =
        ClaudeChatView.LayoutProperty.AddOwner<CommandPalette>();

    private readonly TextBox _filter = new TextBox();
    private readonly Border _spacer = new Border { Height = 4 };
    private readonly StackPanel _list = new StackPanel();
    private readonly TextBlock _header = new TextBlock();
    private readonly TextBlock _empty = new TextBlock();
    private readonly List<Border> _rows = new List<Border>();
    private List<ChatCommand> _shown = new List<ChatCommand>();
    private ChatConversation? _conversation;
    private TextBox? _input;
    private bool _typed;
    private int _active = -1;

    public CommandPalette()
    {
        _filter.Classes.Add("ClaudeChatMenuFilter");
        _filter.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.KeyModifiers == KeyModifiers.None && HandleKey(e.Key)) e.Handled = true;
        }, RoutingStrategies.Tunnel);
        _filter.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty && IsOpen && !_typed) Filter(_filter.Text ?? "");
        };
        _header.Classes.Add("ClaudeChatMenuHeader");
        _empty.Classes.Add("ClaudeChatMenuEmpty");
        _list.Classes.Add("ClaudeChatMenuList");
        _list.Children.Add(_header);
        _list.Children.Add(_empty);
        Frame.Child = new StackPanel
        {
            Children =
            {
                _filter,
                _spacer,
                new ScrollViewer
                {
                    Classes = { "ClaudeChatMenuScroller" },
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = _list,
                },
            },
        };
        Closed += (_, _) => _active = -1;
    }

    /// <summary>The conversation whose commands it lists and runs; another one closes the palette.</summary>
    internal ChatConversation? Conversation
    {
        get => _conversation;
        set
        {
            if (ReferenceEquals(value, _conversation)) return;
            _conversation = value;
            IsOpen = false;
        }
    }

    /// <summary>The reply box: a "/" typed into it empty opens the palette, and the commands go to it.</summary>
    internal TextBox? Input
    {
        get => _input;
        set
        {
            _input = value;
            if (value == null) return;
            // after the text box took the character: only what the user types opens the palette, not a draft shown
            value.AddHandler(TextInputEvent, (_, _) => OnTyped(), RoutingStrategies.Bubble, true);
            value.PropertyChanged += (_, e) =>
            {
                if (e.Property == TextBox.TextProperty && IsOpen && _typed) FollowInput();
            };
        }
    }

    private bool IsTouch => GetValue(LayoutProperty) == ChatLayout.Touch;

    /// <summary>The / button: opens the palette with its filter field, or closes it.</summary>
    internal void Toggle()
    {
        if (IsOpen)
        {
            IsOpen = false;
            return;
        }
        if (!CanAct(_conversation)) return;
        _typed = false;
        _filter.Text = "";
        Filter("");
        Show();
        // the filter field takes the keyboard once the popup has put it on screen
        if (!_filter.Focus()) Dispatcher.UIThread.Post(() => _filter.Focus());
    }

    /// <summary>A key pressed in the reply box or in the filter field; true when the palette took it.</summary>
    internal bool HandleKey(Key key)
    {
        if (!IsOpen) return false;
        switch (key)
        {
            case Key.Escape:
                IsOpen = false;
                _input?.Focus();
                return true;
            case Key.Down:
            case Key.Up:
                if (_shown.Count > 0)
                {
                    var next = key == Key.Down ? (_active < _shown.Count - 1 ? _active + 1 : 0) : (_active > 0 ? _active - 1 : _shown.Count - 1);
                    Mark(next);
                }
                return true;
            case Key.Enter:
                if (_active >= 0)
                {
                    Run(_shown[_active]);
                    return true;
                }
                if (!_typed) return true;
                IsOpen = false; // nothing marked: the reply box sends what was typed
                return false;
            case Key.Tab:
                if (_active >= 0) Insert(_shown[_active]);
                return true;
            default:
                return false;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StringsProperty && IsOpen) Filter(Query);
    }

    private string Query => _typed ? (_input?.Text ?? "/").Substring(1) : _filter.Text ?? "";

    private void OnTyped()
    {
        if (IsOpen || !CanAct(_conversation) || _input == null || _input.Text != "/") return;
        _typed = true;
        Filter("");
        Show();
    }

    /// <summary>Filters by what follows the "/"; a space (the arguments) or no "/" closes the palette.</summary>
    private void FollowInput()
    {
        var text = _input?.Text ?? "";
        if (text.Length == 0 || text[0] != '/' || HasWhiteSpace(text))
        {
            IsOpen = false;
            return;
        }
        Filter(text.Substring(1));
    }

    // ---- the list ----

    private void Filter(string query)
    {
        if (query.StartsWith("/", StringComparison.Ordinal)) query = query.Substring(1);
        var strings = Strings;
        _filter.Watermark = strings.FilterCommands;
        _filter.IsVisible = !_typed;
        _spacer.IsVisible = _typed;
        _header.Text = strings.SlashCommands;
        _empty.Text = strings.NoMatchingCommands;
        _shown = Rank(_conversation?.PaletteCommands ?? Array.Empty<ChatCommand>(), query, strings);
        foreach (var row in _rows) _list.Children.Remove(row);
        _rows.Clear();
        for (var i = 0; i < _shown.Count; i++) _rows.Add(Row(_shown[i], i, strings));
        _list.Children.InsertRange(1, _rows);
        _header.IsVisible = _shown.Count > 0;
        _empty.IsVisible = _shown.Count == 0;
        // the first one is marked once something is typed, as in the extension
        Mark(query.Length > 0 && _shown.Count > 0 ? 0 : -1);
    }

    private Border Row(ChatCommand command, int index, ChatStrings strings)
    {
        var label = new TextBlock { Text = "/" + command.Name };
        label.Classes.Add("ClaudeChatMenuLabel");
        var row = new Border { Child = label };
        row.Classes.Add("ClaudeChatMenuItem");
        var description = Describe(command, strings);
        if (description.Length > 0) ToolTip.SetTip(row, description);
        row.PointerEntered += (_, _) => Mark(index);
        // the desktop runs the command at the press, as the extension's menu does. The touch layout waits for the tap (the
        // pointer let go where it was pressed, within Avalonia's tap threshold), and leaves the press alone, so that a finger
        // which starts to scroll the list scrolls it instead of running the command under it
        row.PointerPressed += (_, e) =>
        {
            if (IsTouch || !e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            Run(command);
        };
        row.Tapped += (_, e) =>
        {
            if (!IsTouch) return;
            e.Handled = true;
            Run(command);
        };
        return row;
    }

    private void Mark(int index)
    {
        _active = index;
        for (var i = 0; i < _rows.Count; i++) _rows[i].Classes.Set("active", i == index);
        if (index >= 0 && index < _rows.Count) _rows[index].BringIntoView();
    }

    /// <summary>
    /// Sends <c>/name</c>: a command of the conversation's own becomes <see cref="ChatConversation.CommandInvoked"/>.
    /// What was typed to find it goes; a draft in the box stays.
    /// </summary>
    private void Run(ChatCommand command)
    {
        var conversation = _conversation;
        var typed = _typed;
        IsOpen = false;
        if (!CanAct(conversation)) return;
        if (typed) _input?.Clear();
        conversation!.Submit("/" + command.Name);
        _input?.Focus();
    }

    /// <summary>Puts <c>/name </c> in the reply box, for the arguments.</summary>
    private void Insert(ChatCommand command)
    {
        IsOpen = false;
        if (_input == null || !CanAct(_conversation)) return;
        var text = "/" + command.Name + " ";
        _input.Text = text;
        _input.CaretIndex = text.Length;
        _input.Focus();
    }

    /// <summary>
    /// The commands that match, best first: the name equal to the query, then starting with it, then having it, then
    /// the description having it; in the claude's order within each. An empty query keeps them all, in that order.
    /// </summary>
    internal static List<ChatCommand> Rank(IReadOnlyList<ChatCommand> commands, string query, ChatStrings strings)
    {
        var ranked = new List<ChatCommand>(commands.Count);
        if (query.Length == 0)
        {
            ranked.AddRange(commands);
            return ranked;
        }
        var buckets = new[] { new List<ChatCommand>(), new List<ChatCommand>(), new List<ChatCommand>(), new List<ChatCommand>() };
        foreach (var command in commands)
        {
            var name = command.Name;
            if (string.Equals(name, query, StringComparison.OrdinalIgnoreCase)) buckets[0].Add(command);
            else if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) buckets[1].Add(command);
            else if (name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) buckets[2].Add(command);
            else if (Describe(command, strings).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) buckets[3].Add(command);
        }
        foreach (var bucket in buckets) ranked.AddRange(bucket);
        return ranked;
    }

    /// <summary>The command's description; the conversation's own commands, listed without one, have the strings'.</summary>
    private static string Describe(ChatCommand command, ChatStrings strings)
    {
        if (!string.IsNullOrEmpty(command.Description)) return command.Description;
        return command.Name switch
        {
            "context" => strings.CommandContextDescription,
            "usage" => strings.CommandUsageDescription,
            "remote-control" or "rc" => strings.CommandRemoteControlDescription,
            "chrome" => strings.CommandChromeDescription,
            _ => "",
        };
    }

    private static bool HasWhiteSpace(string text)
    {
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c)) return true;
        }
        return false;
    }
}
