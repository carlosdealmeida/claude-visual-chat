using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using ClaudeVisualChat.Imaging;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The reply box, like the extension's: the images attached, the text, and a bar with +, / (the command palette, also
/// opened by a "/" typed in the empty box), the prompt cache clock, the Remote Control chip, the "N agents" chip (it
/// opens the Agent map), the model chip (its menu
/// has the models and the effort), the mode and the
/// square buttons: send (enabled when something is written or attached) and, while Claude works, stop; with nothing
/// to send, only stop shows, as in the extension; with something, both, and what is sent waits in line.
/// Images come from the + button's file dialog, from Ctrl+V when the clipboard holds an image and no text, and from
/// image files dropped on the box; PDF, text and CSV files from the same dialog and drop, as chips after the images.
/// One that does not go in says why under the strip until the next one goes in or the message is sent.
/// Enter sends and clears; Shift+Enter breaks the line; Esc stops Claude while it works, and otherwise goes on to the
/// app. The mode picker and the model chip show what the claude reports; picking only asks for a change through the
/// conversation's events. The box takes the mode's color: classes
/// <c>plan</c>, <c>acceptEdits</c> and <c>auto</c> on its frame.
/// In the touch layout (pseudo-class <c>:touch</c>) the bar is another, of 44 px targets: + and the model at the left;
/// the context ring (from the first token), the microphone (while the host's dictation is available), send and, while
/// Claude works, stop at the right. The + and the model ask
/// the view for their sheets (<see cref="AddSheetRequested"/>, <see cref="ModelSheetRequested"/>); the "/", the cache
/// clock, the Remote Control chip, the agents chip and the mode are not in it (the "+" sheet has the commands, the mode
/// and Remote Control, and asks the box for the file dialog and the palette: <see cref="AttachFromPickerAsync"/>,
/// <see cref="OpenCommands"/>). Enter breaks the line there, and Ctrl+Enter sends; so it does in the desktop layout while
/// <see cref="UseCtrlEnterToSend"/> is on (Cmd+Enter too).
/// A pasted text has its invisible characters taken out (<see cref="InvisibleCharacters"/>), and so has the text sent: when some are
/// found then, the box is filled with the clean text and nothing is sent. Either way a notice under the box says how many went, until
/// its × or a change of the text. The long pastes (<see cref="LongPastes"/>) still in a message go with it as inline pastes.
/// The up and down arrows recall the prompts of the conversation (the ones waiting in line too) when the caret is at the start or the end of
/// the text; Ctrl+J breaks the line; Shift+Tab asks for the next of the modes offered. All of these in both layouts.
/// The microphone (<see cref="IChatDictation"/>): a tap starts the listening, which locks the box and puts what is heard at the
/// caret as it comes, a space on each side of the words; a second tap stops it and keeps what was heard. It sends nothing, and
/// nothing is sent while it listens. It ends with the caret after the words and, only if the box had the keyboard when the finger
/// came down, the keyboard back in the box (the soft keyboard does not come up by itself). Text put in the box while it listens (a
/// message the host gives back, a command from the palette) stays, and the words go after it. The conversation or the layout
/// changing, the view turning read-only, the box or the window going away, or the dictation going away or being no longer
/// available, cut it short: the words already in the box stay (in its conversation's draft) and nothing heard after is added.
/// </summary>
internal partial class ChatComposer : UserControl
{
    private static readonly StyledProperty<ChatStrings> StringsProperty =
        ClaudeChatView.StringsProperty.AddOwner<ChatComposer>();

    private static readonly StyledProperty<ChatLayout> LayoutProperty =
        ClaudeChatView.LayoutProperty.AddOwner<ChatComposer>();

