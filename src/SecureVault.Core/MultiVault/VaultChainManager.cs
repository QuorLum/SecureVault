using System.Security.Cryptography;
using SecureVault.Core.Crypto;
using SecureVault.Core.Exceptions;
using SecureVault.Core.Format;
using SecureVault.Core.IO;
using SecureVault.Core.Operations;

namespace SecureVault.Core.MultiVault;

public sealed class SecondaryVaultPart : IDisposable
{
    public int PartIndex { get; }
    public string FilePath { get; }
    public VaultFileLock FileLock { get; }
    public FileStream Stream { get; internal set; }
    public SecondaryVaultHeader Header { get; internal set; }
    public VaultIndex LocalIndex { get; }
    public SemaphoreSlim StreamLock { get; } = new(1, 1);

    public SecondaryVaultPart(
        int partIndex,
        string filePath,
        VaultFileLock fileLock,
        FileStream stream,
        SecondaryVaultHeader header,
        VaultIndex localIndex)
    {
        PartIndex = partIndex;
        FilePath = filePath;
        FileLock = fileLock;
        Stream = stream;
        Header = header;
        LocalIndex = localIndex;
    }

    public void Dispose()
    {
        try { Stream.Flush(flushToDisk: true); } catch { }
        Stream.Dispose();
        FileLock.Dispose();
        StreamLock.Dispose();
    }
}

/// <summary>
/// Coordinates multi-part vault chains (.vault, .vault2, .vault3, etc.) (O01-O07, B23-B26).
/// Enforces 200GB limit with pre-write rollover, master global index, and transparent cross-part streaming.
/// </summary>
public sealed class VaultChainManager : IDisposable
{
    private readonly VaultManager _masterVault;
    private readonly Dictionary<int, SecondaryVaultPart> _secondaryParts = new();
    private long _maxPartSizeBytes;
    private bool _disposed;

    public VaultManager MasterVault => _masterVault;
    public long MaxPartSizeBytes => _maxPartSizeBytes;
    public IReadOnlyDictionary<int, SecondaryVaultPart> SecondaryParts => _secondaryParts;
    public IReadOnlyList<IndexEntry> GlobalFiles => _masterVault.Files;

    public VaultChainManager(VaultManager masterVault, long? maxPartSizeBytes = null)
    {
        _masterVault = masterVault ?? throw new ArgumentNullException(nameof(masterVault));
        _maxPartSizeBytes = maxPartSizeBytes ?? VaultConstants.MaxVaultFileSizeBytes;

        DiscoverAndOpenSecondaryParts();
        SyncAvailability();
    }

    public void SetMaxPartSizeForTesting(long maxPartSizeBytes)
    {
        _maxPartSizeBytes = maxPartSizeBytes;
    }

    public static string GetPartPath(string masterVaultPath, int partIndex)
    {
        if (partIndex == 0) return Path.GetFullPath(masterVaultPath);

        string dir = Path.GetDirectoryName(masterVaultPath) ?? string.Empty;
        string nameWithoutExt = Path.GetFileNameWithoutExtension(masterVaultPath);
        return Path.Combine(dir, $"{nameWithoutExt}.vault{partIndex + 1}");
    }

    public static string GetPartFileName(string masterVaultPath, int partIndex)
    {
        return Path.GetFileName(GetPartPath(masterVaultPath, partIndex));
    }

    private void DiscoverAndOpenSecondaryParts()
    {
        string manifestPath = VaultChainManifest.GetManifestPath(_masterVault.VaultPath);
        List<int> partIndicesToTry = new();

        if (File.Exists(manifestPath))
        {
            try
            {
                var manifest = VaultChainManifest.LoadFromFile(manifestPath);
                foreach (var p in manifest.Parts)
                {
                    if (p.PartIndex > 0) partIndicesToTry.Add(p.PartIndex);
                }
            }
            catch { }
        }

        // Also probe sequentially on disk (.vault2, .vault3...) up to first missing or gap
        for (int i = 1; i <= 100; i++)
        {
            string partPath = GetPartPath(_masterVault.VaultPath, i);
            if (File.Exists(partPath) && !partIndicesToTry.Contains(i))
            {
                partIndicesToTry.Add(i);
            }
            else if (!File.Exists(partPath) && !partIndicesToTry.Contains(i))
            {
                break;
            }
        }

        foreach (int partIndex in partIndicesToTry.OrderBy(x => x))
        {
            string partPath = GetPartPath(_masterVault.VaultPath, partIndex);
            if (!File.Exists(partPath))
                continue;

            try
            {
                var part = OpenSecondaryPart(partPath, partIndex);
                _secondaryParts[partIndex] = part;
            }
            catch
            {
                // Unreadable or corrupted secondary part will be reported via health checks
            }
        }
    }

