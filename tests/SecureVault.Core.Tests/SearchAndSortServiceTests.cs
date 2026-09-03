using SecureVault.Core.Format;
using SecureVault.Core.Organization;

namespace SecureVault.Core.Tests;

public class SearchAndSortServiceTests
{
    private static VaultIndex CreateSampleIndex()
    {
        var index = new VaultIndex();
        index.Entries.Add(new IndexEntry
        {
            FileGuid = Guid.NewGuid(),
            FileName = "photo_vacation.jpg",
            Category = (byte)FileCategory.Photos,
            OriginalSize = 2_000_000,
            Tags = new[] { "travel", "summer" },
            Notes = "Trip to Hawaii",
            DateAddedTicks = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks,
            ProtectionMode = ProtectionMode.SecureMode
        });
        index.Entries.Add(new IndexEntry
        {
            FileGuid = Guid.NewGuid(),
            FileName = "tax_return_2025.pdf",
            Category = (byte)FileCategory.Documents,
            OriginalSize = 500_000,
            Tags = new[] { "finance", "urgent" },
            Notes = "Submitted to accountant",
            DateAddedTicks = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc).Ticks,
            ProtectionMode = ProtectionMode.SecureMode
        });
        index.Entries.Add(new IndexEntry
        {
            FileGuid = Guid.NewGuid(),
            FileName = "video_family.mp4",
            Category = (byte)FileCategory.Videos,
            OriginalSize = 50_000_000,
            Tags = new[] { "family" },
            DateAddedTicks = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc).Ticks,
            ProtectionMode = ProtectionMode.FastObfuscation
        });
        return index;
    }

    [Fact]
    public void SearchByFilename_CaseInsensitiveSubstring()
    {
        var index = CreateSampleIndex();
        var search = new SearchService(index);

        var results = search.SearchByFilename("VACATION");
        Assert.Single(results);
        Assert.Equal("photo_vacation.jpg", results[0].FileName);
    }

    [Fact]
    public void SearchByTags_MatchesExactTag()
    {
        var index = CreateSampleIndex();
        var search = new SearchService(index);

        var results = search.SearchByTags("finance");
        Assert.Single(results);
        Assert.Equal("tax_return_2025.pdf", results[0].FileName);
    }

    [Fact]
    public void SearchBySizeRange_FiltersProperly()
    {
        var index = CreateSampleIndex();
        var search = new SearchService(index);

        // Files between 1MB and 10MB
        var results = search.SearchBySizeRange(1_000_000, 10_000_000);
        Assert.Single(results);
        Assert.Equal("photo_vacation.jpg", results[0].FileName);
    }

    [Fact]
    public void SearchCombined_AppliesAndLogic()
    {
        var index = CreateSampleIndex();
        var search = new SearchService(index);

        var query = new SearchQuery
        {
            Category = FileCategory.Photos,
            Tag = "travel"
        };

        var results = search.SearchCombined(query);
        Assert.Single(results);
        Assert.Equal("photo_vacation.jpg", results[0].FileName);

        // Mismatched criteria returns empty
        query.Tag = "finance";
        Assert.Empty(search.SearchCombined(query));
    }

    [Fact]
    public void Sort_OrdersByAttributesStably()
    {
        var index = CreateSampleIndex();

        // Sort by size descending
        var sortedBySize = SortService.Sort(index.Entries, SortField.Size, SortDirection.Descending, foldersFirst: false);
        Assert.Equal("video_family.mp4", sortedBySize[0].FileName);
        Assert.Equal("photo_vacation.jpg", sortedBySize[1].FileName);
        Assert.Equal("tax_return_2025.pdf", sortedBySize[2].FileName);

        // Sort by name ascending
        var sortedByName = SortService.Sort(index.Entries, SortField.Name, SortDirection.Ascending, foldersFirst: false);
        Assert.Equal("photo_vacation.jpg", sortedByName[0].FileName);
        Assert.Equal("tax_return_2025.pdf", sortedByName[1].FileName);
        Assert.Equal("video_family.mp4", sortedByName[2].FileName);
    }
}
