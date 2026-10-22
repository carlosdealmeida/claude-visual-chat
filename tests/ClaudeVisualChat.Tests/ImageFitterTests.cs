using System;
using ClaudeVisualChat.Imaging;
using SkiaSharp;
using Xunit;

namespace ClaudeVisualChat.Tests;

public class ImageFitterTests
{
    private static byte[] Png(int w, int h, bool noise)
    {
        using var bmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        if (noise)
        {
            var rnd = new Random(42);
            var px = new byte[w * h * 4];
            rnd.NextBytes(px);
            for (var i = 3; i < px.Length; i += 4) px[i] = 255;
            System.Runtime.InteropServices.Marshal.Copy(px, 0, bmp.GetPixels(), px.Length);
        }
        else
        {
            bmp.Erase(new SKColor(30, 120, 200));
        }
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public void Small_png_is_kept_as_is()
    {
        var png = Png(200, 100, false);
        Assert.True(png.Length < 100_000);
        var fitted = ImageFitter.Fit(png, 100_000, 1568);
        Assert.NotNull(fitted);
        Assert.Same(png, fitted!.Data);
        Assert.Equal("image/png", fitted.MediaType);
        Assert.Equal(200, fitted.Width);
        Assert.Equal(100, fitted.Height);
    }

    [Fact]
    public void Long_side_is_capped()
    {
        var fitted = ImageFitter.Fit(Png(3000, 1000, false), 5_000_000, 1568);
        Assert.NotNull(fitted);
        Assert.Equal(1568, Math.Max(fitted!.Width, fitted.Height));
        Assert.InRange(fitted.Height, 522, 524); // 1000 * 1568 / 3000 = 522.67
    }

    [Fact]
    public void Busy_image_becomes_jpeg_under_the_budget()
    {
        var fitted = ImageFitter.Fit(Png(1920, 1080, true), 300_000, 1568);
        Assert.NotNull(fitted);
        Assert.Equal("image/jpeg", fitted!.MediaType);
        Assert.True(fitted.Data.Length <= 300_000);
    }

    [Fact]
    public void Huge_screenshot_fits()
    {
        var fitted = ImageFitter.Fit(Png(5760, 1080, true), 300_000, 1568);
        Assert.NotNull(fitted);
        Assert.True(fitted!.Data.Length <= 300_000);
        Assert.True(Math.Max(fitted.Width, fitted.Height) <= 1568);
    }

    [Fact]
    public void Not_an_image_returns_null()
    {
        var junk = new byte[500];
        new Random(1).NextBytes(junk);
        Assert.Null(ImageFitter.Fit(junk, 100_000, 1568));
        Assert.Null(ImageFitter.Fit(Array.Empty<byte>(), 100_000, 1568));
        Assert.Null(ImageFitter.Measure(junk));
        Assert.Null(ImageFitter.Measure(Array.Empty<byte>()));
    }

    [Fact]
    public void Measure_reads_the_size()
    {
        Assert.Equal((200, 100), ImageFitter.Measure(Png(200, 100, false)));
    }

    [Fact]
    public void Impossible_budget_returns_null()
    {
        Assert.Null(ImageFitter.Fit(Png(1920, 1080, true), 1_000, 1568));
    }

    [Fact]
    public async System.Threading.Tasks.Task No_budget_left_returns_null_at_once()
    {
        // the attachments already fill the budget (or the host lowered it): the remaining bytes are 0 or less
        var fit = System.Threading.Tasks.Task.Run(() => ImageFitter.Fit(Png(200, 200, false), -90, 1568));
        var timeout = System.Threading.Tasks.Task.Delay(System.TimeSpan.FromSeconds(10));
        Assert.Same(fit, await System.Threading.Tasks.Task.WhenAny(fit, timeout));
        Assert.Null(await fit);
        Assert.Null(ImageFitter.Fit(Png(200, 200, false), 0, 1568));
    }

    [Fact]
    public void Thin_wide_image_fits()
    {
        var fitted = ImageFitter.Fit(Png(4000, 100, false), 100_000, 1568);
        Assert.NotNull(fitted);
        Assert.Equal(1568, fitted!.Width);
        Assert.InRange(fitted.Height, 38, 40);
    }

    [Fact]
    public void Zero_alpha_bitmap_is_made_opaque()
    {
        // BITMAPV5HEADER, 32 bpp BI_BITFIELDS with an alpha mask, every alpha byte 0
        var h = new byte[124];
        BitConverter.GetBytes(124).CopyTo(h, 0);
        BitConverter.GetBytes(2).CopyTo(h, 4);
        BitConverter.GetBytes(2).CopyTo(h, 8);
        BitConverter.GetBytes((short)1).CopyTo(h, 12);
        BitConverter.GetBytes((short)32).CopyTo(h, 14);
        BitConverter.GetBytes(3).CopyTo(h, 16);
        BitConverter.GetBytes(16).CopyTo(h, 20);
        BitConverter.GetBytes(0x00FF0000).CopyTo(h, 40);
        BitConverter.GetBytes(0x0000FF00).CopyTo(h, 44);
        BitConverter.GetBytes(0x000000FF).CopyTo(h, 48);
        BitConverter.GetBytes(unchecked((int)0xFF000000)).CopyTo(h, 52);
        var rows = new byte[]
        {
            255, 0, 0, 0,   255, 255, 255, 0,   // B G R A: blue, white
            0, 0, 255, 0,   0, 255, 0, 0,       // red, green
        };
        var dib = new byte[h.Length + rows.Length];
        h.CopyTo(dib, 0);
        rows.CopyTo(dib, h.Length);

        var fitted = ImageFitter.Fit(ClipboardImages.DibToBmp(dib), 100_000, 1568);
        Assert.NotNull(fitted);
        using var bmp = SKBitmap.Decode(fitted!.Data);
        Assert.Equal(new SKColor(255, 0, 0, 255), bmp.GetPixel(0, 0));
        Assert.Equal(new SKColor(0, 255, 0, 255), bmp.GetPixel(1, 0));
        Assert.Equal(new SKColor(0, 0, 255, 255), bmp.GetPixel(0, 1));
        Assert.Equal(new SKColor(255, 255, 255, 255), bmp.GetPixel(1, 1));
    }
}
