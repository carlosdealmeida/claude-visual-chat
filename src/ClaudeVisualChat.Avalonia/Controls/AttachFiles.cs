using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using ClaudeVisualChat.Imaging;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// A file the user picked with the + button or dropped on the reply box: an image goes to the images, a PDF, text or
/// CSV file to the files, and any other is refused with its reason. A file on the disk is known by its path; one
/// without (a sandboxed picker) only by its storage item, read through it.
/// </summary>
internal sealed class AttachSource
{
    private AttachSource(string name, string? localPath, IStorageFile? file)
    {
        Name = name;
        LocalPath = localPath;
        File = file;
    }

    public string Name { get; }

    /// <summary>The full path on this computer; null when the file has none.</summary>
    public string? LocalPath { get; }

    /// <summary>The picked or dropped item; null for a path given by the test seam.</summary>
    public IStorageFile? File { get; }

    public bool IsImage => ImageFiles.IsImageName(Name);

    public bool IsAttachableFile => ChatFile.IsFileName(Name);

    public static AttachSource FromPath(string path) => new AttachSource(Path.GetFileName(path), path, null);

    public static AttachSource FromStorage(IStorageFile file)
    {
        string? local;
        try
        {
            local = file.TryGetLocalPath();
        }
        catch (Exception)
        {
            local = null;
        }
        return new AttachSource(file.Name, local, file);
    }
}

/// <summary>The + button's file dialog and the drop: images and PDF, text and CSV files together.</summary>
internal static class AttachFiles
{
    private static readonly string[] Patterns =
    {
        "*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp", "*.bmp", "*.pdf", "*.txt", "*.log", "*.md", "*.json", "*.csv",
    };

    private static readonly string[] MimeTypes =
    {
        "image/png", "image/jpeg", "image/gif", "image/webp", "image/bmp", "application/pdf", "text/plain",
        "text/markdown", "application/json", "text/csv",
    };

    private static readonly string[] AppleTypes =
    {
        "public.image", "com.adobe.pdf", "public.plain-text", "public.comma-separated-values-text", "public.json",
    };

    /// <summary>Every file a drag carries (folders left out); what each one becomes is decided when it is dropped.</summary>
    public static List<AttachSource> Dropped(IDataObject data)
    {
        var sources = new List<AttachSource>();
        var items = data.GetFiles();
        if (items == null) return sources;
        foreach (var item in items)
        {
            if (item is IStorageFile file) sources.Add(AttachSource.FromStorage(file));
        }
        return sources;
    }

    /// <summary>Test seam: the files a paste finds on the clipboard, as paths (it takes the place of the clipboard).</summary>
    internal static Func<Task<IReadOnlyList<string>>>? PastedFilesOverride;

    /// <summary>
    /// The files on the clipboard (copied in Explorer or a file manager), folders left out; none when it has none or
    /// cannot be read. What each one becomes is decided as for a drop.
    /// </summary>
    public static async Task<List<AttachSource>> PastedAsync(TopLevel? top)
    {
        var sources = new List<AttachSource>();
        if (PastedFilesOverride != null)
        {
            foreach (var path in await PastedFilesOverride()) sources.Add(AttachSource.FromPath(path));
            return sources;
        }
        var clipboard = top?.Clipboard;
        if (clipboard == null) return sources;
        try
        {
            if (await clipboard.GetDataAsync(DataFormats.Files) is IEnumerable<IStorageItem> items)
            {
                foreach (var item in items)
                {
                    if (item is IStorageFile file) sources.Add(AttachSource.FromStorage(file));
                    else item?.Dispose();
                }
            }
        }
        catch (Exception)
        {
            // a clipboard that cannot give its files: nothing attached
        }
        return sources;
    }

    /// <summary>
    /// The file dialog, several files at once; none when cancelled or when there is no dialog. With
    /// <paramref name="files"/> false (a conversation that takes no file, <see cref="ChatConversation.MaxFiles"/> 0), it
    /// offers images only.
    /// </summary>
    public static async Task<IReadOnlyList<AttachSource>> PickAsync(TopLevel? top, ChatStrings strings, bool files = true)
    {
        var picked = new List<AttachSource>();
        if (top == null || !top.StorageProvider.CanOpen) return picked;
        try
        {
            foreach (var file in await top.StorageProvider.OpenFilePickerAsync(Options(strings, files)))
                picked.Add(AttachSource.FromStorage(file));
        }
        catch (Exception)
        {
            // no dialog on this platform
        }
        return picked;
    }

    /// <summary>The dialog's types: images and files first, then images alone; images alone when no file is taken.</summary>
    internal static FilePickerOpenOptions Options(ChatStrings strings, bool files)
    {
        var images = new FilePickerFileType(strings.ImageFiles)
        {
            Patterns = ImageFiles.Patterns,
            MimeTypes = ImageFiles.MimeTypes,
            AppleUniformTypeIdentifiers = new[] { "public.image" },
        };
        return new FilePickerOpenOptions
        {
            AllowMultiple = true,
            FileTypeFilter = files
                ? new[]
                {
                    new FilePickerFileType(strings.AttachableFiles)
                    {
                        Patterns = Patterns,
                        MimeTypes = MimeTypes,
                        AppleUniformTypeIdentifiers = AppleTypes,
                    },
                    images,
                }
                : new[] { images },
        };
    }

    /// <summary>An image's bytes: <see cref="ImageFiles.TooBig"/> over its ceiling, null when it cannot be read.</summary>
    public static async Task<byte[]?> ReadImageAsync(AttachSource source)
    {
        if (source.File != null) return await ImageFiles.ReadAsync(source.File);
        var path = source.LocalPath!;
        return await Task.Run(() =>
        {
            try
            {
                if (new FileInfo(path).Length > ImageFiles.MaxFileBytes) return ImageFiles.TooBig;
                return File.ReadAllBytes(path);
            }
            catch (Exception)
            {
                return null;
            }
        });
    }

    /// <summary>
    /// Adds a PDF, text or CSV file: from its path when it has one (only its start is read; the host reads the rest),
    /// else through its storage item. A file without a local path (a sandboxed picker, a drop from another app) is
    /// read whole into memory, up to the conversation's ceiling (<see cref="ChatConversation.MaxFileBytes"/>, 25 MB by
    /// default), and its bytes go in <see cref="ChatFile.Content"/>: there is no path for the host to read it from.
    /// </summary>
    public static async Task<AddFileResult> AddFileAsync(ChatConversation conversation, AttachSource source)
    {
        if (source.LocalPath != null) return await conversation.AddFileAsync(source.LocalPath);
        if (conversation.Files.Count >= conversation.MaxFiles) return AddFileResult.TooMany;
        var max = conversation.MaxFileBytes;
        byte[]? bytes;
        try
        {
            using var stream = await source.File!.OpenReadAsync();
            if (stream.CanSeek && stream.Length > max) return AddFileResult.TooBig;
            using var copy = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                if (copy.Length + read > max) return AddFileResult.TooBig;
                copy.Write(buffer, 0, read);
            }
            bytes = copy.ToArray();
        }
        catch (Exception)
        {
            bytes = null;
        }
        return bytes == null ? AddFileResult.NotSupported : conversation.AddFile(source.Name, bytes);
    }
}
