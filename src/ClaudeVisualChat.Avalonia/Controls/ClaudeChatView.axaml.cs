using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// Draws a <see cref="ChatConversation"/> in the style of the Claude Code extension for VS Code. The list sticks to
/// its end while the user is there (50 px from it counts), stops following when the user scrolls up and, when the user sends a message,
/// follows it again from wherever it was (unless <see cref="ScrollToEndOnSend"/> is off); its last part fades into the background over
/// the reply box. Each message shows when it was sent (the whole date and time in the tooltip), and a date line marks the first message
/// and each change of day; <see cref="ShowTimestamps"/> hides both. Under the list, the permission
/// request Claude waits on (keys 1, 2 and 3 answer while the focus is in it), the browser's or Remote Control's banner,
/// and the reply box (Enter sends, Shift+Enter breaks the line, Esc stops Claude while it works). Over the end of the
/// list, the <c>/context</c> and <c>/usage</c> panels (Esc closes the one open instead); over the whole view, the Agent map
/// (Esc closes it, or goes back from an agent's card to the list). Over the list alone, the plan of an <c>ExitPlanMode</c> request
/// as a document, which comes by itself with the request and leaves the card under it to be answered; it closes when the plan is
/// approved, stays after a refusal, and Esc or its × closes it (the first Esc: only an Esc with the card on top is the card's "No");
/// the plan's row in the timeline opens it again. A passage selected in it offers "Add Comment", which opens a box over the passage
/// for the comment (Esc closes the box first); with comments, the card's one answer sends them as feedback. The panels and the Agent
/// map opened over the document come over it, and close before it. A click on a diff of the list (or Enter, or Space) opens the Diff
/// View over everything but the image preview: the whole change, side by side, under the file's whole path; Esc, its × or a click on
/// its backdrop closes it, and the keyboard goes back to where it was (a request that came meanwhile gets it then). Under a finished turn, a
/// row copies the answer and bookmarks it and, while the host listens to <see cref="ChatConversation.FeedbackRequested"/>, rates it; the thumb
/// down opens "What went wrong?" over the view, under the Diff View (Esc, Skip or its × close it), and its link shows while the host listens
/// to <see cref="ChatConversation.LinkRequested"/>. A prompt taller than 60 px folds behind "Show more" (open or folded as its
/// <see cref="UserMessageItem.IsTextExpanded"/> says), and the prompt of the turn on screen stays pinned at the top of the list until the
/// next prompt pushes it out; a click on it brings the start of its turn back to the top. What the user does comes back as the
/// conversation's events. A key is marked handled only when the view acts on it, so the app's own shortcuts keep
/// working. Colors and fonts come from the <c>ClaudeChat.*</c> resources, which can be redefined in
/// <see cref="StyledElement.Resources"/>; the default colors are those of VS Code's "Dark 2026" theme, and
/// <c>avares://ClaudeVisualChat.Avalonia/Themes/DarkModern.axaml</c>, merged into the view's resources, gives the
/// "Dark Modern" ones. The app brings its own theme (Fluent or Simple) for the standard controls. With
/// <see cref="Layout"/> set to <see cref="ChatLayout.Touch"/> the view lays itself out for a phone instead: larger
/// text, no timeline, the user's message in a bubble at the right and each tool call in a block of its own; the reply
/// box is a rounded one with a bar of 44 px targets, and the agents chip is at the end of the list. The model pill of
/// that bar opens a sheet that rises from the bottom, with the models and the effort, and its + another, with the photo or
/// file, the commands, the permission mode, the browser and Remote Control; its context ring a third, the usage sheet, which
/// takes the place of the <c>/context</c> and <c>/usage</c> panels there (they stay hidden): the context window by category and,
/// under it, what the host puts there (<see cref="UsageSheetContent"/>) or the plan's limits. The ✕, Esc or a tap on the veil over
/// the rest of the view closes a sheet, and so does a permission request, which the sheet would cover; while one waits, no sheet
/// opens, and a <c>/context</c> or <c>/usage</c> asked for is closed at once. The request itself asks in a short question there
/// ("Edit OrderService.cs?") and its answers go across the block at 48 px. The ✕ of the banners, of the image preview and of the
/// Agent map (and its back arrow) are targets of 44 px there, and when the preview or the Agent map closes the keyboard goes to a
/// request that waits and never to the reply box (that raises the soft keyboard). A host's back button, which does not arrive as Esc
/// on Android, calls <see cref="CloseTopLayer"/>; a host that draws its own parts around the view dims them while a sheet is open
/// (<see cref="IsSheetOpen"/>), with a veil whose tap calls <see cref="DismissSheet"/>. A host's speech to text (<see cref="Dictation"/>) puts a microphone in the reply
/// box's bar there while it is available: what it hears goes into the box at the caret, and is never sent by itself.
/// </summary>
public partial class ClaudeChatView : UserControl
{
    // before the properties: static fields are set in the order they are written
    private static readonly IReadOnlyList<string> DefaultModes =
        new ReadOnlyCollection<string>(new[] { "plan", "default", "acceptEdits", "auto" });

    public static readonly StyledProperty<ChatConversation?> ConversationProperty =
        AvaloniaProperty.Register<ClaudeChatView, ChatConversation?>(nameof(Conversation));

    /// <summary>The texts; inherited by everything inside the view.</summary>
    public static readonly StyledProperty<ChatStrings> StringsProperty =
        AvaloniaProperty.Register<ClaudeChatView, ChatStrings>(
            nameof(Strings), ChatStrings.English, inherits: true, coerce: (_, value) => value ?? ChatStrings.English);

    public static readonly StyledProperty<IReadOnlyList<string>> AvailableModesProperty =
        AvaloniaProperty.Register<ClaudeChatView, IReadOnlyList<string>>(nameof(AvailableModes), DefaultModes);

    /// <summary>
    /// How the view lays itself out: <see cref="ChatLayout.Desktop"/> (the default) or <see cref="ChatLayout.Touch"/>;
    /// inherited by everything inside the view. The view, and each part that changes with it, takes the pseudo-class
    /// <c>:touch</c> while it is <see cref="ChatLayout.Touch"/>.
    /// </summary>
    public static readonly StyledProperty<ChatLayout> LayoutProperty =
        AvaloniaProperty.Register<ClaudeChatView, ChatLayout>(nameof(Layout), ChatLayout.Desktop, inherits: true);

    /// <summary>
    /// What the host shows in the touch layout's usage sheet, under the context window: its own meters, say. A control is
    /// shown as it is; any other object is made into one by the data templates the host has (the view's, its window's or
    /// the application's), as in a <see cref="ContentControl"/>. With none (the default) the sheet shows the plan's limits
    /// the claude reported. It is not drawn outside the touch layout, and only while the sheet is open.
    /// </summary>
    public static readonly StyledProperty<object?> UsageSheetContentProperty =
        AvaloniaProperty.Register<ClaudeChatView, object?>(nameof(UsageSheetContent));

