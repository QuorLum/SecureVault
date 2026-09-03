using SecureVault.Core.Organization;

namespace SecureVault.Core.Tests;

public class AutoCategorizerTests
{
    [Theory]
    [InlineData("portrait.JPG", FileCategory.Photos)]
    [InlineData("photo.png", FileCategory.Photos)]
    [InlineData("movie.MKV", FileCategory.Videos)]
    [InlineData("clip.mp4", FileCategory.Videos)]
    [InlineData("song.flac", FileCategory.Audio)]
    [InlineData("track.MP3", FileCategory.Audio)]
    [InlineData("contract.PDF", FileCategory.Documents)]
    [InlineData("sheet.xlsx", FileCategory.Documents)]
    [InlineData("notes.MD", FileCategory.TextNotes)]
    [InlineData("script.py", FileCategory.TextNotes)]
    [InlineData("setup.EXE", FileCategory.Applications)]
    [InlineData("backup.7z", FileCategory.Archives)]
    [InlineData("archive.tar.gz", FileCategory.Archives)]
    [InlineData("unknown.xyz", FileCategory.Other)]
    [InlineData("NO_EXTENSION", FileCategory.Other)]
    [InlineData("", FileCategory.Other)]
    public void Categorize_CorrectlyMapsExtensions(string filename, FileCategory expected)
    {
        var category = AutoCategorizer.Categorize(filename);
        Assert.Equal(expected, category);
    }
}
