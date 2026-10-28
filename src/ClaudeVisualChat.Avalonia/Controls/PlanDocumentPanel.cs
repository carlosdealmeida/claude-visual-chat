using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The plan of an <c>ExitPlanMode</c> request as a document, where the extension opens it in an editor tab beside the chat: over
/// the list only, so the plan card under it stays in sight and answerable. A header with the plan's title and a ×, then the plan in
/// Markdown in the editor's colors (14 px on a 1.6 line, 16 px over and under it and 24 px at its sides; headings semibold, H1 at
/// 1.6em and H2 at 1.3em, each underlined with a 1 px line) and, over it while the plan waits for an answer, the banner "Ready for
/// review". It opens by itself when the conversation's plan under review comes (<see cref="ChatConversation.CurrentPlan"/>; not in a
/// read-only view) and closes when that plan leaves approved; it stays after a refusal, on its plan, until the next one takes its
/// place. The view opens it again from the plan's row (<see cref="Show"/>). The ×, Esc and the host's back button close it; the view
/// sees to those keys and gives the keyboard back to the card when the document had it (<see cref="HadKeyboard"/>).
/// </summary>
internal sealed class PlanDocumentPanel : Border
{
    private static readonly StyledProperty<ChatStrings> StringsProperty =
        ClaudeChatView.StringsProperty.AddOwner<PlanDocumentPanel>();

    private readonly TextBlock _title = new TextBlock();
    private readonly Button _close;
    private readonly Border _banner = new Border();
    private readonly TextBlock _bannerTitle = new TextBlock();
    private readonly TextBlock _bannerText = new TextBlock();
    private readonly MarkdownBlock _document = new MarkdownBlock();
    private readonly ScrollViewer _scroller;

    private ChatConversation? _conversation;
    private ChatConversation? _tracked;
    private bool _attached;
    private bool _viewIsReadOnly;

    /// <summary>The plan on show, or last shown: the panel's own, which outlives the conversation's plan under review.</summary>
    private PlanReview? _plan;

    /// <summary>The conversation's plan under review the panel last saw: a new one opens the document, one that left approved closes it.</summary>
    private PlanReview? _seen;

    public PlanDocumentPanel()
    {
        _title.Classes.Add("ClaudeChatPlanTitle");
        var icon = new Path();
        icon.Classes.Add("ClaudeChatRemoveIcon");
        _close = new Button { Content = new Border { Width = 20, Height = 20, Child = new Viewbox { Width = 20, Height = 20, Child = icon } } };
        _close.Classes.Add("ClaudeChatButton");
        _close.Classes.Add("ClaudeChatPlanClose");
        _close.Click += (_, _) => Hide();
        DockPanel.SetDock(_close, Dock.Right);
        var header = new Border { Child = new DockPanel { Children = { _close, _title } } };
        header.Classes.Add("ClaudeChatPlanHeader");
        DockPanel.SetDock(header, Dock.Top);

        _bannerTitle.Classes.Add("ClaudeChatPlanBannerTitle");
        _bannerText.Classes.Add("ClaudeChatPlanBannerText");
        _banner.Classes.Add("ClaudeChatPlanBanner");
        _banner.Child = new StackPanel { Children = { _bannerTitle, _bannerText } };
        _document.Classes.Add("ClaudeChatPlanDocument");
        _document.PropertyChanged += (_, e) =>
        {
            if (e.Property == ContentControl.ContentProperty) UnderlineHeadings(_document.Content as Control);
        };
        var page = new StackPanel { Children = { _banner, _document } };
        page.Classes.Add("ClaudeChatPlanPage");
        _scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = page,
        };