    private SecondaryVaultPart OpenSecondaryPart(string partPath, int partIndex)
    {
        var fileLock = new VaultFileLock(partPath);
        try
        {
            var stream = new FileStream(partPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var header = SecondaryVaultHeader.ReadFrom(stream);

            if (header.MasterVaultUUID != _masterVault.VaultUUID)
            {
                throw new CorruptedVaultException($"Secondary vault part {partIndex} UUID mismatch with master vault.");
            }

            if (!header.VerifyHmac(_masterVault.MasterKey))
            {
                throw new CorruptedVaultException($"Secondary vault part {partIndex} header HMAC verification failed.");
            }

            // Read local index
            VaultIndex localIndex = new();
            if (header.LocalIndexOffset > 0 && header.LocalIndexLength > 0)
            {
                localIndex = ReadLocalIndex(stream, header.LocalIndexOffset, header.LocalIndexLength);
            }

            return new SecondaryVaultPart(partIndex, partPath, fileLock, stream, header, localIndex);
        }
        catch
        {
            fileLock.Dispose();
            throw;
        }
    }

    private VaultIndex ReadLocalIndex(Stream stream, ulong offset, ulong length)
    {
        stream.Seek((long)offset, SeekOrigin.Begin);
        byte[] payload = new byte[length];
        int read = stream.ReadAtLeast(payload, (int)length, throwOnEndOfStream: false);
        if (read < (int)length)
        {
            throw new CorruptedIndexException("Secondary part local index block truncated.");
        }

        byte[] nonce = payload[0..12];
        byte[] tag = payload[12..28];
        int cipherLen = BitConverter.ToInt32(payload, 28);
        if (cipherLen <= 0 || 32 + cipherLen > payload.Length)
        {
            throw new CorruptedIndexException("Secondary part invalid index ciphertext length.");
        }

        byte[] ciphertext = payload[32..(32 + cipherLen)];
        byte[] parity = payload[(32 + cipherLen)..];

        byte[] repairedCiphertext = ciphertext;
        if (parity.Length > 0)
        {
            var (repaired, _) = _masterVault.RsCodec.Decode(ciphertext, parity);
            repairedCiphertext = repaired;
        }

        byte[] plaintext = _masterVault.Encryption.DecryptIndex(repairedCiphertext, nonce, tag);
        return VaultIndex.Deserialize(plaintext);
    }

    private void SaveLocalIndex(SecondaryVaultPart part)
    {
        ulong indexOffset = part.Header.LocalIndexOffset > 0 ? part.Header.LocalIndexOffset : (ulong)part.Stream.Position;
        part.Stream.Seek((long)indexOffset, SeekOrigin.Begin);

        part.LocalIndex.Version++;
        byte[] serialized = part.LocalIndex.Serialize();
        var (ciphertext, nonce, tag) = _masterVault.Encryption.EncryptIndex(serialized);
        byte[] parity = _masterVault.RsCodec.Encode(ciphertext);

        byte[] payload = new byte[12 + 16 + 4 + ciphertext.Length + parity.Length];
        nonce.CopyTo(payload, 0);
        tag.CopyTo(payload, 12);
        BitConverter.GetBytes(ciphertext.Length).CopyTo(payload, 28);
        ciphertext.CopyTo(payload, 32);
        parity.CopyTo(payload, 32 + ciphertext.Length);

        ulong pOff = (ulong)part.Stream.Position;
        part.Stream.Write(payload);
        uint pLen = (uint)payload.Length;

        part.Header.LocalIndexOffset = pOff;
        part.Header.LocalIndexLength = pLen;
        part.Header.UpdateHmac(_masterVault.MasterKey);

        part.Stream.SetLength(part.Stream.Position);

        // Rewrite secondary header
        part.Stream.Seek(0, SeekOrigin.Begin);
        part.Header.WriteTo(part.Stream);
        part.Stream.Flush(flushToDisk: true);
    }

    /// <summary>
    /// Synchronizes the IsAvailable flag on all files based on presence of their target vault parts.
    /// </summary>
    public void SyncAvailability()
    {
        foreach (var file in _masterVault.Files)
        {
            if (file.PartIndex == 0)
            {
                file.IsAvailable = true;
            }
            else
            {
                file.IsAvailable = _secondaryParts.ContainsKey(file.PartIndex);
            }
        }
    }

    /// <summary>
    /// Creates and attaches the next secondary vault part (.vault{N+1}).
    /// </summary>
    public SecondaryVaultPart AllocateNextPart()
    {
        int nextIndex = _secondaryParts.Count > 0 ? _secondaryParts.Keys.Max() + 1 : 1;
        string partPath = GetPartPath(_masterVault.VaultPath, nextIndex);

        var fileLock = new VaultFileLock(partPath);
        try
        {
            var stream = new FileStream(partPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            var header = SecondaryVaultHeader.Create(_masterVault.MasterKey, _masterVault.VaultUUID, nextIndex);
            header.WriteTo(stream);

            var localIndex = new VaultIndex();
            var part = new SecondaryVaultPart(nextIndex, partPath, fileLock, stream, header, localIndex);
            SaveLocalIndex(part);

            _secondaryParts[nextIndex] = part;
            UpdateChainManifest();
            return part;
        }
        catch
        {
            fileLock.Dispose();
            if (File.Exists(partPath))
            {
                try { File.Delete(partPath); } catch { }
            }
            throw;
        }
    }

    /// <summary>
    /// Adds a file with strict pre-write rollover check:
    /// If the estimated size exceeds current part remaining capacity, rolls to the next part before writing any chunks.
    /// </summary>
    public async Task<IndexEntry> AddFileAsync(
        Stream sourceStream,
        string fileName,
        string virtualPath = "/",
        ProtectionMode mode = ProtectionMode.SecureMode,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        long sourceLength = sourceStream.CanSeek ? sourceStream.Length : 0;
        long estimatedRequired = sourceLength + (VaultConstants.ChunkHeaderSize * (sourceLength / VaultConstants.DefaultChunkSize + 2)) + 4096;

        // Determine current write target part
        int targetPartIndex = _secondaryParts.Count > 0 ? _secondaryParts.Keys.Max() : 0;
        long currentLength = targetPartIndex == 0 ? _masterVault.StreamLength : _secondaryParts[targetPartIndex].Stream.Length;

        // File-spans-part boundary rule:
        // If file doesn't fit in current part's remaining space, roll over before writing any chunks
        if (currentLength + estimatedRequired > _maxPartSizeBytes)
        {
            var newPart = AllocateNextPart();
            targetPartIndex = newPart.PartIndex;
        }

        IndexEntry entry;
        if (targetPartIndex == 0)
        {
            entry = await _masterVault.AddFileAsync(sourceStream, fileName, virtualPath, mode, progress, ct);
            entry.PartIndex = 0;
        }
        else
        {
            var part = _secondaryParts[targetPartIndex];
            ulong appendOffset = part.Header.LocalIndexOffset > 0 ? part.Header.LocalIndexOffset : (ulong)part.Stream.Length;
            part.Stream.Seek((long)appendOffset, SeekOrigin.Begin);

            var operation = new FileAddOperation(part.Stream, _masterVault.Encryption, _masterVault.RsCodec);
            entry = await operation.ExecuteAsync(sourceStream, fileName, virtualPath, mode, progress, ct);
            entry.PartIndex = targetPartIndex;

            part.LocalIndex.Entries.Add(entry);
            part.Header.LocalIndexOffset = (ulong)part.Stream.Position;
            SaveLocalIndex(part);

            // Add to master global index
            _masterVault.Index.Entries.Add(entry);
            _masterVault.PersistIndexAndFooter();
        }

        entry.IsAvailable = true;
        UpdateChainManifest();
        return entry;
    }

    /// <summary>
    /// Opens a seekable, on-demand decrypting Stream for the specified index entry across any vault chain part (O06).
    /// </summary>
    public Stream OpenFileStream(IndexEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.PartIndex == 0)
        {
            return _masterVault.OpenFileStream(entry);
        }

        if (!_secondaryParts.TryGetValue(entry.PartIndex, out var part))
        {
            string missingName = GetPartFileName(_masterVault.VaultPath, entry.PartIndex);
            throw new VaultPartMissingException(entry.PartIndex, missingName,
                $"Vault part {entry.PartIndex} ('{missingName}') is missing. Re-attach the vault part to access '{entry.FileName}'.");
        }

        var reader = new ChunkReader(part.Stream, _masterVault.Encryption.SecureModeKey, _masterVault.Encryption.ObfuscationKey, _masterVault.RsCodec, part.StreamLock);
        return new VaultFileStream(entry, reader);
    }

    /// <summary>
    /// Reads all decrypted bytes of a file from its respective vault part into memory (C16).
    /// </summary>
    public async Task<byte[]> ReadAllBytesAsync(IndexEntry entry, CancellationToken ct = default)
    {
        using var stream = OpenFileStream(entry);
        using var ms = new MemoryStream((int)entry.OriginalSize);
        await stream.CopyToAsync(ms, ct);
        return ms.ToArray();
    }

    /// <summary>
    /// Moves a file's physical chunks from one vault part to another (O11).
    /// Chunks are independent and non-shared, preserving complete data integrity.
    /// </summary>
    public async Task MoveFileBetweenParts(Guid fileGuid, int targetPartIndex, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var entry = _masterVault.Files.FirstOrDefault(f => f.FileGuid == fileGuid);
        if (entry == null)
            throw new KeyNotFoundException($"File with GUID '{fileGuid}' was not found in global index.");

        if (entry.PartIndex == targetPartIndex)
            return;

        // Ensure target part exists
        if (targetPartIndex > 0 && !_secondaryParts.ContainsKey(targetPartIndex))
        {
            throw new ArgumentException($"Target vault part {targetPartIndex} does not exist in chain.");
        }

        // Read decrypted file data into memory
        byte[] fileData = await ReadAllBytesAsync(entry, ct);

        // Remove from old location local index
        if (entry.PartIndex > 0 && _secondaryParts.TryGetValue(entry.PartIndex, out var oldPart))
        {
            var oldEntry = oldPart.LocalIndex.Entries.FirstOrDefault(e => e.FileGuid == fileGuid);
            if (oldEntry != null)
            {
                oldEntry.IsDeleted = true;
                SaveLocalIndex(oldPart);
            }
        }

        // Write into target part
        using var ms = new MemoryStream(fileData);
        if (targetPartIndex == 0)
        {
            ulong appendOffset = _masterVault.Header.PrimaryIndexOffset > 0 ? _masterVault.Header.PrimaryIndexOffset : (ulong)_masterVault.Stream.Length;
            _masterVault.Stream.Seek((long)appendOffset, SeekOrigin.Begin);

            var operation = new FileAddOperation(_masterVault.Stream, _masterVault.Encryption, _masterVault.RsCodec);
            var newEntry = await operation.ExecuteAsync(ms, entry.FileName, entry.VirtualFolderPath, entry.ProtectionMode, progress, ct);

            // Copy metadata
            entry.PartIndex = 0;
            entry.Chunks = newEntry.Chunks;
            entry.ChunkCount = newEntry.ChunkCount;
            entry.FirstChunkOffset = newEntry.FirstChunkOffset;
            entry.FileSalt = newEntry.FileSalt;
            _masterVault.Header.PrimaryIndexOffset = (ulong)_masterVault.Stream.Position;
            _masterVault.PersistIndexAndFooter();
        }
        else
        {
            var targetPart = _secondaryParts[targetPartIndex];
            ulong appendOffset = targetPart.Header.LocalIndexOffset > 0 ? targetPart.Header.LocalIndexOffset : (ulong)targetPart.Stream.Length;
            targetPart.Stream.Seek((long)appendOffset, SeekOrigin.Begin);

            var operation = new FileAddOperation(targetPart.Stream, _masterVault.Encryption, _masterVault.RsCodec);
            var newEntry = await operation.ExecuteAsync(ms, entry.FileName, entry.VirtualFolderPath, entry.ProtectionMode, progress, ct);

            entry.PartIndex = targetPartIndex;
            entry.Chunks = newEntry.Chunks;
            entry.ChunkCount = newEntry.ChunkCount;
            entry.FirstChunkOffset = newEntry.FirstChunkOffset;
            entry.FileSalt = newEntry.FileSalt;

            targetPart.LocalIndex.Entries.Add(entry);
            targetPart.Header.LocalIndexOffset = (ulong)targetPart.Stream.Position;
            SaveLocalIndex(targetPart);

            _masterVault.PersistIndexAndFooter();
        }

        UpdateChainManifest();
    }

    /// <summary>
    /// Regenerates and saves the live chain linking manifest (<vaultName>.chain.manifest) (O07, B26).
    /// </summary>
    public void UpdateChainManifest()
    {
        string manifestPath = VaultChainManifest.GetManifestPath(_masterVault.VaultPath);
        var manifest = new VaultChainManifest
        {
            VaultName = Path.GetFileNameWithoutExtension(_masterVault.VaultPath),
            VaultUUID = _masterVault.VaultUUID,
            FormatVersion = VaultConstants.CurrentFormatVersion,
            TotalFiles = _masterVault.Files.Count,
            TotalSizeBytes = _masterVault.StreamLength + _secondaryParts.Values.Sum(p => p.Stream.Length),
            LastModifiedUtc = DateTime.UtcNow
        };

        manifest.Parts.Add(new VaultChainPartInfo
        {
            PartIndex = 0,
            FileName = Path.GetFileName(_masterVault.VaultPath),
            FileSizeBytes = _masterVault.StreamLength
        });

        foreach (var (index, part) in _secondaryParts.OrderBy(kv => kv.Key))
        {
            manifest.Parts.Add(new VaultChainPartInfo
            {
                PartIndex = index,
                FileName = Path.GetFileName(part.FilePath),
                FileSizeBytes = part.Stream.Length
            });
        }

        manifest.SaveToFile(manifestPath);
    }

    public void Dispose()
    {
        if (_disposed) return;

        foreach (var part in _secondaryParts.Values)
        {
            part.Dispose();
        }
        _secondaryParts.Clear();

        _disposed = true;
    }
}
