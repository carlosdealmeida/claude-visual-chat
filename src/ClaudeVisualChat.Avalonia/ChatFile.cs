using System;
using System.IO;

namespace ClaudeVisualChat;

/// <summary>
/// A file attached to a message (a PDF, a text or a CSV file), which the host sends to the claude's machine. The
/// conversation fills what it knows; the host writes <see cref="Progress"/> while it sends the file and
/// <see cref="RemotePath"/> once it is there. The chips in the reply box and in the message follow both. Like the rest
/// of the component, set them on the UI thread (post from an upload worker); a change made on another thread is
/// drawn later, on the UI thread, but the event that tells it is raised on that other thread.
/// </summary>
public sealed class ChatFile : ChatObservableObject
{
    /// <summary>How much of the start of a text or CSV file is checked for a null byte.</summary>
    internal const int TextCheckBytes = 8 * 1024;

    private static readonly byte[] PdfMagic = { (byte)'%', (byte)'P', (byte)'D', (byte)'F', (byte)'-' };

    private double? _progress;
    private string? _remotePath;

    internal ChatFile(string name, long size, ChatFileKind kind, string? localPath = null, byte[]? content = null)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Size = size;
        Kind = kind;
        MediaType = MediaTypeOf(kind, name);
        LocalPath = localPath;
        Content = content;
    }

    /// <summary>The file name, without its folder (<c>report.pdf</c>).</summary>
    public string Name { get; }

    /// <summary>The size in bytes; -1 when unknown (a file named by a session file).</summary>
    public long Size { get; }

    /// <summary><c>application/pdf</c>, <c>text/plain</c>, <c>text/markdown</c>, <c>application/json</c> or <c>text/csv</c>.</summary>
    public string MediaType { get; }

    public ChatFileKind Kind { get; }

    /// <summary>The full path on the host's computer, when the file came from its disk; the host reads it from there, in parts.</summary>
    public string? LocalPath { get; }

    /// <summary>The bytes, when the file did not come from the disk (<see cref="ChatConversation.AddFile"/>); else null.</summary>
    public byte[]? Content { get; }

    /// <summary>
    /// How much of the file the host has sent, from 0 to 1; null when it is not being sent. The host sets it; while it
    /// is not null and <see cref="RemotePath"/> is, the chip shows the percentage instead of the size. Set it on the UI
    /// thread.
    /// </summary>
    public double? Progress
    {
        get => _progress;
        set => SetField(ref _progress, value);
    }

    /// <summary>
    /// Where the file is on the claude's machine, once sent; the host sets it, and the message names it with
    /// <see cref="Protocol.AttachedFileLine.Format"/>. A file read back from a session file has it from its line. Set
    /// it on the UI thread.
    /// </summary>
    public string? RemotePath
    {
        get => _remotePath;
        set => SetField(ref _remotePath, value);
    }

    /// <summary>A file named by a marker line: its name is the path's last part, its size unknown.</summary>
    internal static ChatFile FromRemotePath(string remotePath)
    {
        var name = LastSegment(remotePath);
        return new ChatFile(name, -1, KindOf(name) ?? ChatFileKind.Text) { _remotePath = remotePath };
    }

    /// <summary>The part after the last <c>/</c> or <c>\</c>; the whole path when nothing follows the last one.</summary>
    internal static string LastSegment(string path)
    {
        var cut = path.LastIndexOfAny(new[] { '/', '\\' });
        return cut < 0 || cut == path.Length - 1 ? path : path.Substring(cut + 1);
    }

    /// <summary>The kind its extension says (any case); null for any other file.</summary>
    internal static ChatFileKind? KindOf(string name)
    {
        switch (ExtensionOf(name))
        {
            case ".pdf":
                return ChatFileKind.Pdf;
            case ".txt":
            case ".log":
            case ".md":
            case ".json":
                return ChatFileKind.Text;
            case ".csv":
                return ChatFileKind.Csv;
            default:
                return null;
        }
    }

    /// <summary>
    /// The extension of the last part of a name or path, with its dot, in lower case ("" when it has none). Found by
    /// hand: <c>Path.GetExtension</c> throws on characters such as <c>&lt;&gt;|"</c> on .NET Framework, and a path read
    /// from a session file may have them.
    /// </summary>
    internal static string ExtensionOf(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        var start = name!.LastIndexOfAny(new[] { '/', '\\', ':' }) + 1;
        var dot = name.LastIndexOf('.');
        return dot < start || dot == name.Length - 1 ? "" : name.Substring(dot).ToLowerInvariant();
    }

    /// <summary>True for a name that may be attached as a file.</summary>
    internal static bool IsFileName(string? name) => name != null && KindOf(name) != null;

    private static string MediaTypeOf(ChatFileKind kind, string name)
    {
        switch (kind)
        {
            case ChatFileKind.Pdf:
                return "application/pdf";
            case ChatFileKind.Csv:
                return "text/csv";
            default:
                switch (ExtensionOf(name))
                {
                    case ".md":
                        return "text/markdown";
                    case ".json":
                        return "application/json";
                    default:
                        return "text/plain";
                }
        }
    }

    /// <summary>
    /// The start of the file fits its kind: a PDF begins with <c>%PDF-</c>; a text or CSV file has no null byte in
    /// its first <see cref="TextCheckBytes"/> (<paramref name="count"/> bytes of <paramref name="head"/> are given).
    /// </summary>
    internal static bool StartFits(ChatFileKind kind, byte[] head, int count)
    {
        if (kind == ChatFileKind.Pdf)
        {
            if (count < PdfMagic.Length) return false;
            for (var i = 0; i < PdfMagic.Length; i++)
            {
                if (head[i] != PdfMagic[i]) return false;
            }
            return true;
        }
        var end = Math.Min(count, TextCheckBytes);
        for (var i = 0; i < end; i++)
        {
            if (head[i] == 0) return false;
        }
        return true;
    }
}

/// <summary>The kinds of file a message may carry.</summary>
public enum ChatFileKind
{
    Pdf,

    /// <summary>A <c>.txt</c>, <c>.log</c>, <c>.md</c> or <c>.json</c> file.</summary>
    Text,

    Csv,
}

/// <summary>What <see cref="ChatConversation.AddFileAsync"/> and <see cref="ChatConversation.AddFile"/> did with the file.</summary>
public enum AddFileResult
{
    Added,

    /// <summary>The message already has <see cref="ChatConversation.MaxFiles"/> files.</summary>
    TooMany,

    /// <summary>Larger than <see cref="ChatConversation.MaxFileBytes"/>.</summary>
    TooBig,

    /// <summary>Not a PDF, text or CSV file (by its extension and its first bytes), or it could not be read.</summary>
    NotSupported,
}
