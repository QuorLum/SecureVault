using SkiaSharp;
using TagLib;

namespace SecureVault.Core.Media;

/// <summary>
/// Generates WebP thumbnails (<= 200x200 max dimensions) in memory (E08-E13).
/// Zero temporary files written to physical disk.
/// </summary>
public static class ThumbnailGenerator
{
    private const int DefaultMaxDimension = 200;
    private const int WebpQuality = 80;

    /// <summary>
    /// Generates a WebP thumbnail from image data, preserving aspect ratio (E09, E10).
    /// </summary>
    public static byte[] GenerateImageThumbnail(byte[] imageData, int maxDimension = DefaultMaxDimension)
    {
        ArgumentNullException.ThrowIfNull(imageData);
        using var original = ImageDecoder.Decode(imageData);

        return ResizeAndEncodeWebp(original, maxDimension);
    }

    /// <summary>
    /// Extracts embedded album art from audio bytes and generates a WebP thumbnail (E12).
    /// </summary>
    public static byte[]? GenerateAudioThumbnail(byte[] audioData, int maxDimension = DefaultMaxDimension)
    {
        ArgumentNullException.ThrowIfNull(audioData);

        try
        {
            var abstraction = new MemoryFileAbstraction(audioData);
            using var file = TagLib.File.Create(abstraction);

            if (file.Tag.Pictures.Length > 0)
            {
                byte[] pictureBytes = file.Tag.Pictures[0].Data.Data;
                using var original = ImageDecoder.Decode(pictureBytes);
                return ResizeAndEncodeWebp(original, maxDimension);
            }
        }
        catch
        {
            // Fallback gracefully on audio without valid ID3 or album art
        }

        return null;
    }

    /// <summary>
    /// Renders page 1 of a PDF and generates a WebP thumbnail (E13).
    /// </summary>
    public static byte[] GeneratePdfThumbnail(byte[] pdfData, int maxDimension = DefaultMaxDimension)
    {
        ArgumentNullException.ThrowIfNull(pdfData);

        using var renderer = new PdfRenderer(pdfData);
        if (renderer.PageCount == 0)
            throw new InvalidOperationException("PDF contains no pages.");

        var (bgraBytes, width, height) = renderer.RenderPage(0, 1.0);

        using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        unsafe
        {
            fixed (byte* pBytes = bgraBytes)
            {
                bitmap.SetPixels((IntPtr)pBytes);
                return ResizeAndEncodeWebp(bitmap, maxDimension);
            }
        }
    }

    private static byte[] ResizeAndEncodeWebp(SKBitmap bitmap, int maxDimension)
    {
        double ratio = Math.Min((double)maxDimension / bitmap.Width, (double)maxDimension / bitmap.Height);
        int targetWidth = Math.Min(bitmap.Width, Math.Max(1, (int)(bitmap.Width * ratio)));
        int targetHeight = Math.Min(bitmap.Height, Math.Max(1, (int)(bitmap.Height * ratio)));

        using var resized = bitmap.Resize(new SKImageInfo(targetWidth, targetHeight), SKFilterQuality.Medium)
            ?? throw new InvalidOperationException("Failed to resize bitmap for thumbnail.");

        using var image = SKImage.FromBitmap(resized);
        using var data = image.Encode(SKEncodedImageFormat.Webp, WebpQuality);

        return data.ToArray();
    }

    private sealed class MemoryFileAbstraction : TagLib.File.IFileAbstraction
    {
        public string Name => "audio.mp3";
        public Stream ReadStream { get; }
        public Stream WriteStream => throw new NotSupportedException();

        public MemoryFileAbstraction(byte[] data)
        {
            ReadStream = new MemoryStream(data, writable: false);
        }

        public void CloseStream(Stream stream)
        {
            stream.Dispose();
        }
    }
}
