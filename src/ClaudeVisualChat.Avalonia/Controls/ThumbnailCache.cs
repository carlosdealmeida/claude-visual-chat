using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace ClaudeVisualChat.Controls;

/// <summary>
/// Small bitmaps of images, decoded once per image: both sides at most twice the box they are drawn in (sharp on a
/// screen at 200 %), never larger than the image. A long image is held to the box's long side too, so a 1568 × 39
/// screenshot in a 12 px square is not kept at 965 × 24. <see cref="Get"/> decodes on the calling thread;
/// <see cref="Load"/> off the UI thread, for lists that may hold many images.
/// </summary>
internal sealed class ThumbnailCache
{
    /// <summary>How many boxes wide a <see cref="Cover"/> bitmap may be, for an image much longer than its box.</summary>
    private const double CoverWidths = 4;

    private readonly ConditionalWeakTable<ChatImage, Task<Bitmap?>> _bitmaps = new ConditionalWeakTable<ChatImage, Task<Bitmap?>>();
    private readonly double _maxWidth;
    private readonly double _maxHeight;
    private readonly bool _cover;

    /// <param name="maxWidth">The widest the thumbnail is drawn, in pixels.</param>
    /// <param name="maxHeight">The tallest the thumbnail is drawn, in pixels.</param>
    public ThumbnailCache(double maxWidth, double maxHeight)
        : this(maxWidth, maxHeight, false)
    {
    }

    private ThumbnailCache(double maxWidth, double maxHeight, bool cover)
    {
        _maxWidth = maxWidth;
        _maxHeight = maxHeight;
        _cover = cover;
    }

    /// <summary>
    /// For thumbnails drawn <paramref name="height"/> high and cut at their sides past <paramref name="maxWidth"/>
    /// (the extension's <c>object-fit: cover</c>): the bitmap is twice that height (never larger than the image), and
    /// for an image so long that this would make it wider than four times twice the box, as wide as that.
    /// </summary>
    public static ThumbnailCache Cover(double maxWidth, double height) => new ThumbnailCache(maxWidth, height, true);

    /// <summary>Test seam: where <see cref="Load"/> runs a decode. A background thread by default.</summary>
    internal Func<Func<Bitmap?>, Task<Bitmap?>> Run { get; set; } = work => Task.Run(work);

    /// <summary>The image's bitmap, decoded now if it was not yet; null when it cannot be decoded.</summary>
    public Bitmap? Get(ChatImage image)
    {
        if (!_bitmaps.TryGetValue(image, out var task))
        {
            task = Task.FromResult(Decode(image));
            _bitmaps.Add(image, task);
        }
        return task.Result;
    }

    /// <summary>The image's bitmap, decoded through <see cref="Run"/> the first time; null when it cannot be decoded.</summary>
    public Task<Bitmap?> Load(ChatImage image)
    {
        if (_bitmaps.TryGetValue(image, out var task)) return task;
        task = Run(() => Decode(image));
        _bitmaps.Add(image, task);
        return task;
    }

    private Bitmap? Decode(ChatImage image)
    {
        if (!_cover || image.Width <= 0 || image.Height <= 0) return Decode(image, 2 * _maxWidth, 2 * _maxHeight);
        // the height decides, so the cut picture keeps its sharpness; a very long image is held to a width
        var scale = Math.Min(1, 2 * _maxHeight / image.Height);
        var widest = CoverWidths * 2 * _maxWidth;
        if (image.Width * scale > widest) scale = widest / image.Width;
        return Decode(image, image.Width * scale, image.Height * scale);
    }

    /// <summary>
    /// The size an image is drawn at in a box: in proportion, as large as fits, never larger than the image. Zero
    /// when the image's size is unknown.
    /// </summary>
    public static (double Width, double Height) Fit(int width, int height, double maxWidth, double maxHeight)
    {
        if (width <= 0 || height <= 0) return (0, 0);
        var scale = Math.Min(1, Math.Min(maxWidth / width, maxHeight / height));
        return (width * scale, height * scale);
    }

    /// <summary>The image decoded with both sides within <paramref name="maxWidth"/> × <paramref name="maxHeight"/>.</summary>
    internal static Bitmap? Decode(ChatImage image, double maxWidth, double maxHeight)
    {
        var (width, height) = Fit(image.Width, image.Height, maxWidth, maxHeight);
        if (width <= 0 || height <= 0) return null; // the size is unknown: not an image it can read
        var w = (int)Math.Floor(width);
        var h = (int)Math.Floor(height);
        try
        {
            using var stream = new System.IO.MemoryStream(image.ToBytes(), false);
            if (w < 1 || h < 1)
            {
                // a sliver thinner than a pixel at that size: one pixel thick, out of proportion
                using var whole = new Bitmap(stream);
                return whole.CreateScaledBitmap(new Avalonia.PixelSize(Math.Max(1, w), Math.Max(1, h)));
            }
            // the side that limits it decides; the other one follows the proportion, rounded down
            return w * (double)image.Height <= h * (double)image.Width
                ? Bitmap.DecodeToWidth(stream, w)
                : Bitmap.DecodeToHeight(stream, h);
        }
        catch (Exception)
        {
            return null; // not decodable: no picture
        }
    }
}
