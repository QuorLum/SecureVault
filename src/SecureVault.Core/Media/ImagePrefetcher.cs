using System.Collections.Concurrent;
using SecureVault.Core.Format;
using SkiaSharp;

namespace SecureVault.Core.Media;

/// <summary>
/// Pre-decodes adjacent images (currentIndex ± 1) into memory (E16).
/// Limits cache to at most 3 decoded bitmaps to strictly prevent memory bloat.
/// </summary>
public sealed class ImagePrefetcher : IDisposable
{
    private readonly VaultManager _vault;
    private readonly ConcurrentDictionary<Guid, SKBitmap> _cache = new();
    private bool _disposed;

    public ImagePrefetcher(VaultManager vault)
    {
        ArgumentNullException.ThrowIfNull(vault);
        _vault = vault;
    }

    public SKBitmap? GetPrefetched(Guid fileGuid)
    {
        if (_cache.TryGetValue(fileGuid, out var bitmap))
        {
            return bitmap.Copy();
        }
        return null;
    }

    public void PrefetchAdjacent(int currentIndex, IReadOnlyList<IndexEntry> photos)
    {
        if (photos.Count == 0) return;

        var neededGuids = new HashSet<Guid>();

        if (currentIndex > 0)
            neededGuids.Add(photos[currentIndex - 1].FileGuid);

        if (currentIndex < photos.Count - 1)
            neededGuids.Add(photos[currentIndex + 1].FileGuid);

        // Evict any cached bitmaps that are no longer adjacent
        foreach (var key in _cache.Keys)
        {
            if (!neededGuids.Contains(key))
            {
                if (_cache.TryRemove(key, out var oldBmp))
                {
                    oldBmp.Dispose();
                }
            }
        }

        // Asynchronously prefetch missing adjacent photos
        foreach (var guid in neededGuids)
        {
            if (!_cache.ContainsKey(guid))
            {
                var entry = photos.FirstOrDefault(p => p.FileGuid == guid);
                if (entry != null)
                {
                    Task.Run(() =>
                    {
                        try
                        {
                            using var stream = _vault.OpenFileStream(entry);
                            var bitmap = ImageDecoder.Decode(stream);
                            if (!_cache.TryAdd(guid, bitmap))
                            {
                                bitmap.Dispose();
                            }
                        }
                        catch
                        {
                            // Ignore background prefetch decode failures
                        }
                    });
                }
            }
        }
    }

    public void Clear()
    {
        foreach (var bmp in _cache.Values)
        {
            bmp.Dispose();
        }
        _cache.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        Clear();
        _disposed = true;
    }
}
