using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
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

/// <summary>The groups of the command palette, in the order they show; <see cref="PaletteAction.Section"/> is one of these.</summary>
internal static class PaletteSection
{
    internal const string Context = "Context";
    internal const string Model = "Model";
    internal const string Customize = "Customize";
    internal const string Settings = "Settings";
    internal const string SlashCommands = "Slash Commands";
    internal const string Support = "Support";

    /// <summary>The groups in the order they show.</summary>
    internal static readonly string[] Order = { Context, Model, Customize, Settings, SlashCommands, Support };
}

/// <summary>
/// A row of the command palette that is not a slash command: it acts on the view (which gives it the conversation and the reply
/// box). <see cref="Label"/> is the row's name, in English; <see cref="Text"/> gives it in the language of the strings. A row with
/// <see cref="IsOn"/> is a switch, and one that <see cref="KeepsOpen"/> leaves the palette open; the others close it.
/// </summary>
internal sealed record PaletteAction(string Section, string Label, Action<ClaudeChatView> Run, Func<ClaudeChatView, bool>? IsOn = null)
{
    /// <summary>The row's name in the strings' language; the <see cref="Label"/> when null.</summary>
    internal Func<ChatStrings, string>? Text { get; init; }

    /// <summary>Whether the row shows now; always, when null.</summary>
    internal Func<ClaudeChatView, bool>? IsShown { get; init; }

    /// <summary>What shows at the right of the row (the model, the level); nothing when null or when it gives null.</summary>
    internal Func<ClaudeChatView, ChatStrings, string?>? Value { get; init; }

    /// <summary>The palette stays open after the row acts (a switch always does).</summary>
    internal bool KeepsOpen { get; init; }
}

/// <summary>
/// The command palette, like the extension's, in groups (<see cref="PaletteSection"/>): the <see cref="Actions"/> (attach a file, the
/// model, the effort, the usage, the docs) and "Slash Commands", the conversation's
/// <see cref="ChatConversation.PaletteCommands"/>, each as <c>/name</c> with its description in the tooltip. A group with nothing
/// to show is not there, and a divider of 1 px opens each one after the first. The / button opens it with a filter field that takes
/// the keyboard, and without a filter it lists the actions only; a "/" typed in the empty reply box opens it with the slash commands
/// alone and without the field, filtered by what follows the "/" until a space or the "/" goes. A row matches by its name (exactly, then
/// at its start, then anywhere) and then by its description, in the claude's order within each, inside its group. The arrows move the
/// mark, Enter runs the marked row (a command is <see cref="ChatConversation.Submit"/> with <c>/name</c>), Tab puts <c>/name </c>
/// in the reply box, Esc closes and gives the keyboard back to the box. A press on a row runs it; in the touch layout a tap does
/// (the pointer let go where it was pressed), so that a finger that scrolls the list runs nothing. The touch layout has the same
/// groups, the rows at the height they had.
/// </summary>
internal sealed class CommandPalette : ChatMenuPopup
{
    /// <summary>Where "View help docs" goes.</summary>
    internal const string HelpDocsUrl = "https://code.claude.com/docs/en/overview";

    private static readonly StyledProperty<ChatLayout> LayoutProperty =
        ClaudeChatView.LayoutProperty.AddOwner<CommandPalette>();

    private sealed class Entry
    {
        internal ChatCommand? Command;
        internal PaletteAction? Action;
    }

    private readonly TextBox _filter = new TextBox();
    private readonly Border _spacer = new Border { Height = 4 };
    private readonly StackPanel _list = new StackPanel();
    private readonly TextBlock _empty = new TextBlock();
    private readonly List<Border> _rows = new List<Border>();
    private readonly List<Action> _refreshers = new List<Action>();
    private List<Entry> _shown = new List<Entry>();
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
        _empty.Classes.Add("ClaudeChatMenuEmpty");
        _list.Classes.Add("ClaudeChatMenuList");
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