    /// <summary>A mode: its icon on the extension's 26 px box, and its name.</summary>
    private static readonly FuncDataTemplate<Choice> ModeTemplate = new FuncDataTemplate<Choice>((choice, _) =>
    {
        var icon = new Path();
        icon.Classes.Add("ClaudeChatModeIcon");
        icon.Bind(Path.DataProperty, icon.GetResourceObservable(IconKey(choice?.Value)));
        var label = new TextBlock { Text = choice?.Label };
        label.Classes.Add("ClaudeChatModeLabel");
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, label } };
    });

    /// <summary>What was written in each conversation and not sent; it goes with the conversation.</summary>
    private readonly ConditionalWeakTable<ChatConversation, string> _drafts = new ConditionalWeakTable<ChatConversation, string>();

    private ChatConversation? _conversation;
    private ChatConversation? _tracked;
    private IReadOnlyList<string> _modes = Array.Empty<string>();
    private bool _attached;

    /// <summary>What the touch bar holds; the bar has it only while the layout is touch.</summary>
    private readonly Control? _touchBarContent;

    /// <summary>The host's speech to text, if it offers one.</summary>
    private IChatDictation? _dictation;

    /// <summary>The dictation whose availability the box follows; it follows it only while in the window.</summary>
    private IChatDictation? _trackedDictation;

    /// <summary>The listening in progress, null when none. One that was cut short, or has ended, is no longer this one.</summary>
    private Listening? _listening;

    /// <summary>True while the dictation itself puts the words in the box: that text is not someone else's.</summary>
    private bool _inserting;

    /// <summary>True while the view is read-only: the box is hidden, and so is the microphone.</summary>
    private bool _viewIsReadOnly;

    /// <summary>
    /// Whether the box had the keyboard when a finger came down on the microphone, null when no finger is down. A finger takes
    /// the keyboard from the box before the click comes, so the box's own focus says nothing by then; a click with no finger
    /// (the keyboard's Space, a screen reader) reads the box's focus as it is.
    /// </summary>
    private bool? _boxHadFocusAtPress;

    /// <summary>True while the mode picker is set from the conversation, so the change is not the user's.</summary>
    private bool _syncing;

    /// <summary>True while a conversation's draft is put in the box, so the change is not the user's typing.</summary>
    private bool _showingDraft;

    /// <summary>Why the last image or file did not go in; null when it did, or after a send.</summary>
    private Rejection? _rejected;

    /// <summary>The prompts the arrows walk through, oldest first, taken when the walk began; null when none is under way.</summary>
    private List<string>? _recall;

    /// <summary>Where the walk is in <see cref="_recall"/>; the box shows that prompt.</summary>
    private int _recallAt;

    /// <summary>What was in the box before the first Up: it comes back when the walk goes past the newest prompt.</summary>
    private string _recallDraft = "";

    /// <summary>True while a recalled prompt (or the draft, coming back) is put in the box, so the change is not the user's typing.</summary>
    private bool _recalling;

    /// <summary>The long pastes made in each conversation, to tell which of them a message still has; they go with the conversation.</summary>
    private readonly ConditionalWeakTable<ChatConversation, LongPastes> _pastes = new ConditionalWeakTable<ChatConversation, LongPastes>();

    /// <summary>The notice of the invisible characters taken out: how many and what follows; null when there is none.</summary>
    private (int Removed, InvisibleNoticeKind Kind)? _notice;

    /// <summary>The text of the box when the notice came: a different text ends the notice.</summary>
    private string _noticeText = "";

    /// <summary>Test seam: the file dialog of the + button, giving image bytes (it takes the place of the dialog).</summary>
    internal Func<Task<IReadOnlyList<byte[]>>>? PickImagesOverride;

    /// <summary>Test seam: the file dialog of the + button, giving paths of images and files.</summary>
    internal Func<Task<IReadOnlyList<string>>>? PickFilesOverride;

    public ChatComposer()
    {
        InitializeComponent();
        _touchBarContent = TouchBar.Child; // the bar takes it back in the touch layout, and only then
        TouchBar.Child = null;
        ModePicker.ItemTemplate = ModeTemplate;
        AutomationProperties.SetLiveSetting(NoticeText, AutomationLiveSetting.Polite); // the notice is announced
        NoticeClose.Click += (_, _) => ClearNotice();
        Input.AddHandler(KeyDownEvent, OnInputKeyDown, RoutingStrategies.Tunnel);
        Input.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty) return;
            if (!_recalling && string.IsNullOrEmpty(Input.Text)) EndRecall(); // clearing the box ends the walk
            if (_notice != null && (Input.Text ?? "") != _noticeText) ClearNotice(); // the notice is about the text it came with
            // the user's edit, not another conversation's draft coming in: a request that comes now waits for the typing to pause
            if (!_showingDraft && !string.IsNullOrEmpty(Input.Text)) LastEdited = _conversation?.Clock() ?? DateTimeOffset.Now;
            RebaseListening();
            UpdateSend();
            UpdateBusy(); // with text, the square sends; empty while Claude works, it stops
        };
        SendButton.Click += OnSendClick;
        TouchSend.Click += OnSendClick;
        StopButton.Click += OnStopClick;
        TouchStop.Click += OnStopClick;
        TouchAddButton.Click += (_, _) => AddSheetRequested?.Invoke(this, EventArgs.Empty);
        TouchModelChip.OpenSheet = () => ModelSheetRequested?.Invoke(this, EventArgs.Empty);
        // the finger takes the keyboard from the box as it comes down, before the tap lands: the box is asked first. A finger that
        // does not end in a tap (it slides off, a gesture takes it) lets go of the button, which loses the pointer, and leaves nothing
        MicButton.AddHandler(PointerPressedEvent, (_, _) => _boxHadFocusAtPress = Input.IsFocused, RoutingStrategies.Tunnel, true);
        MicButton.AddHandler(PointerCaptureLostEvent, (_, _) => _boxHadFocusAtPress = null, RoutingStrategies.Direct, true);
        MicButton.Click += OnMicClick;
        ModePicker.SelectionChanged += OnModePicked;
        Palette.PlacementTarget = Box;
        Palette.Opener = CommandsButton;
        Palette.Input = Input;
        CommandsButton.Click += (_, _) => OpenCommands();
        ModelMenu.PlacementTarget = Box;
        ModelMenu.Opener = ModelChip;
        ModelChip.Menu = ModelMenu;
        AddButton.Click += async (_, _) => await AttachFromPickerAsync();
        Input.PastingFromClipboard += OnPasting;
        Box.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        Box.AddHandler(DragDrop.DropEvent, OnDrop);
        Strip.RemoveRequested += (_, image) => _tracked?.RemoveAttachment(image);
        Strip.RemoveFileRequested += (_, file) => _tracked?.RemoveFile(file);
        Strip.OpenRequested += (_, e) => PreviewRequested?.Invoke(this, e);
        IsEnabled = false;
        Refresh();
    }

    /// <summary>
    /// The conversation the reply box writes to. What was written and not sent stays with its conversation: another
    /// one shows its own draft, or an empty box, so Enter never sends a draft to the wrong conversation.
    /// </summary>
    internal ChatConversation? Conversation
    {
        get => _conversation;
        set
        {
            if (!ReferenceEquals(value, _conversation))
            {
                CutDictation(); // the words heard so far are in the box: they go to the draft of the conversation that leaves
                KeepDraft(_conversation);
                _conversation = value;
                ClearNotice(); // it was about the other conversation's text
                ShowDraft(value);
            }
            CacheClock.Conversation = value;
            RemoteControlChip.Conversation = value;
            AgentsChip.Conversation = value;
            ContextRing.Conversation = value;
            TouchRing.Conversation = value;
            ModelChip.Conversation = value;
            TouchModelChip.Conversation = value;
            ModelMenu.Conversation = value;
            Palette.Conversation = value;
            IsEnabled = value != null;
            if (_attached) Track(value);
            UpdateMic();
        }
    }

    private void KeepDraft(ChatConversation? conversation)
    {
        if (conversation == null) return;
        _drafts.Remove(conversation);
        var text = Input.Text;
        if (!string.IsNullOrEmpty(text)) _drafts.Add(conversation, text!);
    }

    private void ShowDraft(ChatConversation? conversation)
    {
        var text = conversation != null && _drafts.TryGetValue(conversation, out var draft) ? draft : "";
        LastEdited = null; // the other conversation's typing does not hold this one's requests
        EndRecall(); // the prompts walked through were the other conversation's
        _showingDraft = true;
        try
        {
            Input.Text = text;
        }
        finally
        {
            _showingDraft = false;
        }
        Input.CaretIndex = text.Length;
        // the undo history belongs to the text that left: Ctrl+Z must not bring another conversation's draft back
        Input.IsUndoEnabled = false;
        Input.IsUndoEnabled = true;
    }

    /// <summary>The permission modes offered; one without a label shows its name.</summary>
    internal IReadOnlyList<string>? Modes
    {
        get => _modes;
        set
        {
            _modes = value ?? Array.Empty<string>();
            RebuildModes();
        }
    }

    /// <summary>
    /// True to send with Ctrl+Enter (Cmd+Enter) and break the line with Enter; false (the default) sends with Enter and breaks the line
    /// with Shift+Enter. The touch layout always sends with Ctrl+Enter.
    /// </summary>
    internal bool UseCtrlEnterToSend
    {
        get => _useCtrlEnterToSend;
        set
        {
            _useCtrlEnterToSend = value;
            UpdateNotice(); // it says which key sends
        }
    }

    private bool _useCtrlEnterToSend;

    /// <summary>Ctrl or Cmd with Enter is the send key: where Enter breaks the line (the touch layout, or the option).</summary>
    private bool CtrlEnterSends => IsTouch || _useCtrlEnterToSend;

    /// <summary>The user clicked an attached image: the view shows it.</summary>
    internal event EventHandler<(ChatImage Image, string Name)>? PreviewRequested;

    /// <summary>The user clicked the + of the touch bar: the view shows the sheet that adds to the message.</summary>
    internal event EventHandler? AddSheetRequested;

    /// <summary>The user clicked the model of the touch bar: the view shows the sheet that picks the model and the effort.</summary>
    internal event EventHandler? ModelSheetRequested;

    /// <summary>Nothing written yet (spaces do not count).</summary>
    internal bool IsEmpty => string.IsNullOrWhiteSpace(Input.Text);

    /// <summary>
    /// When the user last left text in the box (the conversation's <see cref="ChatConversation.Clock"/>); null before any, and
    /// after another conversation's draft came in. The view reads it: a permission request that comes while the user types
    /// waits for the typing to pause.
    /// </summary>
    internal DateTimeOffset? LastEdited { get; private set; }

    /// <summary>Nothing written and no image or file attached: Enter would send nothing.</summary>
    internal bool HasNothingToSend =>
        IsEmpty && (_conversation == null || (_conversation.Attachments.Count == 0 && _conversation.Files.Count == 0));

    internal bool FocusInput() => Input.Focus();

    /// <summary>
    /// Opens the command palette with its filter field, or closes it: what the / button does, and what the "Commands" row of the
    /// touch layout's "+" sheet asks for.
    /// </summary>
    internal void OpenCommands() => Palette.Toggle();

    /// <summary>The command palette or the model menu is open: they take Esc first.</summary>
    internal bool HasOpenPopup => Palette.IsOpen || ModelMenu.IsOpen;

    private ChatStrings Strings => GetValue(StringsProperty) ?? ChatStrings.English;

    private bool IsTouch => GetValue(LayoutProperty) == ChatLayout.Touch;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Track(_conversation);
        TrackDictation(_dictation);
        UpdateMic(); // it may have become available, or not, while the box was out of the window
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        Track(null); // the conversation outlives the view: let go of its events (and draw nothing)
        TrackDictation(null); // so does the host's dictation
        CutDictation(); // nothing listens for a box that is not on screen
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StringsProperty) Refresh();
        else if (change.Property == LayoutProperty) UpdateTouch();
        else if (change.Property == IsVisibleProperty && !IsVisible)
        {
            ClosePopups();
            CutDictation();
        }
    }

    /// <summary>
    /// In the touch layout (pseudo-class <c>:touch</c>) the box shows the touch bar and not the desktop's, and the empty
    /// text box says what the app's does. The touch bar's parts are in the tree only then, so that the desktop layout has
    /// one of each (the ring, the model chip, send, stop) and its tree is the one it always was.
    /// </summary>
    private void UpdateTouch()
    {
        var touch = IsTouch;
        ClosePopups(); // an open menu belongs to the layout that leaves: the desktop's model menu has no chip in the touch bar
        PseudoClasses.Set(":touch", touch);
        UpdateNotice(); // the key that sends differs
        DesktopBar.IsVisible = !touch;
        TouchBar.Child = touch ? _touchBarContent : null;
        TouchBar.IsVisible = touch;
        UpdateBusy();
        UpdateMic(); // the desktop layout has no microphone, and a dictation under way has nothing to stop it with there
    }

    /// <summary>
    /// Closes the command palette and the model menu: the reply box goes away (read-only, hidden) or a permission
    /// request comes. Avalonia 11.0 does not tell a control that an ancestor hid it, so the view calls this too.
    /// </summary>
    internal void ClosePopups()
    {
        Palette.IsOpen = false;
        ModelMenu.IsOpen = false;
    }

    /// <summary>
    /// Follows the conversation's events and draws it. Out of the window (the view is being taken out), only the
    /// events go: Avalonia is taking the tree apart, and a control drawn then (the mode list) throws; coming back
    /// draws it all again. The strip lets go of the attachments by itself.
    /// </summary>
    private void Track(ChatConversation? conversation)
    {
        var changed = !ReferenceEquals(conversation, _tracked);
        if (changed)
        {
            if (_tracked != null)
            {
                _tracked.PropertyChanged -= OnConversationChanged;
                _tracked.SubmitUndone -= OnSubmitUndone;
                _tracked.Attachments.CollectionChanged -= OnAttachmentsChanged;
                _tracked.Files.CollectionChanged -= OnAttachmentsChanged;
            }
            _tracked = conversation;
            if (_tracked != null)
            {
                _tracked.PropertyChanged += OnConversationChanged;
                _tracked.SubmitUndone += OnSubmitUndone;
                _tracked.Attachments.CollectionChanged += OnAttachmentsChanged;
                _tracked.Files.CollectionChanged += OnAttachmentsChanged;
            }
        }
        if (!_attached) return;
        if (changed)
        {
            Strip.Images = _tracked?.Attachments;
            Strip.Files = _tracked?.Files;
            ShowRejected(null);
        }
        Refresh();
    }

    private void OnConversationChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ChatConversation.IsBusy):
                UpdateBusy();
                break;
            case nameof(ChatConversation.PermissionMode):
                SelectMode();
                break;
            case nameof(ChatConversation.CurrentPermission):
                if (_tracked?.CurrentPermission != null) ClosePopups(); // the request comes first, the menus would cover it
                UpdateNotice(); // and a banner is hidden under a request
                break;
        }
    }

    /// <summary>
    /// The message never left: it comes back to the box, unless something else was written since. A dictation under way goes on
    /// over it (<see cref="RebaseListening"/>), so that the words heard go after it and it stays when the dictation ends.
    /// </summary>
    private void OnSubmitUndone(object? sender, MessageSubmittedEventArgs e)
    {
        if (!IsEmpty) return;
        Input.Text = e.Text;
        Input.CaretIndex = e.Text.Length;
    }

    private void OnAttachmentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateSend();
        UpdateBusy();
    }

    private void Refresh()
    {
        var strings = Strings;
        Label(SendButton, strings.Send);
        Label(TouchSend, strings.Send);
        Label(StopButton, strings.Stop);
        Label(TouchStop, strings.Stop);
        Label(AddButton, strings.AddAttachment);
        Label(TouchAddButton, strings.AddAttachment);
        Label(CommandsButton, strings.ShowCommands);
        Label(NoticeClose, strings.DismissNotice);
        AutomationProperties.SetName(Input, strings.MessageInput);
        UpdateNotice();
        UpdateMicName();
        ShowRejected(_rejected);
        UpdateBusy();
        UpdateSend();
        RebuildModes();
    }

    private static void Label(Button button, string name)
    {
        AutomationProperties.SetName(button, name);
        ToolTip.SetTip(button, name);
    }

    /// <summary>
    /// While Claude works, stop shows and what is sent waits in line; send shows next to it once something is written
    /// or attached (alone, stop is the extension's square). Idle, only send. The same for both bars' buttons.
    /// </summary>
    private void UpdateBusy()
    {
        var busy = _tracked != null && _tracked.IsBusy;
        var strings = Strings;
        Input.Watermark = busy ? strings.InputPlaceholderBusy : IsTouch ? strings.InputPlaceholderTouch : strings.InputPlaceholder;
        var send = !busy || HasSomethingToSend;
        StopButton.IsVisible = TouchStop.IsVisible = busy;
        SendButton.IsVisible = TouchSend.IsVisible = send;
    }

    /// <summary>Send is enabled when there is something to send, and the box is not taking dictation: what is in it is not all of the message yet.</summary>
    private void UpdateSend() => SendButton.IsEnabled = TouchSend.IsEnabled = HasSomethingToSend && _listening == null;

    /// <summary>Text, or an image or a file attached.</summary>
    private bool HasSomethingToSend =>
        _tracked != null && (!IsEmpty || _tracked.Attachments.Count > 0 || _tracked.Files.Count > 0);

    // ---- keys and buttons ----

    private void OnSendClick(object? sender, RoutedEventArgs e)
    {
        Send();
        Input.Focus();
    }

    private void OnStopClick(object? sender, RoutedEventArgs e) => _tracked?.Stop();

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        // a hidden box (read-only, or an ancestor hidden) may keep the keyboard: its keys send and stop nothing
        if (!IsEffectivelyVisible || _viewIsReadOnly) return;
        // the shortcuts come first: a key with a modifier used to return at once, and these all have one
        if (HandleShortcut(e)) return;
        if (e.Key == Key.Enter && IsSendChord(e.KeyModifiers))
        {
            e.Handled = true; // Ctrl/Cmd+Enter is the send key where Enter breaks the line
            Send();
            return;
        }
        if (e.KeyModifiers != KeyModifiers.None) return; // Shift+Enter: the text box breaks the line
        if (Palette.HandleKey(e.Key))
        {
            e.Handled = true; // the palette's arrows, Enter, Tab and Esc
            return;
        }
        if (e.Key == Key.Enter)
        {
            if (CtrlEnterSends) return; // the text box breaks the line; the send button, or Ctrl+Enter, sends
            e.Handled = true; // Enter is the send key, never a line break
            Send();
        }
        else if (e.Key == Key.Up || e.Key == Key.Down)
        {
            if (Recall(e.Key == Key.Up)) e.Handled = true;
            else GoToEdgeIfStuck(e.Key == Key.Up);
        }
        else if (e.Key == Key.Escape && _tracked != null && _tracked.IsBusy)
        {
            e.Handled = true; // only when it stops something; otherwise the Esc is the app's
            _tracked.Stop();
        }
    }

    /// <summary>Ctrl or Cmd, and nothing else, with Enter: the send key of the touch layout and of <see cref="UseCtrlEnterToSend"/>.</summary>
    private bool IsSendChord(KeyModifiers modifiers) =>
        CtrlEnterSends && (modifiers == KeyModifiers.Control || modifiers == KeyModifiers.Meta);

    /// <summary>
    /// Ctrl+J (alone) breaks the line as Shift+Enter does; Shift+Tab asks for the next permission mode. True when the key was one of them.
    /// With the command palette or the model menu open neither acts (the key is left as it was before them).
    /// (Ctrl+Alt+F, the Focus view's, is the view's: it comes down to it before the box.)
    /// </summary>
    private bool HandleShortcut(KeyEventArgs e)
    {
        if (HasOpenPopup) return false; // like the arrows: with the palette or the model menu open the keys are theirs, and these do nothing
        if (e.Key == Key.J && e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true;
            BreakLine();
            return true;
        }
        if (e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.Shift)
        {
            e.Handled = true; // never the focus moving backwards out of the box
            CycleMode();
            return true;
        }
        return false;
    }

    /// <summary>The text box's own line break, the one Shift+Enter makes, so that the caret, the selection and the undo history agree.</summary>
    private void BreakLine()
    {
        if (Input.IsReadOnly) return; // listening: the box is locked
        Input.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = KeyDownEvent,
            Source = Input,
            Key = Key.Enter,
            KeyModifiers = KeyModifiers.Shift,
        });
    }

    // ---- recalling prompts ----

    /// <summary>
    /// The up and down arrows walk through the prompts of the conversation (those waiting in line too), newest first. Up acts only with
    /// the caret at the very start of the text (an empty box counts) and the first one keeps the draft; it leaves the caret at the start,
    /// so that the next Up goes on. Down acts only with the caret at the end, and leaves it there; going past the newest prompt brings
    /// the draft back. Anywhere else the arrows move the caret (Up at the oldest prompt too: the key goes on). The list is the one taken at
    /// the first Up: what is sent or queued during the walk is not in it. True when the key was used.
    /// </summary>
    private bool Recall(bool older)
    {
        var conversation = _tracked;
        if (conversation == null || _listening != null || ModelMenu.IsOpen) return false;
        if (Input.SelectionStart != Input.SelectionEnd) return false;
        var text = Input.Text ?? "";
        if (older)
        {
            if (Input.CaretIndex != 0) return false;
            if (_recall == null)
            {
                var prompts = conversation.RecallablePrompts();
                if (prompts.Count == 0) return false;
                _recall = prompts;
                _recallAt = prompts.Count;
                _recallDraft = text;
            }
            if (_recallAt == 0) return false; // the oldest: nothing further back, the key is the box's
            ShowRecalled(_recall[--_recallAt], atStart: true);
            return true;
        }
        if (_recall == null || Input.CaretIndex != text.Length) return false;
        if (_recallAt < _recall.Count - 1)
        {
            ShowRecalled(_recall[++_recallAt], atStart: false);
            return true;
        }
        var draft = _recallDraft;
        EndRecall();
        ShowRecalled(draft, atStart: false);
        return true;
    }

    /// <summary>
    /// The browser's arrows, which the extension relies on: Up on the first line of the text goes to its start and Down on the last line
    /// goes to its end, so that the next one recalls. Avalonia's text box leaves the caret where it is there, so it is done here, once the
    /// text box has had the key: a caret that did not move is on the first (last) line. Only where a recall would use it: Up with a
    /// prompt to recall, Down in the middle of a walk.
    /// </summary>
    private void GoToEdgeIfStuck(bool older)
    {
        if (_tracked == null || _listening != null || ModelMenu.IsOpen || Input.SelectionStart != Input.SelectionEnd) return;
        var text = Input.Text ?? "";
        var caret = Input.CaretIndex;
        if (caret == (older ? 0 : text.Length)) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (Input.Text != text || Input.CaretIndex != caret || Input.SelectionStart != Input.SelectionEnd) return; // it moved: not the edge line
            if (older ? _recall != null || _tracked?.RecallablePrompts().Count > 0 : _recall != null) Input.CaretIndex = older ? 0 : text.Length;
        });
    }

    /// <summary>
    /// Puts a prompt in the box, the caret at its start (after an Up, which then goes on) or at its end (after a Down, and for the draft).
    /// It is text set, not typed: the "/" menu opens on a "/" typed into the empty box, so a recalled command leaves it closed until the
    /// person edits the text. It counts as an edit for <see cref="LastEdited"/>, like typing: a permission request that comes right after
    /// waits for the pause.
    /// </summary>
    private void ShowRecalled(string text, bool atStart)
    {
        _recalling = true;
        try
        {
            Input.Text = text;
        }
        finally
        {
            _recalling = false;
        }
        Input.CaretIndex = atStart ? 0 : text.Length;
    }

    private void EndRecall()
    {
        _recall = null;
        _recallDraft = "";
    }

    /// <summary>Shift+Tab: asks the conversation for the next of the modes offered.</summary>
    private void CycleMode()
    {
        var conversation = _tracked;
        if (conversation == null) return;
        var next = NextMode(conversation.PermissionMode, _modes);
        if (next != null) conversation.SetPermissionMode(next);
    }

    /// <summary>The order Shift+Tab walks the modes in (the extension's, without the ones the host does not offer).</summary>
    private static readonly string[] ModeCycle = { "default", "acceptEdits", "plan", "auto" };

    /// <summary>
    /// The mode after <paramref name="current"/> in the cycle that is among <paramref name="offered"/>; null when there is none to go to
    /// (none offered, or only the current one). A current mode outside the cycle (or none yet) goes to the first of the cycle offered.
    /// </summary>
    internal static string? NextMode(string? current, IReadOnlyList<string> offered)
    {
        var from = Array.IndexOf(ModeCycle, current ?? "default");
        for (var step = 1; step <= ModeCycle.Length; step++)
        {
            var candidate = ModeCycle[(from + step + ModeCycle.Length) % ModeCycle.Length];
            if (candidate == current) return null; // all the way round: nothing else is offered
            foreach (var mode in offered)
            {
                if (mode == candidate) return candidate;
            }
        }
        return null;
    }

    private void Send()
    {
        var conversation = _tracked;
        if (conversation == null || !HasSomethingToSend || !IsEffectivelyVisible || _listening != null) return;
        var (text, removed) = InvisibleCharacters.Clean(Input.Text ?? "");
        if (removed > 0)
        {
            // the person sees what would go, and sends it with the next Enter (or has nothing left to send)
            Input.Text = text;
            Input.CaretIndex = text.Length;
            ShowNotice(removed, HasSomethingToSend ? InvisibleNoticeKind.Review : InvisibleNoticeKind.Empty);
            return;
        }
        var pastes = _pastes.GetOrCreateValue(conversation).In(text, InvisibleCharacters.PasteLimit);
        var message = LongPastes.Truncate(text, InvisibleCharacters.PasteLimit);
        if (message.Length < text.Length) message += LongPastes.TruncationNotice;
        // cleared first: a host that refuses the message at once (UndoSubmit inside MessageSubmitted) gets it back here
        Input.Clear();
        ClearNotice();
        ShowRejected(null);
        conversation.SubmitWithPastes(message, pastes);
    }

    // ---- the notice of the invisible characters ----

    private void ShowNotice(int removed, InvisibleNoticeKind kind)
    {
        _notice = (removed, kind);
        _noticeText = Input.Text ?? "";
        UpdateNotice();
    }

    private void ClearNotice()
    {
        if (_notice == null) return;
        _notice = null;
        _noticeText = "";
        UpdateNotice();
    }

    /// <summary>Shows the notice in the language, with the key that sends; a permission request hides it, as it hides the banners.</summary>
    private void UpdateNotice()
    {
        var show = _notice is { } && _tracked?.CurrentPermission == null;
        Notice.IsVisible = show;
        NoticeText.Text = show && _notice is { } notice ? Strings.FormatInvisibleNotice(notice.Removed, notice.Kind, CtrlEnterSends) : null;
    }

    // ---- images and files ----

    /// <summary>
    /// What the + button does: the file dialog, and then what was picked is attached to the conversation shown when it opened,
    /// even if another is shown when the dialog closes. The "Photo or file" row of the touch layout's "+" sheet asks for it too.
    /// </summary>
    internal async Task AttachFromPickerAsync()
    {
        var conversation = _tracked;
        if (PickImagesOverride != null)
        {
            await AttachAsync(conversation, await PickImagesOverride());
            return;
        }
        await AttachSourcesAsync(conversation, await PickAsync(conversation == null || conversation.MaxFiles > 0));
    }

    /// <summary>The + button's dialog: images and PDF, text and CSV files together (images only when no file is taken).</summary>
    private async Task<IReadOnlyList<AttachSource>> PickAsync(bool files)
    {
        if (PickFilesOverride == null) return await AttachFiles.PickAsync(TopLevel.GetTopLevel(this), Strings, files);
        var sources = new List<AttachSource>();
        foreach (var path in await PickFilesOverride()) sources.Add(AttachSource.FromPath(path));
        return sources;
    }

    /// <summary>
    /// Ctrl+V: text wins, so text copied with a picture (a browser, Word) pastes as text. With no text, files copied in
    /// Explorer or a file manager are attached as a drop would attach them (an image file to the images, a PDF, text or
    /// CSV file to the files, any other refused with its reason), else an image (a bitmap) is attached. Text is pasted by
    /// the box itself (<see cref="PasteTextAsync"/>) on every platform, so that it goes in clean; where the clipboard cannot be asked at
    /// once (not Windows) it is read first to see what it holds. Each gesture attaches to the conversation shown when it began.
    /// </summary>
    private void OnPasting(object? sender, RoutedEventArgs e)
    {
        var conversation = _tracked;
        if (!ClipboardImages.CanCheckNow)
        {
            e.Handled = true; // the text, if there is any, is pasted here, clean
            _ = PasteImageWithoutTextAsync(conversation);
            return;
        }
        if (ClipboardImages.HasText())
        {
            e.Handled = true;
            _ = PasteTextAsync(conversation);
            return;
        }
        if (ClipboardImages.HasFiles())
        {
            e.Handled = true;
            _ = PasteFilesAsync(conversation);
            return;
        }
        if (!ClipboardImages.HasImage()) return;
        e.Handled = true;
        _ = PasteImageAsync(conversation);
    }

    /// <summary>
    /// A text from the clipboard goes in at the caret (over the selection) as the text box would put it, but clean: the invisible
    /// characters of its first 50,000 are out, and when some were, the notice says so. A long paste is remembered for the message
    /// it ends up in. Dropped when the conversation changed while the clipboard was read, or the box is locked (listening).
    /// </summary>
    private async Task PasteTextAsync(ChatConversation? conversation)
    {
        string? pasted;
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            pasted = clipboard == null ? null : await clipboard.GetTextAsync();
        }
        catch (Exception)
        {
            return;
        }
        if (string.IsNullOrEmpty(pasted) || !ReferenceEquals(conversation, _tracked) || Input.IsReadOnly || !IsEffectivelyVisible) return;
        var head = LongPastes.Truncate(pasted!, InvisibleCharacters.PasteLimit);
        var (clean, removed) = InvisibleCharacters.Clean(head);
        Input.SelectedText = clean + pasted!.Substring(head.Length);
        if (conversation != null) _pastes.GetOrCreateValue(conversation).Remember(clean);
        if (removed > 0) ShowNotice(removed, InvisibleNoticeKind.Pasted);
    }

    private async Task PasteFilesAsync(ChatConversation? conversation)
    {
        var sources = await AttachFiles.PastedAsync(TopLevel.GetTopLevel(this));
        await AttachSourcesAsync(conversation, sources);
    }

    private async Task PasteImageAsync(ChatConversation? conversation)
    {
        var bytes = await ClipboardImages.ReadImageAsync(TopLevel.GetTopLevel(this));
        await AttachAsync(conversation, new[] { bytes ?? Array.Empty<byte>() }); // unreadable: refused as not an image
    }

    private async Task PasteImageWithoutTextAsync(ChatConversation? conversation)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null) return;
        var files = false;
        var text = false;
        try
        {
            foreach (var format in await clipboard.GetFormatsAsync())
            {
                if (format == DataFormats.Text || format.StartsWith("text/", StringComparison.Ordinal) || format == "UTF8_STRING")
                {
                    text = true;
                    break;
                }
                files |= format == DataFormats.Files;
            }
        }
        catch (Exception)
        {
            text = true; // the formats cannot be read: the text, if the clipboard gives it, is what the person pasted
        }
        if (text)
        {
            await PasteTextAsync(conversation);
            return;
        }
        if (files)
        {
            var sources = await AttachFiles.PastedAsync(TopLevel.GetTopLevel(this));
            if (sources.Count > 0)
            {
                await AttachSourcesAsync(conversation, sources);
                return;
            }
        }
        var bytes = await ClipboardImages.ReadImageAsync(TopLevel.GetTopLevel(this));
        if (bytes != null) await AttachAsync(conversation, new[] { bytes });
    }

    /// <summary>A drag of files: each one is sorted when dropped (an image, a file, or one refused with its reason).</summary>
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = AttachFiles.Dropped(e.Data).Count > 0 ? e.DragEffects & DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var conversation = _tracked;
        var sources = AttachFiles.Dropped(e.Data);
        e.Handled = true;
        await AttachSourcesAsync(conversation, sources);
    }

    /// <summary>
    /// Sorts what was picked or dropped: images go to the images, PDF, text and CSV files to the files, each with
    /// their own limits; any other file is refused. What goes in goes in; the last one refused says why (the images
    /// are attached last), and a batch with none refused clears it. Images beyond the room left are never read.
    /// </summary>
    private async Task AttachSourcesAsync(ChatConversation? conversation, IReadOnlyList<AttachSource> sources)
    {
        if (conversation == null || sources.Count == 0)
        {
            Dispose(sources);
            return;
        }
        var images = new List<byte[]>();
        var maxImages = ImageFiles.ToRead(conversation);
        Rejection? rejected = null;
        try
        {
            foreach (var source in sources)
            {
                if (source.IsImage)
                {
                    if (images.Count >= maxImages) continue; // it would be refused as too many anyway
                    images.Add(await AttachFiles.ReadImageAsync(source) ?? Array.Empty<byte>()); // unreadable: not an image
                }
                else
                {
                    var result = source.IsAttachableFile ? await AttachFiles.AddFileAsync(conversation, source) : AddFileResult.NotSupported;
                    if (result != AddFileResult.Added) rejected = Rejection.Of(result, conversation);
                }
            }
        }
        finally
        {
            Dispose(sources);
        }
        if (await AttachImagesAsync(conversation, images) is { } image) rejected = Rejection.Of(image);
        if (ReferenceEquals(conversation, _tracked)) ShowRejected(rejected);
    }

    private static void Dispose(IReadOnlyList<AttachSource> sources)
    {
        foreach (var source in sources) source.File?.Dispose();
    }

    /// <summary>Attaches the images in order, and says why the last one refused did not go in.</summary>
    private async Task AttachAsync(ChatConversation? conversation, IReadOnlyList<byte[]> images)
    {
        if (conversation == null || images.Count == 0) return;
        var rejected = await AttachImagesAsync(conversation, images);
        if (ReferenceEquals(conversation, _tracked)) ShowRejected(rejected == null ? null : Rejection.Of(rejected.Value));
    }

    /// <summary>Attaches the images in order; why the last one refused did not go in, null when all did.</summary>
    private static async Task<AddImageResult?> AttachImagesAsync(ChatConversation conversation, IReadOnlyList<byte[]> images)
    {
        AddImageResult? rejected = null;
        foreach (var bytes in images)
        {
            var result = ReferenceEquals(bytes, ImageFiles.TooBig) ? AddImageResult.TooBig : await conversation.AddImageAsync(bytes);
            if (result != AddImageResult.Added) rejected = result;
        }
        return rejected;
    }

    private void ShowRejected(Rejection? rejection)
    {
        _rejected = rejection;
        AttachError.Text = rejection?.Text(Strings);
        AttachError.IsVisible = rejection != null;
    }

    /// <summary>Why an image or a file did not go in, with the limits it broke (the text follows the language).</summary>
    private sealed class Rejection
    {
        private readonly AddImageResult? _image;
        private readonly AddFileResult _file;
        private readonly long _maxFileBytes;
        private readonly int _maxFiles;

        private Rejection(AddImageResult? image, AddFileResult file, long maxFileBytes, int maxFiles)
        {
            _image = image;
            _file = file;
            _maxFileBytes = maxFileBytes;
            _maxFiles = maxFiles;
        }

        public static Rejection Of(AddImageResult image) => new Rejection(image, AddFileResult.Added, 0, 0);

        public static Rejection Of(AddFileResult file, ChatConversation conversation) =>
            new Rejection(null, file, conversation.MaxFileBytes, conversation.MaxFiles);

        public string? Text(ChatStrings strings)
        {
            if (_image != null)
            {
                return _image switch
                {
                    AddImageResult.TooMany => strings.TooManyImages,
                    AddImageResult.TooBig => strings.ImageTooBig,
                    AddImageResult.NotAnImage => strings.NotAnImage,
                    _ => null,
                };
            }
            return _file switch
            {
                AddFileResult.TooMany => strings.FormatTooManyFiles(_maxFiles),
                AddFileResult.TooBig => strings.FormatFileTooBig(_maxFileBytes),
                AddFileResult.NotSupported => strings.FileNotSupported,
                _ => null,
            };
        }
    }

    // ---- dictation ----

    /// <summary>
    /// The host's speech to text. The touch layout's bar has a microphone while it is available, the box has a conversation to
    /// write to and the view is not read-only. A listening in progress ends when the microphone goes.
    /// </summary>
    internal IChatDictation? Dictation
    {
        get => _dictation;
        set
        {
            if (ReferenceEquals(value, _dictation)) return;
            _dictation = value;
            if (_attached) TrackDictation(value);
            UpdateMic();
        }
    }

    /// <summary>The view is read-only: the box is hidden, so the microphone goes, and a listening in progress ends.</summary>
    internal bool ViewIsReadOnly
    {
        get => _viewIsReadOnly;
        set
        {
            _viewIsReadOnly = value;
            UpdateMic();
        }
    }

    /// <summary>Follows the dictation's availability while the box is in the window: the host's object outlives the view.</summary>
    private void TrackDictation(IChatDictation? dictation)
    {
        if (ReferenceEquals(dictation, _trackedDictation)) return;
        if (_trackedDictation != null) _trackedDictation.AvailabilityChanged -= OnAvailabilityChanged;
        _trackedDictation = dictation;
        if (_trackedDictation != null) _trackedDictation.AvailabilityChanged += OnAvailabilityChanged;
    }

    /// <summary>The host raises it from any thread.</summary>
    private void OnAvailabilityChanged(object? sender, EventArgs e) => OnUi(UpdateMic);

    /// <summary>
    /// Shows the microphone when the touch layout has it, and takes a listening in progress with it when it goes: the person
    /// would have nothing left to stop it with.
    /// </summary>
    private void UpdateMic()
    {
        var show = IsTouch && _dictation is { IsAvailable: true } && _conversation != null && !_viewIsReadOnly;
        MicButton.IsVisible = show;
        if (!show) CutDictation();
        UpdateMicName();
    }

    private void UpdateMicName() => Label(MicButton, _listening == null ? Strings.Dictate : Strings.StopDictation);

    private void OnMicClick(object? sender, RoutedEventArgs e)
    {
        var boxHadFocus = _boxHadFocusAtPress ?? Input.IsFocused;
        _boxHadFocusAtPress = null;
        if (_listening == null) StartListening(boxHadFocus);
        else Cancel(_listening); // the second tap: the host still gives back what it heard, and that is put in when it does
    }

    /// <summary>
    /// The first tap. The text of the box is kept as it is, and the words go into it at the caret if the box had the keyboard (the
    /// caret says where the person was), else at its end (it says nothing then). The box is locked while it listens.
    /// </summary>
    private void StartListening(bool boxHadFocus)
    {
        var dictation = _dictation;
        if (dictation is not { IsAvailable: true } || _conversation == null) return;
        var text = Input.Text ?? "";
        var run = new Listening(text, boxHadFocus ? Input.CaretIndex : text.Length, boxHadFocus);
        _listening = run;
        ShowListening(true);
        _ = RunListeningAsync(dictation, run);
    }

    /// <summary>
    /// Asks the host to listen, and puts what it gives back in the box when it does (the await brings it back to the UI thread, as
    /// everywhere in this control). Nothing escapes it.
    /// </summary>
    private async Task RunListeningAsync(IChatDictation dictation, Listening run)
    {
        string? heard = null;
        var reports = new Partials(spoken => OnPartial(run, spoken));
        run.Reports = reports;
        try
        {
            heard = await dictation.ListenAsync(reports, run.Stop.Token);
        }
        catch (Exception)
        {
            // a dictation that fails (or throws when it is stopped, which it should not) leaves the words it reported in the box
        }
        EndListening(run, heard);
    }

    /// <summary>What was heard so far replaces what the last report put in.</summary>
    private void OnPartial(Listening run, string? spoken)
    {
        if (!ReferenceEquals(run, _listening)) return; // cut short, or over: the host's late reports go nowhere
        run.Heard = spoken;
        Insert(run, spoken);
    }

    /// <summary>
    /// The host gave back what it heard (or failed): the result goes in as the partials did, and the last partial stays when
    /// there is no result (none, or only blanks). The box is free again, with the caret after the words, and the keyboard back
    /// in it only if it had the keyboard when the person tapped and nobody has taken the keyboard since.
    /// </summary>
    private void EndListening(Listening run, string? heard)
    {
        run.Reports?.Detach();
        run.Stop.Dispose();
        if (!ReferenceEquals(run, _listening)) return; // cut short in the meantime: the box is free, and these words have nowhere to go
        _listening = null;
        Insert(run, string.IsNullOrWhiteSpace(heard) ? run.Heard : heard);
        ShowListening(false);
        if (run.BoxHadFocus && KeyboardIsFree()) Input.Focus();
    }

    /// <summary>
    /// Cuts a listening short: the conversation or the layout changed, the box or the microphone went away. The box is free at once
    /// with the words that are in it (they are the conversation's draft), and what the host gives back later is ignored.
    /// </summary>
    private void CutDictation()
    {
        var run = _listening;
        if (run == null) return;
        _listening = null;
        run.Reports?.Detach(); // a host may keep its progress after the stop: it must not keep the view with it
        ShowListening(false);
        Cancel(run);
    }

    /// <summary>Asks the host to stop. A callback of the host's that throws is the host's: the listening is stopped all the same.</summary>
    private static void Cancel(Listening run)
    {
        try
        {
            run.Stop.Cancel();
        }
        catch (Exception)
        {
        }
    }

    /// <summary>Puts the words in the box in place of the last ones, with the caret after them.</summary>
    private void Insert(Listening run, string? spoken)
    {
        var (text, caret) = DictationText.Insert(run.Text, run.Caret, spoken);
        _inserting = true;
        try
        {
            Input.Text = text;
        }
        finally
        {
            _inserting = false;
        }
        Input.CaretIndex = caret;
    }

    /// <summary>
    /// Text put in the box by anything but the dictation while it listens (the host gives a message back with
    /// <see cref="ChatConversation.UndoSubmit"/>, the palette puts a command in) is the box's text from then on: the words heard
    /// so far, and those still to come, go after it. Without this, the next report would put back the text of the tap.
    /// </summary>
    private void RebaseListening()
    {
        if (_inserting || _listening is not { } run) return;
        var text = Input.Text ?? "";
        run.Rebase(text, text.Length);
    }

    /// <summary>The box is locked while it listens (the words come in, nothing else does) and the microphone is the accent's.</summary>
    private void ShowListening(bool listening)
    {
        Input.IsReadOnly = listening;
        MicButton.Classes.Set("listening", listening);
        UpdateMicName();
        UpdateSend();
    }

    /// <summary>The keyboard is where the tap on the microphone left it, or nowhere: nobody has taken it since.</summary>
    private bool KeyboardIsFree()
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        return focused == null || focused is TopLevel || ReferenceEquals(focused, MicButton) || ReferenceEquals(focused, Input);
    }

    /// <summary>Runs <paramref name="action"/> on the UI thread: at once when already there, else posted. The host reports from any thread.</summary>
    private static void OnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    /// <summary>One listening: the box as it was when the person tapped, and the words heard since.</summary>
    private sealed class Listening
    {
        public Listening(string text, int caret, bool boxHadFocus)
        {
            Text = text;
            Caret = caret;
            BoxHadFocus = boxHadFocus;
        }

        /// <summary>The text of the box before the words; each report puts them in it again, so that it replaces the last.</summary>
        public string Text { get; private set; }

        /// <summary>Where the words go in <see cref="Text"/>.</summary>
        public int Caret { get; private set; }

        /// <summary>What the host reports through; it lets go of the box when the listening is over.</summary>
        public Partials? Reports { get; set; }

        /// <summary>Someone else's text took the box: the words go after it from now on.</summary>
        public void Rebase(string text, int caret)
        {
            Text = text;
            Caret = caret;
        }

        /// <summary>The box had the keyboard when the person tapped.</summary>
        public bool BoxHadFocus { get; }

        /// <summary>The last report of the host's; what stays if it gives back nothing, or fails.</summary>
        public string? Heard { get; set; }

        /// <summary>Cancelled by a second tap, or when the listening is cut short.</summary>
        public CancellationTokenSource Stop { get; } = new CancellationTokenSource();
    }

    /// <summary>
    /// What the host reports through: each report reaches the box on the UI thread, whichever thread it comes from, until the
    /// listening is over. The host may keep it after that, so it then holds nothing of the view's.
    /// </summary>
    private sealed class Partials : IProgress<string>
    {
        private volatile Action<string>? _apply;

        public Partials(Action<string> apply) => _apply = apply;

        /// <summary>The listening is over: later reports go nowhere.</summary>
        public void Detach() => _apply = null;

        public void Report(string value)
        {
            var apply = _apply;
            if (apply != null) OnUi(() => apply(value));
        }
    }

    // ---- the mode ----

    private void RebuildModes()
    {
        var strings = Strings;
        var items = new List<Choice>(_modes.Count);
        foreach (var mode in _modes) items.Add(new Choice(mode, strings.ModeLabel(mode)));
        _syncing = true;
        try
        {
            ModePicker.ItemsSource = items;
        }
        finally
        {
            _syncing = false;
        }
        SelectMode();
    }

    /// <summary>Shows the mode the claude reports; one that is not offered shows as the placeholder.</summary>
    private void SelectMode()
    {
        var mode = _tracked?.PermissionMode;
        Choice? shown = null;
        if (ModePicker.ItemsSource is List<Choice> items)
        {
            foreach (var item in items)
            {
                if (item.Value == mode) shown = item;
            }
        }
        _syncing = true;
        try
        {
            ModePicker.SelectedItem = shown;
            ModePicker.PlaceholderText = shown == null && mode != null ? Strings.ModeLabel(mode) : null;
        }
        finally
        {
            _syncing = false;
        }
        // the frame and the square take the color of the mode the claude is in
        Box.Classes.Set("plan", mode == "plan");
        Box.Classes.Set("acceptEdits", mode == "acceptEdits");
        Box.Classes.Set("auto", mode == "auto" || mode == "bypassPermissions");
    }

    /// <summary>The resource with a mode's icon; a mode without one shows the default mode's.</summary>
    internal static string IconKey(string? mode) => mode switch
    {
        "plan" => "ClaudeChat.Icon.ModePlan",
        "acceptEdits" => "ClaudeChat.Icon.ModeAcceptEdits",
        "auto" or "bypassPermissions" => "ClaudeChat.Icon.ModeAuto",
        _ => "ClaudeChat.Icon.ModeDefault",
    };

    private void OnModePicked(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _tracked == null || ModePicker.SelectedItem is not Choice choice) return;
        _tracked.SetPermissionMode(choice.Value);
        // the chip shows the mode the claude is in, not the one asked for: it moves when the claude confirms, and
        // stays when the claude refuses or confirms the mode it already had (both leave PermissionMode as it is);
        // posted, since the picker is still applying the user's choice
        Dispatcher.UIThread.Post(SelectMode);
    }

    /// <summary>A picker entry: the value sent to the claude and the label shown.</summary>
    private sealed class Choice
    {
        public Choice(string value, string label)
        {
            Value = value;
            Label = label;
        }

        public string Value { get; }

        public string Label { get; }

        public override string ToString() => Label;
    }
}
