using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using ClaudeVisualChat.Imaging;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// The reply box, like the extension's: the images attached, the text, and a bar with +, / (the command palette, also
/// opened by a "/" typed in the empty box), the prompt cache clock, the Remote Control chip, the "N agents" chip (it
/// opens the Agent map), the model chip (its menu
/// has the models and the effort), the mode and the
/// square buttons: send (enabled when something is written or attached) and, while Claude works, stop; with nothing
/// to send, only stop shows, as in the extension; with something, both, and what is sent waits in line.
/// Images come from the + button's file dialog, from Ctrl+V when the clipboard holds an image and no text, and from
/// image files dropped on the box; PDF, text and CSV files from the same dialog and drop, as chips after the images.
/// One that does not go in says why under the strip until the next one goes in or the message is sent.
/// Enter sends and clears; Shift+Enter breaks the line; Esc stops Claude while it works, and otherwise goes on to the
/// app. The mode picker and the model chip show what the claude reports; picking only asks for a change through the
/// conversation's events. The box takes the mode's color: classes
/// <c>plan</c>, <c>acceptEdits</c> and <c>auto</c> on its frame.
/// </summary>
internal partial class ChatComposer : UserControl
{
    private static readonly StyledProperty<ChatStrings> StringsProperty =
        ClaudeChatView.StringsProperty.AddOwner<ChatComposer>();