    /// <summary>
    /// The rows that are not slash commands. The other tasks that add to the palette (the Focus view, the bookmarks) add theirs here;
    /// a row shows when its group has the room, in the order they were added.
    /// </summary>
    internal static IList<PaletteAction> Actions { get; } = new List<PaletteAction>
    {
        new PaletteAction(PaletteSection.Context, "Attach file…", view => _ = view.ReplyBox.AttachFromPickerAsync())
        {
            Text = strings => strings.PaletteAttachFile,
        },
        new PaletteAction(PaletteSection.Model, "Switch model…", view => view.ReplyBox.OpenModelPicker())
        {
            Text = strings => strings.PaletteSwitchModel,
            IsShown = view => view.Conversation?.AvailableModels is { Count: > 0 },
            Value = (view, _) => view.Conversation == null ? null : ModelChip.NameOf(view.Conversation),
        },
        // the model's levels, one at a time round the end, as the model menu's row does; only for a model that takes an effort
        new PaletteAction(PaletteSection.Model, "Effort", view => ModelMenu.CycleEffort(view.Conversation!))
        {
            Text = strings => strings.Effort,
            KeepsOpen = true,
            IsShown = view => view.Conversation?.CurrentModel?.EffortLevels is { Count: > 0 },
            Value = (view, strings) => strings.EffortLabel(ModelMenu.LevelShown(view.Conversation!)),
        },
        new PaletteAction(PaletteSection.Model, "Account & usage…", view => view.Conversation?.Submit("/usage"))
        {
            Text = strings => strings.PaletteAccountUsage,
        },
        // only where the host opens links
        new PaletteAction(PaletteSection.Support, "View help docs", view => view.Conversation?.RequestLink(HelpDocsUrl))
        {
            Text = strings => strings.PaletteHelpDocs,
            IsShown = view => view.Conversation?.CanOpenLinks == true,
        },
    };

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

