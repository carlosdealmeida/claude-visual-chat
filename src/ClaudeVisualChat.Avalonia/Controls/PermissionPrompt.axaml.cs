using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
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
/// The request Claude waits on (<see cref="ChatConversation.CurrentPermission"/>), above the reply box. Hidden while nothing is
/// asked, whatever took the request away.
/// <para>
/// On the desktop it is the card of the extension 2.1.310: a question per tool ("Allow this bash command?", "Make this edit to
/// <c>a.cs</c>?", "Do you want to proceed with <b>mcp__x__y</b>?", the last with the input folded under "Details"), what the tool
/// will do (the command, the url, the edit's diff), the options and, always under them, the field of "No" with "Esc to cancel".
/// The digits go to the visible options in order and the next one to the field; Enter takes the focused option, Esc and "No"
/// refuse at once with what the field says, and ↑/↓ go round the options and the field. With <c>default_to_no</c> the keyboard
/// starts on "No" and the digits answer nothing. When the user has text in the reply box and edited it less than
/// <see cref="TypingPause"/> before, the card waits, and "Waiting for permission…" shows in its place. The request's own
/// description is never shown (the extension does not).
/// </para>
/// <para>
/// No answer reaches a request the user has not seen: keys and clicks on the options are ignored for <see cref="SettleTime"/>
/// after a request comes on screen (drawn disabled meanwhile, on the desktop), and a key held down answers once, until it is
/// released (the keyboard's repeat).
/// </para>
/// <para>
/// In the touch layout (pseudo-class <c>:touch</c>) the request is the 0.5.0 one: the title is a short question ("Edit
/// OrderService.cs?", "Run this command?", "Use Fetch?"), the command or the diff under it is as whole as ever, and the answers are
/// not numbered and go across the block at 48 px, the first one always in the accent. Option 3 opens the field, which has a button
/// under it that sends what is written, as Enter does, and while it is open the command or the diff shrinks to a peek, so that the
/// field and the button stay above the on-screen keyboard. Its pause is <see cref="TouchSettleTime"/>, and it never waits for
/// the typing.
/// </para>
/// </summary>
internal partial class PermissionPrompt : UserControl
{
    /// <summary>How long after the desktop's card comes on screen its options take no keys or clicks (the extension's 500 ms).</summary>
    internal static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(500);

    /// <summary>The same for the touch layout's request, which keeps the 0.5.0 pause.</summary>
    internal static readonly TimeSpan TouchSettleTime = TimeSpan.FromMilliseconds(300);

    /// <summary>How long the desktop's card waits after the user last edited text in the reply box.</summary>
    internal static readonly TimeSpan TypingPause = TimeSpan.FromMilliseconds(1500);

    /// <summary>The most a refusal carries of what the field says (the host's contract).</summary>
    internal const int MaxDenyLength = 4000;

    private static readonly StyledProperty<ChatStrings> StringsProperty =
        ClaudeChatView.StringsProperty.AddOwner<PermissionPrompt>();

    private static readonly StyledProperty<ChatLayout> LayoutProperty =
        ClaudeChatView.LayoutProperty.AddOwner<PermissionPrompt>();

