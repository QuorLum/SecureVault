using SkiaSharp;

namespace SecureVault.Core.Media;

/// <summary>
/// High-performance in-memory image decoder and transformer powered by SkiaSharp (H01-H06).
/// CRITICAL: Operates strictly in memory; never creates temporary files on disk (H15).
/// </summary>
public static class ImageDecoder
{
    /// <summary>
    /// Decodes an image from an arbitrary seekable stream directly into an SKBitmap.
    /// </summary>
    public static SKBitmap Decode(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var skStream = new SKManagedStream(stream, false);
        var bitmap = SKBitmap.Decode(skStream);
        if (bitmap == null)
        {
            throw new InvalidOperationException("Failed to decode image data into bitmap.");
        }
        return bitmap;
    }

    /// <summary>
    /// Decodes an image from byte array in memory.
    /// </summary>
    public static SKBitmap Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        using var ms = new MemoryStream(bytes, writable: false);
        return Decode(ms);
    }

    /// <summary>
    /// Decodes or resizes an image to fit within maxWidth and maxHeight, preserving aspect ratio (H18).
    /// </summary>
    public static SKBitmap DecodeAtResolution(Stream stream, int maxWidth, int maxHeight)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (maxWidth <= 0 || maxHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxWidth), "Dimensions must be positive.");

        var original = Decode(stream);
        if (original.Width <= maxWidth && original.Height <= maxHeight)
            return original;

        double widthRatio = (double)maxWidth / original.Width;
        double heightRatio = (double)maxHeight / original.Height;
        double ratio = Math.Min(widthRatio, heightRatio);

        int targetWidth = Math.Max(1, (int)(original.Width * ratio));
        int targetHeight = Math.Max(1, (int)(original.Height * ratio));

        var resized = original.Resize(new SKImageInfo(targetWidth, targetHeight), SKFilterQuality.Medium);
        original.Dispose();

        return resized ?? throw new InvalidOperationException("Failed to resize bitmap.");
    }

    /// <summary>
    /// Rotates a bitmap by a specified angle in degrees (e.g. 90, -90, 180) in memory (H06).
    /// </summary>
    public static SKBitmap Rotate(SKBitmap source, float degrees)
    {
        ArgumentNullException.ThrowIfNull(source);

        bool swapDimensions = Math.Abs(degrees % 180) > 45 && Math.Abs(degrees % 180) < 135;
        int newWidth = swapDimensions ? source.Height : source.Width;
        int newHeight = swapDimensions ? source.Width : source.Height;

        var rotated = new SKBitmap(newWidth, newHeight);
        using (var canvas = new SKCanvas(rotated))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Translate(newWidth / 2f, newHeight / 2f);
            canvas.RotateDegrees(degrees);
            canvas.Translate(-source.Width / 2f, -source.Height / 2f);
            canvas.DrawBitmap(source, 0, 0);
        }

        return rotated;
    }
}
