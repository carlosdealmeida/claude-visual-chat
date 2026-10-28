using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using ColorTextBlock.Avalonia;

namespace ClaudeVisualChat.Controls;

/// <summary>What the user selected in the plan to comment on (<see cref="PlanDocumentPanel.CommentRequested"/>).</summary>
internal sealed class PlanCommentRequest : EventArgs
{
    public PlanCommentRequest(PlanReview plan, string quote, Rect selection)
    {
        Plan = plan;
        Quote = quote;
        Selection = selection;
    }

    /// <summary>The plan the comment goes to.</summary>
    public PlanReview Plan { get; }

    /// <summary>The selected passage, trimmed.</summary>
    public string Quote { get; }

    /// <summary>Where the passage is, in the document's coordinates.</summary>
    public Rect Selection { get; }
}

/// <summary>
/// The plan of an <c>ExitPlanMode</c> request as a document, where the extension opens it in an editor tab beside the chat: over
/// the list only, so the plan card under it stays in sight and answerable. A header with the plan's title and a ×, then the plan in
/// Markdown in the editor's colors (14 px on a 1.6 line, 16 px over and under it and 24 px at its sides; headings semibold, H1 at
/// 1.6em and H2 at 1.3em, each underlined with a 1 px line) and, over it while the plan waits for an answer, the banner "Ready for
/// review". It opens by itself when the conversation's plan under review comes with its text (<see cref="ChatConversation.CurrentPlan"/>;
/// not in a read-only view, and it closes when the view becomes read-only) and closes when that plan leaves approved; it stays after a
/// refusal, on its plan, until the next one takes its place. The view opens it again from the plan's row (<see cref="Show"/>). The ×,
/// Esc and the host's back button close it; the view sees to those keys and gives the keyboard back to the card when the document had
/// it (<see cref="HadKeyboard"/>).
/// <para>
/// Its paragraphs and headings can be selected (<see cref="PlanTextBlock"/>, one block at a time). While the banner shows, a passage
/// selected offers "Add Comment" 6 px under it, which asks the view for the comment box (<see cref="CommentRequested"/>); the comment
/// written there comes back by <see cref="AddComment"/>. Each passage commented on is marked, with a numbered circle whose tooltip is
/// the comment, for as long as the comment is in the plan (the card's × and the feedback sent take it away).
/// </para>
/// </summary>
internal sealed class PlanDocumentPanel : Border
{
    private static readonly StyledProperty<ChatStrings> StringsProperty =
        ClaudeChatView.StringsProperty.AddOwner<PlanDocumentPanel>();

    /// <summary>The gap between a selected passage and the "Add Comment" under it.</summary>
    internal const double AddCommentGap = 6;

    private readonly TextBlock _title = new TextBlock();
    private readonly Button _close;
    private readonly Border _banner = new Border();
    private readonly TextBlock _bannerTitle = new TextBlock();
    private readonly TextBlock _bannerText = new TextBlock();
    private readonly MarkdownBlock _document = new MarkdownBlock();
    private readonly ScrollViewer _scroller;
    private readonly Canvas _overlay = new Canvas();
    private readonly Button _addComment;
    private readonly TextBlock _addCommentText = new TextBlock();

    /// <summary>The selectable blocks of the plan on show, in the document's order.</summary>
    private readonly List<PlanTextBlock> _blocks = new List<PlanTextBlock>();

    /// <summary>Where each comment's passage is, by block: the plan's comments say what, these say where.</summary>
    private readonly List<Anchor> _anchors = new List<Anchor>();

    private ChatConversation? _conversation;
    private ChatConversation? _tracked;
    private bool _attached;
    private bool _viewIsReadOnly;

    /// <summary>The plan on show, or last shown: the panel's own, which outlives the conversation's plan under review.</summary>
    private PlanReview? _plan;

    /// <summary>The conversation's plan under review the panel last saw: a new one opens the document, one that left approved closes it.</summary>
    private PlanReview? _seen;

