using System.Text;
using System.Text.Json;
using SecureVault.Core.Media;
using SkiaSharp;
using Xunit;

namespace SecureVault.Core.Tests;

public class ThumbnailGeneratorTests
{
    private record ThumbnailVector(
        string Description,
        int InputWidth,
        int InputHeight,
        int MaxDimension,
        int ExpectedWidth,
        int ExpectedHeight);

    [Fact]
    public void GenerateImageThumbnail_MatchesVectorDimensions_AndGeneratesValidWebp()
    {
        string vectorJsonPath = Path.Combine(AppContext.BaseDirectory, "../../../../../tests/vectors/thumbnail-dimensions.json");
        if (!File.Exists(vectorJsonPath))
        {
            vectorJsonPath = Path.Combine(AppContext.BaseDirectory, "tests/vectors/thumbnail-dimensions.json");
        }

        if (File.Exists(vectorJsonPath))
        {
            var json = File.ReadAllText(vectorJsonPath);
            var vectors = JsonSerializer.Deserialize<List<ThumbnailVector>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

            foreach (var vector in vectors)
            {
                using var bmp = new SKBitmap(vector.InputWidth, vector.InputHeight);
                using (var canvas = new SKCanvas(bmp))
                {
                    canvas.Clear(SKColors.DarkBlue);
                }
                using var image = SKImage.FromBitmap(bmp);
                using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
                byte[] inputBytes = encoded.ToArray();

                byte[] thumbWebp = ThumbnailGenerator.GenerateImageThumbnail(inputBytes, vector.MaxDimension);

                // Verify WebP signature: "RIFF" .... "WEBP"
                Assert.True(thumbWebp.Length > 12);
                string riff = Encoding.ASCII.GetString(thumbWebp, 0, 4);
                string webp = Encoding.ASCII.GetString(thumbWebp, 8, 4);
                Assert.Equal("RIFF", riff);
                Assert.Equal("WEBP", webp);

                // Verify decoded dimensions <= MaxDimension
                using var decodedThumb = SKBitmap.Decode(thumbWebp);
                Assert.NotNull(decodedThumb);
                Assert.True(decodedThumb.Width <= vector.MaxDimension);
                Assert.True(decodedThumb.Height <= vector.MaxDimension);
                Assert.Equal(vector.ExpectedWidth, decodedThumb.Width);
                Assert.Equal(vector.ExpectedHeight, decodedThumb.Height);
            }
        }
    }

    [Fact]
    public void GenerateAudioThumbnail_WithoutAlbumArt_ReturnsNullGracefully()
    {
        byte[] dummyAudio = new byte[1024];
        var thumb = ThumbnailGenerator.GenerateAudioThumbnail(dummyAudio);
        Assert.Null(thumb);
    }
}
