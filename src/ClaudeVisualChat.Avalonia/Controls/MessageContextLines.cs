using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The lines under a message for what the IDE sent along with it (<see cref="UserMessageItem.Context"/>): "Terminal output: build",
/// "Browser tab: example.com/docs", "Browser instructions", "Selected code: Program.cs#3-9". Each is one italic line in the secondary
/// color, like a meta line, with a chevron of 16 px, and opens to an italic label over the content, or over "(none)" when the block held
/// nothing. Whether a line is open is the line's own (<see cref="MessageContext.IsExpanded"/>), so a row of the list reused for another
/// message shows that message's lines. It holds no event of its item: coming back into the window draws it again.
/// </summary>
internal sealed class MessageContextLines : StackPanel
{
    private static readonly StyledProperty<ChatStrings> StringsProperty = ClaudeChatView.StringsProperty.AddOwner<MessageContextLines>();

    private bool _attached;

    public MessageContextLines()
    {
        Classes.Add("ClaudeChatContextLines");
    }

    private ChatStrings Strings => GetValue(StringsProperty) ?? ChatStrings.English;

    /// <summary>Styled as a StackPanel.</summary>
    protected override Type StyleKeyOverride => typeof(StackPanel);

    /// <summary>The heads of the lines, in the order of the message.</summary>
    internal ToggleButton[] Heads
    {
        get
        {
            var heads = new ToggleButton[Children.Count];
            for (var i = 0; i < heads.Length; i++) heads[i] = (ToggleButton)((Panel)Children[i]).Children[0];
            return heads;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Update();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_attached) Update();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StringsProperty && _attached) Update();
    }

    private void Update()
    {
        Children.Clear();
        if (!(DataContext is UserMessageItem item)) return;
        foreach (var line in item.Context) Children.Add(Row(line, Strings));
    }

    private static Panel Row(MessageContext line, ChatStrings strings)
    {
        var head = new ToggleButton { IsChecked = line.IsExpanded, HorizontalAlignment = HorizontalAlignment.Left };
        head.Classes.Add("ClaudeChatDisclosure");
        head.Classes.Add("ClaudeChatContextHead");
        var label = new TextBlock { Text = line.Label };
        label.Classes.Add("ClaudeChatContextLabel");
        var chevron = new Path();
        chevron.Classes.Add("ClaudeChatChevron");
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Classes.Add("ClaudeChatContextLine");
        content.Children.Add(label);
        content.Children.Add(chevron);
        head.Content = content;
        AutomationProperties.SetName(head, line.Label);

        var row = new StackPanel();
        row.Children.Add(head);
        // what opens is drawn only while the line is open: a long output costs the list nothing while it is closed
        if (line.IsExpanded) row.Children.Add(Body(line, strings));
        head.Click += (_, _) =>
        {
            line.IsExpanded = head.IsChecked == true;
            if (row.Children.Count > 1) row.Children.RemoveAt(1);
            if (line.IsExpanded) row.Children.Add(Body(line, strings));
        };
        return row;
    }

    private static Control Body(MessageContext line, ChatStrings strings)
    {
        var body = new StackPanel();
        body.Classes.Add("ClaudeChatContextBody");
        var caption = new TextBlock { Text = strings.ContextCaption(line.Kind) };
        caption.Classes.Add("ClaudeChatContextCaption");
        body.Children.Add(caption);
        if (line.Content.Length == 0)
        {
            var none = new TextBlock { Text = strings.ContextNone };
            none.Classes.Add("ClaudeChatContextCaption");
            body.Children.Add(none);
        }
        else
        {
            var text = new SelectableTextBlock { Text = ChatText.Clip(line.Content, ChatConverters.MaxTextChars) };
            text.Classes.Add("ClaudeChatContextText");
            body.Children.Add(text);
        }
        return body;
    }
}
