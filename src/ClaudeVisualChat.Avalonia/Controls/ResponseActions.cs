using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The extension's row of actions under a finished reply, at the end of a turn (in the template of its <see cref="TurnFooterItem"/>):
/// "Copy response", which copies what Claude wrote in the turn (<see cref="ChatTurns.AnswerText"/>) and shows a check for 2 s; the
/// bookmark of the turn's last text (<see cref="ChatConversation.ToggleBookmark"/>); and, while the host takes feedback
/// (<see cref="ChatConversation.CanSendFeedback"/>) and the reply has its line's uuid, "Good response" and "Bad response" with the text
/// beside them, where the link to how feedback is used shows while the host opens links. The thumb down opens "What went wrong?" over
/// the view. A turn that wrote nothing (a reply still arriving, or only what a subagent wrote) has no row. On the desktop the row shows
/// under the pointer or the keyboard, and a bookmarked or rated reply keeps that in sight; in the touch layout the turn's copy is the
/// 0.5.0 button beside it, so the row has only the thumbs (and nothing without them), always shown, at 44 px. A read-only view keeps the
/// copy and the bookmark and drops the thumbs. It follows the 0.3.1 rule: out of the window it lets go of its reply and conversation and
/// stops the check's timer; back, it is drawn as they are now.
/// </summary>
internal sealed class ResponseActions : StackPanel
{
    /// <summary>The page that tells how feedback is used, which the host opens.</summary>
    internal const string FeedbackUrl = "https://code.claude.com/docs/en/data-usage";

    /// <summary>How long the check shows after a copy.</summary>
    private static readonly TimeSpan CopiedFor = TimeSpan.FromSeconds(2);

    private static readonly StyledProperty<ChatStrings> StringsProperty =
        ClaudeChatView.StringsProperty.AddOwner<ResponseActions>();

    private static readonly StyledProperty<ChatLayout> LayoutProperty =
        ClaudeChatView.LayoutProperty.AddOwner<ResponseActions>();

    private static readonly StyledProperty<bool> ViewIsReadOnlyProperty =
        ClaudeChatView.ViewIsReadOnlyProperty.AddOwner<ResponseActions>();

