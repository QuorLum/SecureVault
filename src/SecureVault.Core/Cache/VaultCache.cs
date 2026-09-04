using MessagePack;
using SecureVault.Core.Crypto;
using SecureVault.Core.Format;
using SecureVault.Core.IO;
using SecureVault.Core.Organization;

namespace SecureVault.Core.Cache;

[MessagePackObject]
public sealed class UIState
{
    [Key(0)]
    public double WindowWidth { get; set; } = 1100;

    [Key(1)]
    public double WindowHeight { get; set; } = 720;

    [Key(2)]
    public bool IsMaximized { get; set; }

    [Key(3)]
    public Guid? LastViewedFolderGuid { get; set; }

    [Key(4)]
    public byte? SelectedCategory { get; set; }

    [Key(5)]
    public int SortField { get; set; } = (int)Organization.SortField.Name;

    [Key(6)]
    public int SortDirection { get; set; } = (int)Organization.SortDirection.Ascending;

    [Key(7)]
    public int ViewMode { get; set; } = 0; // 0 = Grid, 1 = Detailed List, 2 = Timeline

    [Key(8)]
    public List<Guid> RecentFileGuids { get; set; } = new();
}

[MessagePackObject]
public sealed class CacheSnapshotData
{
    [Key(0)]
    public Guid VaultUUID { get; set; }

    [Key(1)]
    public ulong VaultIndexVersion { get; set; }

    [Key(2)]
    public long LastSyncUtcTicks { get; set; }

    [Key(3)]
    public byte[] SerializedIndex { get; set; } = Array.Empty<byte>();

    [Key(4)]
    public Dictionary<Guid, byte[]> Thumbnails { get; set; } = new();

    [Key(5)]
    public UIState UIState { get; set; } = new();
}

/// <summary>
/// Manages encrypted local caching for instant startup and thumbnail caching (E01-E05).
/// Stored in %LOCALAPPDATA%\SecureVault\cache\{vaultUUID}.cache.
/// Encrypted with an AES-256-GCM subkey derived from the master key.
/// </summary>
public sealed class VaultCache : IDisposable
{
    private readonly Guid _vaultUUID;
    private readonly SecureBuffer _cacheKey;
    private readonly string _cacheFilePath;
    private bool _disposed;

    public string CacheFilePath => _cacheFilePath;

    public VaultCache(Guid vaultUUID, SecureBuffer cacheKey, string? customCacheDir = null)
    {
        ArgumentNullException.ThrowIfNull(cacheKey);
        _vaultUUID = vaultUUID;
        _cacheKey = new SecureBuffer(cacheKey.Length);
        cacheKey.AsReadOnlySpan().CopyTo(_cacheKey.AsSpan());

        string baseDir = !string.IsNullOrWhiteSpace(customCacheDir)
            ? customCacheDir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SecureVault", "cache");

        Directory.CreateDirectory(baseDir);
        _cacheFilePath = Path.Combine(baseDir, $"{vaultUUID:N}.cache");
    }

    private readonly Dictionary<Guid, byte[]> _inMemoryThumbnails = new();

    public byte[]? GetThumbnail(Guid fileGuid)
    {
        lock (_inMemoryThumbnails)
        {
            if (_inMemoryThumbnails.TryGetValue(fileGuid, out var bytes))
                return bytes;
        }
        return null;
    }

    public void PutThumbnail(Guid fileGuid, byte[] bytes)
    {
        lock (_inMemoryThumbnails)
        {
            _inMemoryThumbnails[fileGuid] = bytes;
        }
    }

    /// <summary>
    /// Atomically persists an encrypted cache snapshot to disk (E01, E02).
    /// </summary>
    public void SaveSnapshot(VaultIndex index, Dictionary<Guid, byte[]> thumbnails, UIState uiState)
    {
        EnsureNotDisposed();
        ArgumentNullException.ThrowIfNull(index);

        var data = new CacheSnapshotData
        {
            VaultUUID = _vaultUUID,
            VaultIndexVersion = index.Version,
            LastSyncUtcTicks = DateTime.UtcNow.Ticks,
            SerializedIndex = index.Serialize(),
            Thumbnails = thumbnails ?? new(),
            UIState = uiState ?? new()
        };

        byte[] plainBytes = MessagePackSerializer.Serialize(data);
        byte[] encryptedBytes = CacheEncryption.Encrypt(plainBytes, _cacheKey);

        AtomicWriter.WriteAllBytes(_cacheFilePath, encryptedBytes);
    }

    /// <summary>
    /// Reads and decrypts the cache snapshot.
    /// If the cache file does not exist, is stale, or is corrupted, returns null gracefully without throwing (E03).
    /// </summary>
    public (VaultIndex? Index, Dictionary<Guid, byte[]>? Thumbnails, UIState? UIState) LoadSnapshot()
    {
        EnsureNotDisposed();

        if (!File.Exists(_cacheFilePath))
            return (null, null, null);

        try
        {
            byte[] encryptedBytes = File.ReadAllBytes(_cacheFilePath);
            byte[] plainBytes = CacheEncryption.Decrypt(encryptedBytes, _cacheKey);

            var data = MessagePackSerializer.Deserialize<CacheSnapshotData>(plainBytes);
            if (data == null || data.VaultUUID != _vaultUUID)
                return (null, null, null);

            var index = VaultIndex.Deserialize(data.SerializedIndex);
            index.Version = data.VaultIndexVersion;

            return (index, data.Thumbnails, data.UIState);
        }
        catch
        {
            // Graceful fallback on corruption per E03 specification
            return (null, null, null);
        }
    }

    /// <summary>
    /// Checks whether the cache is out of date compared to the current VaultIndex version (E04).
    /// </summary>
    public bool IsStale(VaultIndex currentIndex)
    {
        EnsureNotDisposed();
        ArgumentNullException.ThrowIfNull(currentIndex);

        var (cachedIndex, _, _) = LoadSnapshot();
        if (cachedIndex == null)
            return true;

        return cachedIndex.Version != currentIndex.Version;
    }

    /// <summary>
    /// Deletes the cache file from disk.
    /// </summary>
    public void Invalidate()
    {
        EnsureNotDisposed();
        if (File.Exists(_cacheFilePath))
        {
            try { File.Delete(_cacheFilePath); } catch { }
        }
    }

    private void EnsureNotDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(VaultCache));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _cacheKey.Dispose();
        _disposed = true;
    }
}
