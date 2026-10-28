using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The box where the user writes a comment on a passage of the plan, as the extension's: 320 px wide, 8 px under the passage (over
/// it when there is no room under it), with the passage cut at <see cref="MaxQuoteLength"/> characters and "…", a field
/// ("Add your feedback.") and "Cancel" and "Add Comment". Enter adds and Shift+Enter breaks the line (in the touch layout Enter breaks
/// the line, and the button adds); an empty comment adds nothing. Esc closes it: the view sees to that, since the box is a layer over
/// the plan's document and under the sheets. It sits in the view over the list, as the document does, and draws only the box: the
/// rest of its room lets everything through. It closes by itself when the plan it writes about stops being the one under review.
/// </summary>
internal sealed class PlanCommentBox : Panel
{
    /// <summary>The longest passage the box shows whole.</summary>
    internal const int MaxQuoteLength = 120;

    /// <summary>The box's width, at most.</summary>
    internal const double BoxWidth = 320;

    /// <summary>The gap between the passage and the box.</summary>
    internal const double Gap = 8;

    private static readonly StyledProperty<ChatStrings> StringsProperty =
        ClaudeChatView.StringsProperty.AddOwner<PlanCommentBox>();

    private static readonly StyledProperty<ChatLayout> LayoutProperty =
        ClaudeChatView.LayoutProperty.AddOwner<PlanCommentBox>();

    private readonly Border _box = new Border();
    private readonly TextBlock _quote = new TextBlock();
    private readonly TextBox _field = new TextBox();
    private readonly Button _cancel = new Button();
    private readonly Button _add = new Button();
    private readonly TextBlock _cancelText = new TextBlock();
    private readonly TextBlock _addText = new TextBlock();

    private ChatConversation? _conversation;
    private ChatConversation? _tracked;
    private bool _attached;
    private PlanReview? _plan;
    private Rect _selection;