    private readonly Path _copyIcon = new Path();
    private readonly Path _bookmarkIcon = new Path();
    private readonly StackPanel _rating = new StackPanel { Orientation = Orientation.Horizontal };
    private readonly StackPanel _notice = new StackPanel { Orientation = Orientation.Horizontal };
    private readonly TextBlock _thanks = new TextBlock();
    private readonly TextBlock _dot = new TextBlock { Text = " · " };
    private readonly TextBlock _linkText = new TextBlock();
    private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = CopiedFor };

    private bool _attached;
    private bool _copied;
    private ChatConversation? _conversation; // the conversation listened to, only in the window
    private AssistantTextItem? _answer;      // the reply listened to: the turn's last text, only in the window

    public ResponseActions()
    {
        Orientation = Orientation.Horizontal;
        CopyButton = IconButton("ClaudeChatResponseCopy", _copyIcon, "ClaudeChatCopyIcon");
        BookmarkButton = IconButton("ClaudeChatResponseBookmark", _bookmarkIcon, "ClaudeChatBookmarkIcon");
        GoodButton = IconButton("ClaudeChatResponseGood", new Path(), "ClaudeChatThumbUpIcon");
        BadButton = IconButton("ClaudeChatResponseBad", new Path(), "ClaudeChatThumbDownIcon");
        GoodButton.Classes.Add("ClaudeChatResponseThumb");
        BadButton.Classes.Add("ClaudeChatResponseThumb");
        CopyButton.Click += (_, _) => _ = CopyAsync();
        BookmarkButton.Click += (_, _) => ToggleBookmark();
        GoodButton.Click += (_, _) => Rate(true);
        BadButton.Click += (_, _) => Rate(false);

        _thanks.Classes.Add("ClaudeChatResponseNoticeText");
        _dot.Classes.Add("ClaudeChatResponseNoticeText");
        _linkText.Classes.Add("ClaudeChatResponseLinkText");
        LinkButton = new Button { Content = _linkText };
        LinkButton.Classes.Add("ClaudeChatButton");
        LinkButton.Classes.Add("ClaudeChatResponseLink");
        LinkButton.Click += (_, _) => _conversation?.RequestLink(FeedbackUrl);
        _notice.Classes.Add("ClaudeChatResponseNotice");
        _notice.Children.Add(_thanks);
        _notice.Children.Add(_dot);
        _notice.Children.Add(LinkButton);
        // the thumbs and their text show and hide together, as the extension's rating
        _rating.Classes.Add("ClaudeChatResponseRating");
        _rating.Children.Add(GoodButton);
        _rating.Children.Add(BadButton);
        _rating.Children.Add(_notice);

        Children.Add(CopyButton);
        Children.Add(BookmarkButton);
        Children.Add(_rating);
        _timer.Tick += (_, _) => EndCopied();
        IsVisible = false;
        Label();
    }

    internal Button CopyButton { get; }

    internal Button BookmarkButton { get; }

    internal Button GoodButton { get; }

    internal Button BadButton { get; }

    /// <summary>"Learn how we use your feedback", or "Learn how we use it" after a rating.</summary>
    internal Button LinkButton { get; }

    /// <summary>True from a copy until the check goes away.</summary>
    internal bool IsCopied => _copied;

    /// <summary>True while the timer waits to take the check away.</summary>
    internal bool IsTicking => _timer.IsEnabled;

    private ChatStrings Strings => GetValue(StringsProperty) ?? ChatStrings.English;

    private bool IsTouch => GetValue(LayoutProperty) == ChatLayout.Touch;

    private static Button IconButton(string kind, Path icon, string iconClass)
    {
        icon.Classes.Add(iconClass);
        var box = new Viewbox { Child = icon };
        box.Classes.Add("ClaudeChatResponseIcon");
        var button = new Button { Content = box };
        button.Classes.Add("ClaudeChatButton");
        button.Classes.Add("ClaudeChatResponseAction");
        button.Classes.Add(kind);
        return button;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        EndCopied();       // the check does not wait in a control that is out of the window
        Track(null, null); // the conversation and its items outlive the view: let go of their events
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        EndCopied(); // the list reuses the row for another turn
        if (_attached) Refresh();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StringsProperty)
        {
            Label();
        }
        else if (change.Property == LayoutProperty)
        {
            PseudoClasses.Set(":touch", IsTouch);
            if (_attached) Update();
        }
        else if (change.Property == ViewIsReadOnlyProperty)
        {
            if (_attached) Update();
        }
    }

    /// <summary>Finds the turn's reply in the view's conversation, follows it and the conversation, and draws the row for them.</summary>
    private void Refresh()
    {
        var conversation = this.FindAncestorOfType<ClaudeChatView>()?.Conversation;
        AssistantTextItem? answer = null;
        if (conversation != null && DataContext is TurnFooterItem footer)
        {
            var items = conversation.Items;
            var index = items.IndexOf(footer);
            if (index >= 0) answer = ChatTurns.LastAnswer(items, index);
        }
        Track(conversation, answer);
        Update();
    }

    private void Track(ChatConversation? conversation, AssistantTextItem? answer)
    {
        if (!ReferenceEquals(conversation, _conversation))
        {
            if (_conversation != null) _conversation.HostHandlersChanged -= OnHostHandlersChanged;
            _conversation = conversation;
            if (_conversation != null) _conversation.HostHandlersChanged += OnHostHandlersChanged;
        }
        if (!ReferenceEquals(answer, _answer))
        {
            if (_answer != null) _answer.PropertyChanged -= OnAnswerChanged;
            _answer = answer;
            if (_answer != null) _answer.PropertyChanged += OnAnswerChanged;
        }
    }

    private void OnHostHandlersChanged(object? sender, EventArgs e) => Update();

    private void OnAnswerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AssistantTextItem.IsBookmarked) || e.PropertyName == nameof(AssistantTextItem.Rating)
            || e.PropertyName == nameof(AssistantTextItem.Uuid))
            Update();
    }

    /// <summary>Whether the thumbs show: the host takes feedback, the view is not read-only and the reply has its line's uuid.</summary>
    private bool ShowsThumbs =>
        _answer is { Uuid: { Length: > 0 } } && _conversation is { CanSendFeedback: true } && !GetValue(ViewIsReadOnlyProperty);

    private void Update()
    {
        var answer = _answer;
        var touch = IsTouch;
        var thumbs = ShowsThumbs;
        IsVisible = answer != null && (!touch || thumbs);
        CopyButton.IsVisible = !touch;     // the touch layout's copy is the 0.5.0 button beside the row
        BookmarkButton.IsVisible = !touch;
        _rating.IsVisible = thumbs;

        var marked = answer?.IsBookmarked == true;
        BookmarkButton.Classes.Set("marked", marked);
        _bookmarkIcon.Classes.Set("marked", marked);
        var rating = thumbs ? answer!.Rating : null;
        GoodButton.Classes.Set("selected", rating == true);
        BadButton.Classes.Set("selected", rating == false);
        PseudoClasses.Set(":rated", rating != null);

        var links = _conversation?.CanOpenLinks == true;
        _thanks.IsVisible = rating != null;
        _dot.IsVisible = rating != null && links;
        LinkButton.IsVisible = links;
        Label();
    }

    private void Label()
    {
        var strings = Strings;
        AutomationProperties.SetName(CopyButton, _copied ? strings.Copied : strings.CopyResponseToClipboard);
        ToolTip.SetTip(CopyButton, strings.CopyResponse);
        var bookmark = _answer?.IsBookmarked == true ? strings.RemoveBookmark : strings.BookmarkResponse;
        AutomationProperties.SetName(BookmarkButton, bookmark);
        ToolTip.SetTip(BookmarkButton, bookmark);
        AutomationProperties.SetName(GoodButton, strings.GoodResponse);
        ToolTip.SetTip(GoodButton, strings.GoodResponse);
        AutomationProperties.SetName(BadButton, strings.BadResponse);
        ToolTip.SetTip(BadButton, strings.BadResponse);
        _thanks.Text = strings.FeedbackThanks;
        _linkText.Text = _thanks.IsVisible ? strings.FeedbackLearnHowShort : strings.FeedbackLearnHow;
    }

    private void ToggleBookmark()
    {
        if (_answer != null) _conversation?.ToggleBookmark(_answer);
    }

    /// <summary>A thumb: the rating goes to the host; a new bad one asks what went wrong.</summary>
    private void Rate(bool positive)
    {
        var answer = _answer;
        var conversation = _conversation;
        if (answer == null || conversation == null || !ShowsThumbs) return;
        if (conversation.RateAnswer(answer, positive)) this.FindAncestorOfType<ClaudeChatView>()?.ShowFeedback(answer);
    }

    private async Task CopyAsync()
    {
        var text = Answer();
        if (text.Length == 0) return;
        var turn = DataContext;
        if (!await ChatClipboard.SetTextAsync(this, text)) return; // the clipboard refused it: nothing was copied, so nothing is confirmed
        if (!_attached || !ReferenceEquals(DataContext, turn)) return; // the view left, or the list took the row to another turn, meanwhile
        _copied = true;
        _copyIcon.Classes.Set("copied", true);
        Label();
        _timer.Stop();
        _timer.Start(); // a second copy gives the check another 2 s
    }

    /// <summary>The check goes away (its timer's tick, 2 s after a copy): the copy icon and its name come back.</summary>
    internal void EndCopied()
    {
        _timer.Stop();
        if (!_copied) return;
        _copied = false;
        _copyIcon.Classes.Set("copied", false);
        Label();
    }

    /// <summary>What Claude wrote in the turn this row ends; empty when the turn wrote nothing.</summary>
    private string Answer()
    {
        var items = _conversation?.Items;
        if (items == null || !(DataContext is TurnFooterItem footer)) return "";
        var index = items.IndexOf(footer);
        return index < 0 ? "" : ChatTurns.AnswerText(items, index);
    }
}

