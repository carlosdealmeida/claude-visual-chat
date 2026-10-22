using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace ClaudeVisualChat.Imaging;

/// <summary>
/// Reads an image from the clipboard. Avalonia 11.0 has no image API, so on Windows this calls user32/kernel32
/// directly (available since Windows 2000); elsewhere it asks Avalonia for "image/png".
/// </summary>
internal static class ClipboardImages
{
    // seams for the UI tests, which must not touch the real clipboard
    internal static Func<bool>? HasTextOverride;
    internal static Func<bool>? HasImageOverride;
    internal static Func<Task<byte[]?>>? ReadOverride;

    private const uint CF_DIB = 8;
    private const uint CF_UNICODETEXT = 13;
    private const uint CF_DIBV5 = 17;

    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private static readonly Lazy<uint> PngFormat = new Lazy<uint>(() => RegisterClipboardFormat("PNG"));

    public static bool HasText()
    {
        if (HasTextOverride != null) return HasTextOverride();
        return IsWindows && IsClipboardFormatAvailable(CF_UNICODETEXT);
    }

    public static bool HasImage()
    {
        if (HasImageOverride != null) return HasImageOverride();
        if (!IsWindows) return false;
        return IsClipboardFormatAvailable(PngFormat.Value) || IsClipboardFormatAvailable(CF_DIBV5) || IsClipboardFormatAvailable(CF_DIB);
    }

    public static async Task<byte[]?> ReadImageAsync(TopLevel? top)
    {
        if (ReadOverride != null) return await ReadOverride().ConfigureAwait(false);
        if (IsWindows)
        {
            try
            {
                return await Task.Run(ReadWindows).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return null;
            }
        }
        return await ReadAvaloniaAsync(top).ConfigureAwait(false);
    }

    private static async Task<byte[]?> ReadAvaloniaAsync(TopLevel? top)
    {
        var clipboard = top?.Clipboard;
        if (clipboard == null) return null;
        try
        {
            var formats = await clipboard.GetFormatsAsync().ConfigureAwait(true);
            if (Array.IndexOf(formats, "image/png") < 0) return null;
            return await clipboard.GetDataAsync("image/png").ConfigureAwait(true) as byte[];
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static byte[]? ReadWindows()
    {
        var opened = false;
        for (var i = 0; i < 5 && !(opened = OpenClipboard(IntPtr.Zero)); i++) Thread.Sleep(20);
        if (!opened) return null;
        try
        {
            var png = Read(PngFormat.Value);
            if (png != null) return png;
            // CF_DIB first: Windows makes the DIBV5 of a CF_BITMAP with an alpha mask and often zero alpha
            var dib = Read(CF_DIB) ?? Read(CF_DIBV5);
            return dib == null ? null : DibToBmp(dib);
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static byte[]? Read(uint format)
    {
        if (format == 0 || !IsClipboardFormatAvailable(format)) return null;
        var handle = GetClipboardData(format);
        if (handle == IntPtr.Zero) return null;
        var size = (long)GlobalSize(handle).ToUInt64();
        if (size <= 0 || size > int.MaxValue) return null;
        var ptr = GlobalLock(handle);
        if (ptr == IntPtr.Zero) return null;
        try
        {
            var bytes = new byte[size];
            Marshal.Copy(ptr, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    /// <summary>Puts a BITMAPFILEHEADER in front of a clipboard DIB so a decoder reads it as a .bmp file.
    /// A DIB with fewer pixel bytes than its header promises comes back without the "BM" signature, so decoders refuse it.</summary>
    public static byte[] DibToBmp(byte[] dib)
    {
        long offBits = 14;
        var truncated = dib.Length < 40;
        if (dib.Length >= 40)
        {
            var biSize = BitConverter.ToInt32(dib, 0);
            var bitCount = BitConverter.ToInt16(dib, 14);
            var compression = BitConverter.ToInt32(dib, 16);
            var clrUsed = BitConverter.ToInt32(dib, 32);
            long colors = clrUsed != 0 ? clrUsed : (bitCount > 0 && bitCount <= 8 ? 1 << bitCount : 0);
            offBits = 14L + biSize + (biSize == 40 && compression == 3 ? 12 : 0) + colors * 4;
            if (compression == 0 || compression == 3)
            {
                // Skia fills missing rows with zeros instead of failing, so a short DIB is flagged by hand
                var stride = ((long)BitConverter.ToInt32(dib, 4) * bitCount + 31) / 32 * 4;
                var rows = Math.Abs((long)BitConverter.ToInt32(dib, 8));
                truncated = 14L + dib.Length < offBits + stride * rows;
            }
        }
        var bmp = new byte[14 + dib.Length];
        if (!truncated)
        {
            // a truncated DIB keeps no "BM" signature, so the decoder refuses it
            bmp[0] = (byte)'B';
            bmp[1] = (byte)'M';
        }
        BitConverter.GetBytes(bmp.Length).CopyTo(bmp, 2);
        BitConverter.GetBytes((int)Math.Min(offBits, int.MaxValue)).CopyTo(bmp, 10);
        Buffer.BlockCopy(dib, 0, bmp, 14, dib.Length);
        return bmp;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr newOwner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint format);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterClipboardFormat(string name);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr handle);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern UIntPtr GlobalSize(IntPtr handle);
}
