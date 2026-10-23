using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Shape = Avalonia.Controls.Shapes.Path;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using ClaudeVisualChat.Controls;
using ClaudeVisualChat.Protocol;
using SkiaSharp;
using Xunit;

namespace ClaudeVisualChat.Tests.Ui;

/// <summary>PDF, text and CSV files in the reply box and in the messages: the + button, a drop, the chips and their progress.</summary>
public sealed class FileAttachmentTests : IDisposable
{
    private readonly string _folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cvc-ui-files-" + Guid.NewGuid().ToString("N"));

    public FileAttachmentTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, true);
        }
        catch (IOException)
        {
        }
    }

    private string Write(string name, byte[] bytes)
    {
        var path = System.IO.Path.Combine(_folder, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n" + new string('x', 2000) + "\n%%EOF\n");

    private static byte[] Png(int w, int h)
    {
        using var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        bmp.Erase(new SKColor(30, 120, 200));
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

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
        }

        public ChatConversation Conversation { get; }
        public ClaudeChatView View { get; }
        public Window Window { get; }
        public ChatComposer Composer { get; }
        public TextBox Input { get; }
        public List<MessageSubmittedEventArgs> Sent { get; } = new List<MessageSubmittedEventArgs>();

        public AttachmentStrip Strip => Composer.GetVisualDescendants().OfType<AttachmentStrip>().Single();

        /// <summary>The chips of files in the reply box.</summary>
        public List<Button> FileChips =>
            Strip.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("ClaudeChatFileChip")).ToList();

        public List<string> Texts => UiHelpers.Texts(Composer);
    }

    private static Opened Open(ChatConversation? conversation = null) => new Opened(conversation ?? new ChatConversation());

    private static Button RemoveOf(Button chip) =>
        chip.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("ClaudeChatAttachmentRemove"));

    private static void ResetClipboard()
    {
        ClaudeVisualChat.Imaging.ClipboardImages.HasTextOverride = null;
        ClaudeVisualChat.Imaging.ClipboardImages.HasImageOverride = null;
        ClaudeVisualChat.Imaging.ClipboardImages.HasFilesOverride = null;
        ClaudeVisualChat.Imaging.ClipboardImages.ReadOverride = null;
        AttachFiles.PastedFilesOverride = null;
    }

    [AvaloniaFact]
    public void Pasting_files_copied_in_explorer_attaches_them_like_a_drop()
    {
        try
        {
            var pdf = Write("paper.pdf", Pdf);
            var png = Write("shot.png", Png(40, 20));
            var zip = Write("bundle.zip", new byte[] { 0x50, 0x4B, 3, 4 });
            var bitmapRead = 0;
            ClaudeVisualChat.Imaging.ClipboardImages.HasTextOverride = () => false;
            ClaudeVisualChat.Imaging.ClipboardImages.HasFilesOverride = () => true;
            ClaudeVisualChat.Imaging.ClipboardImages.HasImageOverride = () => true; // a bitmap too: the files win
            ClaudeVisualChat.Imaging.ClipboardImages.ReadOverride = () =>
            {
                bitmapRead++;
                return Task.FromResult<byte[]?>(Png(10, 10));
            };
            AttachFiles.PastedFilesOverride = () => Task.FromResult<IReadOnlyList<string>>(new[] { pdf, png, zip });
            var ui = Open();
            ui.Input.Focus();
            UiHelpers.Type(ui.Window, "see ");

            UiHelpers.Press(ui.Window, Key.V, RawInputModifiers.Control);

            // as a drop: the PDF to the files, the image file to the images, the other one refused with its reason
            UiHelpers.PumpUntil(() => ui.Conversation.Files.Count == 1 && ui.Conversation.Attachments.Count == 1);
            Assert.Equal("paper.pdf", ui.Conversation.Files[0].Name);
            Assert.Equal(40, ui.Conversation.Attachments[0].Width);
            Assert.Contains("Only PDF, text and CSV files", ui.Texts);
            Assert.Equal(0, bitmapRead);
            Assert.Equal("see ", ui.Input.Text); // nothing pasted as text
            ui.Window.Close();
        }
        finally
        {
            ResetClipboard();
        }
    }

    [AvaloniaFact]
    public void Pasting_text_with_files_on_the_clipboard_pastes_the_text()
    {
        try
        {
            var asked = 0;
            ClaudeVisualChat.Imaging.ClipboardImages.HasTextOverride = () => true;
            ClaudeVisualChat.Imaging.ClipboardImages.HasFilesOverride = () => true;
            ClaudeVisualChat.Imaging.ClipboardImages.HasImageOverride = () => false;
            AttachFiles.PastedFilesOverride = () =>
            {
                asked++;
                return Task.FromResult<IReadOnlyList<string>>(new[] { Write("x.txt", Encoding.UTF8.GetBytes("x")) });
            };
            var ui = Open();
            ui.Window.Clipboard!.SetTextAsync("hello").Wait();
            ui.Input.Focus();

            UiHelpers.Press(ui.Window, Key.V, RawInputModifiers.Control);
            UiHelpers.Settle();

            Assert.Equal("hello", ui.Input.Text);
            Assert.Empty(ui.Conversation.Files);
            Assert.Equal(0, asked);
            ui.Window.Close();
        }
        finally
        {
            ResetClipboard();
        }
    }

    [AvaloniaFact]
    public void With_no_room_for_files_the_dialog_offers_images_only()
    {
        var both = AttachFiles.Options(ChatStrings.English, files: true).FileTypeFilter!;
        Assert.Equal(new[] { "Images, PDF, text and CSV", ChatStrings.English.ImageFiles }, both.Select(f => f.Name));
        Assert.Contains("*.pdf", both[0].Patterns!);

        var images = Assert.Single(AttachFiles.Options(ChatStrings.English, files: false).FileTypeFilter!);
        Assert.Equal(ChatStrings.English.ImageFiles, images.Name);
        Assert.DoesNotContain(images.Patterns!, p => p == "*.pdf" || p == "*.txt" || p == "*.csv");
    }

    [AvaloniaFact]
    public void Mais_escolhe_arquivo_e_mostra_o_chip()
    {
        var ui = Open();
        var pdf = Write("report.pdf", Pdf);
        var png = Write("shot.png", Png(40, 20));
        var picked = 0;
        ui.Composer.PickFilesOverride = () =>
        {
            picked++;
            return Task.FromResult<IReadOnlyList<string>>(new[] { pdf, png });
        };

        UiHelpers.Click(ui.Window, UiHelpers.ButtonWithText(ui.Composer, "Add"));

        UiHelpers.PumpUntil(() => ui.Conversation.Files.Count == 1 && ui.Conversation.Attachments.Count == 1);
        Assert.Equal(1, picked);
        Assert.Equal("report.pdf", ui.Conversation.Files[0].Name);
        Assert.Equal(40, ui.Conversation.Attachments[0].Width); // the image takes the images' way
        Assert.True(ui.Strip.IsEffectivelyVisible);

        // the chip: the type's icon, the name, the size and the ×
        var chip = Assert.Single(ui.FileChips);
        Assert.Equal(24, chip.Bounds.Height);
        Assert.Contains("report.pdf", UiHelpers.Texts(chip));
        Assert.Contains("2 KB", UiHelpers.Texts(chip));
        var icon = chip.GetVisualDescendants().OfType<Shape>().Single(p => p.Classes.Contains("ClaudeChatFileIcon"));
        Assert.Equal(icon.FindResource("ClaudeChat.Icon.FilePdf"), icon.Data);
        Assert.True(RemoveOf(chip).IsVisible);
        Assert.Equal("Remove attachment", AutomationProperties.GetName(RemoveOf(chip)));

        // the × takes it off
        UiHelpers.Click(ui.Window, RemoveOf(chip));
        Assert.Empty(ui.Conversation.Files);
        Assert.Empty(ui.FileChips);
        Assert.True(ui.Strip.IsVisible); // the image is still there

        // sending a file alone: the message shows its chip, with name and size
        ui.Composer.PickFilesOverride = () => Task.FromResult<IReadOnlyList<string>>(new[] { Write("data.csv", Encoding.UTF8.GetBytes("a,b\n1,2\n")) });
        UiHelpers.Click(ui.Window, UiHelpers.ButtonWithText(ui.Composer, "Add"));
        UiHelpers.PumpUntil(() => ui.Conversation.Files.Count == 1);
        ui.Conversation.RemoveAttachment(ui.Conversation.Attachments[0]);
        UiHelpers.Pump();
        Assert.True(UiHelpers.ButtonWithText(ui.Composer, "Send").IsEnabled);
        ui.Input.Focus();
        UiHelpers.Press(ui.Window, Key.Enter);
        var sent = Assert.Single(ui.Sent);
        Assert.Equal("data.csv", Assert.Single(sent.Files).Name);
        Assert.False(ui.Strip.IsVisible);
        UiHelpers.Pump();
        var shown = MessageChips(ui.View);
        var messageChip = Assert.Single(shown);
        Assert.Equal(new[] { "data.csv", "1 KB" }, UiHelpers.Texts(messageChip));
        var tableIcon = messageChip.GetVisualDescendants().OfType<Shape>().Single(p => p.Classes.Contains("ClaudeChatFileIcon"));
        Assert.Equal(tableIcon.FindResource("ClaudeChat.Icon.Table"), tableIcon.Data);
        ui.Window.Close();
    }

    private static List<Border> MessageChips(Visual root) =>
        root.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("ClaudeChatMessageFile") && b.IsEffectivelyVisible).ToList();

    [AvaloniaFact]
    public void Arrastar_imagem_e_pdf_separa_os_dois()
    {
        var c = new ChatConversation();
        var ui = Open(c);
        var png = new LocalFile(Write("one.png", Png(96, 48)));
        var pdf = new LocalFile(Write("paper.pdf", Pdf));
        var log = new LocalFile(Write("server.log", Encoding.UTF8.GetBytes("ok\n")));
        var data = new DataObject();
        data.Set(DataFormats.Files, new IStorageItem[] { png, pdf, log });

        var over = new DragEventArgs(DragDrop.DragOverEvent, data, ui.Input, default, KeyModifiers.None) { DragEffects = DragDropEffects.Copy | DragDropEffects.Move };
        ui.Input.RaiseEvent(over);
        Assert.Equal(DragDropEffects.Copy, over.DragEffects);
        ui.Input.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, data, ui.Input, default, KeyModifiers.None) { DragEffects = DragDropEffects.Copy });

        UiHelpers.PumpUntil(() => c.Attachments.Count == 1 && c.Files.Count == 2 && png.Disposed && pdf.Disposed && log.Disposed);
        Assert.Equal(96, c.Attachments[0].Width);
        Assert.Equal(new[] { "paper.pdf", "server.log" }, c.Files.Select(f => f.Name).ToArray());
        Assert.Equal(System.IO.Path.GetFullPath(pdf.LocalPath), c.Files[0].LocalPath);
        Assert.Equal(0, pdf.Opens); // read from its path, not loaded through the drop
        Assert.Equal(2, ui.FileChips.Count);
        var textIcon = ui.FileChips[1].GetVisualDescendants().OfType<Shape>().Single(p => p.Classes.Contains("ClaudeChatFileIcon"));
        Assert.Equal(textIcon.FindResource("ClaudeChat.Icon.FileText"), textIcon.Data);

        // a file of another kind: the others go in, and the box says why that one did not
        var zip = new LocalFile(Write("bundle.zip", new byte[] { 0x50, 0x4B, 3, 4, 0 }));
        var more = new LocalFile(Write("more.txt", Encoding.UTF8.GetBytes("more")));
        var mixed = new DataObject();
        mixed.Set(DataFormats.Files, new IStorageItem[] { zip, more });
        ui.Input.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, mixed, ui.Input, default, KeyModifiers.None) { DragEffects = DragDropEffects.Copy });
        UiHelpers.PumpUntil(() => c.Files.Count == 3 && ui.Texts.Contains("Only PDF, text and CSV files"));

        // over the count: the message names the limit
        c.MaxFiles = 3;
        var sixth = new DataObject();
        sixth.Set(DataFormats.Files, new IStorageItem[] { new LocalFile(Write("again.md", Encoding.UTF8.GetBytes("# x"))) });
        ui.Input.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, sixth, ui.Input, default, KeyModifiers.None) { DragEffects = DragDropEffects.Copy });
        UiHelpers.PumpUntil(() => ui.Texts.Contains("Up to 3 files per message"));
        Assert.DoesNotContain("Only PDF, text and CSV files", ui.Texts);

        // too big, in Portuguese
        c.MaxFileBytes = 1024 * 1024;
        c.MaxFiles = 5;
        ui.View.Strings = ChatStrings.PortugueseBrazil;
        var big = System.IO.Path.Combine(_folder, "big.log");
        using (var stream = new FileStream(big, FileMode.CreateNew)) stream.SetLength(2 * 1024 * 1024);
        var tooBig = new DataObject();
        tooBig.Set(DataFormats.Files, new IStorageItem[] { new LocalFile(big) });
        ui.Input.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, tooBig, ui.Input, default, KeyModifiers.None) { DragEffects = DragDropEffects.Copy });
        UiHelpers.PumpUntil(() => ui.Texts.Contains("Arquivo maior que 1 MB"));
        Assert.Equal(3, c.Files.Count);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Chip_mostra_o_progresso_e_esconde_o_x()
    {
        var c = new ChatConversation();
        c.AddFile("notes.txt", Encoding.UTF8.GetBytes(new string('n', 3000)));
        var ui = Open(c);
        var file = c.Files[0];
        var chip = Assert.Single(ui.FileChips);
        Assert.Contains("3 KB", UiHelpers.Texts(chip));
        Assert.True(RemoveOf(chip).IsVisible);

        file.Progress = 0.42;
        UiHelpers.Pump();
        Assert.Contains("Sending 42%", UiHelpers.Texts(chip));
        Assert.DoesNotContain("3 KB", UiHelpers.Texts(chip));
        Assert.False(RemoveOf(chip).IsVisible);

        file.Progress = null;
        UiHelpers.Pump();
        Assert.Contains("3 KB", UiHelpers.Texts(chip));
        Assert.True(RemoveOf(chip).IsVisible);

        // sent: the message's chip shows the upload, and the size again once the file is on the machine
        c.Submit("read it");
        UiHelpers.Pump();
        ui.View.Strings = ChatStrings.PortugueseBrazil;
        file.Progress = 0.076;
        UiHelpers.Pump();
        var shown = Assert.Single(MessageChips(ui.View));
        Assert.Equal(new[] { "notes.txt", "Enviando 8%" }, UiHelpers.Texts(shown));
        file.Progress = 1;
        file.RemotePath = "/home/demo/anexos/c1/notes.txt";
        UiHelpers.Pump();
        Assert.Equal(new[] { "notes.txt", "3 KB" }, UiHelpers.Texts(shown));
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Progress_set_from_another_thread_reaches_the_chip()
    {
        var c = new ChatConversation();
        c.AddFile("notes.txt", Encoding.UTF8.GetBytes(new string('n', 3000)));
        var ui = Open(c);
        var file = c.Files[0];
        var chip = Assert.Single(ui.FileChips);

        // the host's upload worker (a thread of its own: Task.Wait could run the task on this one): the chip does
        // not throw, and draws on the UI thread
        OnWorker(() => file.Progress = 0.3);
        Assert.Contains("3 KB", AllTexts(chip)); // nothing drawn from the worker: the update waits for the UI thread
        UiHelpers.PumpUntil(() => AllTexts(chip).Contains("Sending 30%"));
        Assert.False(RemoveOf(chip).IsVisible);

        // taken out of the window before the posted update runs: that chip draws nothing more
        OnWorker(() => file.Progress = 0.6);
        ui.Window.Content = null;
        UiHelpers.Pump();
        Assert.Contains("Sending 30%", AllTexts(chip));
        ui.Window.Content = ui.View;
        UiHelpers.Pump();
        Assert.Contains("Sending 60%", UiHelpers.Texts(Assert.Single(ui.FileChips)));
        ui.Window.Close();
    }

    private static void OnWorker(Action work)
    {
        Exception? failed = null;
        var worker = new System.Threading.Thread(() =>
        {
            try
            {
                work();
            }
            catch (Exception e)
            {
                failed = e;
            }
        });
        worker.Start();
        worker.Join();
        Assert.Null(failed);
    }

    /// <summary>The texts of a chip, also while it is out of the window.</summary>
    private static List<string> AllTexts(Visual root) =>
        root.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();

    [AvaloniaFact]
    public void A_history_shows_the_chips_without_size()
    {
        var c = new ChatConversation();
        c.LoadTranscript(new[]
        {
            "{\"type\":\"user\",\"uuid\":\"u1\",\"timestamp\":\"2026-10-23T12:00:00.000Z\",\"sessionId\":\"s1\",\"message\":{\"role\":\"user\",\"content\":" +
            System.Text.Json.JsonSerializer.Serialize("what is in it?\n\n" + AttachedFileLine.Format("/home/demo/anexos/c1/paper.pdf")) + "}}",
        });
        var ui = Open(c);

        var chip = Assert.Single(MessageChips(ui.View));
        Assert.Equal(new[] { "paper.pdf" }, UiHelpers.Texts(chip));
        Assert.Equal("/home/demo/anexos/c1/paper.pdf", ToolTip.GetTip(chip));
        Assert.Contains("what is in it?", UiHelpers.Texts(ui.View));
        Assert.DoesNotContain(UiHelpers.Texts(ui.View), t => t.Contains("[attached file:"));
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void The_chips_have_the_extension_measures()
    {
        var c = new ChatConversation();
        c.AddFile("a-file-with-a-rather-long-name-that-is-cut.txt", Encoding.UTF8.GetBytes("hello"));
        var ui = Open(c);
        var chip = Assert.Single(ui.FileChips);

        // the image pill's frame: 24 px, 4 px corners, 180 px at most, a 12 px icon 4 px in
        Assert.Equal(24, chip.Bounds.Height);
        Assert.True(chip.Bounds.Width <= 180);
        var frame = chip.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_ChatButton");
        Assert.Equal(new CornerRadius(4), frame.CornerRadius);
        var icon = chip.GetVisualDescendants().OfType<Viewbox>().Single(v => v.Classes.Contains("ClaudeChatFileIcon"));
        Assert.Equal(12, icon.Bounds.Width);
        Assert.Equal(12, icon.Bounds.Height);
        Assert.Equal(1 + 4, icon.TranslatePoint(default, chip)!.Value.X, 1);
        var meta = chip.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "1 KB");
        Assert.Equal(11, meta.FontSize);
        Assert.Equal(0.6, meta.Opacity, 2);

        // in the message: the same pill, above the text, 6 px over it
        c.Submit("see");
        UiHelpers.Pump();
        var shown = Assert.Single(MessageChips(ui.View));
        Assert.Equal(24, shown.Bounds.Height);
        Assert.True(shown.Bounds.Width <= 180);
        var text = ui.View.GetVisualDescendants().OfType<SelectableTextBlock>().Single(t => t.Text == "see");
        var row = ui.View.GetVisualDescendants().OfType<MessageAttachments>().Single(m => m.IsVisible);
        Assert.Same(row.Parent, text.Parent); // in the message's box, before the text
        Assert.Equal(6, text.Bounds.Top - row.Bounds.Bottom, 1);
        Assert.Equal(shown.Bounds.Height, row.Bounds.Height, 1);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void Images_and_files_of_a_message_share_one_row_above_the_text()
    {
        var c = new ChatConversation();
        c.Attachments.Add(ChatImage.FromBytes(Png(96, 48), "image/png"));
        c.AddFile("paper.pdf", Pdf);
        c.AddFile("data.csv", Encoding.UTF8.GetBytes("a,b"));
        var ui = Open(c);
        c.Submit("both");
        c.Items.Add(new UserMessageItem("", null, null)); // and a message with nothing attached: no row
        UiHelpers.Pump();

        var box = ui.View.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("ClaudeChatUser"));
        var row = box.GetVisualDescendants().OfType<MessageAttachments>().Single();
        var thumb = box.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("ClaudeChatMessageImage"));
        var chips = MessageChips(box);
        Assert.Equal(2, chips.Count);

        // one row, in order: the image, then the files, 4 px apart, all at its top
        var thumbAt = thumb.TranslatePoint(default, row)!.Value;
        var firstAt = chips[0].TranslatePoint(default, row)!.Value;
        var secondAt = chips[1].TranslatePoint(default, row)!.Value;
        Assert.Equal(0, thumbAt.X, 1);
        Assert.Equal(96 + 2 + 4, firstAt.X, 1); // the thumbnail's picture and its 1 px frame
        Assert.Equal(firstAt.X + chips[0].Bounds.Width + 4, secondAt.X, 1);
        Assert.Equal(0, thumbAt.Y, 1);
        Assert.Equal(0, firstAt.Y, 1);
        Assert.Equal(50, row.Bounds.Height, 1);

        // above the text
        var text = box.GetVisualDescendants().OfType<SelectableTextBlock>().Single(t => t.Text == "both");
        Assert.Equal(6, text.Bounds.Top - row.Bounds.Bottom, 1);

        var rows = ui.View.GetVisualDescendants().OfType<MessageAttachments>().ToList();
        Assert.Equal(2, rows.Count);
        Assert.False(rows[1].IsVisible);
        ui.Window.Close();
    }

    [AvaloniaFact]
    public void A_chip_leaves_its_file_alone_once_out_of_the_window()
    {
        var c = new ChatConversation();
        c.AddFile("notes.txt", Encoding.UTF8.GetBytes("hello"));
        var file = c.Files[0];
        var ui = Open(c);
        Assert.Single(ui.FileChips);

        ui.Window.Content = null; // the host takes the view out
        UiHelpers.Pump();
        file.Progress = 0.5; // nothing listens, nothing draws

        ui.Window.Content = ui.View;
        UiHelpers.Pump();
        Assert.Contains("Sending 50%", UiHelpers.Texts(Assert.Single(ui.FileChips)));
        ui.Window.Close();
    }

    /// <summary>A dropped file that lives on the disk.</summary>
    private sealed class LocalFile : IStorageFile
    {
        public LocalFile(string path) => LocalPath = path;

        public string LocalPath { get; }

        public string Name => System.IO.Path.GetFileName(LocalPath);

        public Uri Path => new Uri(LocalPath);

        public bool CanBookmark => false;

        public int Opens { get; private set; }

        public bool Disposed { get; private set; }

        public Task<StorageItemProperties> GetBasicPropertiesAsync() => Task.FromResult(new StorageItemProperties());

        public Task<string?> SaveBookmarkAsync() => Task.FromResult<string?>(null);

        public Task<IStorageFolder?> GetParentAsync() => Task.FromResult<IStorageFolder?>(null);

        public Task DeleteAsync() => Task.CompletedTask;

        public Task<IStorageItem?> MoveAsync(IStorageFolder destination) => Task.FromResult<IStorageItem?>(null);

        public Task<Stream> OpenReadAsync()
        {
            Opens++;
            return Task.FromResult<Stream>(File.OpenRead(LocalPath));
        }

        public Task<Stream> OpenWriteAsync() => throw new NotSupportedException();

        public void Dispose() => Disposed = true;
    }
}
