using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The images attached to the reply box, like the extension's strip above the text: a pill each with a 12 px
/// thumbnail, the name (<c>image.png</c>, <c>image-2.png</c>… by position) and the size in pixels; the × that shows
/// with the pointer on the pill takes the image off; clicking the pill asks for the preview. Hidden when empty.
/// </summary>
internal sealed class AttachmentStrip : Border
{
    private static readonly StyledProperty<ChatStrings> StringsProperty =
        ClaudeChatView.StringsProperty.AddOwner<AttachmentStrip>();

    /// <summary>Thumbnails decoded once per image (the pills are rebuilt when the list changes), for a 12 px square.</summary>
    private static readonly ThumbnailCache Thumbnails = new ThumbnailCache(12, 12);

    private readonly StackPanel _pills = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
    private ObservableCollection<ChatImage>? _images;
    private bool _attached;

    public AttachmentStrip()
    {
        Child = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = _pills,
        };
        IsVisible = false;
    }

    /// <summary>The user clicked the × of an image.</summary>
    public event EventHandler<ChatImage>? RemoveRequested;

    /// <summary>The user clicked a pill: the image and its name.</summary>
    public event EventHandler<(ChatImage Image, string Name)>? OpenRequested;

    /// <summary>
    /// The images shown; the strip follows the collection's changes while it is in a window, and draws them again
    /// when it comes back to one.
    /// </summary>
    public ObservableCollection<ChatImage>? Images
    {
        get => _images;
        set
        {
            if (ReferenceEquals(value, _images)) return;
            if (_attached && _images != null) _images.CollectionChanged -= OnImagesChanged;
            _images = value;
            if (_attached && _images != null) _images.CollectionChanged += OnImagesChanged;
            if (_attached) Rebuild();
        }
    }

    private ChatStrings Strings => GetValue(StringsProperty) ?? ChatStrings.English;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        if (_images != null) _images.CollectionChanged += OnImagesChanged;
        Rebuild();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        // the collection outlives the strip: let go of its events, and draw nothing while Avalonia takes the tree apart
        if (_images != null) _images.CollectionChanged -= OnImagesChanged;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StringsProperty) Rebuild();
    }

    private void OnImagesChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void Rebuild()
    {
        _pills.Children.Clear();
        var images = _images;
        IsVisible = images != null && images.Count > 0;
        if (images == null) return;
        for (var i = 0; i < images.Count; i++) _pills.Children.Add(Pill(images[i], FileName(images[i], i)));
    }

    /// <summary>The extension's names: <c>image.png</c>, then <c>image-2.png</c>, <c>image-3.jpg</c>…</summary>
    internal static string FileName(ChatImage image, int index) =>
        (index == 0 ? "image" : "image-" + (index + 1)) + "." + Extension(image.MediaType);

    private static string Extension(string mediaType) => mediaType switch
    {
        "image/jpeg" => "jpg",
        "image/gif" => "gif",
        "image/webp" => "webp",
        "image/bmp" => "bmp",
        _ => "png",
    };

    private Control Pill(ChatImage image, string name)
    {
        var thumb = new Border { Child = new Image { Source = Thumbnails.Get(image), Stretch = Stretch.UniformToFill } };
        thumb.Classes.Add("ClaudeChatAttachmentThumb");
        // the name takes what is left and is cut with an ellipsis; the size always shows
        var label = new TextBlock { Text = name };
        label.Classes.Add("ClaudeChatAttachmentName");
        Grid.SetColumn(label, 1);
        var meta = new TextBlock { Text = image.Width + "×" + image.Height };
        meta.Classes.Add("ClaudeChatAttachmentMeta");
        Grid.SetColumn(meta, 2);
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Children = { thumb, label, meta } };
        content.Classes.Add("ClaudeChatAttachmentContent");

        var removeName = Strings.RemoveAttachment;
        var icon = new Path();
        icon.Classes.Add("ClaudeChatRemoveIcon");
        var iconBox = new Viewbox { Child = icon };
        iconBox.Classes.Add("ClaudeChatRemoveIcon");
        var remove = new Button { Content = iconBox };
        remove.Classes.Add("ClaudeChatButton");
        remove.Classes.Add("ClaudeChatAttachmentRemove");
        AutomationProperties.SetName(remove, removeName);
        ToolTip.SetTip(remove, removeName);
        remove.Click += (_, e) =>
        {
            e.Handled = true;
            RemoveRequested?.Invoke(this, image);
        };

        var pill = new Button { Content = new Panel { Children = { content, remove } } };
        pill.Classes.Add("ClaudeChatButton");
        pill.Classes.Add("ClaudeChatAttachment");
        ToolTip.SetTip(pill, name);
        pill.Click += (_, _) => OpenRequested?.Invoke(this, (image, name));
        return pill;
    }
}
