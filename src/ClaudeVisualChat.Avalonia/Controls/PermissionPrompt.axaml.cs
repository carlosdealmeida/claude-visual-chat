using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ClaudeVisualChat.Controls;

/// <summary>Time source of <see cref="PermissionPrompt"/>; a test swaps it for a fake one.</summary>
internal interface IPromptClock
{
    TimeSpan Now { get; }
}

/// <summary>
/// The request Claude waits on (<see cref="ChatConversation.CurrentPermission"/>), above the reply box: what the
/// tool will do and the numbered answers. Keys 1, 2 and 3 answer while the focus is inside it, except in the field
/// that option 3 opens, where they are text. Hidden while nothing is asked, whatever took the request away.
/// <para>
/// No answer reaches a request the user has not seen: keys and clicks on the options are ignored for
/// <see cref="SettleTime"/> after a request comes on screen (a double tap, a fast second click), and a key held down
/// answers once, until it is released (the keyboard's repeat).
/// </para>
/// </summary>
internal partial class PermissionPrompt : UserControl
{
    /// <summary>How long after a request comes on screen its options take no keys or clicks.</summary>
    internal static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(300);

    private static readonly StyledProperty<ChatStrings> StringsProperty =
        ClaudeChatView.StringsProperty.AddOwner<PermissionPrompt>();

    private readonly EventHandler<KeyEventArgs> _anyKeyUp;
    private ChatConversation? _conversation;
    private ChatConversation? _tracked;
    private PendingPermission? _request;
    private TimeSpan _shownAt;
    private TopLevel? _topLevel;
    private bool _attached;

    /// <summary>The answer key that went down in the prompt and has not come up yet (the 11.0 events tell no repeat).</summary>
    private Key? _heldKey;

    public PermissionPrompt()
    {
        InitializeComponent();
        _anyKeyUp = OnAnyKeyUp;
        YesButton.Click += (_, _) =>
        {
            if (Settled) Answer(PermissionDecision.Allow);
        };
        AlwaysButton.Click += (_, _) =>
        {
            if (Settled) Answer(PermissionDecision.AllowAlways);
        };
        NoButton.Click += (_, _) =>
        {
            if (Settled) OpenDenyField();
        };
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        DenyBox.AddHandler(KeyDownEvent, OnDenyKeyDown, RoutingStrategies.Tunnel);
        foreach (var control in new Control[] { YesButton, AlwaysButton, NoButton, DenyBox })
        {
            control.GotFocus += (_, _) => UpdateSelected();
            control.LostFocus += (_, _) => UpdateSelected();
        }
        IsVisible = false;
        Refresh();
    }

    /// <summary>
    /// Like the extension, one option shows in the accent: the one with the keyboard, else option 1; none while the
    /// field of option 3 has it.
    /// </summary>
    private void UpdateSelected()
    {
        var options = new[] { YesButton, AlwaysButton, NoButton };
        Button? selected = null;
        foreach (var option in options)
        {
            if (option.IsFocused) selected = option;
        }
        if (selected == null && !DenyBox.IsFocused) selected = YesButton;
        foreach (var option in options) option.Classes.Set("selected", ReferenceEquals(option, selected));
    }

    /// <summary>
    /// Raised when another request comes on screen, or none is left; the argument tells whether the focus was
    /// inside the prompt just before.
    /// </summary>
    internal event Action<bool>? RequestChanged;

    internal ChatConversation? Conversation
    {
        get => _conversation;
        set
        {
            _conversation = value;
            if (_attached) Track(value);
        }
    }

    /// <summary>The request on screen.</summary>
    internal PendingPermission? Request => _request;

    internal IPromptClock Clock { get; set; } = StopwatchClock.Instance;

    private ChatStrings Strings => GetValue(StringsProperty) ?? ChatStrings.English;

    /// <summary>The request on screen has been there long enough to have been seen.</summary>
    private bool Settled => Clock.Now - _shownAt >= SettleTime;

    /// <summary>
    /// True for a key that answered (or went down in) the prompt and is still held: the keyboard repeating it. The
    /// view takes these wherever the focus went, so a held 1 neither answers the next request nor types into the
    /// reply box.
    /// </summary>
    internal bool IsHeld(Key key) => _heldKey == key;

