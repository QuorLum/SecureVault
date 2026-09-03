using SecureVault.Core.Cache;
using SecureVault.Core.Format;
using SecureVault.Core.Organization;

namespace SecureVault.Core.Media;

/// <summary>
/// Background parallel thumbnail generation service (E08, E14).
/// Stores and retrieves WebP thumbnails from the local encrypted cache.
/// </summary>
public sealed class ThumbnailService
{
    private readonly VaultManager _vault;
    private readonly VaultCache _cache;
    private readonly SemaphoreSlim _semaphore = new(Environment.ProcessorCount);

    public ThumbnailService(VaultManager vault, VaultCache cache)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(cache);

        _vault = vault;
        _cache = cache;
    }

    public byte[]? GetThumbnail(Guid fileGuid)
    {
        return _cache.GetThumbnail(fileGuid);
    }

    public void StoreThumbnail(Guid fileGuid, byte[] webpBytes)
    {
        _cache.PutThumbnail(fileGuid, webpBytes);
    }

    public async Task GenerateThumbnailForFileAsync(IndexEntry entry, CancellationToken ct = default)
    {
        if (entry.IsFolder) return;
        if (_cache.GetThumbnail(entry.FileGuid) != null) return;

        await _semaphore.WaitAsync(ct);
        try
        {
            using var stream = _vault.OpenFileStream(entry);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            byte[] bytes = ms.ToArray();

            byte[]? thumb = null;
            var cat = (FileCategory)entry.Category;

            if (cat == FileCategory.Photos)
            {
                thumb = ThumbnailGenerator.GenerateImageThumbnail(bytes);
            }
            else if (cat == FileCategory.Audio)
            {
                thumb = ThumbnailGenerator.GenerateAudioThumbnail(bytes);
            }
            else if (cat == FileCategory.Documents && entry.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                thumb = ThumbnailGenerator.GeneratePdfThumbnail(bytes);
            }

            if (thumb != null)
            {
                _cache.PutThumbnail(entry.FileGuid, thumb);
            }
        }
        catch
        {
            // Ignore individual thumbnail decoding errors
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task GenerateAllThumbnailsAsync(IProgress<int>? progress = null, CancellationToken ct = default)
    {
        List<IndexEntry> candidateList = _vault.Files
            .Where(f => !f.IsFolder && _cache.GetThumbnail(f.FileGuid) == null)
            .ToList();

        if (candidateList.Count == 0) return;

        int processed = 0;
        var tasks = candidateList.Select(async entry =>
        {
            await GenerateThumbnailForFileAsync(entry, ct);
            int count = Interlocked.Increment(ref processed);
            progress?.Report(count);
        });

        await Task.WhenAll(tasks);
    }
}
