using System.Collections.Specialized;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The messages sent while Claude works (<see cref="ChatConversation.QueuedMessages"/>), at the end of the list under "Working…", as
/// the extension's held prompts: each in the bubble of a sent message, without its time, its date line or any action, and with the
/// line "Ran at once, while Claude was working." under it when the claude said so. They are not in the list, so none is pinned at
/// the top. A screen reader hears "You, queued" for each and "Message queued." while there is one. Hidden, taking no room, while
/// the queue is empty.
/// </summary>
internal sealed class QueuedMessageList : StackPanel
{
    private static readonly StyledProperty<ChatStrings> StringsProperty =
        ClaudeChatView.StringsProperty.AddOwner<QueuedMessageList>();

    private ChatConversation? _conversation;
    private ChatConversation? _tracked;
    private bool _attached;

    public QueuedMessageList()
    {
        Classes.Add("ClaudeChatQueued");
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
        IsVisible = false;
    }

    /// <summary>The conversation whose queue it shows.</summary>
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
        Track(null); // the conversation outlives the view: let go of its queue
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StringsProperty && _attached) Update(); // out of the window, nothing is drawn
    }

    private void Track(ChatConversation? conversation)
    {
        if (!ReferenceEquals(conversation, _tracked))
        {
            if (_tracked != null) _tracked.QueuedMessages.CollectionChanged -= OnQueueChanged;
            _tracked = conversation;
            if (_tracked != null) _tracked.QueuedMessages.CollectionChanged += OnQueueChanged;
        }
        // out of the window, only the events go: Avalonia is taking the tree apart, and coming back draws it again
        if (_attached) Update();
    }

    private void OnQueueChanged(object? sender, NotifyCollectionChangedEventArgs e) => Update();

    /// <summary>The rows again, from the queue as it is: a few messages at most, and a held message's notice comes as a change of the queue.</summary>
    private void Update()
    {
        Children.Clear();
        var strings = Strings;
        var template = this.FindResource("ClaudeChat.QueuedMessageTemplate") as IDataTemplate;
        if (_tracked != null)
        {
            foreach (var item in _tracked.QueuedMessages)
            {
                var row = new StackPanel();
                row.Children.Add(new ContentControl { Content = item, ContentTemplate = template });
                if (item.QueuedNotice != null)
                {
                    var notice = new SelectableTextBlock { Text = item.QueuedNotice };
                    notice.Classes.Add("ClaudeChatMetaLine");
                    row.Children.Add(notice);
                }
                AutomationProperties.SetName(row, strings.YouQueued);
                Children.Add(row);
            }
        }
        var any = Children.Count > 0;
        IsVisible = any;
        AutomationProperties.SetName(this, any ? strings.MessageQueued : null);
    }
}
