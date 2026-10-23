using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Threading;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// What a file's chip shows, as the extension's attachment pill does for a document: the type's icon (12 px, in the
/// secondary color), the name cut with an ellipsis, and the size in KB or MB; while the host sends the file, the
/// percentage instead of the size, and the chip's × (in the reply box) hides. It follows the file while it is in a
/// window, and lets go of it when taken out, leaving the tree alone.
/// </summary>
internal sealed class FileChipContent : Grid
{
    private static readonly StyledProperty<ChatStrings> StringsProperty =
        ClaudeChatView.StringsProperty.AddOwner<FileChipContent>();

    private readonly ChatFile _file;
    private readonly Control _frame;
    private readonly Control? _remove;
    private readonly TextBlock _meta;
    private bool _attached;

    /// <param name="file">The file shown.</param>
    /// <param name="frame">The chip, whose tooltip names the file (its path on the claude's machine, once known).</param>
    /// <param name="remove">The chip's ×, hidden while the file is being sent; null for a chip without one.</param>
    public FileChipContent(ChatFile file, Control frame, Control? remove)
    {
        _file = file;
        _frame = frame;
        _remove = remove;
        ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto");
        Classes.Add("ClaudeChatAttachmentContent");

        var icon = new Path();
        icon.Classes.Add("ClaudeChatFileIcon");
        icon.Classes.Add(KindClass(file.Kind));
        var iconBox = new Viewbox { Child = icon };
        iconBox.Classes.Add("ClaudeChatFileIcon");
        var name = new TextBlock { Text = file.Name };
        name.Classes.Add("ClaudeChatAttachmentName");
        SetColumn(name, 1);
        _meta = new TextBlock();
        _meta.Classes.Add("ClaudeChatAttachmentMeta");
        SetColumn(_meta, 2);
        Children.Add(iconBox);
        Children.Add(name);
        Children.Add(_meta);
        Update();
    }

    private ChatStrings Strings => GetValue(StringsProperty) ?? ChatStrings.English;

    /// <summary>Styled as the image pill's content grid.</summary>
    protected override System.Type StyleKeyOverride => typeof(Grid);

    private static string KindClass(ChatFileKind kind) => kind switch
    {
        ChatFileKind.Pdf => "pdf",
        ChatFileKind.Csv => "csv",
        _ => "text",
    };

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        _file.PropertyChanged += OnFileChanged;
        Update();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        // the file outlives the chip: let go of it, and draw nothing while Avalonia takes the tree apart
        _file.PropertyChanged -= OnFileChanged;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StringsProperty && _attached) Update();
    }

    /// <summary>
    /// The host should set the file's progress on the UI thread; one set from its upload worker is drawn on the UI
    /// thread instead, unless the chip has left the window by then.
    /// </summary>
    private void OnFileChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_attached) Update();
            });
            return;
        }
        Update();
    }

    private void Update()
    {
        var progress = _file.Progress;
        var sending = progress != null && _file.RemotePath == null;
        _meta.Text = sending ? Strings.FormatFileUploading(progress!.Value)
            : _file.Size >= 0 ? Strings.FormatFileSize(_file.Size) : null;
        _meta.IsVisible = sending || _file.Size >= 0;
        if (_remove != null) _remove.IsVisible = progress == null;
        ToolTip.SetTip(_frame, _file.RemotePath ?? _file.Name);
    }
}
