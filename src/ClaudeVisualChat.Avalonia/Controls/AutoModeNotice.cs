using System;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// "Auto mode is enabled", the card on an empty conversation, like the extension's: a header with a bolt, the title and a ×
/// (which marks <see cref="ChatConversation.AutoModeNoticeSeen"/>), then what the mode does and, when the host opens links, a
/// "Learn more". It shows only while the mode is auto, nothing was seen, the list has no items and the view is at least 250 px
/// wide and 400 px high (<see cref="ViewSize"/>).
/// </summary>
internal sealed class AutoModeNotice : Border
{
    internal const string DocsUrl = "https://code.claude.com/docs/en/permission-modes";
    internal const double MinViewWidth = 250;
    internal const double MinViewHeight = 400;
    private const string AutoMode = "auto";

    private static readonly StyledProperty<ChatStrings> StringsProperty =
        ClaudeChatView.StringsProperty.AddOwner<AutoModeNotice>();

    private readonly TextBlock _title = new TextBlock();
    private readonly TextBlock _body = new TextBlock();
    private readonly TextBlock _learnText = new TextBlock();
    private readonly Button _learn;
    private readonly Button _close;

    private ChatConversation? _conversation;
    private ChatConversation? _tracked;
    private bool _attached;
    private Size _viewSize;

    public AutoModeNotice()
    {
        Classes.Add("ClaudeChatAutoNotice");
        var bolt = new Path();
        bolt.Classes.Add("ClaudeChatBoltIcon");
        _title.Classes.Add("ClaudeChatAutoNoticeTitle");
        _close = ChatBanner.CreateCloseButton();
        _close.Click += (_, _) =>
        {
            if (_tracked != null) _tracked.AutoModeNoticeSeen = true;
        };

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        header.Classes.Add("ClaudeChatAutoNoticeHeader");
        Grid.SetColumn(_title, 1);
        Grid.SetColumn(_close, 2);
        header.Children.Add(new Viewbox { Width = 16, Height = 16, Child = bolt, VerticalAlignment = VerticalAlignment.Center });
        header.Children.Add(_title);
        header.Children.Add(_close);

        _body.Classes.Add("ClaudeChatAutoNoticeBody");
        _learnText.Classes.Add("ClaudeChatAutoNoticeLinkText");
        _learn = new Button { Content = _learnText, HorizontalAlignment = HorizontalAlignment.Left };
        _learn.Classes.Add("ClaudeChatButton");
        _learn.Classes.Add("ClaudeChatAutoNoticeLink");
        _learn.Click += (_, _) => _tracked?.RequestLink(DocsUrl);

        var body = new StackPanel();
        body.Classes.Add("ClaudeChatAutoNoticeContent");
        body.Children.Add(_body);
        body.Children.Add(_learn);
        Child = new StackPanel { Children = { header, body } };
        IsVisible = false;
    }

    /// <summary>The conversation whose mode and items it follows.</summary>
    internal ChatConversation? Conversation
    {
        get => _conversation;
        set
        {
            _conversation = value;
            if (_attached) Track(value);
        }
    }

    /// <summary>The size of the view (not of the notice's own room): under 250 × 400 the notice hides.</summary>
    internal Size ViewSize
    {
        get => _viewSize;
        set
        {
            if (_viewSize == value) return;
            _viewSize = value;
            if (_attached) Update();
        }
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
        if (change.Property == StringsProperty) Update();
    }

    private void Track(ChatConversation? conversation)
    {
        if (!ReferenceEquals(conversation, _tracked))
        {
            if (_tracked != null)
            {
                _tracked.PropertyChanged -= OnConversationChanged;
                _tracked.Items.CollectionChanged -= OnItemsChanged;
                _tracked.HostHandlersChanged -= OnHostHandlersChanged;
            }
            _tracked = conversation;
            if (_tracked != null)
            {
                _tracked.PropertyChanged += OnConversationChanged;
                _tracked.Items.CollectionChanged += OnItemsChanged;
                _tracked.HostHandlersChanged += OnHostHandlersChanged;
            }
        }
        // out of the window, only the events go: coming back draws it again
        if (_attached) Update();
    }

    private void OnConversationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatConversation.PermissionMode) || e.PropertyName == nameof(ChatConversation.AutoModeNoticeSeen))
            Update();
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Update();

    private void OnHostHandlersChanged(object? sender, EventArgs e) => Update();

    private void Update()
    {
        var conversation = _tracked;
        var visible = conversation != null
            && conversation.PermissionMode == AutoMode
            && !conversation.AutoModeNoticeSeen
            && conversation.Items.Count == 0
            && _viewSize.Width >= MinViewWidth
            && _viewSize.Height >= MinViewHeight;
        IsVisible = visible;
        if (!visible) return;

        var strings = Strings;
        _title.Text = strings.AutoModeEnabled;
        _body.Text = strings.AutoModeNoticeBody;
        _learnText.Text = strings.LearnMore;
        _learn.IsVisible = conversation!.CanOpenLinks;
        AutomationProperties.SetName(_learn, strings.LearnMore);
        var dismiss = strings.Dismiss;
        AutomationProperties.SetName(_close, dismiss);
        ToolTip.SetTip(_close, dismiss);
    }
}
