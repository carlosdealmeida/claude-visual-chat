using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace ClaudeVisualChat.Imaging;

/// <summary>Image files the user picks with the + button or drops on the reply box.</summary>
internal static class ImageFiles
{
    /// <summary>The largest file read; a bigger one is left out as too big.</summary>
    internal const long MaxFileBytes = 50L * 1024 * 1024;

    /// <summary>What <see cref="ReadAsync"/> returns for a file over <see cref="MaxFileBytes"/> (compare by reference).</summary>
    internal static readonly byte[] TooBig = new byte[0];

    /// <summary>The room left for images: one more than fits, so that the conversation says "too many".</summary>
    internal static int ToRead(ChatConversation conversation) =>
        Math.Max(0, conversation.MaxImages - conversation.Attachments.Count) + 1;

    private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" };

    /// <summary>True for a name with an image extension (any case).</summary>
    public static bool IsImageName(string? name)
    {
        var extension = Path.GetExtension(name ?? "");
        foreach (var known in Extensions)
        {
            if (string.Equals(extension, known, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>The image files a drag carries; the others are left out.</summary>
    public static List<IStorageFile> Dropped(IDataObject data)
    {
        var files = new List<IStorageFile>();
        var items = data.GetFiles();
        if (items == null) return files;
        foreach (var item in items)
        {
            if (item is IStorageFile file && IsImageName(file.Name)) files.Add(file);
        }
        return files;
    }

    /// <summary>The file dialog, several images at once; the bytes of each one picked, at most <paramref name="max"/> read (none when cancelled).</summary>
    public static async Task<IReadOnlyList<byte[]>> PickAsync(TopLevel? top, ChatStrings strings, int max = int.MaxValue)
    {
        var picked = new List<byte[]>();
        if (top == null || !top.StorageProvider.CanOpen) return picked;
        IReadOnlyList<IStorageFile> files;
        try
        {
            files = await top.StorageProvider.OpenFilePickerAsync(Options(strings));
        }
        catch (Exception)
        {
            return picked; // no dialog on this platform
        }
        try
        {
            foreach (var file in files)
            {
                if (picked.Count >= max) break; // the rest would be refused as too many anyway
                picked.Add(await ReadAsync(file) ?? Array.Empty<byte>()); // unreadable: refused as not an image
            }
        }
        finally
        {
            foreach (var file in files) file.Dispose();
        }
        return picked;
    }

    private static FilePickerOpenOptions Options(ChatStrings strings) =>
        new FilePickerOpenOptions
        {
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(strings.ImageFiles)
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp", "*.bmp" },
                    MimeTypes = new[] { "image/png", "image/jpeg", "image/gif", "image/webp", "image/bmp" },
                    AppleUniformTypeIdentifiers = new[] { "public.image" },
                },
            },
        };

    /// <summary>The file's bytes; null when it cannot be read.</summary>
    public static async Task<byte[]?> ReadAsync(IStorageFile file)
    {
        try
        {
            using var stream = await file.OpenReadAsync();
            if (stream.CanSeek && stream.Length > MaxFileBytes) return TooBig;
            using var bytes = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                if (bytes.Length + read > MaxFileBytes) return TooBig;
                bytes.Write(buffer, 0, read);
            }
            return bytes.ToArray();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
