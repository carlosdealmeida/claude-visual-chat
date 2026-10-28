using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ClaudeVisualChat.Protocol;
using Xunit;

namespace ClaudeVisualChat.Tests;

/// <summary>
/// Files attached to a message (PDF, text, CSV): the rules for what goes in, what the host gets, and the lines the host
/// adds at the end of the message, read back as chips.
/// </summary>
public sealed class ChatFileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "cvc-files-" + Guid.NewGuid().ToString("N"));

    public ChatFileTests() => Directory.CreateDirectory(_folder);

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
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private string WriteText(string name, string text) => Write(name, Encoding.UTF8.GetBytes(text));

    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj\n<< /Type /Catalog >>\nendobj\n%%EOF\n");

    // ---- what goes in ----

    [Fact]
    public async Task Pdf_de_verdade_entra()
    {
        var c = new ChatConversation();
        var path = Write("report.pdf", Pdf);

        Assert.Equal(AddFileResult.Added, await c.AddFileAsync(path));

        var file = Assert.Single(c.Files);
        Assert.Equal("report.pdf", file.Name);
        Assert.Equal(Pdf.Length, file.Size);
        Assert.Equal("application/pdf", file.MediaType);
        Assert.Equal(ChatFileKind.Pdf, file.Kind);
        Assert.Equal(Path.GetFullPath(path), file.LocalPath);
        Assert.Null(file.Content); // the host reads it from the disk, in parts
        Assert.Null(file.Progress);
        Assert.Null(file.RemotePath);

        // a .pdf that is not a PDF, and a PDF under another name
        Assert.Equal(AddFileResult.NotSupported, await c.AddFileAsync(WriteText("fake.pdf", "hello")));
        Assert.Equal(AddFileResult.NotSupported, await c.AddFileAsync(Write("report.docx", Pdf)));
        Assert.Equal(AddFileResult.NotSupported, await c.AddFileAsync(Path.Combine(_folder, "missing.pdf")));
        Assert.Single(c.Files);
    }

    [Fact]
    public async Task Text_and_csv_kinds_and_media_types()
    {
        var c = new ChatConversation();
        foreach (var name in new[] { "a.txt", "b.LOG", "c.md", "d.json", "e.csv" })
            Assert.Equal(AddFileResult.Added, await c.AddFileAsync(WriteText(name, "x,y\n1,2\n")));

        Assert.Equal(new[] { ChatFileKind.Text, ChatFileKind.Text, ChatFileKind.Text, ChatFileKind.Text, ChatFileKind.Csv },
            c.Files.Select(f => f.Kind).ToArray());
        Assert.Equal(new[] { "text/plain", "text/plain", "text/markdown", "application/json", "text/csv" },
            c.Files.Select(f => f.MediaType).ToArray());
    }

    [Fact]
    public async Task Txt_com_byte_nulo_e_recusado()
    {
        var c = new ChatConversation();
        var binary = Encoding.UTF8.GetBytes("text then a zero:").Concat(new byte[] { 0, 1, 2 }).ToArray();

        Assert.Equal(AddFileResult.NotSupported, await c.AddFileAsync(Write("dump.txt", binary)));
        Assert.Equal(AddFileResult.NotSupported, await c.AddFileAsync(Write("table.csv", binary)));
        Assert.Empty(c.Files);

        // only the first 8 KB are looked at
        var late = Enumerable.Repeat((byte)'a', 9000).Concat(new byte[] { 0 }).ToArray();
        Assert.Equal(AddFileResult.Added, await c.AddFileAsync(Write("late.log", late)));

        // the same rule for bytes that do not come from the disk
        Assert.Equal(AddFileResult.NotSupported, c.AddFile("dump.txt", binary));
        Assert.Equal(AddFileResult.Added, c.AddFile("notes.txt", Encoding.UTF8.GetBytes("hello")));
        Assert.Equal(2, c.Files.Count);
    }

    [Fact]
    public async Task Arquivo_maior_que_o_teto_e_recusado()
    {
        var c = new ChatConversation();
        Assert.Equal(25L * 1024 * 1024, c.MaxFileBytes);
        var path = Path.Combine(_folder, "huge.log");
        using (var stream = new FileStream(path, FileMode.CreateNew))
        {
            var start = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("a line of the log\n", 500))); // 9 KB of text
            stream.Write(start, 0, start.Length);
            stream.SetLength(26L * 1024 * 1024); // its size, without writing 26 MB
        }

        Assert.Equal(AddFileResult.TooBig, await c.AddFileAsync(path));
        Assert.Empty(c.Files);

        // the host may raise or lower the ceiling
        c.MaxFileBytes = 27L * 1024 * 1024;
        Assert.Equal(AddFileResult.Added, await c.AddFileAsync(path));
        Assert.Equal(26L * 1024 * 1024, c.Files[0].Size);

        c.MaxFileBytes = 4;
        Assert.Equal(AddFileResult.TooBig, c.AddFile("notes.txt", Encoding.UTF8.GetBytes("hello")));
    }

    [Fact]
    public async Task Sexto_arquivo_e_recusado()
    {
        var c = new ChatConversation();
        Assert.Equal(5, c.MaxFiles);
        for (var i = 0; i < 4; i++)
            Assert.Equal(AddFileResult.Added, await c.AddFileAsync(WriteText("n" + i + ".txt", "note")));
        Assert.Equal(AddFileResult.Added, c.AddFile("fifth.csv", Encoding.UTF8.GetBytes("a,b")));

        Assert.Equal(AddFileResult.TooMany, await c.AddFileAsync(WriteText("sixth.txt", "note")));
        Assert.Equal(AddFileResult.TooMany, c.AddFile("sixth.txt", Encoding.UTF8.GetBytes("note")));
        Assert.Equal(5, c.Files.Count);

        // images have their own count
        Assert.Empty(c.Attachments);
        c.RemoveFile(c.Files[0]);
        Assert.Equal(AddFileResult.Added, await c.AddFileAsync(WriteText("sixth.txt", "note")));
    }

    [Fact]
    public void Bytes_not_from_the_disk_stay_in_the_file()
    {
        var c = new ChatConversation();
        var bytes = Encoding.UTF8.GetBytes("a,b\n1,2\n");

        Assert.Equal(AddFileResult.Added, c.AddFile("table.csv", bytes));

        var file = Assert.Single(c.Files);
        Assert.Equal("table.csv", file.Name);
        Assert.Equal(bytes.Length, file.Size);
        Assert.Equal(ChatFileKind.Csv, file.Kind);
        Assert.Null(file.LocalPath);
        Assert.Same(bytes, file.Content);
        Assert.Equal(AddFileResult.NotSupported, c.AddFile("song.mp3", bytes));
    }

    [Fact]
    public void Progress_and_remote_path_are_the_hosts_and_notify()
    {
        var c = new ChatConversation();
        c.AddFile("notes.txt", Encoding.UTF8.GetBytes("hello"));
        var file = c.Files[0];
        var changed = new List<string?>();
        file.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        file.Progress = 0.42;
        file.RemotePath = "/home/demo/.claude-ops/conversas/anexos/c1/notes.txt";

        Assert.Equal(new[] { nameof(ChatFile.Progress), nameof(ChatFile.RemotePath) }, changed);
    }

    // ---- sending ----

    [Fact]
    public void Submit_so_com_arquivo_manda()
    {
        var c = new ChatConversation();
        var sent = new List<MessageSubmittedEventArgs>();
        c.MessageSubmitted += (_, e) => sent.Add(e);
        Assert.False(c.Submit(""));

        c.AddFile("notes.txt", Encoding.UTF8.GetBytes("hello"));
        var file = c.Files[0];
        Assert.True(c.Submit("  "));

        var e = Assert.Single(sent);
        Assert.Equal("", e.Text);
        Assert.Same(file, Assert.Single(e.Files));
        Assert.Empty(e.Images);
        Assert.Empty(c.Files);
        var shown = Assert.IsType<UserMessageItem>(Assert.Single(c.Items));
        Assert.Same(file, Assert.Single(shown.Files));

        // a message without files has an empty list, never null
        c.Submit("hi");
        Assert.Empty(sent[1].Files);
        Assert.Empty(Assert.Single(c.QueuedMessages).Files); // sent while busy: held (0.6.0)
        Assert.Empty(new MessageSubmittedEventArgs("x").Files);
        Assert.Empty(new UserMessageItem("x").Files);
    }

    [Fact]
    public void UndoSubmit_devolve_os_arquivos()
    {
        var c = new ChatConversation();
        MessageSubmittedEventArgs? sent = null;
        c.MessageSubmitted += (_, e) => sent = e;
        c.AddFile("notes.txt", Encoding.UTF8.GetBytes("hello"));
        c.AddFile("table.csv", Encoding.UTF8.GetBytes("a,b"));
        var files = c.Files.ToList();
        c.Submit("look");
        files[0].Progress = 0.5; // the host was sending it

        Assert.True(c.UndoSubmit(sent!));

        Assert.Equal(files, c.Files);
        Assert.Null(files[0].Progress); // back in the box, not being sent: the × shows again
        Assert.Empty(c.Items);

        // the box already holds other files: they stay, the sent ones do not come back on top
        c.Submit("again");
        c.AddFile("other.txt", Encoding.UTF8.GetBytes("x"));
        c.UndoSubmit(sent!);
        Assert.Equal("other.txt", Assert.Single(c.Files).Name);
    }

    // ---- the marker lines ----

    [Fact]
    public void Format_writes_the_marker_line()
    {
        Assert.Equal("[attached file: /home/demo/a.pdf]", AttachedFileLine.Format("/home/demo/a.pdf"));
        Assert.Equal(@"[attached file: C:\demo\app\b c.csv]", AttachedFileLine.Format(@"C:\demo\app\b c.csv"));
        Assert.Throws<ArgumentNullException>(() => AttachedFileLine.Format(null!));
    }

    [Fact]
    public void Linha_marcadora_no_fim_vira_chip_e_some_do_texto()
    {
        var message = "look at these\n\n" + AttachedFileLine.Format("/home/demo/anexos/c1/report.pdf") + "\n" +
                      AttachedFileLine.Format(@"C:\demo\anexos\c1\table.csv");

        var (text, paths) = AttachedFileLine.Split(message);

        Assert.Equal("look at these", text);
        Assert.Equal(new[] { "/home/demo/anexos/c1/report.pdf", @"C:\demo\anexos\c1\table.csv" }, paths);

        // Windows line breaks, a file alone, and nothing to split
        AssertSplit("hi\r\n[attached file: /a.txt]\r\n", "hi", "/a.txt");
        AssertSplit("[attached file: /a.txt]", "", "/a.txt");
        AssertSplit("just text\n", "just text\n");

        // live: the claude repeats the line with the markers; the message shows the text and the chips
        var c = new ChatConversation { EchoSubmittedMessages = false };
        c.AppendLine(StreamUserLine(message));
        var item = Assert.IsType<UserMessageItem>(Assert.Single(c.Items));
        Assert.Equal("look at these", item.Text);
        Assert.Equal(new[] { "report.pdf", "table.csv" }, item.Files.Select(f => f.Name).ToArray());
        Assert.Equal(new[] { ChatFileKind.Pdf, ChatFileKind.Csv }, item.Files.Select(f => f.Kind).ToArray());
        Assert.All(item.Files, f => Assert.Equal(-1, f.Size)); // unknown
        Assert.Equal("/home/demo/anexos/c1/report.pdf", item.Files[0].RemotePath);
    }

    private static void AssertSplit(string message, string text, params string[] paths)
    {
        var split = AttachedFileLine.Split(message);
        Assert.Equal(text, split.Text);
        Assert.Equal(paths, split.Paths);
    }

    [Fact]
    public void The_echo_of_a_message_with_files_is_not_shown_twice()
    {
        var c = new ChatConversation();
        c.MessageSubmitted += (_, e) => Assert.Single(e.Files);
        c.AddFile("notes.txt", Encoding.UTF8.GetBytes("hello"));
        c.Submit("look");

        // the host sent the text with the marker line; a claude that repeats user lines repeats it so
        c.AppendLine(StreamUserLine("look\n" + AttachedFileLine.Format("/home/demo/anexos/c1/notes.txt")));

        var item = Assert.IsType<UserMessageItem>(Assert.Single(c.Items));
        Assert.Equal("notes.txt", Assert.Single(item.Files).Name);
    }

    [Fact]
    public void The_echo_of_a_text_ending_in_a_line_break_with_a_file_is_shown_once()
    {
        var c = new ChatConversation();
        c.AddFile("notes.txt", Encoding.UTF8.GetBytes("hello"));
        c.Submit("look\n");

        // the host adds the marker after the text as it is; Split takes the line breaks before the markers off
        c.AppendLine(StreamUserLine("look\n\n" + AttachedFileLine.Format("/home/demo/anexos/c1/notes.txt")));

        var item = Assert.IsType<UserMessageItem>(Assert.Single(c.Items));
        Assert.Equal("notes.txt", Assert.Single(item.Files).Name);
    }

    [Fact]
    public void Trailing_spaces_do_not_hide_the_markers()
    {
        AssertSplit("hi\n[attached file: /a.txt]  \n \t\n", "hi", "/a.txt");
        AssertSplit("hi\r\n[attached file: /a.txt] \r\n[attached file: /b.csv]\t", "hi", "/a.txt", "/b.csv");
        AssertSplit("hi \n \n", "hi \n \n"); // nothing to split: the message as it is
    }

    [Fact]
    public void A_path_that_ends_in_a_separator_keeps_the_whole_path_as_its_name()
    {
        var c = new ChatConversation { EchoSubmittedMessages = false };
        c.AppendLine(StreamUserLine("x\n" + AttachedFileLine.Format("/home/demo/folder/") + "\n" + AttachedFileLine.Format(@"C:\demo\b.csv")));

        var item = Assert.IsType<UserMessageItem>(Assert.Single(c.Items));
        Assert.Equal(new[] { "/home/demo/folder/", "b.csv" }, item.Files.Select(f => f.Name).ToArray());
    }

    [Fact]
    public void Linha_marcadora_no_meio_do_texto_fica_como_texto()
    {
        var message = "before\n[attached file: /home/demo/a.pdf]\nafter";

        var (text, paths) = AttachedFileLine.Split(message);

        Assert.Equal(message, text);
        Assert.Empty(paths);

        // a line that only looks like one stays too
        Assert.Empty(AttachedFileLine.Split("x\n [attached file: /a.txt]").Paths);
        Assert.Empty(AttachedFileLine.Split("x\n[attached file: /a.txt] and more").Paths);
        Assert.Empty(AttachedFileLine.Split("x\n[attached file: ]").Paths);
        // from the end up to the first line that is not one: the marker above the text stays text
        AssertSplit("[attached file: /a.txt]\nmiddle\n[attached file: /b.txt]", "[attached file: /a.txt]\nmiddle", "/b.txt");

        var c = new ChatConversation { EchoSubmittedMessages = false };
        c.AppendLine(StreamUserLine(message));
        var item = Assert.IsType<UserMessageItem>(Assert.Single(c.Items));
        Assert.Equal(message, item.Text);
        Assert.Empty(item.Files);
    }

    [Fact]
    public void Historico_com_linha_marcadora_mostra_o_chip()
    {
        var c = new ChatConversation();
        var text = "summarize it\n\n" + AttachedFileLine.Format(@"C:\Users\demo\.claude-ops\conversas\anexos\c1\server.log");

        c.LoadTranscript(new[] { TranscriptUserLine(text) });

        var item = Assert.IsType<UserMessageItem>(Assert.Single(c.Items));
        Assert.Equal("summarize it", item.Text);
        var file = Assert.Single(item.Files);
        Assert.Equal("server.log", file.Name);
        Assert.Equal(ChatFileKind.Text, file.Kind);
        Assert.Equal(-1, file.Size);
        Assert.Equal(@"C:\Users\demo\.claude-ops\conversas\anexos\c1\server.log", file.RemotePath);
        Assert.Null(file.LocalPath);
        Assert.Null(file.Progress);
    }

    private static string StreamUserLine(string text) => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["type"] = "user",
        ["message"] = new Dictionary<string, object?> { ["role"] = "user", ["content"] = text },
        ["parent_tool_use_id"] = null,
        ["session_id"] = "s1",
    });

    [Theory]
    [InlineData("report.PDF", ".pdf")]
    [InlineData(@"C:\a.b\notes", "")]
    [InlineData("/home/demo/x.md", ".md")]
    [InlineData("name.", "")]
    [InlineData(".csv", ".csv")]
    [InlineData("a<b>|\"c.txt", ".txt")]
    [InlineData("", "")]
    public void The_extension_is_found_without_the_path_rules(string name, string expected) =>
        Assert.Equal(expected, ChatFile.ExtensionOf(name));

    [Fact]
    public void A_marker_with_characters_a_path_cannot_have_still_reads()
    {
        // on .NET Framework, Path.GetExtension throws on these: the session file must still load
        var conversation = new ChatConversation();
        conversation.LoadTranscript(new[] { TranscriptUserLine("look\n" + AttachedFileLine.Format("/tmp/a|b<c>.csv")) });

        var file = Assert.Single(conversation.Items.OfType<UserMessageItem>().Single().Files);
        Assert.Equal(ChatFileKind.Csv, file.Kind);
        Assert.Equal("text/csv", file.MediaType);
        Assert.Equal(AddFileResult.Added, conversation.AddFile("odd\"name|.md", Encoding.UTF8.GetBytes("# hi")));
        Assert.Equal("text/markdown", conversation.Files.Single().MediaType);
    }

    private static string TranscriptUserLine(string text) => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["type"] = "user",
        ["uuid"] = "u1",
        ["parentUuid"] = null,
        ["isSidechain"] = false,
        ["timestamp"] = "2026-10-23T12:00:00.000Z",
        ["sessionId"] = "s1",
        ["message"] = new Dictionary<string, object?> { ["role"] = "user", ["content"] = text },
    });
}
