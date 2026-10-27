using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The "N agents" chip of the reply box's bar, like the extension's: the model chip's round pill with a 6 px dot, 4 px
/// before how many agents are at work (<see cref="ChatConversation.RunningAgents"/>, as the extension counts them:
/// "0 agents" while none is). The dot lights up (class <c>running</c>) while one is. A click opens the Agent map
/// (<see cref="ChatConversation.ShowAgents"/>). As the extension's (2.1.309), it shows once the conversation has an agent
/// (<see cref="ChatConversation.Agents"/>, running or not); hidden, taking no room, before. In the touch layout (pseudo-class
/// <c>:touch</c>) it says "Agents · 2 running" while any is at work, else "Agents · 5" (all of them), and sits at the end of
/// the list instead of in the bar.
/// </summary>
internal sealed class AgentsChip : Button
{
    private static readonly StyledProperty<ChatStrings> StringsProperty =
        ClaudeChatView.StringsProperty.AddOwner<AgentsChip>();

    private static readonly StyledProperty<ChatLayout> LayoutProperty =
        ClaudeChatView.LayoutProperty.AddOwner<AgentsChip>();

    private readonly TextBlock _label = new TextBlock();

    private ChatConversation? _conversation;
    private ChatConversation? _tracked;
    private bool _attached;

    public AgentsChip()
    {
        Classes.Add("ClaudeChatButton");
        Classes.Add("ClaudeChatAgentsChip");
        var dot = new Ellipse();
        dot.Classes.Add("ClaudeChatAgentsChipDot");
        Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { dot, _label } };
        Click += (_, _) => _tracked?.ShowAgents();
        IsVisible = false;
    }

    /// <summary>The conversation whose agents it counts.</summary>
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
        if (change.Property == LayoutProperty) PseudoClasses.Set(":touch", GetValue(LayoutProperty) == ChatLayout.Touch);
        if ((change.Property == StringsProperty || change.Property == LayoutProperty) && _attached) Update(); // out of the window, nothing is drawn
    }

    private void Track(ChatConversation? conversation)
    {
        if (!ReferenceEquals(conversation, _tracked))
        {
            if (_tracked != null)
            {
                _tracked.PropertyChanged -= OnConversationChanged;
                _tracked.Agents.CollectionChanged -= OnAgentsChanged;
            }
            _tracked = conversation;
            if (_tracked != null)
            {
                _tracked.PropertyChanged += OnConversationChanged;
                _tracked.Agents.CollectionChanged += OnAgentsChanged;
            }
        }
        // out of the window, only the events go: Avalonia is taking the tree apart, and coming back draws it again
        if (_attached) Update();
    }

    private void OnConversationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatConversation.RunningAgents)) Update();
    }

    private void OnAgentsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Update();

    private void Update()
    {
        var strings = Strings;
        var running = _tracked?.RunningAgents ?? 0;
        IsVisible = _tracked != null && _tracked.Agents.Count > 0;
        _label.Text = GetValue(LayoutProperty) == ChatLayout.Touch
            ? strings.FormatAgentsChipTouch(running, _tracked?.Agents.Count ?? 0)
            : strings.FormatAgentsChip(running);
        Classes.Set("running", running > 0);
        ToolTip.SetTip(this, strings.AgentMapTitle);
        AutomationProperties.SetName(this, _label.Text + " · " + strings.AgentMapTitle);
    }
}
