using SharpCompress.Archives;

namespace SecureVault.Core.Archives;

public record ArchiveEntryInfo(
    string Path,
    long Size,
    bool IsDirectory,
    DateTime? LastModified);

/// <summary>
/// In-memory multi-format archive inspector and extractor powered by SharpCompress (K09-K11).
/// Supports ZIP, 7Z, TAR, GZ, and RAR.
/// CRITICAL: All extraction is strictly in-memory; never writes unpacked files to physical disk.
/// </summary>
public sealed class ArchiveReader : IDisposable
{
    private readonly IArchive _archive;
    private bool _disposed;

    public ArchiveReader(byte[] archiveData)
    {
        ArgumentNullException.ThrowIfNull(archiveData);
        var ms = new MemoryStream(archiveData, writable: false);
        _archive = ArchiveFactory.Open(ms);
    }

    public ArchiveReader(Stream archiveStream)
    {
        ArgumentNullException.ThrowIfNull(archiveStream);
        var ms = new MemoryStream();
        archiveStream.CopyTo(ms);
        ms.Seek(0, SeekOrigin.Begin);
        _archive = ArchiveFactory.Open(ms);
    }

    /// <summary>
    /// Lists all files and directories inside the archive (K09).
    /// </summary>
    public IReadOnlyList<ArchiveEntryInfo> ListContents()
    {
        EnsureNotDisposed();

        var list = new List<ArchiveEntryInfo>();
        foreach (var entry in _archive.Entries)
        {
            list.Add(new ArchiveEntryInfo(
                entry.Key ?? string.Empty,
                entry.Size,
                entry.IsDirectory,
                entry.LastModifiedTime));
        }

        return list;
    }

    /// <summary>
    /// Extracts a single entry from the archive into memory (K10).
    /// </summary>
    public byte[] ExtractSingle(string entryKey)
    {
        EnsureNotDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(entryKey);

        var entry = _archive.Entries.FirstOrDefault(e =>
            string.Equals(e.Key, entryKey, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e.Key?.Replace('/', '\\'), entryKey.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException($"Entry '{entryKey}' not found inside archive.");

        if (entry.IsDirectory)
        {
            return Array.Empty<byte>();
        }

        using var entryStream = entry.OpenEntryStream();
        using var ms = new MemoryStream();
        entryStream.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Extracts all files from the archive into memory buffers for vault ingestion (K11).
    /// </summary>
    public IReadOnlyList<(string RelativePath, byte[] Data)> ExtractAll()
    {
        EnsureNotDisposed();

        var results = new List<(string RelativePath, byte[] Data)>();

        foreach (var entry in _archive.Entries)
        {
            if (entry.IsDirectory || string.IsNullOrWhiteSpace(entry.Key))
                continue;

            using var entryStream = entry.OpenEntryStream();
            using var ms = new MemoryStream();
            entryStream.CopyTo(ms);
            results.Add((entry.Key, ms.ToArray()));
        }

        return results;
    }

    private void EnsureNotDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ArchiveReader));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _archive.Dispose();
        _disposed = true;
    }
}