/// <summary>
/// "What went wrong?", the box the thumb down under a reply opens, as the extension's: over the whole view on the modal backdrop, a
/// dialog of 400 px at most (the view less 32 px) with the title and a ×, a field of four lines ("Tell us more (optional)"), and "Skip"
/// and "Send feedback" at the right. Ctrl+Enter sends; Enter breaks the line. Esc, the ×, "Skip" or a click on the backdrop closes it
/// without sending more (the rating went already). What it sends goes to the host through
/// <see cref="ChatConversation.FeedbackRequested"/>, trimmed and cut at 4,000 characters. It is the view's own (not a sheet): a layer
/// under the Diff View and over the reply box's menus, which the view closes with its back button, another conversation and the
/// read-only mode. The keyboard goes to the field when it opens and back to where it was when it closes (<see cref="GaveFocusBack"/>).
/// Taken out of the window, it stays open with what the field says.
/// </summary>
internal sealed class FeedbackDialog : Border
{
    /// <summary>The widest the dialog gets.</summary>
    internal const double MostWidth = 400;

    /// <summary>The dialog is the view less this, in width, at most.</summary>
    internal const double Inset = 32;

    private static readonly StyledProperty<ChatStrings> StringsProperty =
        ClaudeChatView.StringsProperty.AddOwner<FeedbackDialog>();

