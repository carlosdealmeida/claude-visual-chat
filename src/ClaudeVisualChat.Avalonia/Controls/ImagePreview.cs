using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// An attached image over the whole view, like the extension's preview: a dark backdrop, the image at its size up to
/// 90 % of the view with 8 px corners, and a round close button on its corner. Esc, the button or a click on the
/// backdrop closes it.
/// </summary>
internal sealed class ImagePreview : Border
{
    private static readonly StyledProperty<ChatStrings> StringsProperty =
        ClaudeChatView.StringsProperty.AddOwner<ImagePreview>();

    private readonly Image _image = new Image { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
    private readonly Border _frame;
    private readonly Button _close;
    private Bitmap? _bitmap;

    public ImagePreview()
    {
        var clip = new Border { Child = _image };
        clip.Classes.Add("ClaudeChatPreviewClip");
        _frame = new Border { Child = clip };
        _frame.Classes.Add("ClaudeChatPreviewFrame");

        var icon = new Path();
        icon.Classes.Add("ClaudeChatRemoveIcon");
        var iconBox = new Viewbox { Child = icon };
        iconBox.Classes.Add("ClaudeChatRemoveIcon");
        _close = new Button { Content = iconBox };
        _close.Classes.Add("ClaudeChatButton");
        _close.Classes.Add("ClaudeChatPreviewClose");
        _close.Click += (_, _) => Hide();

        Child = new Panel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _frame, _close },
        };
        IsVisible = false;
        Focusable = true;
        Label();
    }

    /// <summary>Raised when the preview closes.</summary>
    public event EventHandler? Closed;

    /// <summary>Shows the image, named for screen readers, and puts the focus on the close button.</summary>
    public void Show(ChatImage image, string name)
    {
        Release();
        try
        {
            using var stream = new System.IO.MemoryStream(image.ToBytes(), false);
            _bitmap = new Bitmap(stream);
        }
        catch (Exception)
        {
            return; // nothing to show
        }
        _image.Source = _bitmap;
        AutomationProperties.SetName(this, name);
        IsVisible = true;
        _close.Focus();
    }

    public void Hide()
    {
        if (!IsVisible) return;
        IsVisible = false;
        Release();
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void Release()
    {
        _image.Source = null;
        _bitmap?.Dispose();
        _bitmap = null;
    }

    private ChatStrings Strings => GetValue(StringsProperty) ?? ChatStrings.English;

    private void Label()
    {
        AutomationProperties.SetName(_close, Strings.Close);
        ToolTip.SetTip(_close, Strings.Close);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StringsProperty) Label();
        else if (change.Property == BoundsProperty)
        {
            // the image: 90 % of the view at most
            var size = Bounds.Size;
            _frame.MaxWidth = size.Width * 0.9;
            _frame.MaxHeight = size.Height * 0.9;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key != Key.Escape || !IsVisible) return;
        e.Handled = true;
        Hide();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        // a click on the backdrop, not on the image
        if (!ReferenceEquals(e.Source, this)) return;
        e.Handled = true;
        Hide();
    }
}