    /// <summary>
    /// The speech to text the host offers to the reply box (see <see cref="IChatDictation"/>). While it is set and
    /// <see cref="IChatDictation.IsAvailable"/>, the touch layout's reply box has a microphone; null (the default) has none, and
    /// the desktop layout never has one.
    /// </summary>
    public static readonly StyledProperty<IChatDictation?> DictationProperty =
        AvaloniaProperty.Register<ClaudeChatView, IChatDictation?>(nameof(Dictation));

    /// <summary>
    /// True to only show the conversation: the reply box and the permission request are hidden, while the list keeps
    /// drawing, scrolling and letting the user select text. Pending requests stay in the conversation, and the
    /// reply box keeps its draft. False (the default) shows everything.
    /// </summary>
    public static readonly StyledProperty<bool> IsReadOnlyProperty =
        AvaloniaProperty.Register<ClaudeChatView, bool>(nameof(IsReadOnly));

    /// <summary>
    /// True while a sheet of the touch layout (the model sheet, the + sheet or the usage sheet) is on screen; read only. A host that
    /// draws its own parts around the view (a top bar, say) binds to it to dim them with a veil of its own while the sheet dims the
    /// view, in the color <c>ClaudeChat.SheetOverlay</c>, and has a tap on that veil call <see cref="DismissSheet"/>.
    /// </summary>
    public static readonly DirectProperty<ClaudeChatView, bool> IsSheetOpenProperty =
        AvaloniaProperty.RegisterDirect<ClaudeChatView, bool>(nameof(IsSheetOpen), view => view.IsSheetOpen);

    /// <summary>
    /// True (the default) to bring the list to its end when the user sends a message, even after scrolling up to read: the list
    /// follows the end again. False leaves the list where the user left it. The extension's <c>scrollToBottomOnSend</c>.
    /// </summary>
    public static readonly StyledProperty<bool> ScrollToEndOnSendProperty =
        AvaloniaProperty.Register<ClaudeChatView, bool>(nameof(ScrollToEndOnSend), true);

    /// <summary>
    /// True (the default) to show when each message was sent, its time over it (the whole date and time in its tooltip), with a date
    /// line before the first one and wherever the day changes. False hides the times and the date lines. Inherited by everything
    /// inside the view. The extension's <c>showMessageTimestamps</c>.
    /// </summary>
    public static readonly StyledProperty<bool> ShowTimestampsProperty =
        AvaloniaProperty.Register<ClaudeChatView, bool>(nameof(ShowTimestamps), true, inherits: true);

    /// <summary>True while the conversation is busy; inherited, so a running tool's dot blinks only then.</summary>
    internal static readonly StyledProperty<bool> IsWorkingProperty =
        AvaloniaProperty.Register<ClaudeChatView, bool>("IsWorking", inherits: true);

    /// <summary>
    /// True while the view is <see cref="NarrowWidth"/> px wide or less; inherited, so the rows of Edit and Write leave out their box
    /// there on the desktop, like the extension's (the touch layout keeps it).
    /// </summary>
    internal static readonly StyledProperty<bool> IsNarrowProperty =
        AvaloniaProperty.Register<ClaudeChatView, bool>("IsNarrow", inherits: true);

    /// <summary>
    /// <see cref="IsReadOnly"/>, inherited, so the rows of the list that drop what cannot be done in a read-only view (the thumbs under a
    /// reply) follow it.
    /// </summary>
    internal static readonly StyledProperty<bool> ViewIsReadOnlyProperty =
        AvaloniaProperty.Register<ClaudeChatView, bool>("ViewIsReadOnly", inherits: true);

    /// <summary>At this width or less the view is narrow (the extension's <c>max-width: 500px</c>).</summary>
    internal const double NarrowWidth = 500;

    /// <summary>How close to the end (in pixels) still counts as being at the end: coming back down this close follows the end again.</summary>
    private const double EndTolerance = 50;

    /// <summary>
    /// Closer than this to the end (in pixels) is right at it: a scroll up that leaves it stops following, and while following the list
    /// is brought down to it.
    /// </summary>
    private const double AtEndExactly = 1;

    /// <summary>True while the list follows new items; false once the user scrolls up.</summary>
    private bool _stickToEnd = true;

    private bool _scrollQueued;

    // While the list does not follow its end, the item of the first row on screen, where that row began in the viewport and the list's
    // offset then: when the rows above it change (one put in before it, one that grows, another width) and the list did not move, the
    // list moves with it, so what the user reads stays where it was, as the extension's (the browser's scroll anchoring).
    private object? _anchorItem;
    private double _anchorTop;
    private double _anchorOffset;
    private bool _anchorQueued;

    private ChatConversation? _tracked;
    private bool _attached;
    private bool _isSheetOpen;

    /// <summary>A request came while the Diff View covered the view and would have taken the keyboard: it gets it when that closes.</summary>
    private bool _keyboardOwed;

    /// <summary>The list's vertical scroll bar, from the scroll viewer's template (the theme's).</summary>
    private ScrollBar? _verticalBar;

    /// <summary>The pinned prompt's height cap: this share of the view's height (the extension's 40vh).</summary>
    private const double StickyCap = 0.4;

    // What the search for the pinned prompt already knows, so a scroll looks only at the rows it has not seen: the prompt found last
    // (its index), the rows after it known to hold no prompt (up to _clearThrough), and the rows from the first known to hold none at
    // all (up to _emptyThrough). Rows added at the end keep it; any other change of the list forgets it.
    private int _promptIndex = -1;
    private int _clearThrough = -1;
    private int _emptyThrough = -1;

    private static readonly Uri TouchSizesBase = new Uri("avares://ClaudeVisualChat.Avalonia/");
    private static readonly Uri TouchSizesSource = new Uri("avares://ClaudeVisualChat.Avalonia/Themes/Touch.axaml");

    /// <summary>The touch sizes while they are among the view's merged resources.</summary>
    private ResourceInclude? _touchSizes;

