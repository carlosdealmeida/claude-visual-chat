using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The images of a user's message, under its text: a thumbnail each, 64 px high at most and in proportion (never
/// larger than the image), 4 px corners, 4 px apart, wrapping. A click shows the image over the view, in the
/// preview of the reply box's images; also while the view is read-only. Hidden when there are none. The pictures are
/// decoded off the UI thread, once per image; each thumbnail has its size before its picture comes.
/// </summary>
internal sealed class MessageImages : WrapPanel
{
    /// <summary>The largest a thumbnail is drawn.</summary>
    internal const double ThumbnailMaxWidth = 256;
    internal const double ThumbnailMaxHeight = 64;

    public static readonly StyledProperty<IReadOnlyList<ChatImage>?> ImagesProperty =
        AvaloniaProperty.Register<MessageImages, IReadOnlyList<ChatImage>?>(nameof(Images));

    /// <summary>Decoded once per image, at most twice the size they are drawn at.</summary>
    internal static readonly ThumbnailCache Thumbnails = new ThumbnailCache(ThumbnailMaxWidth, ThumbnailMaxHeight);

    /// <summary>Counts the rebuilds: a decode that ends after another one is for images no longer shown.</summary>
    private int _generation;

    public MessageImages()
    {
        Classes.Add("ClaudeChatMessageImages");
        IsVisible = false;
    }

    public IReadOnlyList<ChatImage>? Images
    {
        get => GetValue(ImagesProperty);
        set => SetValue(ImagesProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(WrapPanel);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ImagesProperty) Rebuild();
    }

    private void Rebuild()
    {
        _generation++; // a decode still running for the images shown before ends nowhere
        Children.Clear();
        var images = Images;
        if (images != null)
        {
            for (var i = 0; i < images.Count; i++)
            {
                var thumbnail = Thumbnail(images[i], AttachmentStrip.FileName(images[i], i));
                if (thumbnail != null) Children.Add(thumbnail);
            }
        }
        IsVisible = Children.Count > 0;
    }

    private Control? Thumbnail(ChatImage image, string name)
    {
        var (width, height) = ThumbnailCache.Fit(image.Width, image.Height, ThumbnailMaxWidth, ThumbnailMaxHeight);
        if (width <= 0 || height <= 0) return null; // not an image it can read
        var picture = new Image
        {
            Width = width, // the size is known before the picture: the list does not move when it comes
            Height = height,
            Stretch = Stretch.Fill, // the bitmap has the image's proportion, rounded to whole pixels
        };
        // decoded off the UI thread (a history may hold many screenshots); shown if this control still shows that image
        var decoding = Thumbnails.Load(image);
        if (decoding.IsCompleted) picture.Source = Result(decoding);
        else
        {
            var generation = _generation;
            decoding.ContinueWith(done => Dispatcher.UIThread.Post(() =>
            {
                if (generation == _generation) picture.Source = Result(done);
            }), TaskScheduler.Default);
        }
        var clip = new Border { Child = picture };
        clip.Classes.Add("ClaudeChatMessageImageClip");
        var button = new Button { Content = clip };
        button.Classes.Add("ClaudeChatButton");
        button.Classes.Add("ClaudeChatMessageImage");
        AutomationProperties.SetName(button, name);
        ToolTip.SetTip(button, name);
        button.Click += (_, _) => button.FindAncestorOfType<ClaudeChatView>()?.ShowPreview(image, name);
        return button;
    }

    private static Bitmap? Result(Task<Bitmap?> decoding) =>
        decoding.Status == TaskStatus.RanToCompletion ? decoding.Result : null;
}
