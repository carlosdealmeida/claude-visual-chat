using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The Remote Control chip of the reply box's bar, like the extension's: an 18 px round pill that says "Remote
/// Control". Connected, it is blue (class <c>on</c>) and a click asks the host to open the session's link (class
/// <c>link</c> while there is one); connecting, blue too, and a click does nothing; failed, the grey pill (the
/// banner over the reply box says why). Off, hidden, taking no room. The tooltip tells the state.
/// </summary>
internal sealed class RemoteControlChip : Button
{
    private static readonly StyledProperty<ChatStrings> StringsProperty =
        ClaudeChatView.StringsProperty.AddOwner<RemoteControlChip>();

    private readonly TextBlock _label = new TextBlock();

    private ChatConversation? _conversation;
    private ChatConversation? _tracked;
    private bool _attached;

    public RemoteControlChip()
    {
        Classes.Add("ClaudeChatButton");
        Classes.Add("ClaudeChatRemoteControl");
        Content = _label;
        Click += (_, _) =>
        {
            if (_tracked != null && _tracked.RemoteControlStatus == ChatRemoteControlStatus.Connected) _tracked.OpenRemoteControl();
        };
        IsVisible = false;
    }

    /// <summary>The conversation whose Remote Control it shows.</summary>
    internal ChatConversation? Conversation
    {
        get => _conversation;
        set
        {
            _conversation = value;
            if (_attached) Track(value);
        }
    }

    private ChatStrings Strings => GetValue(StringsProperty) ?? ChatStrings.English;

    /// <summary>Styled as a Button: the theme's <c>Button.ClaudeChatButton</c> template and the chip's class.</summary>
    protected override System.Type StyleKeyOverride => typeof(Button);

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
        Update();
    }

    private void OnConversationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatConversation.RemoteControlStatus)
            || e.PropertyName == nameof(ChatConversation.RemoteControlUrl)
            || e.PropertyName == nameof(ChatConversation.RemoteControlError))
            Update();
    }

    private void Update()
    {
        var strings = Strings;
        var status = _tracked?.RemoteControlStatus ?? ChatRemoteControlStatus.Off;
        var link = status == ChatRemoteControlStatus.Connected && !string.IsNullOrEmpty(_tracked?.RemoteControlUrl);
        IsVisible = status != ChatRemoteControlStatus.Off;
        _label.Text = strings.RemoteControl;
        Classes.Set("on", status == ChatRemoteControlStatus.Connected || status == ChatRemoteControlStatus.Connecting);
        Classes.Set("link", link);
        Focusable = link; // the extension's chip is a link only when connected; otherwise a status, out of the tab order
        var tip = status switch
        {
            ChatRemoteControlStatus.Connected => strings.RemoteControlActive,
            ChatRemoteControlStatus.Connecting => strings.RemoteControlConnecting,
            ChatRemoteControlStatus.Error => strings.FormatRemoteControlFailedHint(_tracked?.RemoteControlError),
            _ => null,
        };
        ToolTip.SetTip(this, tip);
        // connected, the link is named by its text; otherwise the state is what a screen reader should tell
        AutomationProperties.SetName(this, status == ChatRemoteControlStatus.Connected ? strings.RemoteControl : tip);
    }
}