    public ClaudeChatView()
    {
        InitializeComponent();
        FooterPanel.Children.Remove(TouchAgentsChip); // the touch layout puts it at the end of the list
        Scroller.ScrollChanged += OnScrollChanged;
        Scroller.TemplateApplied += (_, e) => _verticalBar = e.NameScope.Find<ScrollBar>("PART_VerticalScrollBar");
        Sticky.TurnRequested += (_, prompt) => ScrollTurnToTop(prompt);
        // the pinned prompt covers the top of the list: the wheel over it scrolls the list (once its own text has scrolled to its end)
        Sticky.AddHandler(PointerWheelChangedEvent, OnStickyWheel, RoutingStrategies.Bubble);
        // and so does a finger dragged over it (the content follows the finger: down scrolls up)
        Sticky.ListDragged += (_, dy) => ScrollListBy(-dy);
        Composer.Modes = AvailableModes;
        AddSheet.Modes = AvailableModes;
        Prompt.Modes = AvailableModes; // the plan card's option 1
        Prompt.RequestChanged += OnPromptRequestChanged;
        // a request that comes while the user types in the reply box waits for the typing to pause (the desktop's card)
        Prompt.LastTyped = () => Composer.IsEmpty ? null : Composer.LastEdited;
        Composer.PreviewRequested += (_, e) => Preview.Show(e.Image, e.Name);
        Composer.ModelSheetRequested += (_, _) => ModelSheet.Open();
        Composer.AddSheetRequested += (_, _) => AddSheet.Open();
        // the rows that hand over to the reply box: the sheet has closed by the time the box is asked
        AddSheet.AttachRequested += (_, _) => _ = Composer.AttachFromPickerAsync();
        AddSheet.CommandsRequested += (_, _) => Composer.OpenCommands();
        // a request that came while the preview was open was waiting for the keyboard; under the Diff View it waits on
        Preview.Closed += (_, _) =>
        {
            if (!DiffViewer.IsOpen && !Feedback.IsOpen) GiveBackKeyboard();
        };
        // the Diff View gives the keyboard back to where it was; a request that came meanwhile, or a place that went, gets it as above
        DiffViewer.Closed += (_, _) =>
        {
            var owed = _keyboardOwed;
            _keyboardOwed = false;
            if (_attached && ((owed && Prompt.Request != null) || !DiffViewer.GaveFocusBack)) GiveBackKeyboard();
        };
        // "What went wrong?" does the same
        Feedback.Closed += (_, _) =>
        {
            var owed = _keyboardOwed;
            _keyboardOwed = false;
            if (_attached && ((owed && Prompt.Request != null) || !Feedback.GaveFocusBack)) GiveBackKeyboard();
        };
        ContextPanel.FocusReleased += (_, _) => GiveBackKeyboard();
        UsagePanel.FocusReleased += (_, _) => GiveBackKeyboard();
        AgentMap.FocusReleased += (_, _) => GiveBackKeyboard();
        // the plan's document closed with the keyboard in it (its ×): the card gets it back
        PlanDocument.Closed += (_, _) =>
        {
            PlanComment.Cancel(); // the passage it wrote about went with the document
            if (PlanDocument.HadKeyboard) GiveBackKeyboard();
        };
        // "Add Comment" in the document opens the comment box over the passage; what it adds goes back to the document
        PlanDocument.CommentRequested += (_, e) => PlanComment.Open(e);
        PlanComment.Submitted += (_, text) => PlanDocument.AddComment(text);
        PlanComment.Closed += (_, cancelled) =>
        {
            if (cancelled) PlanDocument.CancelComment();
            if (PlanComment.HadKeyboard) GiveBackKeyboard();
        };
        // a sheet is on screen while its layer is visible, whatever opened or closed it
        foreach (var sheet in new ChatSheet[] { ModelSheet, AddSheet, UsageSheet })
        {
            sheet.PropertyChanged += (_, e) =>
            {
                if (e.Property == IsVisibleProperty) IsSheetOpen = ModelSheet.IsOpen || AddSheet.IsOpen || UsageSheet.IsOpen;
            };
        }
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnKeyDownNotTaken, RoutingStrategies.Bubble);
    }

    public ChatConversation? Conversation
    {
        get => GetValue(ConversationProperty);
        set => SetValue(ConversationProperty, value);
    }

    public ChatStrings Strings
    {
        get => GetValue(StringsProperty);
        set => SetValue(StringsProperty, value);
    }

    /// <summary>Permission modes offered to the user. <c>plan, default, acceptEdits, auto</c> by default.</summary>
    public IReadOnlyList<string> AvailableModes
    {
        get => GetValue(AvailableModesProperty);
        set => SetValue(AvailableModesProperty, value);
    }

    /// <summary>
    /// The layout: <see cref="ChatLayout.Desktop"/>, the extension's (the default), or <see cref="ChatLayout.Touch"/>, for
    /// a phone. Touch brings larger text and no timeline; its sizes are the <c>ClaudeChat.*</c> keys of
    /// <c>Themes/Touch.axaml</c>, merged into the view's resources while it lasts, so a key the app redefines in the
    /// view's resources still wins.
    /// </summary>
    public ChatLayout Layout
    {
        get => GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    /// <summary>
    /// What the host puts in the touch layout's usage sheet under the context window, in place of the plan's limits (see
    /// <see cref="UsageSheetContentProperty"/>). Null by default.
    /// </summary>
    public object? UsageSheetContent
    {
        get => GetValue(UsageSheetContentProperty);
        set => SetValue(UsageSheetContentProperty, value);
    }

    /// <summary>
    /// The host's speech to text, which puts the words it hears into the reply box (never sends them). The touch layout shows a
    /// microphone in the reply box's bar while it is available. Null by default.
    /// </summary>
    public IChatDictation? Dictation
    {
        get => GetValue(DictationProperty);
        set => SetValue(DictationProperty, value);
    }

    /// <summary>Shows the conversation only: no reply box, no permission request. False by default.</summary>
    public bool IsReadOnly
    {
        get => GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    /// <summary>Brings the list to its end when the user sends a message (see <see cref="ScrollToEndOnSendProperty"/>). True by default.</summary>
    public bool ScrollToEndOnSend
    {
        get => GetValue(ScrollToEndOnSendProperty);
        set => SetValue(ScrollToEndOnSendProperty, value);
    }

    /// <summary>Shows the messages' times and the date lines between days (see <see cref="ShowTimestampsProperty"/>). True by default.</summary>
    public bool ShowTimestamps
    {
        get => GetValue(ShowTimestampsProperty);
        set => SetValue(ShowTimestampsProperty, value);
    }

    /// <summary>
    /// True while a sheet of the touch layout is on screen (see <see cref="IsSheetOpenProperty"/>). It follows every way a sheet
    /// opens and closes: the user, <see cref="DismissSheet"/>, <see cref="CloseTopLayer"/>, a permission request, another
    /// conversation, the layout going back to the desktop's and the view becoming read-only (the usage sheet stays then).
    /// </summary>
    public bool IsSheetOpen
    {
        get => _isSheetOpen;
        private set => SetAndRaise(IsSheetOpenProperty, ref _isSheetOpen, value);
    }

    /// <summary>
    /// Does what a tap on the veil of the sheet on top does, and returns true; false, and nothing closes, when no sheet is open.
    /// For a host that dims its own parts around the view while a sheet is open (see <see cref="IsSheetOpen"/>): a tap on its veil
    /// calls this. The whole sheet closes, from any of its pages; the usage sheet's closing is the panel's
    /// (<see cref="ChatConversation.ClosePanel"/>), as with its own veil.
    /// </summary>
    public bool DismissSheet() =>
        // the sheets sit over each other in the order they are in the view's grid: the usage sheet is the last, over the others
        UsageSheet.DismissFromVeil() || AddSheet.DismissFromVeil() || ModelSheet.DismissFromVeil();

    /// <summary>
    /// Closes what is on top of the view and returns true; false when nothing was open. For a host's back button
    /// (Android's), which does not arrive as Esc.
    /// </summary>
    /// <remarks>
    /// One layer per call, the top-most first: the image preview; the Diff View; "What went wrong?"; a menu of the reply box (the commands or the model); a sheet
    /// of the touch layout, the usage sheet before the others, and an inner page of it going back to the first before the sheet
    /// closes; the box where the user writes a comment on the plan; an agent's card in the Agent map, which goes back to the map; the
    /// panel that is open (the Agent map, or the desktop's <c>/context</c> and <c>/usage</c>); the document of a plan over the list;
    /// the field where the user writes why a request is refused. A permission request is
    /// no layer: it has to be answered. The keyboard goes back as it does when the user closes the layer.
    /// </remarks>
    public bool CloseTopLayer()
    {
        if (Preview.IsVisible)
        {
            Preview.Hide();
            return true;
        }
        if (DiffViewer.IsOpen)
        {
            DiffViewer.Hide();
            return true;
        }
        if (Feedback.IsOpen)
        {
            Feedback.Hide();
            return true;
        }
        if (Composer.HasOpenPopup)
        {
            Composer.ClosePopups();
            return true;
        }
        // the sheets sit over each other in the order they are in the view's grid: the usage sheet is the last, over the others
        if (UsageSheet.Back() || AddSheet.Back() || ModelSheet.Back()) return true;
        if (PlanComment.Cancel()) return true;
        if (AgentMap.Back()) return true;
        if (Conversation is { ShownPanel: not ChatPanel.None } conversation)
        {
            conversation.ClosePanel();
            return true;
        }
        // the plan's document is long and stays open: every passing layer opened after it is over it
        if (PlanDocument.IsOpen)
        {
            PlanDocument.Hide();
            return true;
        }
        return Prompt.CloseDenyFieldIfOpen();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ConversationProperty)
        {
            ItemList.ItemsSource = Conversation?.Items;
            _stickToEnd = true;
            ForgetAnchor(); // its item was the other conversation's
            QueueScrollToEnd();
            Composer.Conversation = Conversation; // before the prompt, which asks the reply box about focus
            Prompt.Conversation = Conversation;
            BrowserBanner.Conversation = Conversation;
            RemoteControlBanner.Conversation = Conversation;
            TouchAgentsChip.Conversation = Conversation;
            QueuedList.Conversation = Conversation;
            ContextPanel.Conversation = Conversation;
            UsagePanel.Conversation = Conversation;
            AgentMap.Conversation = Conversation;
            PlanDocument.Conversation = Conversation; // another conversation closes the plan of this one
            PlanComment.Conversation = Conversation;  // and the comment box on it
            ModelSheet.Conversation = Conversation; // another conversation closes the sheets
            AddSheet.Conversation = Conversation;
            UsageSheet.Conversation = Conversation; // and the usage sheet follows the new one's panel
            DiffViewer.Hide(); // its call was the other conversation's
            Feedback.Hide();   // and so was its reply
            Preview.Hide(); // its image was the other conversation's
            Sticky.Hide();  // and so was the pinned prompt: the next scroll finds this one's
            ForgetPrompt();
            if (_attached) Track(Conversation);
        }
        else if (change.Property == IsReadOnlyProperty && IsReadOnly)
        {
            SetValue(ViewIsReadOnlyProperty, true); // the thumbs under the replies go
            Feedback.Hide();                        // a rating cannot go from here: the box closes, sending nothing
            BottomPanel.IsVisible = false;
            ListFade.IsVisible = false;         // there is no reply box to fade into
            PlanComment.Cancel();               // a plan that cannot be answered here takes no comments
            PlanDocument.ViewIsReadOnly = true; // nor opens by itself (the one that did closes)
            Composer.ViewIsReadOnly = true; // a dictation under way stops (the words stay in the box), and the microphone goes
            Composer.ClosePopups(); // what is picked in a menu of a hidden reply box would go nowhere visible
            ModelSheet.Close();     // nor in a sheet; the usage sheet is information, and stays, as the panels it replaces do
            AddSheet.Close();
        }
        else if (change.Property == IsReadOnlyProperty)
        {
            SetValue(ViewIsReadOnlyProperty, false);
            BottomPanel.IsVisible = true;
            ListFade.IsVisible = true;
            PlanDocument.ViewIsReadOnly = false;
            Composer.ViewIsReadOnly = false;
            Prompt.RestartPause(); // a request that came back must not be answered by a stray key
        }
        else if (change.Property == DictationProperty)
        {
            Composer.Dictation = Dictation;
        }
        else if (change.Property == StringsProperty)
        {
            WorkingText.Text = Strings.Working;
            if (_tracked != null) _tracked.Strings = Strings;
        }
        else if (change.Property == AvailableModesProperty)
        {
            Composer.Modes = AvailableModes;
            AddSheet.Modes = AvailableModes;
            Prompt.Modes = AvailableModes;
        }
        else if (change.Property == UsageSheetContentProperty)
        {
            UsageSheet.HostContent = UsageSheetContent;
        }
        else if (change.Property == BoundsProperty)
        {
            // not before the first layout, when the view has no width yet: the rows would drop their boxes and draw them again
            var width = Bounds.Width;
            if (width > 0) SetValue(IsNarrowProperty, width <= NarrowWidth);
            Sticky.CapHeight = Math.Max(0, StickyCap * Bounds.Height);
        }
        else if (change.Property == LayoutProperty)
        {
            PseudoClasses.Set(":touch", Layout == ChatLayout.Touch);
            WorkingRow.Classes.Set("touch", Layout == ChatLayout.Touch); // it hangs from the list, outside the item templates
            UpdateTouchSizes();
            ShowTouchAgents(Layout == ChatLayout.Touch);
            CloseBlockedPanel(); // a panel open on the desktop, beside a request, is a sheet now
        }
    }

    /// <summary>
    /// The agents chip at the end of the list is in the tree only in the touch layout; the desktop layout has the reply
    /// box's, and one chip of each in its tree. It goes right after the queued messages, so the footer keeps its order
    /// (working row, queued messages, chip) however often the layout changes.
    /// </summary>
    private void ShowTouchAgents(bool touch)
    {
        var footer = FooterPanel.Children;
        if (touch && !footer.Contains(TouchAgentsChip)) footer.Insert(footer.IndexOf(QueuedList) + 1, TouchAgentsChip);
        else if (!touch) footer.Remove(TouchAgentsChip);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        UpdateTouchSizes(); // the host may have swapped the resources after setting the layout
        Track(Conversation);
        // the pinned prompt as the list is now, once it is laid out (a list that did not move raises no scroll to say so)
        Dispatcher.UIThread.Post(() =>
        {
            if (_attached) UpdateSticky();
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// The touch layout's sizes are a dictionary of their own, merged into the view's resources while the layout is
    /// <see cref="ChatLayout.Touch"/> and taken out when it goes back, so the <c>ClaudeChat.*</c> keys answer with the
    /// phone's values without a desktop setter changing. It goes first among the merged dictionaries, so the app's own
    /// win over it, and it is never merged twice. An app that swaps the whole <see cref="StyledElement.Resources"/> loses
    /// the merge (XAML does it after the attributes); entering the window makes it again.
    /// </summary>
    private void UpdateTouchSizes()
    {
        var merged = Resources.MergedDictionaries;
        var index = _touchSizes == null ? -1 : merged.IndexOf(_touchSizes);
        if (Layout == ChatLayout.Touch)
        {
            if (index >= 0) return;
            _touchSizes = new ResourceInclude(TouchSizesBase) { Source = TouchSizesSource };
            merged.Insert(0, _touchSizes);
        }
        else if (index >= 0)
        {
            merged.RemoveAt(index);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        Track(null); // the conversation outlives the view: let go of its events
    }

    private void Track(ChatConversation? conversation)
    {
        if (!ReferenceEquals(conversation, _tracked))
        {
            if (_tracked != null)
            {
                _tracked.PropertyChanged -= OnConversationChanged;
                _tracked.CopyRequested -= OnCopyRequested;
                _tracked.MessageSubmitted -= OnMessageSubmitted;
                _tracked.Items.CollectionChanged -= OnItemsChanged;
            }
            _tracked = conversation;
            ForgetPrompt(); // what the search for the pinned prompt knew was about the list as it was while followed
            if (_tracked != null)
            {
                _tracked.PropertyChanged += OnConversationChanged;
                _tracked.CopyRequested += OnCopyRequested;
                _tracked.MessageSubmitted += OnMessageSubmitted;
                _tracked.Items.CollectionChanged += OnItemsChanged;
                _tracked.Strings = Strings; // the lines it says itself come in this view's language
            }
        }
        // out of the window, only the events go: Avalonia is taking the tree apart, and coming back draws it again
        if (_attached)
        {
            UpdateWorking();
            CloseBlockedPanel();
        }
    }

    /// <summary>
    /// A message sent (from the reply box, the palette or the host): with <see cref="ScrollToEndOnSend"/>, the list follows its end again
    /// and goes there, however far up the user was. It goes at once (the extension glides there for up to 2 s, then jumps).
    /// </summary>
    private void OnMessageSubmitted(object? sender, MessageSubmittedEventArgs e)
    {
        if (!ScrollToEndOnSend) return;
        _stickToEnd = true; // first: the scroll job does nothing without it
        QueueScrollToEnd();
    }

    /// <summary>
    /// <c>/copy</c>: writes the reply to the clipboard and tells the conversation how it went, which says it in a line. The line is
    /// the conversation's, so it comes even if the view left meanwhile.
    /// </summary>
    private async void OnCopyRequested(object? sender, string text)
    {
        if (sender is not ChatConversation conversation) return;
        var ok = await ChatClipboard.SetTextAsync(this, text);
        conversation.ReportCopy(ok, text);
    }

    private void OnConversationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatConversation.IsBusy)
            || e.PropertyName == nameof(ChatConversation.IsStreaming)
            || e.PropertyName == nameof(ChatConversation.PermissionMode))
            UpdateWorking();
        else if (e.PropertyName == nameof(ChatConversation.ShownPanel))
            CloseBlockedPanel();
    }

    /// <summary>
    /// In the touch layout the /context and /usage panels are the usage sheet, which would cover the request Claude waits on,
    /// so it does not open while one waits. A panel asked for in the meantime (a typed command, the host) is closed at once,
    /// or the sheet would come up by itself when the request is answered. (A request that comes while the sheet is open
    /// closes it by itself: the sheets see to that. The Agent map is not touched: it covers the request on the desktop too.)
    /// Also done when the view comes into the window, gets another conversation or becomes touch, since the panel may have
    /// been asked for before.
    /// </summary>
    private void CloseBlockedPanel()
    {
        var conversation = _tracked;
        if (conversation is { CurrentPermission: not null, ShownPanel: ChatPanel.Context or ChatPanel.Usage } && Layout == ChatLayout.Touch)
            conversation.ClosePanel();
    }

    /// <summary>
    /// While Claude works, running tools blink; and while nothing is arriving either (no text or thought in pieces),
    /// "Working…" shows at the end of the list, its dot in the color of the mode, like the extension's line.
    /// </summary>
    private void UpdateWorking()
    {
        var conversation = _tracked;
        var busy = conversation != null && conversation.IsBusy;
        SetValue(IsWorkingProperty, busy);
        WorkingText.Text = Strings.Working;
        var on = busy && !conversation!.IsStreaming;
        WorkingRow.IsVisible = on;
        WorkingRow.Classes.Set("on", on); // the dot's blinking, only while it shows
        var mode = conversation?.PermissionMode;
        WorkingRow.Classes.Set("plan", mode == "plan");
        WorkingRow.Classes.Set("acceptEdits", mode == "acceptEdits");
        WorkingRow.Classes.Set("auto", mode == "auto" || mode == "bypassPermissions");
    }

    /// <summary>
    /// A new request takes the keyboard only when the reply box has nothing to send (no text, no image) and the focus
    /// is in the view or nowhere, so a number typed in the reply box, or elsewhere in the app, never answers it; never
    /// while an image preview, the Diff View or the Agent map covers the view (it gets the keyboard when that closes). While the prompt
    /// has the keyboard, the next request keeps it; when none is left, the reply box gets it back, except in the touch layout, where the
    /// keyboard goes nowhere (the reply box with the keyboard is the soft keyboard coming up after every answer).
    /// </summary>
    private void OnPromptRequestChanged(bool hadFocus)
    {
        if (DiffViewer.IsOpen || Feedback.IsOpen)
        {
            // the Diff View (or "What went wrong?") has the keyboard (the user opened it from the list, in the view): the request gets it when it closes, as
            // it would have now, unless the reply box has something to send
            _keyboardOwed = Prompt.Request != null && (hadFocus || Composer.HasNothingToSend);
            return;
        }
        if (Preview.IsVisible || AgentMap.IsVisible) return;
        if (Prompt.Request != null)
        {
            if (hadFocus || (Composer.HasNothingToSend && FocusIsHereOrNowhere())) Prompt.FocusFirstOption();
        }
        else if (hadFocus)
        {
            // In the touch layout it is taken off the control that had it as well: Avalonia leaves it on a control that an
            // ancestor hid, which is what the answer that was tapped is now.
            if (Layout == ChatLayout.Touch) TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
            else Composer.FocusInput();
        }
    }

    /// <summary>
    /// The keyboard repeating a key held down in the prompt goes nowhere: it would answer the next request, or type
    /// into the reply box once none is left. Taken (handled) only in that case.
    /// </summary>
    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (Prompt.IsHeld(e.Key)) e.Handled = true;
        // Esc with the Diff View open closes it and only it, wherever the keyboard is: it is over everything but the preview
        else if (IsDiffEsc(e)) CloseDiff(e);
        // Esc with "What went wrong?" open closes it and only it (it is under the Diff View and the preview only)
        else if (IsFeedbackEsc(e)) CloseFeedback(e);
        // Esc with the comment box open closes it and only it (it is over the panels and the document), unless a menu, a sheet or the
        // preview is over it
        else if (IsCommentEsc(e)) CloseComment(e);
        // Esc with the plan's document open closes it and only it, wherever the keyboard is (the card under it too: only an
        // Esc with the card on top is its "No"), unless a menu, a sheet, the preview or a panel opened over it is on top: the
        // panel closes first
        else if (IsPlanEsc(e)) ClosePlan(e);
        // Esc with a panel open closes it, before the reply box would stop Claude with it; the menus of the reply box
        // and the request take theirs first (an Esc the request leaves closes the panel on the way back up)
        else if (IsPanelEsc(e) && !Composer.HasOpenPopup && !Prompt.IsKeyboardFocusWithin) ClosePanel(e);
    }

    /// <summary>An Esc nothing under the view took: an open panel closes.</summary>
    private void OnKeyDownNotTaken(object? sender, KeyEventArgs e)
    {
        if (IsPanelEsc(e)) ClosePanel(e);
        else if (Prompt.IsFolded && Prompt.IsKeyboardFocusWithin) ScrollConversation(e);
    }

    /// <summary>
    /// The folded card answers no key, so the keys that scroll a page reach the conversation behind it: the arrows, Page Up and
    /// Page Down, Home and End.
    /// </summary>
    private void ScrollConversation(KeyEventArgs e)
    {
        if (e.Handled || e.KeyModifiers != KeyModifiers.None) return;
        switch (e.Key)
        {
            case Key.Up: Scroller.LineUp(); break;
            case Key.Down: Scroller.LineDown(); break;
            case Key.PageUp: Scroller.PageUp(); break;
            case Key.PageDown: Scroller.PageDown(); break;
            case Key.Home: Scroller.ScrollToHome(); break;
            case Key.End: Scroller.ScrollToEnd(); break;
            default: return;
        }
        e.Handled = true;
    }

    private bool IsPanelEsc(KeyEventArgs e) =>
        !e.Handled && e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && !Preview.IsVisible
        && Conversation is { ShownPanel: not ChatPanel.None };

    private bool IsPlanEsc(KeyEventArgs e) =>
        !e.Handled && e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && PlanDocument.IsOpen
        && !Preview.IsVisible && !Composer.HasOpenPopup && !IsSheetOpen;

    private bool IsDiffEsc(KeyEventArgs e) =>
        !e.Handled && e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && DiffViewer.IsOpen && !Preview.IsVisible;

    /// <summary>Esc on the Diff View closes it; held, it goes no further (the card under it would take it for its "No").</summary>
    private void CloseDiff(KeyEventArgs e)
    {
        e.Handled = true;
        Prompt.Hold(e.Key);
        DiffViewer.Hide();
    }

    private bool IsFeedbackEsc(KeyEventArgs e) =>
        !e.Handled && e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && Feedback.IsOpen && !Preview.IsVisible && !DiffViewer.IsOpen;

    /// <summary>Esc on "What went wrong?" closes it without sending more; held, it goes no further.</summary>
    private void CloseFeedback(KeyEventArgs e)
    {
        e.Handled = true;
        Prompt.Hold(e.Key);
        Feedback.Hide();
    }

    private bool IsCommentEsc(KeyEventArgs e) =>
        !e.Handled && e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && PlanComment.IsOpen
        && !Preview.IsVisible && !Composer.HasOpenPopup && !IsSheetOpen;

    /// <summary>
    /// Esc on the plan's document closes it, or the panel opened over it (the Agent map's card first). The key counts as held in the
    /// card until it comes up, so the keyboard repeating it does not go on to refuse the plan.
    /// </summary>
    private void ClosePlan(KeyEventArgs e)
    {
        e.Handled = true;
        Prompt.Hold(e.Key);
        if (Conversation is { ShownPanel: not ChatPanel.None } conversation)
        {
            if (!AgentMap.Back()) conversation.ClosePanel();
            return;
        }
        PlanDocument.Hide();
    }

    /// <summary>Esc on the comment box closes it without a comment; held, it goes no further.</summary>
    private void CloseComment(KeyEventArgs e)
    {
        e.Handled = true;
        Prompt.Hold(e.Key);
        PlanComment.Cancel();
    }

    /// <summary>Esc on an open panel closes it; on an agent's card in the Agent map, it goes back to the list.</summary>
    private void ClosePanel(KeyEventArgs e)
    {
        e.Handled = true;
        if (AgentMap.Back()) return;
        Conversation?.ClosePanel();
    }

    /// <summary>
    /// Opens the plan of an <c>ExitPlanMode</c> row in the document over the list: the plan under review when the row is its call
    /// (with its banner), else the row's plan to read. False, and nothing opens, when the row carries no plan's text.
    /// </summary>
    internal bool ShowPlan(ToolCallItem call)
    {
        var current = Conversation?.CurrentPlan;
        var plan = current?.Request?.ToolUseId is { Length: > 0 } id && id == call.ToolUseId ? current : PlanReview.ReadOnly(call.Input);
        if (plan == null) return false;
        PlanDocument.Show(plan);
        return true;
    }

    /// <summary>
    /// Opens the Diff View over the whole view on the diff of <paramref name="call"/>, whole and side by side; also in a read-only
    /// view. False, and nothing opens, when the call has no diff to show.
    /// </summary>
    internal bool ShowDiff(ToolCallItem call)
    {
        if (call == null || (call.Diff is not { Count: > 0 } && ToolPresentation.Diff(call.Name, call.Input, maxLinesPerSide: null) is not { Count: > 0 }))
            return false;
        DiffViewer.Show(call);
        return true;
    }

    /// <summary>
    /// Opens "What went wrong?" over the view about <paramref name="item"/>, a reply the user rated bad. False, and nothing opens, while
    /// the host takes no feedback or the view is read-only.
    /// </summary>
    internal bool ShowFeedback(AssistantTextItem item)
    {
        var conversation = Conversation;
        if (item == null || conversation == null || !conversation.CanSendFeedback || IsReadOnly) return false;
        Feedback.Show(conversation, item);
        return true;
    }

    /// <summary>Shows an image over the view: an attached one, or one of a message.</summary>
    internal void ShowPreview(ChatImage image, string name) => Preview.Show(image, name);

    /// <summary>
    /// What had the keyboard went away (the preview, a panel): a request waiting gets it, else the reply box (which
    /// does not take it while hidden). Not in the touch layout, where the reply box with the keyboard is the soft keyboard
    /// coming up: there only a request takes it, and the keyboard is nowhere.
    /// </summary>
    private void GiveBackKeyboard()
    {
        if (Prompt.Request != null && Prompt.IsEffectivelyVisible) Prompt.FocusFirstOption();
        else if (Layout != ChatLayout.Touch) Composer.FocusInput();
        else ReleaseHiddenFocus();
    }

    /// <summary>
    /// Avalonia leaves the keyboard on a control that an ancestor hid, which is what the ✕ of the preview or of the Agent map is
    /// once it closes. It is taken off such a control of the view, and off no other: not one that is on screen, and not the host's
    /// own, outside the view, which the view has no say on.
    /// </summary>
    private void ReleaseHiddenFocus()
    {
        var focus = TopLevel.GetTopLevel(this)?.FocusManager;
        if (focus?.GetFocusedElement() is Visual { IsEffectivelyVisible: false } hidden && this.IsVisualAncestorOf(hidden)) focus.ClearFocus();
    }

    private bool FocusIsHereOrNowhere()
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        return focused == null || focused is TopLevel || IsKeyboardFocusWithin;
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        // the event bubbles: the code blocks of the markdown have scroll viewers of their own
        if (!ReferenceEquals(e.Source, Scroller)) return;

        UpdateGutter();
        UpdateSticky(); // it also comes when the rows change height: a prompt opened, more text, another width
        var distance = DistanceToEnd();
        // When the list gets taller (the request under it goes, the window grows), the scroll viewer brings the offset
        // down to keep it in range, while new items may arrive in the same pass: that is not the user going up. (A
        // scroll up smaller than that growth, in that very pass, is taken for it too; the user's scrolling comes from
        // input, between passes, so this is rare.)
        var keptInRange = e.ViewportDelta.Y > 0 && -e.OffsetDelta.Y <= e.ViewportDelta.Y + 0.5;
        var wentUp = e.OffsetDelta.Y < 0 && !keptInRange;
        // Going up from the end by any amount (the wheel, a finger, the keys ↑, Page Up and Home, the scroll bar) is the user going up
        // to read; a wheel that an inner scroller takes, or a sideways one, does not move the list. Coming back down to 50 px of the
        // end follows it again, and so does the list ending up at its very end (the rows above got shorter). New rows alone, with
        // the list not moving, change nothing: a user who went up stays up.
        if (wentUp && distance > AtEndExactly) _stickToEnd = false;
        else if ((e.OffsetDelta.Y != 0 && distance <= EndTolerance) || ReachedEndOfOldEstimate(e)) _stickToEnd = true;

        if (_stickToEnd && distance > AtEndExactly) QueueScrollToEnd(); // new items, or a better estimate of the ones above

        if (_stickToEnd) ForgetAnchor();
        else QueueKeepAnchor();
    }

    /// <summary>The first row on screen becomes the one the list keeps in place while it does not follow its end.</summary>
    private void SetAnchor()
    {
        _anchorItem = null;
        var best = double.PositiveInfinity;
        foreach (var row in ItemList.GetRealizedContainers())
        {
            if (!row.IsVisible || !(Top(row) is double top) || top + row.Bounds.Height <= 0.5 || top >= best) continue;
            best = top;
            _anchorItem = row.DataContext;
        }
        _anchorTop = best;
        _anchorOffset = Scroller.Offset.Y;
    }

    private void ForgetAnchor() => _anchorItem = null;

    /// <summary>
    /// After the layout pass (not inside it, where ScrollChanged is raised, and the rows are not yet where the pass puts them): when the
    /// list did not move since the first row on screen was taken, and that row did (rows came or grew above it), the list moves by as
    /// much and the row is back where it was; otherwise the first row on screen now is taken. A row that left (taken out, or given to
    /// another item) leaves its place to the first row on screen now.
    /// </summary>
    private void QueueKeepAnchor()
    {
        if (_anchorQueued) return;
        _anchorQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _anchorQueued = false;
            if (!_attached || _stickToEnd) return;
            if (_anchorItem != null && Scroller.Offset.Y == _anchorOffset && RowOf(_anchorItem) is { } row
                && Top(row) is double top && Math.Abs(top - _anchorTop) > 0.5)
            {
                ScrollListBy(top - _anchorTop); // its own scroll takes the row again, where it is then
                if (Scroller.Offset.Y != _anchorOffset) return;
            }
            SetAnchor();
        });
    }

    /// <summary>
    /// The drawn row of <paramref name="item"/>, or null. Looked for among the rows drawn only (the list may have given the row that held it
    /// to another item, when rows came before it).
    /// </summary>
    private Control? RowOf(object item)
    {
        foreach (var row in ItemList.GetRealizedContainers())
        {
            if (row.IsVisible && ReferenceEquals(row.DataContext, item)) return row;
        }
        return null;
    }

    /// <summary>
    /// True when a scroll down ended at the end the list estimated before this very pass (the End key, the scroll bar
    /// dragged to its bottom): the pass then measured the items it reached and moved the end further down, so
    /// the offset is no longer at the end, yet the user asked for it. Following the end from here makes it get there.
    /// </summary>
    private bool ReachedEndOfOldEstimate(ScrollChangedEventArgs e)
    {
        if (e.OffsetDelta.Y <= 0) return false;
        var before = (Scroller.Extent.Height - e.ExtentDelta.Y) - (Scroller.Viewport.Height - e.ViewportDelta.Y)
                     - Scroller.Offset.Y;
        return before <= EndTolerance;
    }

    private double DistanceToEnd() => Scroller.Extent.Height - Scroller.Viewport.Height - Scroller.Offset.Y;

    /// <summary>
    /// Pins the prompt of the turn on screen to the top of the list (the extension's sticky prompt): the first drawn row that reaches
    /// under the top belongs to the turn of the nearest prompt before it, which is pinned while its own row is above the top (or not
    /// drawn), unless it has no text. The next prompt's row, when drawn, pushes it up. Only the rows the list has drawn are looked at,
    /// and the search back for the prompt starts from what the last one found, so a scroll or a new row costs a few rows, not the list.
    /// </summary>
    private void UpdateSticky()
    {
        if (!_attached) return;
        var items = ItemList.ItemsSource as IList;
        if (items == null || items.Count == 0)
        {
            Sticky.Hide();
            return;
        }
        var rows = ItemList.GetRealizedContainers();
        var topIndex = -1;
        foreach (var row in rows)
        {
            var index = ItemList.IndexFromContainer(row);
            if (index < 0 || (topIndex >= 0 && index >= topIndex) || !row.IsVisible) continue;
            if (Top(row) is double top && top + row.Bounds.Height > 0.5) topIndex = index;
        }
        var promptIndex = topIndex < 0 ? -1 : FindPrompt(items, topIndex);
        if (promptIndex < 0 || !(items[promptIndex] is UserMessageItem prompt) || string.IsNullOrWhiteSpace(prompt.Text))
        {
            Sticky.Hide(); // before any prompt, or a turn whose prompt has no text (an image alone)
            return;
        }
        // its own row on screen: it is where it is, nothing to pin
        var own = ItemList.ContainerFromIndex(promptIndex);
        if (own != null && own.IsVisible && Top(own) is double ownTop && ownTop > -0.5 && ownTop < Scroller.Viewport.Height)
        {
            Sticky.Hide();
            return;
        }
        // the next prompt's row, when it is drawn under the top (one above it would have been found as the prompt)
        var nextIndex = int.MaxValue;
        var nextTop = double.PositiveInfinity;
        foreach (var row in rows)
        {
            var index = ItemList.IndexFromContainer(row);
            if (index <= promptIndex || index >= nextIndex || index >= items.Count || !(items[index] is UserMessageItem) || !row.IsVisible) continue;
            if (Top(row) is double top && top > -0.5)
            {
                nextIndex = index;
                nextTop = top;
            }
        }
        PlaceSticky();
        Sticky.Show(prompt, nextTop);
    }

    /// <summary>Where <paramref name="row"/> begins, from the top of the list's viewport.</summary>
    private double? Top(Control row) => row.TranslatePoint(default, Scroller)?.Y;

    /// <summary>
    /// The index of the nearest prompt at or before <paramref name="from"/>, or -1 when none is. Only the rows the last searches did not
    /// see are read: within a turn, a scroll reads none, or the few it moved past.
    /// </summary>
    private int FindPrompt(IList items, int from)
    {
        from = Math.Min(from, items.Count - 1);
        var known = _promptIndex >= 0 && _promptIndex <= from;
        var stop = known ? Math.Max(_promptIndex, Math.Min(_clearThrough, from)) : Math.Min(_emptyThrough, from);
        for (var i = from; i > stop; i--)
        {
            if (!(items[i] is UserMessageItem)) continue;
            _promptIndex = i;
            _clearThrough = from;
            return i;
        }
        if (known)
        {
            _clearThrough = Math.Max(_clearThrough, from);
            return _promptIndex;
        }
        _emptyThrough = Math.Max(_emptyThrough, from);
        return -1;
    }

    private void ForgetPrompt()
    {
        _promptIndex = -1;
        _clearThrough = -1;
        _emptyThrough = -1;
    }

    /// <summary>Rows added after everything the search knows leave it right; any other change of the list makes it start over.</summary>
    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add
            && e.NewStartingIndex > Math.Max(_promptIndex, Math.Max(_clearThrough, _emptyThrough)))
            return;
        ForgetPrompt();
    }

    /// <summary>The pinned prompt over the list's text: inside its padding, and clear of a scroll bar drawn over it or beside it.</summary>
    private void PlaceSticky()
    {
        var padding = ItemList.Padding;
        var bar = Math.Max(0, Scroller.Bounds.Width - Scroller.Viewport.Width);
        var margin = new Thickness(padding.Left, 0, padding.Right + ItemList.Margin.Right + bar, 0);
        if (Sticky.Margin != margin) Sticky.Margin = margin;
    }

    /// <summary>
    /// A click on the pinned prompt: the start of its turn (its row) comes to the top of the list, which stops following the end. The row
    /// may be far above, not drawn: the list draws it first.
    /// </summary>
    private void ScrollTurnToTop(UserMessageItem prompt)
    {
        var items = ItemList.ItemsSource as IList;
        if (!_attached || items == null) return;
        var index = _promptIndex >= 0 && _promptIndex < items.Count && ReferenceEquals(items[_promptIndex], prompt)
            ? _promptIndex
            : items.IndexOf(prompt);
        if (index < 0) return;
        ItemList.ScrollIntoView(index);
        Scroller.UpdateLayout();
        var row = ItemList.ContainerFromIndex(index);
        var top = row?.TranslatePoint(default, ItemList)?.Y;
        if (top == null) return;
        _stickToEnd = false;
        var end = Math.Max(0, Scroller.Extent.Height - Scroller.Viewport.Height);
        Scroller.SetCurrentValue(ScrollViewer.OffsetProperty, new Vector(Scroller.Offset.X, Math.Min(top.Value, end)));
    }

    /// <summary>The wheel over the pinned prompt that its own text did not take: the list scrolls, as it would under the pointer.</summary>
    private void OnStickyWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.Handled || e.Delta.Y == 0) return;
        ScrollListBy(-e.Delta.Y * WheelStep);
        e.Handled = true;
    }

    /// <summary>Scrolls the list by <paramref name="dy"/> px (down is positive), within its ends: what the pinned prompt over it passes on.</summary>
    internal void ScrollListBy(double dy)
    {
        if (dy == 0) return;
        var end = Math.Max(0, Scroller.Extent.Height - Scroller.Viewport.Height);
        var y = Math.Max(0, Math.Min(end, Scroller.Offset.Y + dy));
        Scroller.SetCurrentValue(ScrollViewer.OffsetProperty, new Vector(Scroller.Offset.X, y));
    }

    /// <summary>What one notch of the wheel scrolls, as the list's own scroll viewer does.</summary>
    private const double WheelStep = 50;

    /// <summary>
    /// While the list scrolls under a scroll bar drawn over it (Fluent's, which hides itself: <c>AllowAutoHide</c>),
    /// its right side gives the bar its room (<c>ClaudeChat.ScrollBarGutter</c>), as the extension's scroll bar takes
    /// it; so the bar never covers the text. A bar with a column of its own beside the list (Simple's) already has its
    /// room, and the list keeps all of its width. Narrower, the list only gets taller, and wider only shorter, so it
    /// settles.
    /// </summary>
    private void UpdateGutter()
    {
        var scrolls = Scroller.Extent.Height > Scroller.Viewport.Height + 0.5;
        var gutter = scrolls && ScrollBarCoversList()
            && this.TryFindResource("ClaudeChat.ScrollBarGutter", out var value) && value is double width ? width : 0;
        if (ItemList.Margin.Right != gutter) ItemList.Margin = new Thickness(0, 0, gutter, 0);
    }

    /// <summary>
    /// True when the vertical scroll bar is drawn over the list's area rather than beside it. Read from where the
    /// theme's template puts the two, since the property that asks for it (<c>AllowAutoHide</c>) does not tell
    /// whether the template honors it.
    /// </summary>
    private bool ScrollBarCoversList()
    {
        var bar = _verticalBar;
        var content = Scroller.Presenter;
        if (bar == null || content == null || !bar.IsVisible) return false;
        var barLeft = bar.TranslatePoint(default, Scroller);
        var contentRight = content.TranslatePoint(new Point(content.Bounds.Width, 0), Scroller);
        return barLeft != null && contentRight != null && contentRight.Value.X > barLeft.Value.X + 0.5;
    }

    /// <summary>
    /// Moves to the end after the current layout pass (not inside it, where ScrollChanged is raised), once, however
    /// many times it is asked. The list estimates the height of the items it has not drawn, so reaching the real end
    /// can take a few rounds; each one runs as its own dispatcher job.
    /// </summary>
    private void QueueScrollToEnd()
    {
        if (_scrollQueued) return;
        _scrollQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _scrollQueued = false;
            if (!_stickToEnd || DistanceToEnd() <= AtEndExactly) return;
            var end = Math.Max(0, Scroller.Extent.Height - Scroller.Viewport.Height);
            Scroller.SetCurrentValue(ScrollViewer.OffsetProperty, new Vector(Scroller.Offset.X, end));
        });
    }
}
