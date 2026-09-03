using SecureVault.Core.Format;

namespace SecureVault.Core.Organization;

/// <summary>
/// Service managing file tags and favorites flags within the VaultIndex (D05, D06).
/// </summary>
public sealed class TagService
{
    private readonly VaultIndex _index;

    public TagService(VaultIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);
        _index = index;
    }

    /// <summary>
    /// Adds a tag to a file entry (case-insensitive deduplication).
    /// </summary>
    public void AddTag(Guid fileGuid, string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        var entry = _index.Entries.FirstOrDefault(e => e.FileGuid == fileGuid && !e.IsDeleted);
        if (entry == null)
            return;

        string normalized = tag.Trim();
        var existing = new HashSet<string>(entry.Tags ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        if (existing.Add(normalized))
        {
            entry.Tags = existing.ToArray();
            entry.DateModifiedTicks = DateTime.UtcNow.Ticks;
        }
    }

    /// <summary>
    /// Removes a tag from a file entry.
    /// </summary>
    public void RemoveTag(Guid fileGuid, string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        var entry = _index.Entries.FirstOrDefault(e => e.FileGuid == fileGuid && !e.IsDeleted);
        if (entry == null || entry.Tags == null)
            return;

        string normalized = tag.Trim();
        var existing = new HashSet<string>(entry.Tags, StringComparer.OrdinalIgnoreCase);

        if (existing.Remove(normalized))
        {
            entry.Tags = existing.ToArray();
            entry.DateModifiedTicks = DateTime.UtcNow.Ticks;
        }
    }

    /// <summary>
    /// Retrieves all tags for a specific file.
    /// </summary>
    public IReadOnlyList<string> GetTags(Guid fileGuid)
    {
        var entry = _index.Entries.FirstOrDefault(e => e.FileGuid == fileGuid && !e.IsDeleted);
        return entry?.Tags ?? Array.Empty<string>();
    }

    /// <summary>
    /// Enumerates all unique tags across all active files in the vault.
    /// </summary>
    public IReadOnlyList<string> GetAllUniqueTags()
    {
        var allTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in _index.Entries)
        {
            if (!entry.IsDeleted && entry.Tags != null)
            {
                foreach (var tag in entry.Tags)
                {
                    allTags.Add(tag);
                }
            }
        }

        return allTags.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Sets or unsets the favorite flag for a file entry (D06).
    /// </summary>
    public void SetFavorite(Guid fileGuid, bool isFavorite)
    {
        var entry = _index.Entries.FirstOrDefault(e => e.FileGuid == fileGuid && !e.IsDeleted);
        if (entry != null && entry.IsFavorite != isFavorite)
        {
            entry.IsFavorite = isFavorite;
            entry.DateModifiedTicks = DateTime.UtcNow.Ticks;
        }
    }

    /// <summary>
    /// Retrieves all active favorite files in the vault.
    /// </summary>
    public IReadOnlyList<IndexEntry> GetFavorites()
    {
        return _index.Entries
            .Where(e => !e.IsFolder && !e.IsDeleted && e.IsFavorite)
            .OrderBy(e => e.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
