using SecureVault.Core.Media;
using SkiaSharp;
using Xunit;

namespace SecureVault.Core.Tests;

public class ImageDecoderTests
{
    private static byte[] CreateTestImageBytes(int width, int height, SKEncodedImageFormat format = SKEncodedImageFormat.Png)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.DarkBlue);

        using var paint = new SKPaint { Color = SKColors.Cyan, IsAntialias = true };
        canvas.DrawCircle(width / 2f, height / 2f, Math.Min(width, height) / 4f, paint);

        using var image = surface.Snapshot();
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    [Fact]
    public void Decode_ValidPng_DecodesAccuratelyInMemory()
    {
        byte[] bytes = CreateTestImageBytes(300, 150, SKEncodedImageFormat.Png);

        using var bitmap = ImageDecoder.Decode(bytes);

        Assert.NotNull(bitmap);
        Assert.Equal(300, bitmap.Width);
        Assert.Equal(150, bitmap.Height);
    }

    [Fact]
    public void DecodeAtResolution_DownsamplesProportionally()
    {
        byte[] bytes = CreateTestImageBytes(400, 200, SKEncodedImageFormat.Jpeg);
        using var ms = new MemoryStream(bytes);

        using var resized = ImageDecoder.DecodeAtResolution(ms, 200, 200);

        Assert.NotNull(resized);
        // 400x200 fitted in 200x200 should scale to 200x100
        Assert.Equal(200, resized.Width);
        Assert.Equal(100, resized.Height);
    }

    [Fact]
    public void DecodeAtResolution_DoesNotUpscaleSmallerImage()
    {
        byte[] bytes = CreateTestImageBytes(80, 60, SKEncodedImageFormat.Png);
        using var ms = new MemoryStream(bytes);

        using var result = ImageDecoder.DecodeAtResolution(ms, 500, 500);

        Assert.Equal(80, result.Width);
        Assert.Equal(60, result.Height);
    }

    [Theory]
    [InlineData(90, 100, 200)]
    [InlineData(-90, 100, 200)]
    [InlineData(180, 200, 100)]
    [InlineData(360, 200, 100)]
    public void Rotate_AppliesCorrectAngleAndSwapsDimensions(float degrees, int expectedWidth, int expectedHeight)
    {
        byte[] bytes = CreateTestImageBytes(200, 100, SKEncodedImageFormat.Png);
        using var original = ImageDecoder.Decode(bytes);

        using var rotated = ImageDecoder.Rotate(original, degrees);

        Assert.NotNull(rotated);
        Assert.Equal(expectedWidth, rotated.Width);
        Assert.Equal(expectedHeight, rotated.Height);
    }

    [Fact]
    public void ExifMetadataReader_HandlesStreamWithoutExifGracefully()
    {
        byte[] bytes = CreateTestImageBytes(100, 100, SKEncodedImageFormat.Png);
        using var ms = new MemoryStream(bytes);

        var exif = ExifMetadataReader.Read(ms);

        Assert.NotNull(exif);
        Assert.Null(exif.CameraModel);
        Assert.Null(exif.GpsCoordinates);
    }
}
