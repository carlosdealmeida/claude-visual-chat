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
/// <para>
/// In the touch layout (pseudo-class <c>:touch</c>) the title is a short question ("Edit OrderService.cs?", "Run this
/// command?", "Use Fetch?"), the command or the diff under it is as whole as ever, and the answers are not numbered and go
/// across the block at 48 px, the first one always in the accent. The field of option 3 has a button under it that
/// sends what is written, as Enter does, and while it is open the command or the diff shrinks to a peek, so that
/// the field and the button stay above the on-screen keyboard.
/// </para>
/// </summary>
internal partial class PermissionPrompt : UserControl
{
    /// <summary>How long after a request comes on screen its options take no keys or clicks.</summary>
    internal static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(300);

    private static readonly StyledProperty<ChatStrings> StringsProperty =
        ClaudeChatView.StringsProperty.AddOwner<PermissionPrompt>();

    private static readonly StyledProperty<ChatLayout> LayoutProperty =
        ClaudeChatView.LayoutProperty.AddOwner<PermissionPrompt>();

    /// <summary>The resource that is the most the detail (the command or the diff) grows to before it scrolls; the XAML binds to it.</summary>
    private const string DetailHeightKey = "ClaudeChat.PromptDetailMaxHeight";

    /// <summary>The resource that is its height instead while the touch layout's field of option 3 is open.</summary>
    private const string DetailPeekKey = "ClaudeChat.PromptDetailPeekHeight";

    private readonly EventHandler<KeyEventArgs> _anyKeyUp;
    private ChatConversation? _conversation;
    private ChatConversation? _tracked;
    private PendingPermission? _request;
    private TimeSpan _shownAt;
    private TopLevel? _topLevel;
    private bool _attached;

    /// <summary>True while the detail's height is bound to <see cref="DetailPeekKey"/> (see <see cref="UpdateDenyField"/>).</summary>
    private bool _peeking;

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
        DenySendButton.Click += (_, _) =>
        {
            // the field opens only after the pause, but the request may come back on screen with it open (the view leaves read-only)
            if (Settled) SendDeny();
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
    /// field of option 3 has it. Not in the touch layout, where the first answer is the accent whatever has the keyboard
    /// (a finger that presses another answer must not move it), so none is selected.
    /// </summary>
    private void UpdateSelected()
    {
        var options = new[] { YesButton, AlwaysButton, NoButton };
        Button? selected = null;
        if (!IsTouch)
        {
            foreach (var option in options)
            {
                if (option.IsFocused) selected = option;
            }
            if (selected == null && !DenyBox.IsFocused) selected = YesButton;
        }
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

    private bool IsTouch => GetValue(LayoutProperty) == ChatLayout.Touch;

    /// <summary>The keyboard is in the field of option 3, or on the button under it: what is typed there is text, not an answer.</summary>
    private bool InDenyField => DenyBox.IsKeyboardFocusWithin || DenySendButton.IsKeyboardFocusWithin;

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
        // back in a window, the request comes on screen again: the pause starts over and the view hears of it
        _request = null;
        Track(null); // the conversation outlives the view: let go of its events
    }

    /// <summary>Remembers an answer key going down in the prompt until it comes up.</summary>
    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (_request == null || e.KeyModifiers != KeyModifiers.None) return;
        var digit = OptionOf(e.Key) != 0;
        if (digit && InDenyField) return; // text in the field, free to repeat
        if (digit || e.Key == Key.Enter) _heldKey = e.Key; // Enter clicks the focused option
    }

    private void OnAnyKeyUp(object? sender, KeyEventArgs e)
    {
        if (_heldKey == e.Key) _heldKey = null;
    }

