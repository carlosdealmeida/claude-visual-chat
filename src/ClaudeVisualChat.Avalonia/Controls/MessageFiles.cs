using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The files of a user's message, in its attachments row (<see cref="MessageAttachments"/>) after the images: a pill
/// each (the reply box's frame, without the ×), 4 px apart. A pill shows the size when it is known (not for a file
/// read back from a session file) and the upload while the host sends the file; its tooltip names the file on the
/// claude's machine once known. Hidden when there are none.
/// </summary>
internal sealed class MessageFiles : StackPanel
{
    public static readonly StyledProperty<IReadOnlyList<ChatFile>?> FilesProperty =
        AvaloniaProperty.Register<MessageFiles, IReadOnlyList<ChatFile>?>(nameof(Files));

    public MessageFiles()
    {
        Classes.Add("ClaudeChatMessageFiles");
        Orientation = Orientation.Horizontal;
        Spacing = 4;
        VerticalAlignment = VerticalAlignment.Top;
        IsVisible = false;
    }

    public IReadOnlyList<ChatFile>? Files
    {
        get => GetValue(FilesProperty);
        set => SetValue(FilesProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(StackPanel);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FilesProperty) Rebuild();
    }

    private void Rebuild()
    {
        Children.Clear();
        var files = Files;
        if (files != null)
        {
            foreach (var file in files) Children.Add(Pill(file));
        }
        IsVisible = Children.Count > 0;
    }

    private static Control Pill(ChatFile file)
    {
        var pill = new Border();
        pill.Child = new FileChipContent(file, pill, null);
        pill.Classes.Add("ClaudeChatMessageFile");
        AutomationProperties.SetName(pill, file.Name);
        return pill;
    }
}