    /// <summary>The document on show opened by itself (a plan that came), not from the plan's row.</summary>
    private bool _openedBySelf;

    /// <summary>The plan whose comments the panel listens to (only in the window).</summary>
    private PlanReview? _watched;

    /// <summary>The passage "Add Comment" offers, and then the one the comment box writes about.</summary>
    private Anchor? _offer;
    private Anchor? _pending;

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
            if (e.Property == ContentControl.ContentProperty) OnRendered(_document.Content as Control);
        };
        var page = new StackPanel { Children = { _banner, _document } };
        page.Classes.Add("ClaudeChatPlanPage");

        // "Add Comment": it takes no focus, so the block keeps its selection while it is clicked
        _addComment = new Button { Content = _addCommentText, Focusable = false, IsVisible = false };
        _addComment.Classes.Add("ClaudeChatButton");
        _addComment.Classes.Add("ClaudeChatPlanAddComment");
        _addComment.Click += (_, _) => RequestComment();
        _overlay.Children.Add(_addComment);

        _scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = new Panel { Children = { page, _overlay } },
        };

        Child = new DockPanel { Children = { header, _scroller } };
        IsVisible = false;
        Label();
    }

    /// <summary>Raised when the document closes: its ×, Esc, the host's back button, or its plan approved.</summary>
    public event EventHandler? Closed;

    /// <summary>Raised by "Add Comment": the view opens the comment box over the passage.</summary>
    internal event EventHandler<PlanCommentRequest>? CommentRequested;

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
            _anchors.Clear();
            _pending = null;
            _conversation = value;
            if (_attached) Track(value);
        }
    }

    /// <summary>
    /// True while the view only shows the conversation: a plan that comes does not open the document by itself (it cannot be
    /// answered there), the one that did closes, and the document has no banner and takes no comments.
    /// </summary>
    internal bool ViewIsReadOnly
    {
        get => _viewIsReadOnly;
        set
        {
            _viewIsReadOnly = value;
            if (value && _openedBySelf) Hide();
            if (_attached) Fill();
        }
    }

    /// <summary>The plan on show, or the last one shown (it is kept when the document closes).</summary>
    internal PlanReview? Plan => _plan;

    /// <summary>True while the document is on screen.</summary>
    public bool IsOpen => IsVisible;

    /// <summary>The keyboard was in the document when it last closed: the view gives it back to the card.</summary>
    internal bool HadKeyboard { get; private set; }

    /// <summary>
    /// True while the plan on show takes comments: it is the one waiting for an answer, in a view that can answer it (the banner
    /// shows then).
    /// </summary>
    internal bool CanComment =>
        _plan != null && !_plan.IsReadOnly && !ViewIsReadOnly && ReferenceEquals(_plan, _tracked?.CurrentPlan);

    /// <summary>The selectable blocks of the plan on show.</summary>
    internal IReadOnlyList<PlanTextBlock> Blocks => _blocks;

    /// <summary>Shows <paramref name="plan"/>, from its top when it is another one than the one on show.</summary>
    public void Show(PlanReview plan) => Open(plan, bySelf: false);

    /// <summary>Closes the document; it keeps its plan.</summary>
    public void Hide()
    {
        if (!IsVisible) return;
        HadKeyboard = IsKeyboardFocusWithin;
        IsVisible = false;
        _openedBySelf = false;
        _pending = null;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Adds the comment the box wrote on the passage "Add Comment" asked about; true when it was added (not an empty one, and the
    /// plan still takes comments).
    /// </summary>
    internal bool AddComment(string text)
    {
        var pending = _pending;
        _pending = null;
        if (pending == null || !CanComment || !ReferenceEquals(pending.Plan, _plan)) return false;
        var plan = pending.Plan;
        var count = plan.Comments.Count;
        plan.AddComment(pending.Quote, pending.Heading, text);
        if (plan.Comments.Count == count) return false;
        _anchors.Add(pending.With(plan.Comments[plan.Comments.Count - 1]));
        Mark();
        return true;
    }

    /// <summary>The comment box closed without a comment.</summary>
    internal void CancelComment() => _pending = null;

    private void Open(PlanReview plan, bool bySelf)
    {
        if (plan == null) throw new ArgumentNullException(nameof(plan));
        var other = !ReferenceEquals(plan, _plan);
        _plan = plan;
        _openedBySelf = bySelf;
        if (other) _pending = null;
        Watch();
        Fill();
        if (other) _scroller.Offset = default;
        IsVisible = true;
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
        Track(null); // the conversation outlives the view: let go of its events, and of its plan's
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
        Watch();
        // out of the window, only the events go: coming back opens or closes the document for what changed meanwhile
        if (_attached) Update();
    }

    /// <summary>Listens to the comments of the plan on show while in the window, and to no other plan's.</summary>
    private void Watch()
    {
        var plan = _attached ? _plan : null;
        if (ReferenceEquals(plan, _watched)) return;
        if (_watched != null) _watched.Comments.CollectionChanged -= OnCommentsChanged;
        _watched = plan;
        if (_watched != null) _watched.Comments.CollectionChanged += OnCommentsChanged;
    }

    private void OnConversationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatConversation.CurrentPlan)) Update();
    }

    private void OnCommentsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Mark();

    /// <summary>
    /// A new plan under review opens the document on it (the next plan after a refusal takes the place of the one on show), when it
    /// has a text to show; the plan on show that left approved closes it. One that left refused, or unanswered (the turn was stopped),
    /// leaves it open.
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
                if (!ViewIsReadOnly && current.Request != null && PlanReview.HasPlan(current.Request.Input)) Open(current, bySelf: true);
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
        _addCommentText.Text = strings.PlanAddComment;
        var plan = _plan;
        _title.Text = plan == null ? "" : plan.Heading ?? strings.PlanTitle;
        _document.Markdown = plan?.Markdown;
        _banner.IsVisible = plan != null && CanComment;
        Mark();
    }

    private void Label()
    {
        AutomationProperties.SetName(_close, Strings.Close);
        ToolTip.SetTip(_close, Strings.Close);
    }

    /// <summary>The Markdown was drawn again: its blocks become selectable, its H1 and H2 underlined, and the comments marked.</summary>
    private void OnRendered(Control? root)
    {
        foreach (var block in _blocks) block.PropertyChanged -= OnBlockChanged;
        _blocks.Clear();
        _offer = null;
        _pending = null;
        MakeSelectable(root);
        foreach (var block in _blocks) block.PropertyChanged += OnBlockChanged;
        UnderlineHeadings(root);
        Mark();
    }

    /// <summary>
    /// Each text block of the Markdown but the list markers goes into a <see cref="PlanTextBlock"/> in its place, which the user can
    /// select (the Markdown's own selects nothing).
    /// </summary>
    private void MakeSelectable(Control? control)
    {
        switch (control)
        {
            case Panel panel:
                var children = panel.Children;
                for (var i = 0; i < children.Count; i++)
                {
                    if (children[i] is CTextBlock text && !text.Classes.Contains("ListMarker"))
                    {
                        var block = new PlanTextBlock(text);
                        children[i] = block;
                        _blocks.Add(block);
                    }
                    else
                    {
                        MakeSelectable(children[i]);
                    }
                }
                break;
            case Decorator decorator when decorator.Child is CTextBlock text && !text.Classes.Contains("ListMarker"):
                var only = new PlanTextBlock(text);
                decorator.Child = only;
                _blocks.Add(only);
                break;
            case Decorator decorator:
                MakeSelectable(decorator.Child);
                break;
            case ContentControl content when content.Content is Control inner:
                MakeSelectable(inner);
                break;
        }
    }

    private void OnBlockChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == SelectableTextBlock.SelectionStartProperty || e.Property == SelectableTextBlock.SelectionEndProperty)
            UpdateAddComment();
    }

    /// <summary>"Add Comment" 6 px under the bottom left of the passage selected, while the plan takes comments.</summary>
    private void UpdateAddComment()
    {
        _offer = null;
        if (CanComment)
        {
            for (var i = 0; i < _blocks.Count; i++)
            {
                var block = _blocks[i];
                if (block.SelectedRange is not { } range || block.SelectionBounds is not { } bounds) continue;
                var at = block.TranslatePoint(bounds.BottomLeft, _overlay);
                var corner = block.TranslatePoint(bounds.TopLeft, this);
                if (at == null || corner == null) continue;
                _offer = new Anchor(_plan!, i, range.Start, range.End, block.Words.Substring(range.Start, range.End - range.Start),
                    HeadingOf(i), new Rect(corner.Value, bounds.Size));
                Canvas.SetLeft(_addComment, at.Value.X);
                Canvas.SetTop(_addComment, at.Value.Y + AddCommentGap);
                break;
            }
        }
        _addComment.IsVisible = _offer != null;
    }

    /// <summary>The text of the nearest heading over block <paramref name="index"/> (itself, when it is a heading).</summary>
    private string? HeadingOf(int index)
    {
        for (var i = index; i >= 0; i--)
        {
            if (_blocks[i].IsHeading) return _blocks[i].Words.Trim();
        }
        return null;
    }

    private void RequestComment()
    {
        var offer = _offer;
        if (offer == null || !CanComment) return;
        _pending = offer;
        _addComment.IsVisible = false;
        CommentRequested?.Invoke(this, new PlanCommentRequest(offer.Plan, offer.Quote, offer.Selection));
    }

    /// <summary>Marks each comment's passage in its block, numbered in the plan's order; forgets the passages of comments taken away.</summary>
    private void Mark()
    {
        var plan = _plan;
        var comments = plan?.Comments;
        _anchors.RemoveAll(a => ReferenceEquals(a.Plan, plan) && (comments == null || IndexOf(comments, a.Comment) < 0)
                                || !ReferenceEquals(a.Plan, plan) && !ReferenceEquals(a.Plan, _tracked?.CurrentPlan));
        for (var i = 0; i < _blocks.Count; i++)
        {
            var marks = new List<PlanMark>();
            foreach (var anchor in _anchors)
            {
                if (anchor.Block != i || !ReferenceEquals(anchor.Plan, plan) || anchor.End > _blocks[i].Words.Length) continue;
                marks.Add(new PlanMark(anchor.Start, anchor.End, IndexOf(comments!, anchor.Comment) + 1, anchor.Comment!.Text));
            }
            _blocks[i].SetMarks(marks);
        }
        if (!CanComment) _addComment.IsVisible = false;
    }

    private static int IndexOf(IList<PlanComment> comments, PlanComment? comment)
    {
        for (var i = 0; i < comments.Count; i++)
        {
            if (ReferenceEquals(comments[i], comment)) return i;
        }
        return -1;
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

    /// <summary>A passage of a block of a plan: offered, asked about, or carrying a comment.</summary>
    private sealed class Anchor
    {
        public Anchor(PlanReview plan, int block, int start, int end, string quote, string? heading, Rect selection, PlanComment? comment = null)
        {
            Plan = plan;
            Block = block;
            Start = start;
            End = end;
            Quote = quote;
            Heading = heading;
            Selection = selection;
            Comment = comment;
        }

        public PlanReview Plan { get; }

        public int Block { get; }

        public int Start { get; }

        public int End { get; }

        public string Quote { get; }

        public string? Heading { get; }

        public Rect Selection { get; }

        public PlanComment? Comment { get; }

        public Anchor With(PlanComment comment) => new Anchor(Plan, Block, Start, End, Quote, Heading, Selection, comment);
    }
}