    private static readonly JsonSerializerOptions IndentedJson = new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // "<" and accents as they are: the user reads it
    };

    /// <summary>The resource that is the most the detail (the command or the diff) grows to before it scrolls; the XAML binds to it.</summary>
    private const string DetailHeightKey = "ClaudeChat.PromptDetailMaxHeight";

    /// <summary>The resource that is its height instead while the touch layout's field of option 3 is open.</summary>
    private const string DetailPeekKey = "ClaudeChat.PromptDetailPeekHeight";

    private readonly EventHandler<KeyEventArgs> _anyKeyUp;

    /// <summary>Ticks while the desktop's card arms or waits for the typing to pause; stopped out of the window.</summary>
    private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };

    private ChatConversation? _conversation;
    private ChatConversation? _tracked;
    private PendingPermission? _request;
    private TimeSpan _shownAt;
    private TopLevel? _topLevel;
    private bool _attached;

    /// <summary>True while the desktop's card waits for the typing in the reply box to pause (<see cref="TypingPause"/>).</summary>
    private bool _waiting;

    /// <summary>True while the touch layout's field of option 3 is open (the desktop's is always there).</summary>
    private bool _denyOpen;

    /// <summary>True while the detail's height is bound to <see cref="DetailPeekKey"/> (see <see cref="UpdateDenyField"/>).</summary>
    private bool _peeking;

    /// <summary>The answer key that went down in the prompt and has not come up yet (the 11.0 events tell no repeat).</summary>
    private Key? _heldKey;

    public PermissionPrompt()
    {
        InitializeComponent();
        _anyKeyUp = OnAnyKeyUp;
        _timer.Tick += (_, _) => Tick();
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
            if (!Settled) return;
            if (IsTouch) OpenDenyField();
            else Refuse(); // at once, with what the field says
        };
        DenySendButton.Click += (_, _) =>
        {
            // the field opens only after the pause, but the request may come back on screen with it open (the view leaves read-only)
            if (Settled) Refuse();
        };
        DetailsButton.PropertyChanged += (_, e) =>
        {
            if (e.Property == ToggleButton.IsCheckedProperty) UpdateDetails();
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
    /// Like the extension, one option shows in the accent: the one with the keyboard, else the one Enter starts on (option 1, or
    /// "No" with <c>default_to_no</c>); none while the field has it. Not in the touch layout, where the first answer is the accent
    /// whatever has the keyboard (a finger that presses another answer must not move it), so none is selected.
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
            if (selected == null && !DenyBox.IsFocused) selected = StartOption;
        }
        foreach (var option in options) option.Classes.Set("selected", ReferenceEquals(option, selected));
    }

    /// <summary>
    /// Raised when another request comes on screen, or none is left, or the desktop's card stops waiting for the typing; the
    /// argument tells whether the focus was inside the prompt just before.
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

    /// <summary>
    /// When the user last edited the reply box, while it has text (the conversation's <see cref="ChatConversation.Clock"/>); null
    /// when it is empty. The view hands it over from the reply box.
    /// </summary>
    internal Func<DateTimeOffset?>? LastTyped { get; set; }

    /// <summary>True while the timer runs: the desktop's card arming, or waiting for the typing to pause.</summary>
    internal bool IsTicking => _timer.IsEnabled;

    /// <summary>
    /// The timer's tick: the card that waited for the typing comes once it paused, and the options are drawn enabled once
    /// armed; the timer stops when neither is left.
    /// </summary>
    internal void Tick()
    {
        if (_waiting && !TypedJustNow()) StopWaiting();
        UpdateArming();
        UpdateTimer();
    }

    private ChatStrings Strings => GetValue(StringsProperty) ?? ChatStrings.English;

    private bool IsTouch => GetValue(LayoutProperty) == ChatLayout.Touch;

    /// <summary>The keyboard is in the field of "No", or on the button under it: what is typed there is text, not an answer.</summary>
    private bool InDenyField => DenyBox.IsKeyboardFocusWithin || DenySendButton.IsKeyboardFocusWithin;

    /// <summary>The request on screen has been there long enough to have been seen.</summary>
    private bool Settled => Clock.Now - _shownAt >= (IsTouch ? TouchSettleTime : SettleTime);

    /// <summary>The option the desktop's card starts on: "No" with <c>default_to_no</c>, else option 1.</summary>
    private Button StartOption => !IsTouch && _request is { DefaultToNo: true } ? NoButton : YesButton;

    /// <summary>A panel is open (Esc closes it first: it is on top of the card).</summary>
    private bool PanelOpen => _tracked is { ShownPanel: not ChatPanel.None };

    /// <summary>
    /// True for a key that answered (or went down in) the prompt and is still held: the keyboard repeating it. The
    /// view takes these wherever the focus went, so a held 1 neither answers the next request nor types into the
    /// reply box.
    /// </summary>
    internal bool IsHeld(Key key) => _heldKey == key;

    /// <summary>
    /// Puts the keyboard on the option Enter starts on (option 1, or the desktop card's "No" with <c>default_to_no</c>), so Enter
    /// and the digits answer at once. Not while the card waits for the typing in the reply box.
    /// </summary>
    internal void FocusFirstOption()
    {
        if (_waiting) return;
        // a prompt never shown before has no template yet, so its buttons are not on screen: lay it out first
        if (!YesButton.IsAttachedToVisualTree()) TopLevel.GetTopLevel(this)?.UpdateLayout();
        if (YesButton.IsAttachedToVisualTree())
        {
            StartOption.Focus(NavigationMethod.Tab);
            return;
        }
        // asked inside a layout pass (a view built by a template), where laying out again does nothing: the
        // buttons arrive with this pass, so focus right after it, if the same request is still on screen
        var request = _request;
        Dispatcher.UIThread.Post(() =>
        {
            if (request != null && ReferenceEquals(request, _request) && !_waiting) StartOption.Focus(NavigationMethod.Tab);
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
        _timer.Stop(); // no timer runs in a control that is out of the window
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
    internal void RestartPause()
    {
        _shownAt = Clock.Now;
        UpdateArming();
        UpdateTimer();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StringsProperty) Refresh();
        else if (change.Property == LayoutProperty) UpdateTouch();
    }

    /// <summary>
    /// In the touch layout (pseudo-class <c>:touch</c>) the request is the 0.5.0 one: the title asks (the layout changes what
    /// <see cref="Fill"/> writes), no answer is "selected" (the first is the accent), and the field of option 3, when open, has its
    /// button and the detail a peek (<see cref="UpdateDenyField"/>); it does not wait for the typing either.
    /// </summary>
    private void UpdateTouch()
    {
        PseudoClasses.Set(":touch", IsTouch);
        if (IsTouch && _waiting) StopWaiting();
        UpdateSelected();
        Refresh();
        UpdateDenyField();
        UpdateArming();
        UpdateTimer();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        // only plain keys: Ctrl+1 and the like belong to the app, and what is typed in the field is text
        if (e.Handled || _request == null || e.KeyModifiers != KeyModifiers.None || InDenyField)
            return;
        if (IsTouch) OnTouchKeyDown(e);
        else OnCardKeyDown(e);
    }

    /// <summary>The desktop card's keys: the digits, Esc for "No", and ↑/↓ (Enter is the focused button's own).</summary>
    private void OnCardKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Up || e.Key == Key.Down)
        {
            e.Handled = true;
            Move(e.Key == Key.Down ? 1 : -1);
            return;
        }
        if (e.Key == Key.Escape)
        {
            if (PanelOpen) return; // the panel is on top: the view closes it first
            e.Handled = true;
            if (!Settled) return;
            _heldKey = Key.Escape; // a held Esc refuses once
            Refuse();
            return;
        }
        var digit = OptionOf(e.Key);
        if (digit == 0 || _request!.DefaultToNo) return; // "No" first: the digits answer nothing
        var options = VisibleOptions();
        if (digit > options.Count + 1) return;
        e.Handled = true; // a handled key types no character
        if (!Settled) return; // too soon: the user has not seen this request yet
        if (digit == options.Count + 1) DenyBox.Focus(NavigationMethod.Tab); // the digit after the last option: the field
        else Activate(options[digit - 1]);
    }

    /// <summary>The touch layout's keys, as in 0.5.0: 1, 2 and 3, the last opening the field.</summary>
    private void OnTouchKeyDown(KeyEventArgs e)
    {
        var option = OptionOf(e.Key);
        if (option == 0 || option > 3 || (option == 2 && !AlwaysButton.IsVisible)) return;
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

    /// <summary>The option a digit stands for (1 to 9), or 0.</summary>
    private static int OptionOf(Key key) => key switch
    {
        >= Key.D1 and <= Key.D9 => key - Key.D0,
        >= Key.NumPad1 and <= Key.NumPad9 => key - Key.NumPad0,
        _ => 0,
    };

    /// <summary>The options on screen, in order.</summary>
    private List<Button> VisibleOptions()
    {
        var options = new List<Button> { YesButton };
        if (AlwaysButton.IsVisible) options.Add(AlwaysButton);
        options.Add(NoButton);
        return options;
    }

    private void Activate(Button option)
    {
        if (ReferenceEquals(option, YesButton)) Answer(PermissionDecision.Allow);
        else if (ReferenceEquals(option, AlwaysButton)) Answer(PermissionDecision.AllowAlways);
        else Refuse();
    }

    /// <summary>↑/↓ on the desktop's card: round the options and the field.</summary>
    private void Move(int step)
    {
        var stops = new List<Control>(VisibleOptions()) { DenyBox };
        var at = stops.FindIndex(c => c.IsKeyboardFocusWithin);
        var next = at < 0 ? 0 : (at + step + stops.Count) % stops.Count;
        stops[next].Focus(NavigationMethod.Tab);
    }

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
            _waiting = request != null && TypedJustNow();
            ResetDenyField();
            DetailsButton.IsChecked = false;
            Fill();
            DetailScroller.SetCurrentValue(ScrollViewer.OffsetProperty, default(Vector)); // a new request starts at its top
        }
        UpdateMorePending();
        UpdateShown();
        UpdateArming();
        UpdateTimer();
        if (changed && _attached) RequestChanged?.Invoke(hadFocus);
    }

    /// <summary>The user edited the text that is in the reply box less than <see cref="TypingPause"/> ago (desktop only).</summary>
    private bool TypedJustNow() =>
        !IsTouch && _tracked != null && LastTyped?.Invoke() is { } typed && _tracked.Clock() - typed < TypingPause;

    /// <summary>The typing paused: the card comes on screen now, its pause starts, and the view hears of it.</summary>
    private void StopWaiting()
    {
        var hadFocus = IsKeyboardFocusWithin;
        _waiting = false;
        _shownAt = Clock.Now;
        UpdateShown();
        if (_attached && _request != null) RequestChanged?.Invoke(hadFocus);
    }

    /// <summary>The prompt shows while there is a request; the card, or "Waiting for permission…" in its place.</summary>
    private void UpdateShown()
    {
        IsVisible = _request != null;
        Frame.IsVisible = !_waiting;
        WaitingText.IsVisible = _waiting;
    }

    /// <summary>The desktop card's options are drawn disabled until it is armed.</summary>
    private void UpdateArming() => Options.Classes.Set("arming", !IsTouch && _request != null && !_waiting && !Settled);

    private void UpdateTimer()
    {
        var needed = _attached && _request != null && (_waiting || Options.Classes.Contains("arming"));
        if (needed && !_timer.IsEnabled) _timer.Start();
        else if (!needed) _timer.Stop();
    }

    private void Refresh()
    {
        var strings = Strings;
        YesText.Text = strings.PermissionYes;
        NoText.Text = IsTouch ? strings.PermissionNo : strings.PermissionRefuse;
        DenyBox.Watermark = strings.DenyPlaceholder;
        DenySendText.Text = strings.Send;
        HintText.Text = strings.PermissionEscToCancel;
        WaitingText.Text = strings.WaitingForPermission;
        DetailsText.Text = strings.PermissionDetails;
        Fill();
        UpdateMorePending();
    }

    /// <summary>The title, what the tool will do and the options of the request on screen.</summary>
    private void Fill()
    {
        DiffPanel.Children.Clear();
        NotesPanel.Children.Clear();
        ExtraText.Text = "";
        DetailsJson.Text = "";
        var request = _request;
        var strings = Strings;
        // "this session" only when every rule it saves stays in the session: the claude may keep one in the settings
        AlwaysText.Text = request == null || request.AllowAlwaysStaysInSession
            ? strings.PermissionYesAlways
            : strings.PermissionYesAlwaysBeyondSession;
        var detail = "";
        if (request != null)
        {
            detail = IsTouch ? FillTouch(request, strings) : FillCard(request, strings);
        }
        else
        {
            SetTitle("");
        }
        // never cut: the end of a command may be the part that matters; a long one scrolls inside the prompt
        SummaryText.Text = detail;
        SummaryBox.IsVisible = detail.Length > 0;
        ExtraBox.IsVisible = !string.IsNullOrEmpty(ExtraText.Text);
        AlwaysButton.IsVisible = request != null && request.CanAllowAlways;
        HintText.IsVisible = !IsTouch;
        UpdateNumbers();
        UpdateDetails();
        UpdateSelected();
    }

    /// <summary>The touch layout's request, as in 0.5.0: a short question and the command or the file's diff. Returns the detail.</summary>
    private string FillTouch(PendingPermission request, ChatStrings strings)
    {
        var detail = request.Summary;
        switch (KindOf(request.ToolName))
        {
            case ToolKind.Command:
                SetTitle(strings.PermissionRunCommandQuestion);
                // the whole command, not its summary's first line: the user says yes to all of it
                detail = InputString(request, "command") ?? detail;
                break;
            case ToolKind.File:
                SetTitle(EditFileQuestion(request, strings));
                AddDiff(request, strings);
                break;
            default:
                SetTitle(strings.FormatUseToolQuestion(request.DisplayName));
                break;
        }
        return detail;
    }

    /// <summary>The desktop's card, as the extension 2.1.310 asks for each tool. Returns what goes in the box under the title.</summary>
    private string FillCard(PendingPermission request, ChatStrings strings)
    {
        switch (request.ToolName)
        {
            case "Bash":
            case "PowerShell":
                SetTitle(request.ToolName == "Bash" ? strings.PermissionBash : strings.PermissionPowerShell);
                AddNote(InputString(request, "description"), muted: true);
                // the whole command: the user says yes to all of it (the extension lets it be edited; the host can't send that)
                return InputString(request, "command") ?? request.Summary;
            case "Edit":
            case "Write":
            case "Read":
                var name = FileName(request, "file_path");
                if (name.Length == 0) break; // nothing to name: asked as any other tool
                SetTitle(request.ToolName == "Edit" ? strings.PermissionEdit
                    : request.ToolName == "Write" ? strings.PermissionWrite : strings.PermissionRead, name, "ClaudeChatPermissionPath");
                // unlike the extension, which opens an editor tab with it, the diff stays in the card: the component has no tab
                if (request.ToolName != "Read") AddDiff(request, strings);
                return "";
            case "Glob":
            case "Grep":
                var path = InputString(request, "path");
                var glob = request.ToolName == "Glob";
                if (string.IsNullOrEmpty(path)) SetTitle(glob ? strings.PermissionGlobAnywhere : strings.PermissionGrepAnywhere);
                else SetTitle(glob ? strings.PermissionGlob : strings.PermissionGrep, path, "ClaudeChatPermissionPath");
                return "";
            case "WebFetch":
                SetTitle(strings.PermissionWebFetch);
                return InputString(request, "url") ?? "";
            case "WebSearch":
                SetTitle(strings.PermissionWebSearch);
                ExtraText.Text = strings.FormatSearchDomains(InputStrings(request, "allowed_domains"), InputStrings(request, "blocked_domains"));
                return InputString(request, "query") ?? "";
            case "Skill":
                var skill = (InputString(request, "skill") ?? "").TrimStart('/');
                SetTitle(strings.PermissionSkill, "/" + skill, "ClaudeChatPermissionPath");
                AddNote(strings.PermissionSkillNote, muted: false);
                AddNote(SkillDescription(skill), muted: true);
                var arguments = InputString(request, "args");
                if (!string.IsNullOrEmpty(arguments)) AddNote(strings.FormatSkillArguments(arguments!), muted: true);
                return "";
        }
        // everything else (MCP, MultiEdit, NotebookEdit, Agent…): the raw name in bold, and its input folded under "Details"
        SetTitle(strings.PermissionProceed, request.ToolName, "ClaudeChatPermissionTool");
        if (request.Input.ValueKind == JsonValueKind.Object && request.Input.EnumerateObject().MoveNext())
            DetailsJson.Text = JsonSerializer.Serialize(request.Input, IndentedJson).Replace("\r\n", "\n");
        return "";
    }

    /// <summary>The diff of an edit, all of it (not the list's 20 lines a side: the user says yes to the whole change, Ruling 30).</summary>
    private void AddDiff(PendingPermission request, ChatStrings strings)
    {
        var diff = ToolPresentation.Diff(request.ToolName, request.Input, maxLinesPerSide: null);
        if (diff != null && diff.Count > 0) DiffPanel.Children.Add(ToolCallView.DiffBox(diff, strings, whole: true)); // the request scrolls
    }

    /// <summary>A line under the desktop card's title: the description's color (<paramref name="muted"/>) or the text's.</summary>
    private void AddNote(string? text, bool muted)
    {
        if (string.IsNullOrEmpty(text)) return;
        var line = new TextBlock { Text = text };
        line.Classes.Add(muted ? "ClaudeChatPermissionDescription" : "ClaudeChatPermissionNote");
        NotesPanel.Children.Add(line);
    }

    /// <summary>A skill's description, from the commands the claude lists; null when it does not list it.</summary>
    private string? SkillDescription(string skill)
    {
        if (_tracked == null || skill.Length == 0) return null;
        foreach (var command in _tracked.Commands)
        {
            if (string.Equals(command.Name, skill, StringComparison.Ordinal)) return command.Description;
        }
        return null;
    }

    /// <summary>
    /// The title: plain, or with <c>{0}</c> taken by <paramref name="value"/> in a run of its own, of class
    /// <paramref name="valueClass"/> (a file's name in the editor's font, a tool's raw name in bold).
    /// </summary>
    private void SetTitle(string format, string? value = null, string? valueClass = null)
    {
        var inlines = TitleText.Inlines!;
        inlines.Clear();
        var at = value == null ? -1 : format.IndexOf("{0}", StringComparison.Ordinal);
        if (at < 0)
        {
            TitleText.Text = format;
            return;
        }
        TitleText.Text = null;
        if (at > 0) inlines.Add(new Run(format.Substring(0, at)));
        var run = new Run(value);
        if (valueClass != null) run.Classes.Add(valueClass);
        inlines.Add(run);
        var rest = format.Substring(at + 3);
        if (rest.Length > 0) inlines.Add(new Run(rest));
    }

    /// <summary>
    /// The desktop's numbers are those of the visible options, in order ("No" is 2 without option 2), and none shows with
    /// <c>default_to_no</c>; the touch layout hides them.
    /// </summary>
    private void UpdateNumbers()
    {
        var number = 1;
        YesNumber.Text = (number++).ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (AlwaysButton.IsVisible || IsTouch) AlwaysNumber.Text = (number++).ToString(System.Globalization.CultureInfo.InvariantCulture);
        NoNumber.Text = IsTouch ? "3" : number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Options.Classes.Set("noNumbers", !IsTouch && _request is { DefaultToNo: true });
    }

    /// <summary>"Details" shows when there is an input to fold, and the input when it is unfolded.</summary>
    private void UpdateDetails()
    {
        var any = !string.IsNullOrEmpty(DetailsJson.Text);
        DetailsButton.IsVisible = any;
        DetailsBox.IsVisible = any && DetailsButton.IsChecked == true;
    }

    private void UpdateMorePending()
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
        if (!_denyOpen)
        {
            DenyBox.Clear();
            _denyOpen = true;
            UpdateDenyField();
        }
        DenyBox.Focus(NavigationMethod.Tab);
    }

    /// <summary>A new request: the field starts empty (and, in the touch layout, closed).</summary>
    private void ResetDenyField()
    {
        _denyOpen = false;
        DenyBox.Clear();
        UpdateDenyField();
    }

    /// <summary>
    /// The host's back button (<see cref="ClaudeChatView.CloseTopLayer"/>) closes the touch layout's field of option 3 when it is
    /// open and on screen (a request that is hidden, in a read-only view, keeps its field as it was), and the request stays. The
    /// keyboard goes to "No", the answer that opened the field, if the field had it, as Esc in the field does it. False when there
    /// was no field to close, and always on the desktop, where the field is part of the card and no layer.
    /// </summary>
    internal bool CloseDenyFieldIfOpen()
    {
        if (!IsTouch || !_denyOpen || !DenyBox.IsEffectivelyVisible) return false;
        var hadKeyboard = InDenyField;
        CloseDenyField();
        if (hadKeyboard) NoButton.Focus(NavigationMethod.Tab);
        return true;
    }

    private void CloseDenyField()
    {
        _denyOpen = false;
        DenyBox.Clear();
        UpdateDenyField();
    }

    /// <summary>
    /// The desktop's field is always there, under the options. The touch layout's opens with option 3, and is typed in with the
    /// on-screen keyboard up, which leaves the view some 400 DIPs. So in that layout, while the field is open, the button under it
    /// is there and the detail (the command or the diff) shrinks to a peek, for the title, the answers, the field and its button
    /// to stay above the keyboard; closing the field gives the detail its room back. The detail's height is the resource
    /// <see cref="DetailPeekKey"/> then, the one of <see cref="DetailHeightKey"/> otherwise.
    /// </summary>
    private void UpdateDenyField()
    {
        var touchField = IsTouch && _denyOpen;
        DenyBox.IsVisible = IsTouch ? _denyOpen : true;
        DenySendButton.IsVisible = touchField;
        if (touchField == _peeking) return;
        _peeking = touchField;
        DetailScroller.Bind(MaxHeightProperty, DetailScroller.GetResourceObservable(touchField ? DetailPeekKey : DetailHeightKey));
    }

    private void OnDenyKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None) return;
        if (IsTouch)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                Refuse();
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CloseDenyField();
                NoButton.Focus(NavigationMethod.Tab);
            }
            return;
        }
        // the desktop: Enter and Esc both send "No" with the text; ↑/↓ go on round the options
        if (e.Key == Key.Up || e.Key == Key.Down)
        {
            e.Handled = true;
            Move(e.Key == Key.Down ? 1 : -1);
        }
        else if (e.Key == Key.Enter || (e.Key == Key.Escape && !PanelOpen))
        {
            e.Handled = true;
            if (!Settled) return;
            _heldKey = e.Key;
            Refuse();
        }
    }

    /// <summary>
    /// Answers no with what is written in the field ("No" or Esc on the desktop, Enter in the field, the touch layout's button):
    /// nothing written is a plain no, and the host sends the claude its default text; what is written goes trimmed, and cut to
    /// <see cref="MaxDenyLength"/> characters.
    /// </summary>
    private void Refuse()
    {
        var text = DenyBox.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            Answer(PermissionDecision.Deny);
            return;
        }
        var message = text!.Trim();
        if (message.Length > MaxDenyLength) message = message.Substring(0, MaxDenyLength);
        Answer(PermissionDecision.Deny, message);
    }

    /// <summary>
    /// The touch layout's title for an edit: "Edit OrderService.cs?", the file named by the last part of its path (the
    /// notebook's, for a NotebookEdit). A request that gives no path asks about the tool.
    /// </summary>
    private static string EditFileQuestion(PendingPermission request, ChatStrings strings)
    {
        var name = FileName(request, request.ToolName == "NotebookEdit" ? "notebook_path" : "file_path");
        return name.Length > 0 ? strings.FormatEditFileQuestion(name) : strings.FormatUseToolQuestion(request.DisplayName);
    }

    /// <summary>The last part of a path in the tool's input; empty when it gives none.</summary>
    private static string FileName(PendingPermission request, string property)
    {
        var path = InputString(request, property);
        return string.IsNullOrEmpty(path) ? "" : ChatFile.LastSegment(path!);
    }

    /// <summary>A text of the tool's input; null when the input has none by that name.</summary>
    private static string? InputString(PendingPermission request, string name) =>
        request.Input.ValueKind == JsonValueKind.Object
        && request.Input.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>The texts of a list in the tool's input; empty when it has none by that name.</summary>
    private static IReadOnlyList<string> InputStrings(PendingPermission request, string name)
    {
        var list = new List<string>();
        if (request.Input.ValueKind == JsonValueKind.Object
            && request.Input.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String) list.Add(item.GetString()!);
            }
        }
        return list;
    }

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

    /// <summary>The touch layout's kinds of request (0.5.0).</summary>
    private static ToolKind KindOf(string toolName) => toolName switch
    {
        "Bash" or "PowerShell" => ToolKind.Command,
        "Edit" or "Write" or "MultiEdit" or "NotebookEdit" => ToolKind.File,
        _ => ToolKind.Other,
    };
}