    /// <summary>The view the palette is in, whose conversation and reply box the <see cref="Actions"/> act on.</summary>
    private ClaudeChatView? View => this.FindLogicalAncestorOfType<ClaudeChatView>();

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
                    var entry = _shown[_active];
                    if (entry.Action != null) Run(entry.Action);
                    else Run(entry.Command!);
                    return true;
                }
                if (!_typed) return true;
                IsOpen = false; // nothing marked: the reply box sends what was typed
                return false;
            case Key.Tab:
                if (_active >= 0 && _shown[_active].Command is { } command) Insert(command);
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
        _empty.Text = strings.NoMatchingCommands;
        _shown = new List<Entry>();
        _rows.Clear();
        _refreshers.Clear();
        _list.Children.Clear();

        // the actions are for the button's menu; a "/" typed in the box is looking for a command. The commands are not listed by the
        // button before something is typed in its field
        var view = _typed ? null : View;
        var actions = new List<PaletteAction>();
        if (view != null)
        {
            foreach (var action in Actions)
            {
                if (action.IsShown == null || action.IsShown(view)) actions.Add(action);
            }
        }
        var commands = _typed || query.Length > 0
            ? Rank(_conversation?.PaletteCommands ?? Array.Empty<ChatCommand>(), query, strings)
            : new List<ChatCommand>();
        var sections = new List<string>(PaletteSection.Order);
        foreach (var action in actions)
        {
            if (!sections.Contains(action.Section)) sections.Add(action.Section);
        }
        var first = true;
        foreach (var section in sections)
        {
            var entries = new List<Entry>();
            var ofSection = actions.FindAll(a => a.Section == section);
            foreach (var action in RankBy(ofSection, query, a => LabelOf(a, strings), _ => "")) entries.Add(new Entry { Action = action });
            if (section == PaletteSection.SlashCommands)
            {
                foreach (var command in commands) entries.Add(new Entry { Command = command });
            }
            if (entries.Count == 0) continue;
            if (!first) _list.Children.Add(Divider());
            first = false;
            _list.Children.Add(Header(HeaderOf(section, strings)));
            foreach (var entry in entries)
            {
                var row = entry.Action != null ? ActionRow(entry.Action, _shown.Count, strings, view!) : Row(entry.Command!, _shown.Count, strings);
                _shown.Add(entry);
                _rows.Add(row);
                _list.Children.Add(row);
            }
        }
        _empty.IsVisible = _shown.Count == 0;
        _list.Children.Add(_empty);
        // the first one is marked once something is typed, as in the extension
        Mark(query.Length > 0 && _shown.Count > 0 ? 0 : -1);
    }

    private static TextBlock Header(string text)
    {
        var header = new TextBlock { Text = text };
        header.Classes.Add("ClaudeChatMenuHeader");
        return header;
    }

    private static Border Divider()
    {
        var divider = new Border();
        divider.Classes.Add("ClaudeChatMenuDivider");
        return divider;
    }

    private static string HeaderOf(string section, ChatStrings strings) => section switch
    {
        PaletteSection.Context => strings.PaletteContext,
        PaletteSection.Model => strings.Model,
        PaletteSection.Customize => strings.PaletteCustomize,
        PaletteSection.Settings => strings.PaletteSettings,
        PaletteSection.SlashCommands => strings.SlashCommands,
        PaletteSection.Support => strings.PaletteSupport,
        _ => section,
    };

    private static string LabelOf(PaletteAction action, ChatStrings strings) => action.Text?.Invoke(strings) ?? action.Label;

    private Border Row(ChatCommand command, int index, ChatStrings strings)
    {
        var label = new TextBlock { Text = "/" + command.Name };
        label.Classes.Add("ClaudeChatMenuLabel");
        var row = new Border { Child = label };
        row.Classes.Add("ClaudeChatMenuItem");
        var description = Describe(command, strings);
        if (description.Length > 0) ToolTip.SetTip(row, description);
        Wire(row, index, () => Run(command));
        return row;
    }

    /// <summary>
    /// A row of an action: its name, and at the right the value it shows or its switch. A switch (and the others that keep the
    /// palette open) is drawn again after the action acts, so that the thumb slides.
    /// </summary>
    private Border ActionRow(PaletteAction action, int index, ChatStrings strings, ClaudeChatView view)
    {
        var label = new TextBlock { Text = LabelOf(action, strings) };
        label.Classes.Add("ClaudeChatMenuLabel");
        var dock = new DockPanel();
        if (action.IsOn != null)
        {
            var thumb = new Border();
            thumb.Classes.Add("ClaudeChatSwitchThumb");
            var track = new Border { Child = thumb };
            track.Classes.Add("ClaudeChatSwitch");
            var host = new Panel { Children = { track } };
            host.Classes.Add("ClaudeChatSwitchHost");
            DockPanel.SetDock(host, Dock.Right);
            dock.Children.Add(host);
            void Refresh() => track.Classes.Set("on", action.IsOn(view));
            Refresh();
            _refreshers.Add(Refresh);
        }
        else if (action.Value != null)
        {
            var value = new TextBlock();
            value.Classes.Add("ClaudeChatMenuValue");
            DockPanel.SetDock(value, Dock.Right);
            dock.Children.Add(value);
            void Refresh()
            {
                var text = action.Value(view, strings);
                value.Text = text;
                value.IsVisible = !string.IsNullOrEmpty(text);
            }
            Refresh();
            _refreshers.Add(Refresh);
        }
        dock.Children.Add(label);
        var row = new Border { Child = dock };
        row.Classes.Add("ClaudeChatMenuItem");
        Wire(row, index, () => Run(action));
        return row;
    }

    /// <summary>
    /// The pointer marks the row it is over. The desktop runs it at the press, as the extension's menu does. The touch layout waits
    /// for the tap (the pointer let go where it was pressed, within Avalonia's tap threshold), and leaves the press alone, so that a
    /// finger which starts to scroll the list scrolls it instead of running the row under it.
    /// </summary>
    private void Wire(Border row, int index, Action run)
    {
        row.PointerEntered += (_, _) => Mark(index);
        row.PointerPressed += (_, e) =>
        {
            if (IsTouch || !e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            run();
        };
        row.Tapped += (_, e) =>
        {
            if (!IsTouch) return;
            e.Handled = true;
            run();
        };
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

    /// <summary>
    /// Does what the row does, with the view. The palette closes first, and the keyboard goes back to the box, which the action may
    /// take (the model menu takes it for the model chip); a switch and the rows that keep the palette open stay open, and show what
    /// they are now.
    /// </summary>
    private void Run(PaletteAction action)
    {
        var view = View;
        var keeps = action.IsOn != null || action.KeepsOpen;
        if (!keeps || !CanAct(_conversation) || view == null) IsOpen = false;
        if (!CanAct(_conversation) || view == null) return;
        if (!keeps) _input?.Focus();
        action.Run(view);
        if (!keeps) return;
        foreach (var refresh in _refreshers) refresh();
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
    internal static List<ChatCommand> Rank(IReadOnlyList<ChatCommand> commands, string query, ChatStrings strings) =>
        RankBy(commands, query, command => command.Name, command => Describe(command, strings));

    /// <summary>The same four buckets for anything with a name and a description.</summary>
    private static List<T> RankBy<T>(IReadOnlyList<T> items, string query, Func<T, string> nameOf, Func<T, string> descriptionOf)
    {
        var ranked = new List<T>(items.Count);
        if (query.Length == 0)
        {
            ranked.AddRange(items);
            return ranked;
        }
        var buckets = new[] { new List<T>(), new List<T>(), new List<T>(), new List<T>() };
        foreach (var item in items)
        {
            var name = nameOf(item);
            if (string.Equals(name, query, StringComparison.OrdinalIgnoreCase)) buckets[0].Add(item);
            else if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) buckets[1].Add(item);
            else if (name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) buckets[2].Add(item);
            else if (descriptionOf(item).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) buckets[3].Add(item);
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
            "copy" => strings.CommandCopyDescription,
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
