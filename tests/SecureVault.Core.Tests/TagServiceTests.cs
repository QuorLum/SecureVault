using SecureVault.Core.Format;
using SecureVault.Core.Organization;

namespace SecureVault.Core.Tests;

public class TagServiceTests
{
    [Fact]
    public void AddAndRemoveTags_DeduplicatesAndUpdates()
    {
        var index = new VaultIndex();
        var service = new TagService(index);

        var fileGuid = Guid.NewGuid();
        index.Entries.Add(new IndexEntry
        {
            FileGuid = fileGuid,
            FileName = "photo.jpg"
        });

        service.AddTag(fileGuid, "vacation");
        service.AddTag(fileGuid, "VACATION"); // Case-insensitive duplicate
        service.AddTag(fileGuid, "summer");

        var tags = service.GetTags(fileGuid);
        Assert.Equal(2, tags.Count);
        Assert.Contains("vacation", tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("summer", tags, StringComparer.OrdinalIgnoreCase);

        service.RemoveTag(fileGuid, "vacation");
        var updated = service.GetTags(fileGuid);
        Assert.Single(updated);
        Assert.Equal("summer", updated[0]);
    }

    [Fact]
    public void GetAllUniqueTags_AggregatesAcrossFiles()
    {
        var index = new VaultIndex();
        var service = new TagService(index);

        var file1 = Guid.NewGuid();
        var file2 = Guid.NewGuid();

        index.Entries.Add(new IndexEntry { FileGuid = file1, FileName = "a.txt" });
        index.Entries.Add(new IndexEntry { FileGuid = file2, FileName = "b.txt" });

        service.AddTag(file1, "work");
        service.AddTag(file1, "urgent");
        service.AddTag(file2, "work");
        service.AddTag(file2, "personal");

        var all = service.GetAllUniqueTags();
        Assert.Equal(3, all.Count);
        Assert.Equal(new[] { "personal", "urgent", "work" }, all);
    }

    [Fact]
    public void SetFavorite_UpdatesFlagAndQueriesFavorites()
    {
        var index = new VaultIndex();
        var service = new TagService(index);

        var file1 = Guid.NewGuid();
        var file2 = Guid.NewGuid();

        index.Entries.Add(new IndexEntry { FileGuid = file1, FileName = "fav.txt" });
        index.Entries.Add(new IndexEntry { FileGuid = file2, FileName = "regular.txt" });

        service.SetFavorite(file1, true);

        var favorites = service.GetFavorites();
        Assert.Single(favorites);
        Assert.Equal("fav.txt", favorites[0].FileName);

        service.SetFavorite(file1, false);
        Assert.Empty(service.GetFavorites());
    }
}