    /// <summary>
    /// The prompt is shown again after being hidden (the view left read-only): the user has not seen the request yet,
    /// so the pause before a key answers it starts over.
    /// </summary>
    internal void RestartPause() => _shownAt = Clock.Now;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StringsProperty) Refresh();
        else if (change.Property == LayoutProperty) UpdateTouch();
    }

    /// <summary>
    /// In the touch layout (pseudo-class <c>:touch</c>) the title asks (the layout changes what <see cref="Fill"/> writes), no
    /// answer is "selected" (the first is the accent), and the field of option 3, when open, has its button and the detail a
    /// peek (<see cref="UpdateDenyField"/>).
    /// </summary>
    private void UpdateTouch()
    {
        PseudoClasses.Set(":touch", IsTouch);
        UpdateSelected();
        Refresh();
        UpdateDenyField();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        // only plain digits: Ctrl+1 and the like belong to the app, and what is typed in the field is text
        if (e.Handled || _request == null || e.KeyModifiers != KeyModifiers.None || InDenyField)
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
        // out of the window, only the events go: Avalonia is taking the tree apart, and coming back draws it again
        if (_attached) Update();
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
        DenySendText.Text = strings.Send;
        Fill();
        UpdateWaiting();
    }

    /// <summary>The title, what the tool will do and the options of the request on screen.</summary>
    private void Fill()
    {
        DiffPanel.Children.Clear();
        var request = _request;
        var strings = Strings;
        var touch = IsTouch;
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
                TitleText.Text = touch ? strings.PermissionRunCommandQuestion : strings.PermissionRunCommand;
                // the whole command, not its summary's first line: the user says yes to all of it
                detail = InputString(request, "command") ?? detail;
                break;
            case ToolKind.File:
                TitleText.Text = touch ? EditFileQuestion(request, strings) : strings.PermissionEditFile;
                // all of it, not the list's 20 lines a side: the user says yes to the whole change (Ruling 30)
                var diff = ToolPresentation.Diff(request.ToolName, request.Input, maxLinesPerSide: null);
                if (diff != null && diff.Count > 0) DiffPanel.Children.Add(ToolCallView.DiffBox(diff, strings, whole: true)); // all of it: the request scrolls
                break;
            default:
                TitleText.Text = touch ? strings.FormatUseToolQuestion(request.DisplayName) : strings.FormatUseTool(request.DisplayName);
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
            UpdateDenyField();
        }
        DenyBox.Focus(NavigationMethod.Tab);
    }

    private void CloseDenyField()
    {
        DenyBox.IsVisible = false;
        DenyBox.Clear();
        UpdateDenyField();
    }

    /// <summary>
    /// The host's back button (<see cref="ClaudeChatView.CloseTopLayer"/>) closes the field of option 3 when it is open and on
    /// screen (a request that is hidden, in a read-only view, keeps its field as it was), and the request stays. The keyboard
    /// goes to "No", the answer that opened the field, if the field had it, as Esc in the field does it. False when there was no
    /// field to close.
    /// </summary>
    internal bool CloseDenyFieldIfOpen()
    {
        if (!DenyBox.IsEffectivelyVisible) return false;
        var hadKeyboard = InDenyField;
        CloseDenyField();
        if (hadKeyboard) NoButton.Focus(NavigationMethod.Tab);
        return true;
    }

    /// <summary>
    /// The field of option 3 is typed in, and in the touch layout that is with the on-screen keyboard up, which leaves the view
    /// some 400 DIPs. So in that layout, while the field is open, the button under it is there and the detail (the command or the
    /// diff) shrinks to a peek, for the title, the answers, the field and its button to stay above the keyboard; closing the
    /// field gives the detail its room back. The detail's height is the resource <see cref="DetailPeekKey"/> then, the one of
    /// <see cref="DetailHeightKey"/> otherwise.
    /// </summary>
    private void UpdateDenyField()
    {
        var touchField = IsTouch && DenyBox.IsVisible;
        DenySendButton.IsVisible = touchField;
        if (touchField == _peeking) return;
        _peeking = touchField;
        DetailScroller.Bind(MaxHeightProperty, DetailScroller.GetResourceObservable(touchField ? DetailPeekKey : DetailHeightKey));
    }

    private void OnDenyKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None) return;
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            SendDeny();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            CloseDenyField();
            NoButton.Focus(NavigationMethod.Tab);
        }
    }

    /// <summary>
    /// Answers no with what is written in the field (Enter, or the touch layout's button): nothing written is a plain no, and the
    /// host sends the claude its default text.
    /// </summary>
    private void SendDeny()
    {
        var text = DenyBox.Text;
        Answer(PermissionDecision.Deny, string.IsNullOrWhiteSpace(text) ? null : text!.Trim());
    }

    /// <summary>
    /// The touch layout's title for an edit: "Edit OrderService.cs?", the file named by the last part of its path (the
    /// notebook's, for a NotebookEdit). A request that gives no path asks about the tool.
    /// </summary>
    private static string EditFileQuestion(PendingPermission request, ChatStrings strings)
    {
        var path = InputString(request, request.ToolName == "NotebookEdit" ? "notebook_path" : "file_path");
        var name = string.IsNullOrEmpty(path) ? "" : ChatFile.LastSegment(path!);
        return name.Length > 0 ? strings.FormatEditFileQuestion(name) : strings.FormatUseToolQuestion(request.DisplayName);
    }

    /// <summary>A text of the tool's input; null when the input has none by that name.</summary>
    private static string? InputString(PendingPermission request, string name) =>
        request.Input.ValueKind == JsonValueKind.Object
        && request.Input.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
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
