using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// A banner over the reply box, like the extension's: a line of text and a × at its right, tucked under the box (its
/// last 8 px go under it), in the blue tint or, with class <c>error</c>, in the error color. Hidden while a permission
/// request shows, as the extension hides it under another popup. Each kind says what it shows and what its × does.
/// </summary>
internal abstract class ChatBanner : Border
{
    private static readonly StyledProperty<ChatStrings> StringsProperty =
        ClaudeChatView.StringsProperty.AddOwner<ChatBanner>();

    private readonly TextBlock _text = new TextBlock();
    private readonly Button _close;

    private ChatConversation? _conversation;
    private ChatConversation? _tracked;
    private bool _attached;

    protected ChatBanner()
    {
        Classes.Add("ClaudeChatBanner");
        _text.Classes.Add("ClaudeChatBannerText");
        _close = CreateCloseButton();
        _close.Click += (_, _) =>
        {
            if (_tracked != null) Close(_tracked);
        };
        DockPanel.SetDock(_close, Dock.Right);
        Child = new DockPanel { Children = { _close, _text } };
        IsVisible = false;
    }

    /// <summary>The × of a banner or notice: a 12 px icon in the banner's color (44 px square in the touch layout).</summary>
    internal static Button CreateCloseButton()
    {
        var icon = new Path();
        icon.Classes.Add("ClaudeChatRemoveIcon");
        var iconBox = new Viewbox { Child = icon };
        iconBox.Classes.Add("ClaudeChatRemoveIcon");
        var close = new Button { Content = iconBox };
        close.Classes.Add("ClaudeChatButton");
        close.Classes.Add("ClaudeChatBannerClose");
        return close;
    }

    /// <summary>The conversation whose state it shows.</summary>
    internal ChatConversation? Conversation
    {
        get => _conversation;
        set
        {
            _conversation = value;
            if (_attached) Track(value);
        }
    }

    /// <summary>The text to show, or null for none (the banner hides); <paramref name="error"/> for the red one.</summary>
    protected abstract string? Describe(ChatConversation conversation, ChatStrings strings, out bool error);

    /// <summary>The name and tooltip of the ×.</summary>
    protected abstract string CloseName(ChatStrings strings);

    /// <summary>What the × does.</summary>
    protected abstract void Close(ChatConversation conversation);

    /// <summary>True when a change of this property of the conversation may change the banner.</summary>
    protected abstract bool Concerns(string? propertyName);

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
        if (change.Property == StringsProperty) Update();
    }

    private void Track(ChatConversation? conversation)
    {
        if (!ReferenceEquals(conversation, _tracked))
        {
            if (_tracked != null) _tracked.PropertyChanged -= OnConversationChanged;
            _tracked = conversation;
            if (_tracked != null) _tracked.PropertyChanged += OnConversationChanged;
        }
        // out of the window, only the events go: Avalonia is taking the tree apart, and coming back draws it again
        if (_attached) Update();
    }

    private void OnConversationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatConversation.CurrentPermission) || Concerns(e.PropertyName)) Update();
    }

    private void Update()
    {
        var strings = Strings;
        var error = false;
        var conversation = _tracked;
        // a permission request on the screen covers it
        var text = conversation == null || conversation.CurrentPermission != null
            ? null
            : Describe(conversation, strings, out error);
        IsVisible = text != null;
        _text.Text = text;
        Classes.Set("error", error);
        var name = CloseName(strings);
        AutomationProperties.SetName(_close, name);
        ToolTip.SetTip(_close, name);
    }
}

/// <summary>
/// The browser's banner: connecting, connected, no browser, or its error (red); the × disconnects the browser
/// (<see cref="ChatConversation.DisconnectBrowser"/>). Hidden while the browser is off.
/// </summary>
internal sealed class BrowserBanner : ChatBanner
{
    protected override string? Describe(ChatConversation conversation, ChatStrings strings, out bool error)
    {
        error = conversation.BrowserStatus == ChatBrowserStatus.Error;
        return conversation.BrowserStatus switch
        {
            ChatBrowserStatus.Connecting => strings.BrowserConnecting,
            ChatBrowserStatus.Connected => strings.BrowserConnected,
            ChatBrowserStatus.NoBrowser => strings.BrowserNoBrowsers,
            ChatBrowserStatus.Error => strings.FormatBrowserFailed(conversation.BrowserError),
            _ => null,
        };
    }

    protected override string CloseName(ChatStrings strings) => strings.DisconnectBrowser;

    protected override void Close(ChatConversation conversation) => conversation.DisconnectBrowser();

    protected override bool Concerns(string? propertyName) =>
        propertyName == nameof(ChatConversation.BrowserStatus) || propertyName == nameof(ChatConversation.BrowserError);
}

/// <summary>
/// The red banner of a Remote Control error; the × turns Remote Control off (the <c>remote-control off</c> command).
/// As in the extension, the browser's banner takes its place while the browser is not off.
/// </summary>
internal sealed class RemoteControlBanner : ChatBanner
{
    protected override string? Describe(ChatConversation conversation, ChatStrings strings, out bool error)
    {
        error = true;
        if (conversation.RemoteControlStatus != ChatRemoteControlStatus.Error || conversation.BrowserStatus != ChatBrowserStatus.Off)
            return null;
        return strings.FormatRemoteControlFailed(conversation.RemoteControlError);
    }

    protected override string CloseName(ChatStrings strings) => strings.Dismiss;

    protected override void Close(ChatConversation conversation) => conversation.InvokeCommand("remote-control", "off");

    protected override bool Concerns(string? propertyName) =>
        propertyName == nameof(ChatConversation.RemoteControlStatus)
        || propertyName == nameof(ChatConversation.RemoteControlError)
        || propertyName == nameof(ChatConversation.BrowserStatus);
}