    /// <summary>Puts the keyboard on option 1, so Enter, 1, 2 and 3 answer at once.</summary>
    internal void FocusFirstOption()
    {
        // a prompt never shown before has no template yet, so its buttons are not on screen: lay it out first
        if (!YesButton.IsAttachedToVisualTree()) TopLevel.GetTopLevel(this)?.UpdateLayout();
        if (YesButton.IsAttachedToVisualTree())
        {
            YesButton.Focus(NavigationMethod.Tab);
            return;
        }
        // asked inside a layout pass (a view built by a template), where laying out again does nothing: the
        // buttons arrive with this pass, so focus right after it, if the same request is still on screen
        var request = _request;
        Dispatcher.UIThread.Post(() =>
        {
            if (request != null && ReferenceEquals(request, _request)) YesButton.Focus(NavigationMethod.Tab);
        }, DispatcherPriority.Loaded);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        // a held key may come up anywhere (the focus moves on after an answer): watch the whole window
        _topLevel = TopLevel.GetTopLevel(this);
        _topLevel?.AddHandler(KeyUpEvent, _anyKeyUp, RoutingStrategies.Tunnel, handledEventsToo: true);
        Track(_conversation);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        _topLevel?.RemoveHandler(KeyUpEvent, _anyKeyUp);
        _topLevel = null;
        _heldKey = null;
        Track(null); // the conversation outlives the view: let go of its events
    }

