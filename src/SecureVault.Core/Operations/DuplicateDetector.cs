using SecureVault.Core.Format;

namespace SecureVault.Core.Operations;

public sealed class DuplicateGroup
{
    public string PlaintextSha256Hex { get; init; } = string.Empty;
    public List<IndexEntry> Files { get; init; } = new();
    public int DuplicateCount => Files.Count;
    public ulong FileSize => Files.FirstOrDefault()?.OriginalSize ?? 0;

    /// <summary>
    /// Total storage wasted by redundant physical copies.
    /// Files that share chunk offsets with a sibling consume 0 additional physical chunk bytes.
    /// </summary>
    public ulong WastedStorageBytes { get; set; }
}

/// <summary>
/// Scans vault index entries to identify duplicate files based on plaintext SHA-256 (C21).
/// Differentiates between independent duplicate chunks (which waste disk space) and shared chunks.
/// </summary>
public static class DuplicateDetector
{
    public static IReadOnlyList<DuplicateGroup> FindDuplicates(VaultIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);
        return FindDuplicates(index.Entries);
    }

    public static IReadOnlyList<DuplicateGroup> FindDuplicates(IEnumerable<IndexEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var liveFiles = entries
            .Where(e => !e.IsDeleted && !e.IsFolder && e.OriginalSize > 0)
            .ToList();

        var groups = liveFiles
            .GroupBy(e => Convert.ToHexString(e.PlaintextSHA256).ToLowerInvariant())
            .Where(g => g.Count() > 1)
            .Select(g =>
            {
                var files = g.ToList();
                var dupGroup = new DuplicateGroup
                {
                    PlaintextSha256Hex = g.Key,
                    Files = files
                };

                // Track distinct physical chunk offsets to calculate actual wasted space
                HashSet<ulong> seenOffsets = new();
                ulong wastedBytes = 0;
                bool isFirstFile = true;

                foreach (var file in files)
                {
                    if (isFirstFile)
                    {
                        foreach (var chunk in file.Chunks)
                        {
                            seenOffsets.Add(chunk.AbsoluteOffset);
                        }
                        isFirstFile = false;
                        continue;
                    }

                    // If file has independent physical chunks not shared with previous files in group, it wastes space
                    bool hasIndependentChunks = file.Chunks.Any(c => !seenOffsets.Contains(c.AbsoluteOffset));
                    if (hasIndependentChunks)
                    {
                        wastedBytes += file.OriginalSize;
                        foreach (var chunk in file.Chunks)
                        {
                            seenOffsets.Add(chunk.AbsoluteOffset);
                        }
                    }
                }

                dupGroup.WastedStorageBytes = wastedBytes;
                return dupGroup;
            })
            .OrderByDescending(g => g.WastedStorageBytes)
            .ToList();

        return groups;
    }
}
