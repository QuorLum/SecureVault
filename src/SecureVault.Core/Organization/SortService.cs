using SecureVault.Core.Format;

namespace SecureVault.Core.Organization;

public enum SortField
{
    Name,
    DateAdded,
    DateModified,
    Size,
    Type
}

public enum SortDirection
{
    Ascending,
    Descending
}

/// <summary>
/// Provides stable multi-attribute sorting for vault index entries (D15).
/// </summary>
public static class SortService
{
    public static IReadOnlyList<IndexEntry> Sort(
        IEnumerable<IndexEntry> entries,
        SortField field,
        SortDirection direction,
        bool foldersFirst = true)
    {
        ArgumentNullException.ThrowIfNull(entries);

        // Primary partitioning: folders first if requested
        IOrderedEnumerable<IndexEntry> ordered;

        if (foldersFirst)
        {
            ordered = entries.OrderByDescending(e => e.IsFolder);

            ordered = field switch
            {
                SortField.Name => direction == SortDirection.Ascending
                    ? ordered.ThenBy(e => e.FileName, StringComparer.OrdinalIgnoreCase)
                    : ordered.ThenByDescending(e => e.FileName, StringComparer.OrdinalIgnoreCase),

                SortField.DateAdded => direction == SortDirection.Ascending
                    ? ordered.ThenBy(e => e.DateAddedTicks)
                    : ordered.ThenByDescending(e => e.DateAddedTicks),

                SortField.DateModified => direction == SortDirection.Ascending
                    ? ordered.ThenBy(e => e.DateModifiedTicks)
                    : ordered.ThenByDescending(e => e.DateModifiedTicks),

                SortField.Size => direction == SortDirection.Ascending
                    ? ordered.ThenBy(e => e.OriginalSize)
                    : ordered.ThenByDescending(e => e.OriginalSize),

                SortField.Type => direction == SortDirection.Ascending
                    ? ordered.ThenBy(e => e.Category).ThenBy(e => e.FileName, StringComparer.OrdinalIgnoreCase)
                    : ordered.ThenByDescending(e => e.Category).ThenBy(e => e.FileName, StringComparer.OrdinalIgnoreCase),

                _ => ordered.ThenBy(e => e.FileName, StringComparer.OrdinalIgnoreCase)
            };
        }
        else
        {
            ordered = field switch
            {
                SortField.Name => direction == SortDirection.Ascending
                    ? entries.OrderBy(e => e.FileName, StringComparer.OrdinalIgnoreCase)
                    : entries.OrderByDescending(e => e.FileName, StringComparer.OrdinalIgnoreCase),

                SortField.DateAdded => direction == SortDirection.Ascending
                    ? entries.OrderBy(e => e.DateAddedTicks)
                    : entries.OrderByDescending(e => e.DateAddedTicks),

                SortField.DateModified => direction == SortDirection.Ascending
                    ? entries.OrderBy(e => e.DateModifiedTicks)
                    : entries.OrderByDescending(e => e.DateModifiedTicks),

                SortField.Size => direction == SortDirection.Ascending
                    ? entries.OrderBy(e => e.OriginalSize)
                    : entries.OrderByDescending(e => e.OriginalSize),

                SortField.Type => direction == SortDirection.Ascending
                    ? entries.OrderBy(e => e.Category).ThenBy(e => e.FileName, StringComparer.OrdinalIgnoreCase)
                    : entries.OrderByDescending(e => e.Category).ThenBy(e => e.FileName, StringComparer.OrdinalIgnoreCase),

                _ => entries.OrderBy(e => e.FileName, StringComparer.OrdinalIgnoreCase)
            };
        }

        return ordered.ToList();
    }
}