    /// <summary>Remembers an answer key going down in the prompt until it comes up.</summary>
    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (_request == null || e.KeyModifiers != KeyModifiers.None) return;
        var digit = OptionOf(e.Key) != 0;
        if (digit && DenyBox.IsKeyboardFocusWithin) return; // text in the field, free to repeat
        if (digit || e.Key == Key.Enter) _heldKey = e.Key; // Enter clicks the focused option
    }

    private void OnAnyKeyUp(object? sender, KeyEventArgs e)
    {
        if (_heldKey == e.Key) _heldKey = null;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StringsProperty) Refresh();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        // only plain digits: Ctrl+1 and the like belong to the app, and what is typed in the field is text
        if (e.Handled || _request == null || e.KeyModifiers != KeyModifiers.None || DenyBox.IsKeyboardFocusWithin)
            return;
        var option = OptionOf(e.Key);
        if (option == 0 || (option == 2 && !AlwaysButton.IsVisible)) return;
        e.Handled = true; // a handled key types no character, so the 3 never lands in the field
        if (!Settled) return; // too soon: the user has not seen this request yet
        switch (option)
        {
            case 1:
                Answer(PermissionDecision.Allow);
                break;
            case 2:
                Answer(PermissionDecision.AllowAlways);
                break;
            default:
                OpenDenyField();
                break;
        }
    }

    /// <summary>The option a key stands for (1 to 3), or 0.</summary>
    private static int OptionOf(Key key) => key switch
    {
        Key.D1 or Key.NumPad1 => 1,
        Key.D2 or Key.NumPad2 => 2,
        Key.D3 or Key.NumPad3 => 3,
        _ => 0,
    };

    private void Track(ChatConversation? conversation)
    {
        if (!ReferenceEquals(conversation, _tracked))
        {
            if (_tracked != null)
            {
                _tracked.PropertyChanged -= OnConversationChanged;
                _tracked.PendingPermissions.CollectionChanged -= OnPendingChanged;
            }
            _tracked = conversation;
            if (_tracked != null)
            {
                _tracked.PropertyChanged += OnConversationChanged;
                _tracked.PendingPermissions.CollectionChanged += OnPendingChanged;
            }
        }
        Update();
    }

    private void OnConversationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatConversation.CurrentPermission)) Update();
    }

    private void OnPendingChanged(object? sender, NotifyCollectionChangedEventArgs e) => Update();

    /// <summary>Shows the conversation's current request, the next one or none.</summary>
    private void Update()
    {
        var request = _tracked?.CurrentPermission;
        var changed = !ReferenceEquals(request, _request);
        var hadFocus = IsKeyboardFocusWithin;
        if (changed)
        {
            _request = request;
            _shownAt = Clock.Now;
            CloseDenyField();
            Fill();
            DetailScroller.SetCurrentValue(ScrollViewer.OffsetProperty, default(Vector)); // a new request starts at its top
        }
        UpdateWaiting();
        IsVisible = request != null;
        if (changed && _attached) RequestChanged?.Invoke(hadFocus);
    }

    private void Refresh()
    {
        var strings = Strings;
        YesText.Text = strings.PermissionYes;
        NoText.Text = strings.PermissionNo;
        DenyBox.Watermark = strings.DenyPlaceholder;
        Fill();
        UpdateWaiting();
    }

    /// <summary>The title, what the tool will do and the options of the request on screen.</summary>
    private void Fill()
    {
        DiffPanel.Children.Clear();
        var request = _request;
        var strings = Strings;
        // "this session" only when every rule it saves stays in the session: the claude may keep one in the settings
        AlwaysText.Text = request == null || request.AllowAlwaysStaysInSession
            ? strings.PermissionYesAlways
            : strings.PermissionYesAlwaysBeyondSession;
        if (request == null)
        {
            TitleText.Text = "";
            SummaryText.Text = "";
            AlwaysButton.IsVisible = false;
            return;
        }

        var detail = request.Summary;
        switch (KindOf(request.ToolName))
        {
            case ToolKind.Command:
                TitleText.Text = strings.PermissionRunCommand;
                // the whole command, not its summary's first line: the user says yes to all of it
                detail = Command(request) ?? detail;
                break;
            case ToolKind.File:
                TitleText.Text = strings.PermissionEditFile;
                // all of it, not the list's 20 lines a side: the user says yes to the whole change (Ruling 30)
                var diff = ToolPresentation.Diff(request.ToolName, request.Input, maxLinesPerSide: null);
                if (diff != null && diff.Count > 0) DiffPanel.Children.Add(ToolCallView.DiffBox(diff, strings, whole: true)); // all of it: the request scrolls
                break;
            default:
                TitleText.Text = strings.FormatUseTool(request.DisplayName);
                break;
        }
        // never cut: the end of a command may be the part that matters; a long one scrolls inside the prompt
        SummaryText.Text = detail;
        SummaryBox.IsVisible = detail.Length > 0;
        AlwaysButton.IsVisible = request.CanAllowAlways;
    }

    private void UpdateWaiting()
    {
        var waiting = _tracked == null || _request == null ? 0 : _tracked.PendingPermissions.Count - 1;
        MoreText.Text = waiting > 0 ? Strings.FormatMorePending(waiting) : "";
        MoreText.IsVisible = waiting > 0;
    }

    private void Answer(PermissionDecision decision, string? message = null)
    {
        var conversation = _tracked;
        var request = _request;
        if (conversation == null || request == null) return;
        conversation.AnswerPermission(request, decision, message);
    }

    private void OpenDenyField()
    {
        if (_request == null) return;
        if (!DenyBox.IsVisible)
        {
            DenyBox.Clear();
            DenyBox.IsVisible = true;
        }
        DenyBox.Focus(NavigationMethod.Tab);
    }

    private void CloseDenyField()
    {
        DenyBox.IsVisible = false;
        DenyBox.Clear();
    }

    private void OnDenyKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None) return;
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            var text = DenyBox.Text;
            // nothing written: a plain no, and the host sends the claude its default text
            Answer(PermissionDecision.Deny, string.IsNullOrWhiteSpace(text) ? null : text!.Trim());
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CloseDenyField();
            NoButton.Focus(NavigationMethod.Tab);
        }
    }

    private static string? Command(PendingPermission request) =>
        request.Input.ValueKind == JsonValueKind.Object
        && request.Input.TryGetProperty("command", out var command)
        && command.ValueKind == JsonValueKind.String
            ? command.GetString()
            : null;

    /// <summary>Real time, from a stopwatch.</summary>
    private sealed class StopwatchClock : IPromptClock
    {
        public static readonly StopwatchClock Instance = new StopwatchClock();

        private readonly Stopwatch _watch = Stopwatch.StartNew();

        public TimeSpan Now => _watch.Elapsed;
    }

    private enum ToolKind
    {
        Command,
        File,
        Other,
    }

    private static ToolKind KindOf(string toolName) => toolName switch
    {
        "Bash" or "PowerShell" => ToolKind.Command,
        "Edit" or "Write" or "MultiEdit" or "NotebookEdit" => ToolKind.File,
        _ => ToolKind.Other,
    };
}
