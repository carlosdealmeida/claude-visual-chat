using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// What a user's message took, above its text as the extension's message attachments (2.1.309): the images'
/// thumbnails (<see cref="MessageImages"/>), then the files' pills (<see cref="MessageFiles"/>), 4 px apart in one row
/// that scrolls sideways, 6 px over the text. Hidden when the message took nothing.
/// </summary>
internal sealed class MessageAttachments : ScrollViewer
{
    public static readonly StyledProperty<IReadOnlyList<ChatImage>?> ImagesProperty =
        AvaloniaProperty.Register<MessageAttachments, IReadOnlyList<ChatImage>?>(nameof(Images));

    public static readonly StyledProperty<IReadOnlyList<ChatFile>?> FilesProperty =
        AvaloniaProperty.Register<MessageAttachments, IReadOnlyList<ChatFile>?>(nameof(Files));

    private readonly MessageImages _images = new MessageImages();
    private readonly MessageFiles _files = new MessageFiles();

    public MessageAttachments()
    {
        Classes.Add("ClaudeChatMessageAttachments");
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        // the thumbnails keep 4 px after each one, so the files come 4 px after the last
        Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { _images, _files } };
        IsVisible = false;
    }

    public IReadOnlyList<ChatImage>? Images
    {
        get => GetValue(ImagesProperty);
        set => SetValue(ImagesProperty, value);
    }

    public IReadOnlyList<ChatFile>? Files
    {
        get => GetValue(FilesProperty);
        set => SetValue(FilesProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(ScrollViewer);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ImagesProperty) _images.Images = Images;
        else if (change.Property == FilesProperty) _files.Files = Files;
        else return;
        IsVisible = Images is { Count: > 0 } || Files is { Count: > 0 };
    }
}