        Child = new DockPanel { Children = { header, _scroller } };
        IsVisible = false;
        Label();
    }

    /// <summary>Raised when the document closes: its ×, Esc, the host's back button, or its plan approved.</summary>
    public event EventHandler? Closed;

    /// <summary>The conversation whose plans it shows; another one closes the document and forgets its plan.</summary>
    internal ChatConversation? Conversation
    {
        get => _conversation;
        set
        {
            if (ReferenceEquals(value, _conversation)) return;
            Hide();
            _plan = null;
            _seen = null;
            _conversation = value;
            if (_attached) Track(value);
        }
    }

    /// <summary>
    /// True while the view only shows the conversation: a plan that comes does not open the document by itself (it cannot be
    /// answered there), and the document has no banner.
    /// </summary>
    internal bool ViewIsReadOnly
    {
        get => _viewIsReadOnly;
        set
        {
            _viewIsReadOnly = value;
            if (_attached) Fill();
        }
    }

    /// <summary>The plan on show, or the last one shown (it is kept when the document closes).</summary>
    internal PlanReview? Plan => _plan;

    /// <summary>True while the document is on screen.</summary>
    public bool IsOpen => IsVisible;

    /// <summary>The keyboard was in the document when it last closed: the view gives it back to the card.</summary>
    internal bool HadKeyboard { get; private set; }

    /// <summary>Shows <paramref name="plan"/>, from its top when it is another one than the one on show.</summary>
    public void Show(PlanReview plan)
    {
        if (plan == null) throw new ArgumentNullException(nameof(plan));
        var other = !ReferenceEquals(plan, _plan);
        _plan = plan;
        Fill();
        if (other) _scroller.Offset = default;
        IsVisible = true;
    }

    /// <summary>Closes the document; it keeps its plan.</summary>
    public void Hide()
    {
        if (!IsVisible) return;
        HadKeyboard = IsKeyboardFocusWithin;
        IsVisible = false;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private ChatStrings Strings => GetValue(StringsProperty) ?? ChatStrings.English;

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
            Label();
            if (_attached) Fill();
        }
    }

    private void Track(ChatConversation? conversation)
    {
        if (!ReferenceEquals(conversation, _tracked))
        {
            if (_tracked != null) _tracked.PropertyChanged -= OnConversationChanged;
            _tracked = conversation;
            if (_tracked != null) _tracked.PropertyChanged += OnConversationChanged;
        }
        // out of the window, only the events go: coming back opens or closes the document for what changed meanwhile
        if (_attached) Update();
    }

    private void OnConversationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatConversation.CurrentPlan)) Update();
    }

    /// <summary>
    /// A new plan under review opens the document on it (the next plan after a refusal takes the place of the one on show); the plan
    /// on show that left approved closes it. One that left refused, or unanswered (the turn was stopped), leaves it open.
    /// </summary>
    private void Update()
    {
        var current = _tracked?.CurrentPlan;
        if (!ReferenceEquals(current, _seen))
        {
            var left = _seen;
            _seen = current;
            if (current != null)
            {
                if (!ViewIsReadOnly) Show(current);
            }
            else if (left != null && ReferenceEquals(left, _plan) && left.Outcome == PlanOutcome.Approved)
            {
                Hide();
            }
        }
        Fill();
    }

    /// <summary>The plan's title and text, and the banner while the plan on show is the one waiting for an answer.</summary>
    private void Fill()
    {
        var strings = Strings;
        _bannerTitle.Text = strings.PlanReadyForReview;
        _bannerText.Text = strings.PlanSelectToComment;
        var plan = _plan;
        _title.Text = plan == null ? "" : plan.Heading ?? strings.PlanTitle;
        _document.Markdown = plan?.Markdown;
        _banner.IsVisible = plan != null && !ViewIsReadOnly && ReferenceEquals(plan, _tracked?.CurrentPlan);
    }

    private void Label()
    {
        AutomationProperties.SetName(_close, Strings.Close);
        ToolTip.SetTip(_close, Strings.Close);
    }

    /// <summary>
    /// The Markdown draws its headings as text alone; the H1 and H2 of the document are underlined, as the extension's, so each goes
    /// into a frame whose bottom border is that line (<c>Border.ClaudeChatPlanRule</c>, classes <c>h1</c> and <c>h2</c>).
    /// </summary>
    private static void UnderlineHeadings(Control? root)
    {
        if (root is not Panel panel) return;
        var children = panel.Children;
        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            var level = child.Classes.Contains("Heading1") ? "h1" : child.Classes.Contains("Heading2") ? "h2" : null;
            if (level == null) continue;
            children.RemoveAt(i);
            var rule = new Border { Child = child };
            rule.Classes.Add("ClaudeChatPlanRule");
            rule.Classes.Add(level);
            children.Insert(i, rule);
        }
    }
}
