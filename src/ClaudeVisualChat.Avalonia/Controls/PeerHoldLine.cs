using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The notice of a message from another session that is held, released or dropped (<see cref="PeerHoldItem"/>): a meta line (italic,
/// secondary, selectable) and, after it, when the item points to the user settings file and the host listens to
/// <see cref="ChatConversation.UserSettingsFileRequested"/>, a space and the link "Open user settings file", underlined, in the
/// color of the text, with the hand over it. A click asks the host. The line follows its item and the host's handlers only while it
/// is in the window, so a row of the list reused for another item shows that item's notice.
/// </summary>
internal sealed class PeerHoldLine : SelectableTextBlock
{
    private static readonly StyledProperty<ChatStrings> StringsProperty = ClaudeChatView.StringsProperty.AddOwner<PeerHoldLine>();

    private static readonly Cursor HandCursor = new Cursor(StandardCursorType.Hand);

    private readonly Run _body = new Run();
    private readonly Run _gap = new Run(" ");
    private readonly TextBlock _link = new TextBlock { TextDecorations = Avalonia.Media.TextDecorations.Underline, Cursor = HandCursor };
    private readonly InlineUIContainer _linkHost;

    private PeerHoldItem? _item;
    private ChatConversation? _host;
    private bool _attached;
    private bool _pressed;

    public PeerHoldLine()
    {
        Classes.Add("ClaudeChatMetaLine");
        _linkHost = new InlineUIContainer(_link);
        _link.PointerPressed += OnLinkPressed;
        _link.PointerReleased += OnLinkReleased;
        _link.PointerCaptureLost += (_, _) => _pressed = false;
    }

    /// <summary>The notice's text, before the link.</summary>
    internal string Body => _body.Text ?? "";

    /// <summary>The link to the user settings file (in the line only while <see cref="IsLinkShown"/>).</summary>
    internal TextBlock Link => _link;

    /// <summary>Whether the link follows the text: the item points to the user settings file and the host listens.</summary>
    internal bool IsLinkShown => Inlines != null && Inlines.Contains(_linkHost);

    private ChatStrings Strings => GetValue(StringsProperty) ?? ChatStrings.English;

    /// <summary>Styled as a SelectableTextBlock: the theme's <c>SelectableTextBlock.ClaudeChatMetaLine</c>.</summary>
    protected override Type StyleKeyOverride => typeof(SelectableTextBlock);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Track(DataContext as PeerHoldItem);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        Track(null); // the conversation outlives the view: let go of its events
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_attached) Track(DataContext as PeerHoldItem);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_attached && (change.Property == StringsProperty || change.Property == ForegroundProperty)) Update();
    }

    private void Track(PeerHoldItem? item)
    {
        _item = item;
        var wanted = item != null && _attached ? this.FindAncestorOfType<ClaudeChatView>()?.Conversation : null;
        if (!ReferenceEquals(wanted, _host))
        {
            if (_host != null) _host.HostHandlersChanged -= OnHostHandlersChanged;
            _host = wanted;
            if (_host != null) _host.HostHandlersChanged += OnHostHandlersChanged;
        }
        if (_attached) Update();
    }

    private void OnHostHandlersChanged(object? sender, EventArgs e) => Update();

    private void Update()
    {
        var item = _item;
        if (item == null) return;
        var showLink = item.PointsToUserSettings && _host?.CanOpenUserSettingsFile == true;
        _body.Text = item.Text;
        _link.Text = Strings.PeerOpenSettings;
        _link.Foreground = Foreground; // the link is in the color of the text
        var inlines = Inlines;
        if (inlines == null) return;
        if (inlines.Count == 0) inlines.Add(_body);
        var shown = inlines.Contains(_linkHost);
        if (showLink && !shown)
        {
            inlines.Add(_gap);
            inlines.Add(_linkHost);
        }
        else if (!showLink && shown)
        {
            inlines.Remove(_linkHost);
            inlines.Remove(_gap);
        }
    }

    private void OnLinkPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_link).Properties.IsLeftButtonPressed) return;
        _pressed = true;
        e.Handled = true; // the press is the link's, not the start of a selection
    }

    private void OnLinkReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_pressed) return;
        _pressed = false;
        e.Handled = true;
        _host?.RequestUserSettingsFile();
    }
}