    private static readonly StyledProperty<ChatLayout> LayoutProperty =
        ClaudeChatView.LayoutProperty.AddOwner<FeedbackDialog>();

    private readonly TextBlock _title = new TextBlock();
    private readonly Button _close;
    private readonly Border _dialog;
    private readonly TextBlock _skipText = new TextBlock();
    private readonly TextBlock _sendText = new TextBlock();

    private ChatConversation? _conversation;
    private AssistantTextItem? _item;
    private bool _attached;

    /// <summary>What had the keyboard when the box opened, and whether it showed the keyboard's focus.</summary>
    private Control? _returnFocus;
    private bool _returnByKey;

    public FeedbackDialog()
    {
        _title.Classes.Add("ClaudeChatFeedbackTitle");
        var icon = new Path();
        icon.Classes.Add("ClaudeChatRemoveIcon");
        var iconBox = new Viewbox { Child = icon };
        iconBox.Classes.Add("ClaudeChatDiffViewCloseIcon");
        _close = new Button { Content = iconBox };
        _close.Classes.Add("ClaudeChatButton");
        _close.Classes.Add("ClaudeChatDiffViewClose");
        _close.Click += (_, _) => Hide();
        DockPanel.SetDock(_close, Dock.Right);
        var header = new DockPanel { Children = { _close, _title } };
        header.Classes.Add("ClaudeChatFeedbackHeader");

        Field.Classes.Add("ClaudeChatFeedbackField");
        Field.AcceptsReturn = true;
        Field.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        Field.AddHandler(KeyDownEvent, OnFieldKeyDown, RoutingStrategies.Tunnel);

        SkipButton = new Button { Content = _skipText };
        SkipButton.Classes.Add("ClaudeChatButton");
        SkipButton.Classes.Add("ClaudeChatFeedbackSkip");
        SkipButton.Click += (_, _) => Hide();
        SendButton = new Button { Content = _sendText };
        SendButton.Classes.Add("ClaudeChatButton");
        SendButton.Classes.Add("ClaudeChatFeedbackSend");
        SendButton.Click += (_, _) => Send();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { SkipButton, SendButton } };
        buttons.Classes.Add("ClaudeChatFeedbackButtons");

