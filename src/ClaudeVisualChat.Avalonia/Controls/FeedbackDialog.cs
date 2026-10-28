using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace ClaudeVisualChat.Controls;

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
        Field.MaxLength = ChatConversation.MaxFeedbackLength; // what goes to the host stops there anyway
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
