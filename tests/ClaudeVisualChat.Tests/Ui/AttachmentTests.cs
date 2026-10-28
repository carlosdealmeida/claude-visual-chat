using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClaudeVisualChat.Controls;
using ClaudeVisualChat.Imaging;
using SkiaSharp;
using Xunit;

namespace ClaudeVisualChat.Tests.Ui;

/// <summary>Images in the reply box: the + button, Ctrl+V, dropped files, the strip of pills and the preview.</summary>
public class AttachmentTests
{
    private sealed class Opened
    {
        public Opened(ChatConversation conversation)
        {
            Conversation = conversation;
            conversation.MessageSubmitted += (_, e) => Sent.Add(e);
            View = new ClaudeChatView { Conversation = conversation };
            Window = UiHelpers.Show(View);
            Composer = UiHelpers.Composer(View);
            Input = UiHelpers.Input(View);
            Unhandled = UiHelpers.UnhandledKeys(Window);
        }

        public ChatConversation Conversation { get; }
        public ClaudeChatView View { get; }
        public Window Window { get; }
        public ChatComposer Composer { get; }
        public TextBox Input { get; }
        public List<Key> Unhandled { get; }
        public List<MessageSubmittedEventArgs> Sent { get; } = new List<MessageSubmittedEventArgs>();

        public AttachmentStrip Strip => Composer.GetVisualDescendants().OfType<AttachmentStrip>().Single();

        public List<Button> Pills =>
            Strip.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("ClaudeChatAttachment")).ToList();

        public ImagePreview Preview => View.GetVisualDescendants().OfType<ImagePreview>().Single();