    /// <summary>A mode: its icon on the extension's 26 px box, and its name.</summary>
    private static readonly FuncDataTemplate<Choice> ModeTemplate = new FuncDataTemplate<Choice>((choice, _) =>
    {
        var icon = new Path();
        icon.Classes.Add("ClaudeChatModeIcon");
        icon.Bind(Path.DataProperty, icon.GetResourceObservable(IconKey(choice?.Value)));
        var label = new TextBlock { Text = choice?.Label };
        label.Classes.Add("ClaudeChatModeLabel");
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, label } };
    });

    /// <summary>What was written in each conversation and not sent; it goes with the conversation.</summary>
    private readonly ConditionalWeakTable<ChatConversation, string> _drafts = new ConditionalWeakTable<ChatConversation, string>();

    private ChatConversation? _conversation;
    private ChatConversation? _tracked;
    private IReadOnlyList<string> _modes = Array.Empty<string>();
    private bool _attached;

    /// <summary>True while the mode picker is set from the conversation, so the change is not the user's.</summary>
    private bool _syncing;

    /// <summary>Why the last image or file did not go in; null when it did, or after a send.</summary>
    private Rejection? _rejected;

    /// <summary>Test seam: the file dialog of the + button, giving image bytes (it takes the place of the dialog).</summary>
    internal Func<Task<IReadOnlyList<byte[]>>>? PickImagesOverride;

    /// <summary>Test seam: the file dialog of the + button, giving paths of images and files.</summary>
    internal Func<Task<IReadOnlyList<string>>>? PickFilesOverride;

    public ChatComposer()
    {
        InitializeComponent();
        ModePicker.ItemTemplate = ModeTemplate;
        Input.AddHandler(KeyDownEvent, OnInputKeyDown, RoutingStrategies.Tunnel);
        Input.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty) return;
            UpdateSend();
            UpdateBusy(); // with text, the square sends; empty while Claude works, it stops
        };
        SendButton.Click += (_, _) =>
        {
            Send();
            Input.Focus();
        };
        StopButton.Click += (_, _) => _tracked?.Stop();
        ModePicker.SelectionChanged += OnModePicked;
        Palette.PlacementTarget = Box;
        Palette.Opener = CommandsButton;
        Palette.Input = Input;
        CommandsButton.Click += (_, _) => Palette.Toggle();
        ModelMenu.PlacementTarget = Box;
        ModelMenu.Opener = ModelChip;
        ModelChip.Menu = ModelMenu;
        AddButton.Click += async (_, _) =>
        {
            var conversation = _tracked; // the one shown now, even if another is shown when the dialog closes
            if (PickImagesOverride != null)
            {
                await AttachAsync(conversation, await PickImagesOverride());
                return;
            }
            await AttachSourcesAsync(conversation, await PickAsync(conversation == null || conversation.MaxFiles > 0));
        };
        Input.PastingFromClipboard += OnPasting;
        Box.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        Box.AddHandler(DragDrop.DropEvent, OnDrop);
        Strip.RemoveRequested += (_, image) => _tracked?.RemoveAttachment(image);
        Strip.RemoveFileRequested += (_, file) => _tracked?.RemoveFile(file);
        Strip.OpenRequested += (_, e) => PreviewRequested?.Invoke(this, e);
        IsEnabled = false;
        Refresh();
    }

    /// <summary>
    /// The conversation the reply box writes to. What was written and not sent stays with its conversation: another
    /// one shows its own draft, or an empty box, so Enter never sends a draft to the wrong conversation.
    /// </summary>
    internal ChatConversation? Conversation
    {
        get => _conversation;
        set
        {
            if (!ReferenceEquals(value, _conversation))
            {
                KeepDraft(_conversation);
                _conversation = value;
                ShowDraft(value);
            }
            CacheClock.Conversation = value;
            RemoteControlChip.Conversation = value;
            AgentsChip.Conversation = value;
            ContextRing.Conversation = value;
            ModelChip.Conversation = value;
            ModelMenu.Conversation = value;
            Palette.Conversation = value;
            IsEnabled = value != null;
            if (_attached) Track(value);
        }
    }

    private void KeepDraft(ChatConversation? conversation)
    {
        if (conversation == null) return;
        _drafts.Remove(conversation);
        var text = Input.Text;
        if (!string.IsNullOrEmpty(text)) _drafts.Add(conversation, text!);
    }

    private void ShowDraft(ChatConversation? conversation)
    {
        var text = conversation != null && _drafts.TryGetValue(conversation, out var draft) ? draft : "";
        Input.Text = text;
        Input.CaretIndex = text.Length;
        // the undo history belongs to the text that left: Ctrl+Z must not bring another conversation's draft back
        Input.IsUndoEnabled = false;
        Input.IsUndoEnabled = true;
    }

    /// <summary>The permission modes offered; one without a label shows its name.</summary>
    internal IReadOnlyList<string>? Modes
    {
        get => _modes;
        set
        {
            _modes = value ?? Array.Empty<string>();
            RebuildModes();
        }
    }

    /// <summary>The user clicked an attached image: the view shows it.</summary>
    internal event EventHandler<(ChatImage Image, string Name)>? PreviewRequested;

    /// <summary>Nothing written yet (spaces do not count).</summary>
    internal bool IsEmpty => string.IsNullOrWhiteSpace(Input.Text);

    /// <summary>Nothing written and no image or file attached: Enter would send nothing.</summary>
    internal bool HasNothingToSend =>
        IsEmpty && (_conversation == null || (_conversation.Attachments.Count == 0 && _conversation.Files.Count == 0));

    internal bool FocusInput() => Input.Focus();

    /// <summary>The command palette or the model menu is open: they take Esc first.</summary>
    internal bool HasOpenPopup => Palette.IsOpen || ModelMenu.IsOpen;

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
        Track(null); // the conversation outlives the view: let go of its events (and draw nothing)
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StringsProperty) Refresh();
        else if (change.Property == IsVisibleProperty && !IsVisible) ClosePopups();
    }

    /// <summary>
    /// Closes the command palette and the model menu: the reply box goes away (read-only, hidden) or a permission
    /// request comes. Avalonia 11.0 does not tell a control that an ancestor hid it, so the view calls this too.
    /// </summary>
    internal void ClosePopups()
    {
        Palette.IsOpen = false;
        ModelMenu.IsOpen = false;
    }

    /// <summary>
    /// Follows the conversation's events and draws it. Out of the window (the view is being taken out), only the
    /// events go: Avalonia is taking the tree apart, and a control drawn then (the mode list) throws; coming back
    /// draws it all again. The strip lets go of the attachments by itself.
    /// </summary>
    private void Track(ChatConversation? conversation)
    {
        var changed = !ReferenceEquals(conversation, _tracked);
        if (changed)
        {
            if (_tracked != null)
            {
                _tracked.PropertyChanged -= OnConversationChanged;
                _tracked.SubmitUndone -= OnSubmitUndone;
                _tracked.Attachments.CollectionChanged -= OnAttachmentsChanged;
                _tracked.Files.CollectionChanged -= OnAttachmentsChanged;
            }
            _tracked = conversation;
            if (_tracked != null)
            {
                _tracked.PropertyChanged += OnConversationChanged;
                _tracked.SubmitUndone += OnSubmitUndone;
                _tracked.Attachments.CollectionChanged += OnAttachmentsChanged;
                _tracked.Files.CollectionChanged += OnAttachmentsChanged;
            }
        }
        if (!_attached) return;
        if (changed)
        {
            Strip.Images = _tracked?.Attachments;
            Strip.Files = _tracked?.Files;
            ShowRejected(null);
        }
        Refresh();
    }

    private void OnConversationChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ChatConversation.IsBusy):
                UpdateBusy();
                break;
            case nameof(ChatConversation.PermissionMode):
                SelectMode();
                break;
            case nameof(ChatConversation.CurrentPermission):
                if (_tracked?.CurrentPermission != null) ClosePopups(); // the request comes first, the menus would cover it
                break;
        }
    }

    /// <summary>The message never left: it comes back to the box, unless something else was written since.</summary>
    private void OnSubmitUndone(object? sender, MessageSubmittedEventArgs e)
    {
        if (!IsEmpty) return;
        Input.Text = e.Text;
        Input.CaretIndex = e.Text.Length;
    }

    private void OnAttachmentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateSend();
        UpdateBusy();
    }

    private void Refresh()
    {
        var strings = Strings;
        Label(SendButton, strings.Send);
        Label(StopButton, strings.Stop);
        Label(AddButton, strings.AddAttachment);
        Label(CommandsButton, strings.ShowCommands);
        ShowRejected(_rejected);
        UpdateBusy();
        UpdateSend();
        RebuildModes();
    }

    private static void Label(Button button, string name)
    {
        AutomationProperties.SetName(button, name);
        ToolTip.SetTip(button, name);
    }

    /// <summary>
    /// While Claude works, stop shows and what is sent waits in line; send shows next to it once something is written
    /// or attached (alone, stop is the extension's square). Idle, only send.
    /// </summary>
    private void UpdateBusy()
    {
        var busy = _tracked != null && _tracked.IsBusy;
        var strings = Strings;
        Input.Watermark = busy ? strings.InputPlaceholderBusy : strings.InputPlaceholder;
        StopButton.IsVisible = busy;
        SendButton.IsVisible = !busy || HasSomethingToSend;
    }

    private void UpdateSend() => SendButton.IsEnabled = HasSomethingToSend;

    /// <summary>Text, or an image or a file attached.</summary>
    private bool HasSomethingToSend =>
        _tracked != null && (!IsEmpty || _tracked.Attachments.Count > 0 || _tracked.Files.Count > 0);

    // ---- keys and buttons ----

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        // a hidden box (read-only, or an ancestor hidden) may keep the keyboard: its keys send and stop nothing
        if (!IsEffectivelyVisible) return;
        if (e.KeyModifiers != KeyModifiers.None) return; // Shift+Enter: the text box breaks the line
        if (Palette.HandleKey(e.Key))
        {
            e.Handled = true; // the palette's arrows, Enter, Tab and Esc
            return;
        }
        if (e.Key == Key.Enter)
        {
            e.Handled = true; // Enter is the send key, never a line break
            Send();
        }
        else if (e.Key == Key.Escape && _tracked != null && _tracked.IsBusy)
        {
            e.Handled = true; // only when it stops something; otherwise the Esc is the app's
            _tracked.Stop();
        }
    }

    private void Send()
    {
        var conversation = _tracked;
        if (conversation == null || !HasSomethingToSend || !IsEffectivelyVisible) return;
        var text = Input.Text ?? "";
        // cleared first: a host that refuses the message at once (UndoSubmit inside MessageSubmitted) gets it back here
        Input.Clear();
        ShowRejected(null);
        conversation.Submit(text);
    }

    // ---- images and files ----

    /// <summary>The + button's dialog: images and PDF, text and CSV files together (images only when no file is taken).</summary>
    private async Task<IReadOnlyList<AttachSource>> PickAsync(bool files)
    {
        if (PickFilesOverride == null) return await AttachFiles.PickAsync(TopLevel.GetTopLevel(this), Strings, files);
        var sources = new List<AttachSource>();
        foreach (var path in await PickFilesOverride()) sources.Add(AttachSource.FromPath(path));
        return sources;
    }

    /// <summary>
    /// Ctrl+V: text wins, so text copied with a picture (a browser, Word) pastes as text. With no text, files copied in
    /// Explorer or a file manager are attached as a drop would attach them (an image file to the images, a PDF, text or
    /// CSV file to the files, any other refused with its reason), else an image (a bitmap) is attached. On Windows this
    /// happens instead of the paste; elsewhere the text box pastes, and what is found with no text is attached. Each
    /// gesture attaches to the conversation shown when it began.
    /// </summary>
    private void OnPasting(object? sender, RoutedEventArgs e)
    {
        var conversation = _tracked;
        if (!ClipboardImages.CanCheckNow)
        {
            _ = PasteImageWithoutTextAsync(conversation);
            return;
        }
        if (ClipboardImages.HasText()) return;
        if (ClipboardImages.HasFiles())
        {
            e.Handled = true;
            _ = PasteFilesAsync(conversation);
            return;
        }
        if (!ClipboardImages.HasImage()) return;
        e.Handled = true;
        _ = PasteImageAsync(conversation);
    }

    private async Task PasteFilesAsync(ChatConversation? conversation)
    {
        var sources = await AttachFiles.PastedAsync(TopLevel.GetTopLevel(this));
        await AttachSourcesAsync(conversation, sources);
    }

    private async Task PasteImageAsync(ChatConversation? conversation)
    {
        var bytes = await ClipboardImages.ReadImageAsync(TopLevel.GetTopLevel(this));
        await AttachAsync(conversation, new[] { bytes ?? Array.Empty<byte>() }); // unreadable: refused as not an image
    }

    private async Task PasteImageWithoutTextAsync(ChatConversation? conversation)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null) return;
        var files = false;
        try
        {
            foreach (var format in await clipboard.GetFormatsAsync())
            {
                if (format == DataFormats.Text || format.StartsWith("text/", StringComparison.Ordinal) || format == "UTF8_STRING") return;
                files |= format == DataFormats.Files;
            }
        }
        catch (Exception)
        {
            return;
        }
        if (files)
        {
            var sources = await AttachFiles.PastedAsync(TopLevel.GetTopLevel(this));
            if (sources.Count > 0)
            {
                await AttachSourcesAsync(conversation, sources);
                return;
            }
        }
        var bytes = await ClipboardImages.ReadImageAsync(TopLevel.GetTopLevel(this));
        if (bytes != null) await AttachAsync(conversation, new[] { bytes });
    }

    /// <summary>A drag of files: each one is sorted when dropped (an image, a file, or one refused with its reason).</summary>
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = AttachFiles.Dropped(e.Data).Count > 0 ? e.DragEffects & DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var conversation = _tracked;
        var sources = AttachFiles.Dropped(e.Data);
        e.Handled = true;
        await AttachSourcesAsync(conversation, sources);
    }

    /// <summary>
    /// Sorts what was picked or dropped: images go to the images, PDF, text and CSV files to the files, each with
    /// their own limits; any other file is refused. What goes in goes in; the last one refused says why (the images
    /// are attached last), and a batch with none refused clears it. Images beyond the room left are never read.
    /// </summary>
    private async Task AttachSourcesAsync(ChatConversation? conversation, IReadOnlyList<AttachSource> sources)
    {
        if (conversation == null || sources.Count == 0)
        {
            Dispose(sources);
            return;
        }
        var images = new List<byte[]>();
        var maxImages = ImageFiles.ToRead(conversation);
        Rejection? rejected = null;
        try
        {
            foreach (var source in sources)
            {
                if (source.IsImage)
                {
                    if (images.Count >= maxImages) continue; // it would be refused as too many anyway
                    images.Add(await AttachFiles.ReadImageAsync(source) ?? Array.Empty<byte>()); // unreadable: not an image
                }
                else
                {
                    var result = source.IsAttachableFile ? await AttachFiles.AddFileAsync(conversation, source) : AddFileResult.NotSupported;
                    if (result != AddFileResult.Added) rejected = Rejection.Of(result, conversation);
                }
            }
        }
        finally
        {
            Dispose(sources);
        }
        if (await AttachImagesAsync(conversation, images) is { } image) rejected = Rejection.Of(image);
        if (ReferenceEquals(conversation, _tracked)) ShowRejected(rejected);
    }

    private static void Dispose(IReadOnlyList<AttachSource> sources)
    {
        foreach (var source in sources) source.File?.Dispose();
    }

    /// <summary>Attaches the images in order, and says why the last one refused did not go in.</summary>
    private async Task AttachAsync(ChatConversation? conversation, IReadOnlyList<byte[]> images)
    {
        if (conversation == null || images.Count == 0) return;
        var rejected = await AttachImagesAsync(conversation, images);
        if (ReferenceEquals(conversation, _tracked)) ShowRejected(rejected == null ? null : Rejection.Of(rejected.Value));
    }

    /// <summary>Attaches the images in order; why the last one refused did not go in, null when all did.</summary>
    private static async Task<AddImageResult?> AttachImagesAsync(ChatConversation conversation, IReadOnlyList<byte[]> images)
    {
        AddImageResult? rejected = null;
        foreach (var bytes in images)
        {
            var result = ReferenceEquals(bytes, ImageFiles.TooBig) ? AddImageResult.TooBig : await conversation.AddImageAsync(bytes);
            if (result != AddImageResult.Added) rejected = result;
        }
        return rejected;
    }

    private void ShowRejected(Rejection? rejection)
    {
        _rejected = rejection;
        AttachError.Text = rejection?.Text(Strings);
        AttachError.IsVisible = rejection != null;
    }

    /// <summary>Why an image or a file did not go in, with the limits it broke (the text follows the language).</summary>
    private sealed class Rejection
    {
        private readonly AddImageResult? _image;
        private readonly AddFileResult _file;
        private readonly long _maxFileBytes;
        private readonly int _maxFiles;

        private Rejection(AddImageResult? image, AddFileResult file, long maxFileBytes, int maxFiles)
        {
            _image = image;
            _file = file;
            _maxFileBytes = maxFileBytes;
            _maxFiles = maxFiles;
        }

        public static Rejection Of(AddImageResult image) => new Rejection(image, AddFileResult.Added, 0, 0);

        public static Rejection Of(AddFileResult file, ChatConversation conversation) =>
            new Rejection(null, file, conversation.MaxFileBytes, conversation.MaxFiles);

        public string? Text(ChatStrings strings)
        {
            if (_image != null)
            {
                return _image switch
                {
                    AddImageResult.TooMany => strings.TooManyImages,
                    AddImageResult.TooBig => strings.ImageTooBig,
                    AddImageResult.NotAnImage => strings.NotAnImage,
                    _ => null,
                };
            }
            return _file switch
            {
                AddFileResult.TooMany => strings.FormatTooManyFiles(_maxFiles),
                AddFileResult.TooBig => strings.FormatFileTooBig(_maxFileBytes),
                AddFileResult.NotSupported => strings.FileNotSupported,
                _ => null,
            };
        }
    }

    // ---- the mode ----

    private void RebuildModes()
    {
        var strings = Strings;
        var items = new List<Choice>(_modes.Count);
        foreach (var mode in _modes) items.Add(new Choice(mode, strings.ModeLabel(mode)));
        _syncing = true;
        try
        {
            ModePicker.ItemsSource = items;
        }
        finally
        {
            _syncing = false;
        }
        SelectMode();
    }

    /// <summary>Shows the mode the claude reports; one that is not offered shows as the placeholder.</summary>
    private void SelectMode()
    {
        var mode = _tracked?.PermissionMode;
        Choice? shown = null;
        if (ModePicker.ItemsSource is List<Choice> items)
        {
            foreach (var item in items)
            {
                if (item.Value == mode) shown = item;
            }
        }
        _syncing = true;
        try
        {
            ModePicker.SelectedItem = shown;
            ModePicker.PlaceholderText = shown == null && mode != null ? Strings.ModeLabel(mode) : null;
        }
        finally
        {
            _syncing = false;
        }
        // the frame and the square take the color of the mode the claude is in
        Box.Classes.Set("plan", mode == "plan");
        Box.Classes.Set("acceptEdits", mode == "acceptEdits");
        Box.Classes.Set("auto", mode == "auto" || mode == "bypassPermissions");
    }

    /// <summary>The resource with a mode's icon; a mode without one shows the default mode's.</summary>
    private static string IconKey(string? mode) => mode switch
    {
        "plan" => "ClaudeChat.Icon.ModePlan",
        "acceptEdits" => "ClaudeChat.Icon.ModeAcceptEdits",
        "auto" or "bypassPermissions" => "ClaudeChat.Icon.ModeAuto",
        _ => "ClaudeChat.Icon.ModeDefault",
    };

    private void OnModePicked(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncing || _tracked == null || ModePicker.SelectedItem is not Choice choice) return;
        _tracked.SetPermissionMode(choice.Value);
        // the chip shows the mode the claude is in, not the one asked for: it moves when the claude confirms, and
        // stays when the claude refuses or confirms the mode it already had (both leave PermissionMode as it is);
        // posted, since the picker is still applying the user's choice
        Dispatcher.UIThread.Post(SelectMode);
    }

    /// <summary>A picker entry: the value sent to the claude and the label shown.</summary>
    private sealed class Choice
    {
        public Choice(string value, string label)
        {
            Value = value;
            Label = label;
        }

        public string Value { get; }

        public string Label { get; }

        public override string ToString() => Label;
    }
}
