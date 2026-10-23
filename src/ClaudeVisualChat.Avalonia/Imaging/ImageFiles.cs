using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;

namespace ClaudeVisualChat.Imaging;

/// <summary>Image files the user picks with the + button or drops on the reply box (sorted by <c>AttachFiles</c>).</summary>
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

    /// <summary>The image patterns of the file dialog.</summary>
    internal static readonly string[] Patterns = { "*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp", "*.bmp" };

    /// <summary>The image media types of the file dialog.</summary>
    internal static readonly string[] MimeTypes = { "image/png", "image/jpeg", "image/gif", "image/webp", "image/bmp" };

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