        public Border Box => Composer.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("ClaudeChatComposer"));

        public List<string> Texts => UiHelpers.Texts(Composer);

        public void Type(string text)
        {
            Input.Focus();
            UiHelpers.Type(Window, text);
        }

        /// <summary>The + button gives these images, as if picked in the file dialog.</summary>
        public void Pick(params byte[][] images)
        {
            Composer.PickImagesOverride = () => Task.FromResult<IReadOnlyList<byte[]>>(images);
            UiHelpers.Click(Window, UiHelpers.ButtonWithText(Composer, View.Strings.AddAttachment));
        }
    }

    private static Opened Open(ChatConversation? conversation = null) => new Opened(conversation ?? new ChatConversation());

    private static byte[] Png(int w, int h)
    {
        using var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        bmp.Erase(new SKColor(30, 120, 200));
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Runs the UI jobs until <paramref name="done"/>: images are fitted off the UI thread.</summary>
    private static void PumpUntil(Func<bool> done)
    {
        var clock = Stopwatch.StartNew();
        while (!done() && clock.Elapsed < TimeSpan.FromSeconds(10))
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        Dispatcher.UIThread.RunJobs();
        Assert.True(done());
    }

    /// <summary>Lets the background work run, for the checks that something did not happen.</summary>
    private static void Settle()
    {
        for (var i = 0; i < 20; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
    }

    private static void ResetClipboard()
    {
        ClipboardImages.HasTextOverride = null;
        ClipboardImages.HasImageOverride = null;
        ClipboardImages.ReadOverride = null;
    }

    // ---- the brief ----

    [AvaloniaFact]
    public void Plus_opens_the_picker_and_adds_images()
    {
        var ui = Open();
        var picked = 0;
        ui.Composer.PickImagesOverride = () =>
        {
            picked++;
            return Task.FromResult<IReadOnlyList<byte[]>>(new[] { Png(96, 48), Png(20, 30) });
        };

        UiHelpers.Click(ui.Window, UiHelpers.ButtonWithText(ui.Composer, "Add"));

        PumpUntil(() => ui.Conversation.Attachments.Count == 2);
        Assert.Equal(1, picked);
        Assert.Equal(96, ui.Conversation.Attachments[0].Width);
        Assert.Equal(20, ui.Conversation.Attachments[1].Width);
        Assert.Empty(ui.Sent);

        ui.Composer.PickImagesOverride = () => Task.FromResult<IReadOnlyList<byte[]>>(Array.Empty<byte[]>());
        UiHelpers.Click(ui.Window, UiHelpers.ButtonWithText(ui.Composer, "Add")); // cancelled: nothing changes
        Settle();
        Assert.Equal(2, ui.Conversation.Attachments.Count);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Pasting_text_pastes_text()
    {
        try
        {
            var read = 0;
            ClipboardImages.HasTextOverride = () => true;
            ClipboardImages.HasImageOverride = () => true; // text and image together, as copied from a browser or Word
            ClipboardImages.ReadOverride = () =>
            {
                read++;
                return Task.FromResult<byte[]?>(Png(10, 10));
            };
            var ui = Open();
            ui.Window.Clipboard!.SetTextAsync("hello").Wait();
            ui.Input.Focus();

            UiHelpers.Press(ui.Window, Key.V, RawInputModifiers.Control);
            Settle();

            Assert.Equal("hello", ui.Input.Text);
            Assert.Empty(ui.Conversation.Attachments);
            Assert.Equal(0, read);
            ui.Window.Close();
        }
        finally
        {
            ResetClipboard();
        }
    }

    [AvaloniaFact]
    public void Pasting_an_image_attaches_it()
    {
        try
        {
            ClipboardImages.HasTextOverride = () => false;
            ClipboardImages.HasImageOverride = () => true;
            ClipboardImages.ReadOverride = () => Task.FromResult<byte[]?>(Png(96, 48));
            var ui = Open();
            ui.Window.Clipboard!.SetTextAsync("stale").Wait(); // only an image on the real clipboard: the text box pastes nothing
            ui.Input.Focus();
            ui.Type("look: ");

            UiHelpers.Press(ui.Window, Key.V, RawInputModifiers.Control);

            PumpUntil(() => ui.Conversation.Attachments.Count == 1);
            Assert.Equal("look: ", ui.Input.Text);
            Assert.Equal(96, ui.Conversation.Attachments[0].Width);
            Assert.True(ui.Input.IsFocused);
            ui.Window.Close();
        }
        finally
        {
            ResetClipboard();
        }
    }

    [AvaloniaFact]
    public void Strip_shows_and_removes()
    {
        var c = new ChatConversation();
        var ui = Open(c);
        Assert.False(ui.Strip.IsVisible);

        c.Attachments.Add(ChatImage.FromBytes(Png(96, 48), "image/png"));
        c.Attachments.Add(ChatImage.FromBytes(Png(20, 30), "image/jpeg"));
        c.Attachments.Add(ChatImage.FromBytes(Png(8, 8), "image/webp"));
        UiHelpers.Pump();

        Assert.True(ui.Strip.IsEffectivelyVisible);
        Assert.Equal(3, ui.Pills.Count);
        var texts = ui.Texts;
        Assert.Equal(new[] { "image.png", "96×48", "image-2.jpg", "20×30", "image-3.webp", "8×8" },
            texts.Where(t => t.StartsWith("image") || t.Contains("×")).ToList());
        foreach (var pill in ui.Pills)
        {
            var thumb = pill.GetVisualDescendants().OfType<Image>().Single();
            Assert.NotNull(thumb.Source);
            Assert.Equal(12, thumb.Bounds.Width);
            Assert.Equal(12, thumb.Bounds.Height);
            Assert.True(((Bitmap)thumb.Source!).PixelSize.Width <= 48); // decoded small
            Assert.Equal(24, pill.Bounds.Height);
        }

        // the ×: removes that image; the names follow the position
        var removes = ui.Strip.GetVisualDescendants().OfType<Button>()
            .Where(b => AutomationProperties.GetName(b) == "Remove attachment").ToList();
        Assert.Equal(3, removes.Count);
        Assert.Equal("Remove attachment", ToolTip.GetTip(removes[0]));
        UiHelpers.Click(ui.Window, removes[0]);
        Assert.Equal(2, c.Attachments.Count);
        Assert.Equal(20, c.Attachments[0].Width);
        Assert.Contains("image.jpg", ui.Texts);
        Assert.Contains("image-2.webp", ui.Texts);
        Assert.False(ui.Preview.IsVisible); // the × does not open the preview

        // the pill: the preview over the whole view, Esc closes it
        Assert.False(ui.Preview.IsVisible);
        UiHelpers.Click(ui.Window, ui.Pills[0]);
        Assert.True(ui.Preview.IsEffectivelyVisible);
        Assert.Equal(ui.View.Bounds.Size, ui.Preview.Bounds.Size);
        var shown = ui.Preview.GetVisualDescendants().OfType<Image>().Single(i => i.IsEffectivelyVisible);
        Assert.Equal(20, ((Bitmap)shown.Source!).PixelSize.Width);
        Assert.Equal(new[] { "Close" }, UiHelpers.ButtonNames(ui.Preview));
        ui.Unhandled.Clear();
        UiHelpers.Press(ui.Window, Key.Escape);
        Assert.False(ui.Preview.IsVisible);
        Assert.DoesNotContain(Key.Escape, ui.Unhandled); // the Esc closed the preview: it is not the app's

        UiHelpers.Click(ui.Window, ui.Pills[1]); // the close button too
        Assert.True(ui.Preview.IsVisible);
        UiHelpers.Click(ui.Window, UiHelpers.ButtonWithText(ui.Preview, "Close"));
        Assert.False(ui.Preview.IsVisible);

        c.Attachments.Clear();
        UiHelpers.Pump();
        Assert.False(ui.Strip.IsVisible);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Enter_sends_images_without_text()
    {
        var c = new ChatConversation();
        var ui = Open(c);
        var send = UiHelpers.ButtonWithText(ui.Composer, "Send");
        Assert.False(send.IsEnabled);

        ui.Pick(Png(96, 48));
        PumpUntil(() => c.Attachments.Count == 1);
        Assert.True(send.IsEnabled);
        var image = c.Attachments[0];

        ui.Input.Focus();
        UiHelpers.Press(ui.Window, Key.Enter);

        var sent = Assert.Single(ui.Sent);
        Assert.Equal("", sent.Text);
        Assert.Same(image, Assert.Single(sent.Images));
        Assert.Empty(c.Attachments);
        Assert.False(ui.Strip.IsVisible);
        Assert.False(send.IsEnabled);

        // taken back: the image returns to the strip
        c.UndoSubmit(sent);
        UiHelpers.Pump();
        Assert.True(ui.Strip.IsVisible);
        Assert.True(SendButton(ui).IsEnabled);
        ui.Window.Close();
    }

    private static Button SendButton(Opened ui) =>
        ui.Composer.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("ClaudeChatSend"));

    [AvaloniaFact]
    public void Busy_with_only_an_image_shows_send_next_to_stop()
    {
        var c = new ChatConversation();
        var ui = Open(c);
        c.Submit("go");
        UiHelpers.Pump();
        Assert.False(SendButton(ui).IsVisible);

        c.Attachments.Add(ChatImage.FromBytes(Png(8, 8), "image/png"));
        UiHelpers.Pump();
        Assert.True(SendButton(ui).IsEffectivelyVisible);
        Assert.True(SendButton(ui).IsEnabled);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Rejected_image_shows_why()
    {
        var c = new ChatConversation { MaxImages = 1 };
        var ui = Open(c);

        ui.Pick(Png(10, 10));
        PumpUntil(() => c.Attachments.Count == 1);
        ui.Pick(Png(12, 12));
        PumpUntil(() => ui.Texts.Contains("Too many images in one message"));
        Assert.Single(c.Attachments);

        // the next image that goes in takes the message away
        c.RemoveAttachment(c.Attachments[0]);
        ui.Pick(Png(12, 12));
        PumpUntil(() => c.Attachments.Count == 1);
        Assert.DoesNotContain("Too many images in one message", ui.Texts);

        c.MaxImages = 10;
        ui.Pick(System.Text.Encoding.UTF8.GetBytes("hello, not a picture"));
        PumpUntil(() => ui.Texts.Contains("Not an image"));

        c.ImageBudgetBytes = 10;
        ui.Pick(Png(200, 200));
        PumpUntil(() => ui.Texts.Contains("Image too large to attach"));
        Assert.DoesNotContain("Not an image", ui.Texts);
        Assert.Single(c.Attachments);

        // sending takes it away too
        ui.Input.Focus();
        UiHelpers.Press(ui.Window, Key.Enter);
        Assert.Single(ui.Sent);
        Assert.DoesNotContain("Image too large to attach", ui.Texts);

        // in Portuguese
        ui.View.Strings = ChatStrings.PortugueseBrazil;
        ui.Pick(System.Text.Encoding.UTF8.GetBytes("nope"));
        PumpUntil(() => ui.Texts.Contains("Não é uma imagem"));
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Dropping_image_files()
    {
        var c = new ChatConversation();
        var ui = Open(c);
        Assert.True(DragDrop.GetAllowDrop(ui.Box));
        Assert.True(DragDrop.GetAllowDrop(ui.Input)); // dropped on the text too

        var data = new DataObject();
        data.Set(DataFormats.Files, new IStorageItem[]
        {
            new FakeFile("one.png", Png(96, 48)),
            new FakeFile("notes.docx", System.Text.Encoding.UTF8.GetBytes("text")),
            new FakeFile("Two.PNG", Png(20, 30)),
        });

        var over = new DragEventArgs(DragDrop.DragOverEvent, data, ui.Input, default, KeyModifiers.None) { DragEffects = DragDropEffects.Copy | DragDropEffects.Move };
        ui.Input.RaiseEvent(over);
        Assert.Equal(DragDropEffects.Copy, over.DragEffects);

        var drop = new DragEventArgs(DragDrop.DropEvent, data, ui.Input, default, KeyModifiers.None) { DragEffects = DragDropEffects.Copy };
        ui.Input.RaiseEvent(drop);
        Assert.True(drop.Handled);

        PumpUntil(() => c.Attachments.Count == 2);
        Assert.Equal(96, c.Attachments[0].Width);
        Assert.Equal(20, c.Attachments[1].Width);
        Settle();
        Assert.Equal(2, c.Attachments.Count);
        Assert.DoesNotContain("Not an image", ui.Texts);
        Assert.Contains("Unsupported file type(s): .docx", ui.Texts); // a file of another kind says why it stayed out, by its type (0.6.0)
        Assert.Empty(c.Files);
        Assert.Equal("", ui.Input.Text ?? "");

        // no files at all: the drag says no
        var text = new DataObject();
        text.Set(DataFormats.Text, "just text");
        var refused = new DragEventArgs(DragDrop.DragOverEvent, text, ui.Input, default, KeyModifiers.None) { DragEffects = DragDropEffects.Copy };
        ui.Input.RaiseEvent(refused);
        Assert.Equal(DragDropEffects.None, refused.DragEffects);
        ui.Window.Close();
    }

    // ---- the drop overlay: the whole view takes files (0.6.0) ----

    [AvaloniaFact]
    public void Dragging_files_over_the_view_shows_drop_to_attach_as_context()
    {
        var ui = Open();
        var overlay = UiHelpers.DropOverlay(ui.View);
        var list = UiHelpers.ItemList(ui.View);
        UiHelpers.Pump();
        Assert.False(overlay.IsVisible); // there, hidden, until files are dragged in
        Assert.False(overlay.IsHitTestVisible);

        // a drag of text asks for nothing: no overlay, and the view says no
        var text = new DataObject();
        text.Set(DataFormats.Text, "just text");
        UiHelpers.Drag(DragDrop.DragEnterEvent, text, list);
        var refused = UiHelpers.Drag(DragDrop.DragOverEvent, text, list);
        UiHelpers.Pump();
        Assert.False(overlay.IsVisible);
        Assert.Equal(DragDropEffects.None, refused.DragEffects);

        // files coming in over the list: the whole view is the drop, and says so
        var files = UiHelpers.Dragged(new FakeFile("one.png", Png(8, 8)));
        UiHelpers.Drag(DragDrop.DragEnterEvent, files, list);
        var over = UiHelpers.Drag(DragDrop.DragOverEvent, files, list, DragDropEffects.Copy | DragDropEffects.Move);
        UiHelpers.Pump();
        Assert.True(overlay.IsEffectivelyVisible);
        Assert.Equal(DragDropEffects.Copy, over.DragEffects);
        Assert.True(over.Handled);
        Assert.Contains("Drop to attach as context", UiHelpers.Texts(overlay));
        AssertLooksLikeTheExtensions(ui.View, overlay);

        // the clicks go through it
        var middle = ui.View.TranslatePoint(new Point(ui.View.Bounds.Width / 2, 200), ui.Window)!.Value;
        var hit = Assert.IsAssignableFrom<Visual>(UiHelpers.HitTest(ui.Window, middle));
        Assert.DoesNotContain(overlay, hit.GetSelfAndVisualAncestors());

        // the touch layout shows the same thing, in its language
        var touch = UiHelpers.TouchView(new ChatConversation());
        var phone = UiHelpers.ShowPhone(touch);
        UiHelpers.Drag(DragDrop.DragEnterEvent, files, UiHelpers.ItemList(touch));
        UiHelpers.Pump();
        var touchOverlay = UiHelpers.DropOverlay(touch);
        Assert.True(touchOverlay.IsEffectivelyVisible);
        Assert.Contains("Solte para anexar como contexto", UiHelpers.Texts(touchOverlay));
        AssertLooksLikeTheExtensions(touch, touchOverlay);
        phone.Close();
        ui.Window.Close();
    }

    /// <summary>The extension's: 15 % of its orange over the view with a dashed line of 2 px, and a pill with the label.</summary>
    private static void AssertLooksLikeTheExtensions(ClaudeChatView view, Control overlay)
    {
        Assert.InRange(overlay.Bounds.Width, view.Bounds.Width - 1, view.Bounds.Width + 1); // the whole view
        Assert.InRange(overlay.Bounds.Height, view.Bounds.Height - 1, view.Bounds.Height + 1);
        var frame = overlay.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Rectangle>().Single();
        Assert.Equal(Avalonia.Media.Color.Parse("#26D97757"), Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(frame.Fill).Color);
        Assert.Equal(Avalonia.Media.Color.Parse("#D97757"), Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(frame.Stroke).Color);
        Assert.Equal(2, frame.StrokeThickness);
        Assert.NotEmpty(frame.StrokeDashArray!);

        var pill = overlay.GetVisualDescendants().OfType<Border>().Single();
        Assert.Equal(new CornerRadius(6), pill.CornerRadius);
        Assert.Equal(new Thickness(16, 8), pill.Padding);
        var sideBar = Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(view.FindResource("ClaudeChat.Background"));
        Assert.Equal(sideBar.Color, Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(pill.Background).Color);
        Assert.Equal(1, pill.BoxShadow.Count);
        var shadow = pill.BoxShadow[0];
        Assert.Equal((0d, 2d, 8d, 0d), (shadow.OffsetX, shadow.OffsetY, shadow.Blur, shadow.Spread));
        Assert.Equal(Avalonia.Media.Color.Parse("#00000026"), shadow.Color);
        var label = pill.GetVisualDescendants().OfType<TextBlock>().Single();
        Assert.Equal(Avalonia.Media.FontWeight.Medium, label.FontWeight);

        // in the middle of the view; compared with the view, not with a measured text
        var center = pill.TranslatePoint(new Point(pill.Bounds.Width / 2, pill.Bounds.Height / 2), view)!.Value;
        Assert.InRange(center.X, view.Bounds.Width / 2 - 1, view.Bounds.Width / 2 + 1);
        Assert.InRange(center.Y, view.Bounds.Height / 2 - 1, view.Bounds.Height / 2 + 1);
    }

    [AvaloniaFact]
    public void Leaving_or_dropping_hides_the_overlay()
    {
        var c = new ChatConversation();
        var ui = Open(c);
        var overlay = UiHelpers.DropOverlay(ui.View);
        var list = UiHelpers.ItemList(ui.View);
        var files = UiHelpers.Dragged(new FakeFile("one.png", Png(8, 8)));

        // out of the view: gone
        UiHelpers.Drag(DragDrop.DragEnterEvent, files, list);
        Assert.True(overlay.IsVisible);
        UiHelpers.Drag(DragDrop.DragLeaveEvent, files, list);
        UiHelpers.Pump();
        Assert.False(overlay.IsVisible);

        // from the list onto the reply box, inside the view: the window raises the leave of the one and the enter of the other, in
        // that order, and the overlay stays (it is not hidden and shown again)
        var changes = 0;
        overlay.PropertyChanged += (_, e) => changes += e.Property == Visual.IsVisibleProperty ? 1 : 0;
        UiHelpers.Drag(DragDrop.DragEnterEvent, files, list);
        Assert.Equal(1, changes);
        UiHelpers.Drag(DragDrop.DragLeaveEvent, files, list);
        UiHelpers.Drag(DragDrop.DragEnterEvent, files, ui.Input);
        UiHelpers.Pump();
        UiHelpers.Settle();
        Assert.True(overlay.IsVisible);
        Assert.Equal(1, changes);
        UiHelpers.Drag(DragDrop.DragOverEvent, files, ui.Input);
        Assert.True(overlay.IsVisible);

        // and back onto the list, and then really out of the view
        UiHelpers.Drag(DragDrop.DragLeaveEvent, files, ui.Input);
        UiHelpers.Drag(DragDrop.DragEnterEvent, files, list);
        UiHelpers.Settle();
        Assert.True(overlay.IsVisible);
        UiHelpers.Drag(DragDrop.DragLeaveEvent, files, list);
        UiHelpers.Settle();
        Assert.False(overlay.IsVisible);

        // a leave that was followed by the pointer coming back does not hide it later on
        UiHelpers.Drag(DragDrop.DragEnterEvent, files, list);
        UiHelpers.Drag(DragDrop.DragLeaveEvent, files, list);
        UiHelpers.Drag(DragDrop.DragOverEvent, files, ui.Input); // the over alone brings it back
        UiHelpers.Settle();
        Assert.True(overlay.IsVisible);

        // the drop hides it at once, wherever it fell
        var drop = UiHelpers.Drag(DragDrop.DropEvent, files, ui.Input);
        Assert.True(drop.Handled);
        Assert.False(overlay.IsVisible);
        PumpUntil(() => c.Attachments.Count == 1);
        Settle();
        Assert.False(overlay.IsVisible);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Dropping_on_the_list_attaches_like_dropping_on_the_box()
    {
        var c = new ChatConversation();
        var ui = Open(c);
        var list = UiHelpers.ItemList(ui.View);

        var onList = new FakeFile("list.png", Png(40, 20));
        UiHelpers.Drag(DragDrop.DragEnterEvent, UiHelpers.Dragged(onList), list);
        UiHelpers.Drag(DragDrop.DropEvent, UiHelpers.Dragged(onList), list);
        PumpUntil(() => c.Attachments.Count == 1 && onList.Disposed);
        Assert.Equal(40, c.Attachments[0].Width);

        // on the box, the text or the strip the view takes it too, once: the box no longer has a handler of its own that would
        // attach it a second time
        var onBox = new FakeFile("box.png", Png(30, 10));
        UiHelpers.Drag(DragDrop.DropEvent, UiHelpers.Dragged(onBox), ui.Input);
        var onFrame = new FakeFile("frame.png", Png(20, 10));
        UiHelpers.Drag(DragDrop.DropEvent, UiHelpers.Dragged(onFrame), ui.Box);
        PumpUntil(() => c.Attachments.Count == 3 && onBox.Disposed && onFrame.Disposed);
        Settle();
        Assert.Equal(new[] { 40, 30, 20 }, c.Attachments.Select(a => a.Width).ToArray());
        Assert.Equal(1, onBox.Opens);
        Assert.Equal(1, onFrame.Opens);

        // over the box the drag is accepted all the same, and the view answers it
        var over = UiHelpers.Drag(DragDrop.DragOverEvent, UiHelpers.Dragged(onBox), ui.Input, DragDropEffects.Copy | DragDropEffects.Move);
        Assert.Equal(DragDropEffects.Copy, over.DragEffects);

        // the drop goes to the conversation shown, and with none there is nothing to attach to
        ui.View.Conversation = null;
        UiHelpers.Pump();
        var late = new FakeFile("late.png", Png(8, 8));
        UiHelpers.Drag(DragDrop.DropEvent, UiHelpers.Dragged(late), list);
        PumpUntil(() => late.Disposed);
        Assert.Equal(3, c.Attachments.Count);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void An_unsupported_drop_names_the_file_types()
    {
        var c = new ChatConversation();
        var ui = Open(c);
        var list = UiHelpers.ItemList(ui.View);

        // the ones the box would not take, by type, once each however many there are, in the order they came; a file without a
        // type is named by its name; what can go in goes in
        var drop = UiHelpers.Dragged(
            new FakeFile("notes.docx", new byte[] { 1 }), new FakeFile("one.png", Png(8, 8)), new FakeFile("a.ZIP", new byte[] { 1 }),
            new FakeFile("b.zip", new byte[] { 1 }), new FakeFile("README", new byte[] { 1 }));
        UiHelpers.Drag(DragDrop.DropEvent, drop, list);
        PumpUntil(() => c.Attachments.Count == 1 && ui.Texts.Contains("Unsupported file type(s): .docx, .zip, README"));
        Assert.Empty(c.Files);

        // the next drop that is all right takes the notice away
        UiHelpers.Drag(DragDrop.DropEvent, UiHelpers.Dragged(new FakeFile("two.png", Png(8, 8))), list);
        PumpUntil(() => c.Attachments.Count == 2);
        Assert.DoesNotContain(ui.Texts, t => t.StartsWith("Unsupported", StringComparison.Ordinal));

        // in Portuguese
        ui.View.Strings = ChatStrings.PortugueseBrazil;
        UiHelpers.Drag(DragDrop.DropEvent, UiHelpers.Dragged(new FakeFile("x.exe", new byte[] { 1 })), list);
        PumpUntil(() => ui.Texts.Contains("Tipo(s) de arquivo não suportado(s): .exe"));
        ui.Window.Close();
    }

    /// <summary>A file the drop carries.</summary>
    internal sealed class FakeFile : IStorageFile
    {
        private readonly byte[] _bytes;

        public FakeFile(string name, byte[] bytes)
        {
            Name = name;
            _bytes = bytes;
        }

        public string Name { get; }

        public Uri Path => new Uri("file:///C:/drop/" + Name);

        public bool CanBookmark => false;

        public Task<StorageItemProperties> GetBasicPropertiesAsync() => Task.FromResult(new StorageItemProperties());

        public Task<string?> SaveBookmarkAsync() => Task.FromResult<string?>(null);

        public Task<IStorageFolder?> GetParentAsync() => Task.FromResult<IStorageFolder?>(null);

        public Task DeleteAsync() => Task.CompletedTask;

        public Task<IStorageItem?> MoveAsync(IStorageFolder destination) => Task.FromResult<IStorageItem?>(null);

        public int Opens { get; private set; }

        public bool Disposed { get; private set; }

        public Task<Stream> OpenReadAsync()
        {
            Opens++;
            return Task.FromResult<Stream>(new MemoryStream(_bytes, false));
        }

        public Task<Stream> OpenWriteAsync() => throw new NotSupportedException();

        public void Dispose() => Disposed = true;
    }

    [AvaloniaFact]
    public void A_dropped_file_over_50_MB_is_refused_as_too_big_and_all_handles_are_disposed()
    {
        var c = new ChatConversation();
        var ui = Open(c);
        var huge = new FakeFile("huge.png", new byte[50 * 1024 * 1024 + 1]);
        var small = new FakeFile("small.png", Png(20, 30));
        var data = new DataObject();
        data.Set(DataFormats.Files, new IStorageItem[] { huge, small });
        ui.Input.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, data, ui.Input, default, KeyModifiers.None) { DragEffects = DragDropEffects.Copy });
        PumpUntil(() => c.Attachments.Count == 1 && huge.Disposed && small.Disposed);
        Settle();
        Assert.Single(c.Attachments);
        Assert.Contains("Image too large to attach", ui.Texts);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Dropping_stops_reading_once_the_images_would_not_fit()
    {
        var c = new ChatConversation { MaxImages = 1 };
        var ui = Open(c);
        var files = new[] { new FakeFile("a.png", Png(8, 8)), new FakeFile("b.png", Png(8, 8)), new FakeFile("c.png", Png(8, 8)) };
        var data = new DataObject();
        data.Set(DataFormats.Files, files.Cast<IStorageItem>().ToArray());
        ui.Input.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, data, ui.Input, default, KeyModifiers.None) { DragEffects = DragDropEffects.Copy });
        PumpUntil(() => files.All(f => f.Disposed));
        Settle();
        Assert.Equal(new[] { 1, 1, 0 }, files.Select(f => f.Opens).ToArray()); // one fits, one says "too many", the third is never read
        Assert.Single(c.Attachments);
        ui.Window.Close();
    }

    // ---- switching conversations ----

    [AvaloniaFact]
    public void The_attachments_stay_with_their_conversation()
    {
        var a = new ChatConversation();
        var b = new ChatConversation();
        a.Attachments.Add(ChatImage.FromBytes(Png(8, 8), "image/png"));
        var ui = Open(a);
        Assert.Single(ui.Pills);

        ui.View.Conversation = b;
        UiHelpers.Pump();
        Assert.False(ui.Strip.IsVisible);
        Assert.False(SendButton(ui).IsEnabled);
        a.Attachments.Add(ChatImage.FromBytes(Png(8, 8), "image/png")); // A no longer reaches the view
        UiHelpers.Pump();
        Assert.False(ui.Strip.IsVisible);

        ui.View.Conversation = a;
        UiHelpers.Pump();
        Assert.Equal(2, ui.Pills.Count);
        Assert.True(SendButton(ui).IsEnabled);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void An_image_goes_to_the_conversation_it_was_added_in()
    {
        try
        {
            var read = new TaskCompletionSource<byte[]?>();
            ClipboardImages.HasTextOverride = () => false;
            ClipboardImages.HasImageOverride = () => true;
            ClipboardImages.ReadOverride = () => read.Task;
            var a = new ChatConversation();
            var b = new ChatConversation();
            var ui = Open(a);

            // pasted in A; B is shown while the clipboard is read
            ui.Input.Focus();
            UiHelpers.Press(ui.Window, Key.V, RawInputModifiers.Control);
            Settle();
            ui.View.Conversation = b;
            UiHelpers.Pump();
            read.SetResult(Png(10, 10));
            PumpUntil(() => a.Attachments.Count == 1);
            Settle();
            Assert.Empty(b.Attachments);
            Assert.False(ui.Strip.IsVisible);

            // picked in B; A is shown while the dialog is open
            var pick = new TaskCompletionSource<IReadOnlyList<byte[]>>();
            ui.Composer.PickImagesOverride = () => pick.Task;
            UiHelpers.Click(ui.Window, UiHelpers.ButtonWithText(ui.Composer, "Add"));
            ui.View.Conversation = a;
            UiHelpers.Pump();
            pick.SetResult(new[] { Png(12, 12) });
            PumpUntil(() => b.Attachments.Count == 1);
            Settle();
            Assert.Single(a.Attachments);
            Assert.Single(ui.Pills); // A's own
            ui.Window.Close();
        }
        finally
        {
            ResetClipboard();
        }
    }

    // ---- the preview and a permission request ----

    private static void Ask(ChatConversation c, string id)
    {
        using var input = System.Text.Json.JsonDocument.Parse("{\"command\":\"ls\"}");
        c.Apply(new ClaudeVisualChat.Protocol.PermissionRequested(id, "Bash", "Bash", null, "tool-" + id, input.RootElement.Clone(), null));
        UiHelpers.Pump();
    }

    [AvaloniaFact]
    public void A_request_waits_for_the_preview_to_close()
    {
        var c = new ChatConversation();
        var answers = new List<PermissionAnsweredEventArgs>();
        c.PermissionAnswered += (_, e) => answers.Add(e);
        c.Attachments.Add(ChatImage.FromBytes(Png(96, 48), "image/png"));
        var ui = Open(c);
        var prompt = UiHelpers.Prompt(ui.View);

        UiHelpers.Click(ui.Window, ui.Pills[0]);
        Assert.True(ui.Preview.IsVisible);
        Ask(c, "r1");
        Assert.True(prompt.IsVisible);
        Assert.False(prompt.IsKeyboardFocusWithin); // under the backdrop: not answerable
        Assert.True(ui.Preview.IsKeyboardFocusWithin);

        UiHelpers.Press(ui.Window, Key.D1);
        Assert.Empty(answers);
        Assert.True(ui.Preview.IsVisible);

        UiHelpers.Press(ui.Window, Key.Escape); // Esc still closes the preview
        Assert.False(ui.Preview.IsVisible);
        Assert.Empty(answers);
        Assert.True(prompt.IsKeyboardFocusWithin); // now the request has the keyboard

        // Enter in the preview closes it (its close button), and answers nothing
        UiHelpers.Click(ui.Window, ui.Pills[0]);
        Assert.True(ui.Preview.IsKeyboardFocusWithin);
        UiHelpers.Press(ui.Window, Key.Enter);
        Assert.False(ui.Preview.IsVisible);
        Assert.Empty(answers);
        Assert.True(prompt.IsKeyboardFocusWithin);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_request_leaves_the_keyboard_to_a_box_with_an_image()
    {
        var c = new ChatConversation();
        c.Attachments.Add(ChatImage.FromBytes(Png(8, 8), "image/png"));
        var ui = Open(c);
        ui.Input.Focus();

        Ask(c, "r1"); // the user is about to send the image: Enter must stay the reply box's

        Assert.True(UiHelpers.Prompt(ui.View).IsVisible);
        Assert.True(ui.Input.IsFocused);
        Assert.False(UiHelpers.Prompt(ui.View).IsKeyboardFocusWithin);
        ui.Window.Close();
    }

    // ---- the look ----

    [AvaloniaFact]
    public void The_strip_and_its_pills_have_the_extension_measures()
    {
        var c = new ChatConversation();
        c.Attachments.Add(ChatImage.FromBytes(Png(96, 48), "image/png"));
        c.Attachments.Add(ChatImage.FromBytes(Png(20, 30), "image/png"));
        var ui = Open(c);
        var strip = ui.Strip;
        var input = ui.Input;

        // above the text, inside the box: 6 px around the pills, a line under it
        var top = strip.TranslatePoint(default, ui.Box)!.Value;
        Assert.Equal(1, top.Y, 1);
        Assert.Equal(6 + 24 + 6 + 1, strip.Bounds.Height, 1);
        Assert.True(input.TranslatePoint(default, ui.Box)!.Value.Y >= top.Y + strip.Bounds.Height);
        Assert.Equal(new Thickness(0, 0, 0, 1), strip.BorderThickness);

        var pills = ui.Pills;
        var first = pills[0].TranslatePoint(default, strip)!.Value;
        var second = pills[1].TranslatePoint(default, strip)!.Value;
        Assert.Equal(6, first.X, 1);
        Assert.Equal(6, first.Y, 1);
        Assert.Equal(first.X + pills[0].Bounds.Width + 4, second.X, 1); // 4 px apart

        var frame = pills[0].GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_ChatButton");
        Assert.Equal(new CornerRadius(4), frame.CornerRadius);
        Assert.Equal(new Thickness(1), frame.BorderThickness);
        var thumb = pills[0].GetVisualDescendants().OfType<Image>().Single();
        Assert.Equal(1 + 4, thumb.TranslatePoint(default, pills[0])!.Value.X, 1); // the border, then 4 px
        var label = pills[0].GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "image.png");
        Assert.Equal(11, label.FontSize);
        Assert.Equal(Avalonia.Media.FontWeight.Medium, label.FontWeight);
        var meta = pills[0].GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "96×48");
        Assert.Equal(11, meta.FontSize);
        Assert.Equal(0.6, meta.Opacity, 2);
        Assert.True(pills[0].Bounds.Width <= 180);

        // the ×: 32 px over the right end, shown with the pointer on the pill
        var remove = pills[0].GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Remove attachment");
        Assert.Equal(32, remove.Bounds.Width);
        Assert.Equal(0, remove.Opacity);
        var center = pills[0].TranslatePoint(new Point(10, 10), ui.Window)!.Value;
        ui.Window.MouseMove(center, RawInputModifiers.None);
        UiHelpers.Pump();
        Assert.Equal(1, remove.Opacity);
        ui.Window.Close();
    }
}