    public PlanCommentBox()
    {
        _quote.Classes.Add("ClaudeChatPlanCommentQuote");
        var quote = new Border { Child = _quote };
        quote.Classes.Add("ClaudeChatPlanCommentQuoteBar");
        _field.Classes.Add("ClaudeChatPlanCommentField");
        _field.AcceptsReturn = true;
        _field.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        _field.AddHandler(KeyDownEvent, OnFieldKeyDown, RoutingStrategies.Tunnel);
        _cancel.Content = _cancelText;
        _cancel.Classes.Add("ClaudeChatButton");
        _cancel.Classes.Add("ClaudeChatPlanCommentCancel");
        _cancel.Click += (_, _) => Close(cancelled: true);
        _add.Content = _addText;
        _add.Classes.Add("ClaudeChatButton");
        _add.Classes.Add("ClaudeChatPlanCommentAdd");
        _add.Click += (_, _) => Submit();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { _cancel, _add } };
        buttons.Classes.Add("ClaudeChatPlanCommentButtons");
        _box.Classes.Add("ClaudeChatPlanCommentBox");
        _box.HorizontalAlignment = HorizontalAlignment.Left;
        _box.VerticalAlignment = VerticalAlignment.Top;
        _box.Child = new StackPanel { Children = { quote, _field, buttons } };
        Children.Add(_box);
        IsVisible = false;
        Label();
    }

    /// <summary>Raised with the comment when "Add Comment" (or Enter) adds one; the box has closed.</summary>
    internal event EventHandler<string>? Submitted;

    /// <summary>Raised when the box closes, whatever closed it; the argument is true when no comment was added.</summary>
    internal event EventHandler<bool>? Closed;

    /// <summary>The conversation whose plan under review the box writes about.</summary>
    internal ChatConversation? Conversation
    {
        get => _conversation;
        set
        {
            if (ReferenceEquals(value, _conversation)) return;
            Close(cancelled: true);
            _conversation = value;
            if (_attached) Track(value);
        }
    }

    /// <summary>True while the box is on screen.</summary>
    internal bool IsOpen => IsVisible;

    /// <summary>The keyboard was in the box when it last closed.</summary>
    internal bool HadKeyboard { get; private set; }

    /// <summary>The plan the open box writes about.</summary>
    internal PlanReview? Plan => _plan;

    /// <summary>The passage as the box shows it.</summary>
    internal string QuoteText => _quote.Text ?? "";

    /// <summary>The field.</summary>
    internal TextBox Field => _field;

    /// <summary>Opens the box under the passage of <paramref name="request"/>, its field empty and with the keyboard.</summary>
    internal void Open(PlanCommentRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        _plan = request.Plan;
        _selection = request.Selection;
        _quote.Text = PlanReview.Clip(request.Quote, MaxQuoteLength);
        _field.Text = "";
        IsVisible = true;
        InvalidateArrange();
        _field.Focus(NavigationMethod.Tab);
    }

    /// <summary>Closes the box without a comment; false when it was not open.</summary>
    internal bool Cancel()
    {
        if (!IsVisible) return false;
        Close(cancelled: true);
        return true;
    }

    private ChatStrings Strings => GetValue(StringsProperty) ?? ChatStrings.English;

    private bool IsTouch => GetValue(LayoutProperty) == ChatLayout.Touch;

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
        if (change.Property == StringsProperty) Label();
        else if (change.Property == LayoutProperty) PseudoClasses.Set(":touch", IsTouch);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = Math.Max(0, Math.Min(BoxWidth, availableSize.Width - 2 * Gap));
        _box.Width = double.IsNaN(width) || double.IsInfinity(width) ? BoxWidth : width;
        _box.Measure(availableSize);
        return default;
    }

    /// <summary>8 px under the passage, from its left (kept in the room, 8 px from its sides); over it when there is no room under it.</summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = _box.DesiredSize;
        var x = Math.Max(0, Math.Min(_selection.Left, finalSize.Width - size.Width - Gap));
        var y = _selection.Bottom + Gap;
        if (y + size.Height > finalSize.Height - Gap)
        {
            var over = _selection.Top - Gap - size.Height;
            y = over >= 0 ? over : Math.Max(0, finalSize.Height - size.Height);
        }
        _box.Arrange(new Rect(new Point(x, y), size));
        return finalSize;
    }

    private void Track(ChatConversation? conversation)
    {
        if (!ReferenceEquals(conversation, _tracked))
        {
            if (_tracked != null) _tracked.PropertyChanged -= OnConversationChanged;
            _tracked = conversation;
            if (_tracked != null) _tracked.PropertyChanged += OnConversationChanged;
        }
        // out of the window, only the events go: coming back closes the box if its plan went meanwhile
        if (_attached) Update();
    }

    private void OnConversationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatConversation.CurrentPlan)) Update();
    }

    /// <summary>The plan it writes about is no longer the one under review (answered, cancelled, another conversation): it closes.</summary>
    private void Update()
    {
        if (IsVisible && !ReferenceEquals(_plan, _tracked?.CurrentPlan)) Close(cancelled: true);
    }

    private void OnFieldKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            Close(cancelled: true);
        }
        else if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && !IsTouch)
        {
            e.Handled = true; // Shift+Enter goes on to the field, which breaks the line
            Submit();
        }
    }

    /// <summary>Adds what the field says; an empty comment adds nothing, and the box stays.</summary>
    private void Submit()
    {
        var text = _field.Text;
        if (string.IsNullOrWhiteSpace(text)) return;
        Close(cancelled: false);
        Submitted?.Invoke(this, text!);
    }

    private void Close(bool cancelled)
    {
        if (!IsVisible) return;
        HadKeyboard = IsKeyboardFocusWithin;
        IsVisible = false;
        _field.Text = "";
        _plan = null;
        Closed?.Invoke(this, cancelled);
    }

    private void Label()
    {
        var strings = Strings;
        _field.Watermark = strings.PlanCommentPlaceholder;
        _cancelText.Text = strings.PlanCommentCancel;
        _addText.Text = strings.PlanAddComment;
    }
}