        _dialog = new Border
        {
            Child = new StackPanel { Children = { header, Field, buttons } },
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _dialog.Classes.Add("ClaudeChatFeedbackDialog");
        KeyboardNavigation.SetTabNavigation(_dialog, KeyboardNavigationMode.Cycle); // Tab stays in the box
        Child = _dialog;
        IsVisible = false;
        Label();
    }

    /// <summary>Raised when the box closes, whatever closed it.</summary>
    public event EventHandler? Closed;

    /// <summary>True while the box is on screen.</summary>
    internal bool IsOpen => IsVisible;

    /// <summary>The reply the open box is about; null while closed.</summary>
    internal AssistantTextItem? Item => _item;

    /// <summary>The field where the user tells what went wrong.</summary>
    internal TextBox Field { get; } = new TextBox();

    internal Button SkipButton { get; }

    internal Button SendButton { get; }

    /// <summary>The keyboard went back, when the box last closed, to what had it before; false when that was gone.</summary>
    internal bool GaveFocusBack { get; private set; }

    private ChatStrings Strings => GetValue(StringsProperty) ?? ChatStrings.English;

    /// <summary>Opens the box about <paramref name="item"/>, of <paramref name="conversation"/>, its field empty and with the keyboard.</summary>
    internal void Show(ChatConversation conversation, AssistantTextItem item)
    {
        if (!IsVisible) RememberFocus();
        _conversation = conversation ?? throw new ArgumentNullException(nameof(conversation));
        _item = item ?? throw new ArgumentNullException(nameof(item));
        Field.Text = "";
        IsVisible = true;
        Field.Focus(NavigationMethod.Tab);
    }

    /// <summary>Closes the box without sending what it says, and gives the keyboard back to what had it, when that is still on screen.</summary>
    internal void Hide()
    {
        if (!IsVisible) return;
        IsVisible = false;
        _conversation = null;
        _item = null;
        Field.Text = "";
        var back = _returnFocus;
        _returnFocus = null;
        GaveFocusBack = _attached && back is { IsEffectivelyVisible: true, Focusable: true } && back.IsAttachedToVisualTree()
            && back.Focus(_returnByKey ? NavigationMethod.Tab : NavigationMethod.Pointer);
        Closed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Sends what the field says (it may be empty) about the reply, and closes.</summary>
    private void Send()
    {
        var conversation = _conversation;
        var item = _item;
        if (conversation == null || item == null) return;
        var text = Field.Text;
        Hide();
        conversation.SendFeedbackDetails(item, text);
    }

    private void RememberFocus()
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
        _returnFocus = focused;
        _returnByKey = focused != null && focused.Classes.Contains(":focus-visible");
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false; // it holds no events and no timer: open, it stays open
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StringsProperty)
        {
            Label();
        }
        else if (change.Property == LayoutProperty)
        {
            PseudoClasses.Set(":touch", GetValue(LayoutProperty) == ChatLayout.Touch);
        }
        else if (change.Property == BoundsProperty)
        {
            _dialog.Width = Math.Max(0, Math.Min(MostWidth, Bounds.Width - Inset));
        }
    }

    private void Label()
    {
        var strings = Strings;
        _title.Text = strings.FeedbackTitle;
        Field.Watermark = strings.FeedbackPlaceholder;
        _skipText.Text = strings.FeedbackSkip;
        _sendText.Text = strings.FeedbackSend;
        AutomationProperties.SetName(_close, strings.Close);
        ToolTip.SetTip(_close, strings.Close);
    }

    private void OnFieldKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (e.KeyModifiers == KeyModifiers.Control || e.KeyModifiers == KeyModifiers.Meta))
        {
            e.Handled = true;
            Send();
        }
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
