using System;
using SkiaSharp;

namespace ClaudeVisualChat.Imaging;

/// <summary>An image ready to send: the encoded bytes, their media type and the pixel size.</summary>
internal sealed record FittedImage(byte[] Data, string MediaType, int Width, int Height);

/// <summary>Shrinks an image until it fits a byte budget and a longest-side cap. Safe on any thread.</summary>
internal static class ImageFitter
{
    private const int MinSide = 64;
    private const int JpegQuality = 85;

    /// <summary>The pixel size of an encoded image, or null when the bytes are not an image.</summary>
    public static (int Width, int Height)? Measure(byte[] encoded)
    {
        if (encoded == null || encoded.Length == 0) return null;
        try
        {
            using var codec = SKCodec.Create(new SKMemoryStream(encoded));
            if (codec == null) return null;
            return (codec.Info.Width, codec.Info.Height);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the image as is when it already fits, otherwise scaled down (PNG, or JPEG on white when PNG is too big).
    /// Null when the bytes are not an image or no size above 64 px fits the budget.
    /// </summary>
    public static FittedImage? Fit(byte[] encoded, int maxBytes, int maxSide)
    {
        // no budget left (the attachments fill it): nothing fits, and the shrinking loop would never end
        if (encoded == null || encoded.Length == 0 || maxBytes <= 0) return null;
        try
        {
            return FitCore(encoded, maxBytes, maxSide);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static FittedImage? FitCore(byte[] encoded, int maxBytes, int maxSide)
    {
        SKEncodedImageFormat format;
        using (var codec = SKCodec.Create(new SKMemoryStream(encoded)))
        {
            if (codec == null) return null;
            format = codec.EncodedFormat;
        }

        using var decoded = SKBitmap.Decode(encoded);
        if (decoded == null || decoded.Width <= 0 || decoded.Height <= 0) return null;

        var mediaType = MediaTypeOf(format);
        if (mediaType != null && encoded.Length <= maxBytes && Math.Max(decoded.Width, decoded.Height) <= maxSide)
            return new FittedImage(encoded, mediaType, decoded.Width, decoded.Height);

        using var opaque = AllAlphaZero(decoded) ? MadeOpaque(encoded, decoded) : null;
        var source = opaque ?? decoded;

        var scale = Math.Min(1.0, (double)maxSide / Math.Max(source.Width, source.Height));
        while (true)
        {
            var w = Math.Max(1, (int)Math.Round(source.Width * scale));
            var h = Math.Max(1, (int)Math.Round(source.Height * scale));

            using var scaled = w == source.Width && h == source.Height
                ? source.Copy()
                : source.Resize(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul), SKFilterQuality.High);
            if (scaled == null) return null;

            var data = Encode(scaled, SKEncodedImageFormat.Png, 100);
            var type = "image/png";
            if (data.Length > maxBytes)
            {
                data = EncodeJpegOnWhite(scaled);
                type = "image/jpeg";
            }
            if (data.Length <= maxBytes) return new FittedImage(data, type, w, h);

            var next = scale * Math.Min(0.9, Math.Sqrt((double)maxBytes / data.Length) * 0.9);
            if (Math.Min(source.Width, source.Height) * next < MinSide) return null;
            scale = next;
        }
    }

    // a 32-bit bitmap whose alpha bytes are all 0 is a screenshot that never set alpha (DIBV5 made from CF_BITMAP)
    private static bool AllAlphaZero(SKBitmap bitmap)
    {
        if (bitmap.ColorType != SKColorType.Bgra8888 && bitmap.ColorType != SKColorType.Rgba8888) return false;
        var px = bitmap.GetPixelSpan();
        for (var i = 3; i < px.Length; i += 4)
            if (px[i] != 0) return false;
        return true;
    }

    private static SKBitmap? MadeOpaque(byte[] encoded, SKBitmap decoded)
    {
        // decoded premultiplied, zero alpha wiped the colors; read it again straight, then set alpha to 255
        using var straight = SKBitmap.Decode(encoded, new SKImageInfo(decoded.Width, decoded.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        if (straight == null) return null;
        var px = straight.Bytes;
        for (var i = 3; i < px.Length; i += 4) px[i] = 255;
        System.Runtime.InteropServices.Marshal.Copy(px, 0, straight.GetPixels(), px.Length);
        var flat = new SKBitmap(new SKImageInfo(decoded.Width, decoded.Height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(flat);
        canvas.DrawBitmap(straight, 0, 0);
        return flat;
    }

    private static string? MediaTypeOf(SKEncodedImageFormat format) => format switch
    {
        SKEncodedImageFormat.Png => "image/png",
        SKEncodedImageFormat.Jpeg => "image/jpeg",
        SKEncodedImageFormat.Gif => "image/gif",
        SKEncodedImageFormat.Webp => "image/webp",
        _ => null,
    };

    private static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format, int quality)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, quality);
        return data.ToArray();
    }

    private static byte[] EncodeJpegOnWhite(SKBitmap bitmap)
    {
        using var flat = new SKBitmap(new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(flat))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawBitmap(bitmap, 0, 0);
        }
        return Encode(flat, SKEncodedImageFormat.Jpeg, JpegQuality);
    }
}
