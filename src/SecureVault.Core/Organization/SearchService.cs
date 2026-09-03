using SecureVault.Core.Format;

namespace SecureVault.Core.Organization;

/// <summary>
/// Combined query parameters for multi-criteria search filtering (D14).
/// </summary>
public sealed class SearchQuery
{
    public string? Filename { get; set; }
    public string? Tag { get; set; }
    public string? Notes { get; set; }
    public FileCategory? Category { get; set; }
    public DateTime? DateStart { get; set; }
    public DateTime? DateEnd { get; set; }
    public long? MinBytes { get; set; }
    public long? MaxBytes { get; set; }
    public ProtectionMode? Protection { get; set; }
    public bool IncludeDeleted { get; set; } = false;
    public bool IncludeFolders { get; set; } = false;
}

/// <summary>
/// Executes high-speed, in-memory searches and filters over vault metadata (D08-D14).
/// Never touches physical disk files; searches operate entirely on VaultIndex.
/// </summary>
public sealed class SearchService
{
    private readonly VaultIndex _index;

    public SearchService(VaultIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);
        _index = index;
    }

    /// <summary>
    /// Searches files by substring in filename (case-insensitive) (D08).
    /// </summary>
    public IReadOnlyList<IndexEntry> SearchByFilename(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return _index.Entries.Where(e => !e.IsDeleted).ToList();

        return _index.Entries
            .Where(e => !e.IsDeleted && e.FileName.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Searches files possessing an exact tag (case-insensitive) (D09).
    /// </summary>
    public IReadOnlyList<IndexEntry> SearchByTags(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return Array.Empty<IndexEntry>();

        string normalized = tag.Trim();
        return _index.Entries
            .Where(e => !e.IsDeleted && e.Tags != null && e.Tags.Any(t => string.Equals(t, normalized, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    /// <summary>
    /// Searches files by substring in notes (case-insensitive) (D10).
    /// </summary>
    public IReadOnlyList<IndexEntry> SearchByNotes(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<IndexEntry>();

        return _index.Entries
            .Where(e => !e.IsDeleted && !string.IsNullOrEmpty(e.Notes) && e.Notes.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Filters files by category (D11).
    /// </summary>
    public IReadOnlyList<IndexEntry> SearchByCategory(FileCategory category)
    {
        byte categoryByte = (byte)category;
        return _index.Entries
            .Where(e => !e.IsFolder && !e.IsDeleted && e.Category == categoryByte)
            .ToList();
    }

    /// <summary>
    /// Filters files within a date range (UTC) (D12).
    /// </summary>
    public IReadOnlyList<IndexEntry> SearchByDateRange(DateTime start, DateTime end)
    {
        long startTicks = start.ToUniversalTime().Ticks;
        long endTicks = end.ToUniversalTime().Ticks;

        return _index.Entries
            .Where(e => !e.IsDeleted && e.DateAddedTicks >= startTicks && e.DateAddedTicks <= endTicks)
            .ToList();
    }

    /// <summary>
    /// Filters files within a byte size range (D13).
    /// </summary>
    public IReadOnlyList<IndexEntry> SearchBySizeRange(long minBytes, long maxBytes)
    {
        ulong min = minBytes < 0 ? 0 : (ulong)minBytes;
        ulong max = maxBytes < 0 ? ulong.MaxValue : (ulong)maxBytes;

        return _index.Entries
            .Where(e => !e.IsFolder && !e.IsDeleted && e.OriginalSize >= min && e.OriginalSize <= max)
            .ToList();
    }

    /// <summary>
    /// Filters files by cryptographic protection mode (D14).
    /// </summary>
    public IReadOnlyList<IndexEntry> SearchByProtection(ProtectionMode mode)
    {
        return _index.Entries
            .Where(e => !e.IsFolder && !e.IsDeleted && e.ProtectionMode == mode)
            .ToList();
    }

    /// <summary>
    /// Combines multiple filter criteria using Boolean AND logic (D14).
    /// </summary>
    public IReadOnlyList<IndexEntry> SearchCombined(SearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var filtered = _index.Entries.AsEnumerable();

        if (!query.IncludeDeleted)
            filtered = filtered.Where(e => !e.IsDeleted);

        if (!query.IncludeFolders)
            filtered = filtered.Where(e => !e.IsFolder);

        if (!string.IsNullOrWhiteSpace(query.Filename))
            filtered = filtered.Where(e => e.FileName.Contains(query.Filename, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(query.Tag))
        {
            string normTag = query.Tag.Trim();
            filtered = filtered.Where(e => e.Tags != null && e.Tags.Any(t => string.Equals(t, normTag, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(query.Notes))
            filtered = filtered.Where(e => !string.IsNullOrEmpty(e.Notes) && e.Notes.Contains(query.Notes, StringComparison.OrdinalIgnoreCase));

        if (query.Category.HasValue)
        {
            byte cat = (byte)query.Category.Value;
            filtered = filtered.Where(e => e.Category == cat);
        }

        if (query.DateStart.HasValue)
        {
            long start = query.DateStart.Value.ToUniversalTime().Ticks;
            filtered = filtered.Where(e => e.DateAddedTicks >= start);
        }

        if (query.DateEnd.HasValue)
        {
            long end = query.DateEnd.Value.ToUniversalTime().Ticks;
            filtered = filtered.Where(e => e.DateAddedTicks <= end);
        }

        if (query.MinBytes.HasValue)
        {
            ulong min = (ulong)query.MinBytes.Value;
            filtered = filtered.Where(e => e.OriginalSize >= min);
        }

        if (query.MaxBytes.HasValue)
        {
            ulong max = (ulong)query.MaxBytes.Value;
            filtered = filtered.Where(e => e.OriginalSize <= max);
        }

        if (query.Protection.HasValue)
        {
            filtered = filtered.Where(e => e.ProtectionMode == query.Protection.Value);
        }

        return filtered.ToList();
    }
}
