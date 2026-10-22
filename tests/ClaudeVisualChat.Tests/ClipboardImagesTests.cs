using System;
using System.IO;
using ClaudeVisualChat.Imaging;
using SkiaSharp;
using Xunit;

namespace ClaudeVisualChat.Tests;

public class ClipboardImagesTests
{
    // 2x2: top-left red, top-right green, bottom-left blue, bottom-right white
    private static readonly SKColor[] Expected =
    {
        new SKColor(255, 0, 0), new SKColor(0, 255, 0),
        new SKColor(0, 0, 255), new SKColor(255, 255, 255),
    };

    private static byte[] Header(int size, int bitCount, int compression, int clrUsed = 0, int imageSize = 0)
    {
        var h = new byte[size];
        BitConverter.GetBytes(size).CopyTo(h, 0);
        BitConverter.GetBytes(2).CopyTo(h, 4);
        BitConverter.GetBytes(2).CopyTo(h, 8);
        BitConverter.GetBytes((short)1).CopyTo(h, 12);
        BitConverter.GetBytes((short)bitCount).CopyTo(h, 14);
        BitConverter.GetBytes(compression).CopyTo(h, 16);
        BitConverter.GetBytes(imageSize).CopyTo(h, 20);
        BitConverter.GetBytes(clrUsed).CopyTo(h, 32);
        return h;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        using var ms = new MemoryStream();
        foreach (var p in parts) ms.Write(p, 0, p.Length);
        return ms.ToArray();
    }

    // bottom-up rows: blue, white first, then red, green
    private static byte[] Rows32() => new byte[]
    {
        255, 0, 0, 255,   255, 255, 255, 255,   // B G R A: blue, white
        0, 0, 255, 255,   0, 255, 0, 255,       // red, green
    };

    private static void AssertColors(byte[] bmpBytes)
    {
        using var bmp = SKBitmap.Decode(bmpBytes);
        Assert.NotNull(bmp);
        Assert.Equal(2, bmp.Width);
        Assert.Equal(2, bmp.Height);
        Assert.Equal(Expected[0], Rgb(bmp.GetPixel(0, 0)));
        Assert.Equal(Expected[1], Rgb(bmp.GetPixel(1, 0)));
        Assert.Equal(Expected[2], Rgb(bmp.GetPixel(0, 1)));
        Assert.Equal(Expected[3], Rgb(bmp.GetPixel(1, 1)));
    }

    private static SKColor Rgb(SKColor c) => new SKColor(c.Red, c.Green, c.Blue);

    [Fact]
    public void Dib_32bpp_rgb()
    {
        var dib = Concat(Header(40, 32, 0, 0, 16), Rows32());
        AssertColors(ClipboardImages.DibToBmp(dib));
    }

    [Fact]
    public void Dib_32bpp_bitfields()
    {
        var masks = new byte[12];
        BitConverter.GetBytes(0x00FF0000).CopyTo(masks, 0);
        BitConverter.GetBytes(0x0000FF00).CopyTo(masks, 4);
        BitConverter.GetBytes(0x000000FF).CopyTo(masks, 8);
        var dib = Concat(Header(40, 32, 3, 0, 16), masks, Rows32());
        AssertColors(ClipboardImages.DibToBmp(dib));
    }

    [Fact]
    public void Dib_24bpp()
    {
        // each row 2 px * 3 = 6 bytes, padded to 8
        var rows = new byte[]
        {
            255, 0, 0,  255, 255, 255,  0, 0,
            0, 0, 255,  0, 255, 0,      0, 0,
        };
        var dib = Concat(Header(40, 24, 0, 0, 16), rows);
        AssertColors(ClipboardImages.DibToBmp(dib));
    }

    [Fact]
    public void Dib_v5_header()
    {
        var h = Header(124, 32, 0, 0, 16);
        var dib = Concat(h, Rows32());
        AssertColors(ClipboardImages.DibToBmp(dib));
    }

    [Fact]
    public void Truncated_dib_is_refused_without_exception()
    {
        var dib = Concat(Header(40, 32, 0, 0, 16), new byte[] { 255, 0, 0, 255 });
        var bmp = ClipboardImages.DibToBmp(dib);
        Assert.True(Refused(bmp));
        Assert.True(Refused(ClipboardImages.DibToBmp(new byte[10])));
    }

    // SkiaSharp 2.88 throws ArgumentNullException from Decode(byte[]) when no codec recognises the bytes
    private static bool Refused(byte[] bytes)
    {
        try
        {
            using var bmp = SKBitmap.Decode(bytes);
            return bmp == null;
        }
        catch (ArgumentNullException)
        {
            return true;
        }
    }
}
