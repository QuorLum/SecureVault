using System.Security.Cryptography;
using SecureVault.Core.Cache;
using SecureVault.Core.Crypto;
using SecureVault.Core.Exceptions;
using SecureVault.Core.Format;
using SecureVault.Core.IO;
using SecureVault.Core.Operations;

namespace SecureVault.Core;

/// <summary>
/// Primary facade for creating, opening, managing, and locking SecureVault containers.
/// Implements IDisposable to guarantee all cryptographic keys are scrubbed on close.
/// </summary>
public sealed class VaultManager : IDisposable
{
    private readonly string _vaultPath;
    private readonly VaultFileLock _fileLock;
    private FileStream _stream;
    private readonly SemaphoreSlim _streamLock = new(1, 1);
    private readonly SecureBuffer _masterKey;
    private readonly EncryptionService _encryption;
    private readonly ReedSolomonCodec _rsCodec;
    private readonly VaultHeader _header;
    private readonly VaultIndex _index;
    private readonly VaultCache _cache;
    private bool _disposed;

    public string VaultPath => _vaultPath;
    public Guid VaultUUID => _header.VaultUUID;
    public IReadOnlyList<IndexEntry> Files => _index.Entries.Where(e => !e.IsDeleted).ToList();
    public bool IsLocked => _disposed;
    public VaultCache Cache => _cache;
    public long StreamLength
    {
        get
        {
            _streamLock.Wait();
            try
            {
                return _stream.Length;
            }
            finally
            {
                _streamLock.Release();
            }
        }
    }

    internal SecureBuffer MasterKey => _masterKey;
    internal EncryptionService Encryption => _encryption;
    internal ReedSolomonCodec RsCodec => _rsCodec;
    internal VaultHeader Header => _header;
    internal VaultIndex Index => _index;
    internal FileStream Stream => _stream;
    internal SemaphoreSlim StreamLock => _streamLock;
    internal void PersistIndexAndFooter() => SaveIndexAndFooter();

    /// <summary>
    /// Persists the UI state and cached thumbnails to encrypted local cache.
    /// </summary>
    public void SaveCacheSnapshot(Cache.UIState uiState, Dictionary<Guid, byte[]>? thumbnails = null)
    {
        EnsureUnlocked();
        _cache.SaveSnapshot(_index, thumbnails ?? new(), uiState);
    }

    internal void UpdateStreamAfterCompaction(FileStream newStream, VaultHeader newHeader)
    {
        _stream = newStream;
        _header.PrimaryIndexOffset = newHeader.PrimaryIndexOffset;
        _header.PrimaryIndexLength = newHeader.PrimaryIndexLength;
        _header.BackupIndexOffset = newHeader.BackupIndexOffset;
        _header.BackupIndexLength = newHeader.BackupIndexLength;
        _header.HeaderHMAC = newHeader.HeaderHMAC;
    }

    private VaultManager(
        string vaultPath,
        VaultFileLock fileLock,
        FileStream stream,
        SecureBuffer masterKey,
        VaultHeader header,
        VaultIndex index)
    {
        _vaultPath = vaultPath;
        _fileLock = fileLock;
        _stream = stream;
        _masterKey = masterKey;
        _header = header;
        _index = index;
        _rsCodec = new ReedSolomonCodec();
        _encryption = new EncryptionService(masterKey);

        using var cacheKey = CacheEncryption.DeriveCacheKey(masterKey);
        _cache = new VaultCache(header.VaultUUID, cacheKey);
    }

    /// <summary>
    /// Creates a brand-new encrypted vault file with the specified password.
    /// Returns the active VaultManager instance and the 24-word recovery phrase.
    /// </summary>
    public static async Task<(VaultManager Manager, string[] RecoveryWords)> CreateAsync(
        string vaultPath,
        string password,
        int memoryCostKb = KeyDerivation.DefaultMemoryCostKb,
        int iterations = KeyDerivation.DefaultIterations,
        int parallelism = KeyDerivation.DefaultParallelism)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        string fullPath = Path.GetFullPath(vaultPath);
        if (File.Exists(fullPath))
        {
            throw new VaultAlreadyExistsException($"Vault file already exists at '{fullPath}'.");
        }

        return await Task.Run(() =>
        {
            var fileLock = new VaultFileLock(fullPath);
            try
            {
                var masterKey = new SecureBuffer(WrappedKeyPair.MasterKeySize);
                RandomNumberGenerator.Fill(masterKey.AsSpan());

                var (recoveryWords, recoverySeed) = RecoveryKeyGenerator.Generate();

                var header = VaultHeader.Create(
                    masterKey,
                    password,
                    recoverySeed,
                    memoryCostKb: memoryCostKb,
                    iterations: iterations,
                    parallelism: parallelism);

                var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                var index = new VaultIndex();
                var rsCodec = new ReedSolomonCodec();
                using var encryption = new EncryptionService(masterKey);

                // Write initial header placeholder
                header.WriteTo(stream);

                // Write empty index (primary + backup)
                var (pOff, pLen, bOff, bLen) = index.WriteToVault(stream, encryption, rsCodec);
                header.PrimaryIndexOffset = pOff;
                header.PrimaryIndexLength = pLen;
                header.BackupIndexOffset = bOff;
                header.BackupIndexLength = bLen;
                header.UpdateHmac(masterKey);

                // Write footer
                var footer = new VaultFooter
                {
                    PrimaryIndexOffset = pOff,
                    PrimaryIndexLength = pLen,
                    BackupIndexOffset = bOff,
                    BackupIndexLength = bLen,
                    VaultDataSize = (ulong)stream.Position + VaultFooter.FooterSize
                };
                footer.UpdateHmac(masterKey);
                footer.WriteTo(stream);

                // Rewrite finalized header at stream beginning
                stream.Seek(0, SeekOrigin.Begin);
                header.WriteTo(stream);
                stream.Flush(flushToDisk: true);

                var manager = new VaultManager(fullPath, fileLock, stream, masterKey, header, index);
                return (manager, recoveryWords);
            }
            catch
            {
                fileLock.Dispose();
                if (File.Exists(fullPath))
                {
                    try { File.Delete(fullPath); } catch { }
                }
                throw;
            }
        });
    }

    /// <summary>
    /// Unlocks an existing vault file using password.
    /// </summary>
    public static async Task<VaultManager> OpenAsync(string vaultPath, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        string fullPath = Path.GetFullPath(vaultPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Vault file not found at '{fullPath}'.", fullPath);
        }

        return await Task.Run(() =>
        {
            var fileLock = new VaultFileLock(fullPath);
            try
            {
                var stream = new FileStream(fullPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                var header = VaultHeader.ReadFrom(stream);

                var masterKey = KeyWrapping.UnwrapWithPassword(
                    header.KeyData,
                    password,
                    (int)header.Argon2MemoryKb,
                    header.Argon2Iterations,
                    header.Argon2Parallelism);

                if (!header.VerifyHmac(masterKey))
                {
                    masterKey.Dispose();
                    throw new CorruptedVaultException("Vault header HMAC validation failed. Vault is corrupted or tampered.");
                }

                var rsCodec = new ReedSolomonCodec();
                using var tempEncryption = new EncryptionService(masterKey);
                var index = VaultIndex.ReadFromVault(stream, tempEncryption, rsCodec, header);

                return new VaultManager(fullPath, fileLock, stream, masterKey, header, index);
            }
            catch
            {
                fileLock.Dispose();
                throw;
            }
        });
    }

    /// <summary>
    /// Unlocks an existing vault file using the 24-word recovery phrase.
    /// </summary>
    public static async Task<VaultManager> OpenWithRecoveryKeyAsync(string vaultPath, string[] recoveryWords)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultPath);
        ArgumentNullException.ThrowIfNull(recoveryWords);

        string fullPath = Path.GetFullPath(vaultPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Vault file not found at '{fullPath}'.", fullPath);
        }

        return await Task.Run(() =>
        {
            byte[] recoverySeed = RecoveryKeyGenerator.WordsToSeed(recoveryWords);

            var fileLock = new VaultFileLock(fullPath);
            try
            {
                var stream = new FileStream(fullPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                var header = VaultHeader.ReadFrom(stream);

                var masterKey = KeyWrapping.UnwrapWithRecoveryKey(header.KeyData, recoverySeed);

                if (!header.VerifyHmac(masterKey))
                {
                    masterKey.Dispose();
                    throw new CorruptedVaultException("Vault header HMAC validation failed.");
                }

                var rsCodec = new ReedSolomonCodec();
                using var tempEncryption = new EncryptionService(masterKey);
                var index = VaultIndex.ReadFromVault(stream, tempEncryption, rsCodec, header);

                return new VaultManager(fullPath, fileLock, stream, masterKey, header, index);
            }
            catch
            {
                fileLock.Dispose();
                throw;
            }
        });
    }

    /// <summary>
    /// Reads the password hint stored in the unencrypted section of the vault header without unlocking (A05).
    /// </summary>
    public static string? GetPasswordHint(string vaultPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultPath);
        string fullPath = Path.GetFullPath(vaultPath);
        if (!File.Exists(fullPath)) return null;

        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var header = VaultHeader.ReadFrom(stream);
        return header.PasswordHint;
    }

    /// <summary>
    /// Password hint for the current open vault.
    /// </summary>
    public string? PasswordHint => _header?.PasswordHint;

    /// <summary>
    /// Sets or clears the password hint for the current vault, atomically updating header and HMAC (A05).
    /// </summary>
    public void SetPasswordHint(string? hint)
    {
        EnsureUnlocked();
        _header.PasswordHint = hint;
        _header.UpdateHmac(_masterKey);

        _stream.Seek(0, SeekOrigin.Begin);
        _header.WriteTo(_stream);
        _stream.Flush(flushToDisk: true);
    }

    /// <summary>
    /// Changes the vault password without modifying file data or master key (A04).
    /// </summary>
    public void ChangePassword(string newPassword)
    {
        EnsureUnlocked();
        ArgumentException.ThrowIfNullOrWhiteSpace(newPassword);

        _header.KeyData = KeyWrapping.RewrapPasswordOnly(
            _header.KeyData,
            _masterKey,
            newPassword,
            (int)_header.Argon2MemoryKb,
            _header.Argon2Iterations,
            _header.Argon2Parallelism);

        _header.UpdateHmac(_masterKey);

        _stream.Seek(0, SeekOrigin.Begin);
        _header.WriteTo(_stream);
        _stream.Flush(flushToDisk: true);
    }

    /// <summary>
    /// Adds a file to the vault stream, persisting encrypted chunks and updating the index.
    /// </summary>
    public async Task<IndexEntry> AddFileAsync(
        Stream sourceStream,
        string fileName,
        string virtualPath = "/",
        ProtectionMode mode = ProtectionMode.SecureMode,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        EnsureUnlocked();
        ArgumentNullException.ThrowIfNull(sourceStream);

        Stream effectiveStream = sourceStream;
        IDisposable? cleanup = null;

        try
        {
            // If sourceStream reads from this exact vault container stream (e.g. during CopyAsync),
            // spool it first before acquiring the stream write lock to prevent stream position collision or lock inversion.
            if (sourceStream is VaultFileStream vfs && ReferenceEquals(vfs.UnderlyingStream, _stream))
            {
                if (sourceStream.CanSeek && sourceStream.Length > 32 * 1024 * 1024)
                {
                    var secureTemp = new SecureVault.Core.Security.SecureTempFile();
                    await sourceStream.CopyToAsync(secureTemp.Stream, cancellationToken);
                    secureTemp.Stream.Seek(0, SeekOrigin.Begin);
                    effectiveStream = secureTemp.Stream;
                    cleanup = secureTemp;
                }
                else
                {
                    var mem = new MemoryStream();
                    await sourceStream.CopyToAsync(mem, cancellationToken);
                    mem.Seek(0, SeekOrigin.Begin);
                    effectiveStream = mem;
                    cleanup = mem;
                }
            }

            await _streamLock.WaitAsync(cancellationToken);
            try
            {
                // Seek past existing files to append new file block (before primary index)
                ulong appendOffset = _header.PrimaryIndexOffset > 0 ? _header.PrimaryIndexOffset : (ulong)_stream.Length;
                _stream.Seek((long)appendOffset, SeekOrigin.Begin);

                var operation = new FileAddOperation(_stream, _encryption, _rsCodec);
                var entry = await operation.ExecuteAsync(effectiveStream, fileName, virtualPath, mode, progress, cancellationToken);

                _index.Entries.Add(entry);
                _header.PrimaryIndexOffset = (ulong)_stream.Position;
                SaveIndexAndFooter();

                return entry;
            }
            finally
            {
                _streamLock.Release();
            }
        }
        finally
        {
            cleanup?.Dispose();
        }
    }

    /// <summary>
    /// Opens a seekable, on-demand decrypting Stream for the specified index entry (C18).
    /// </summary>
    public Stream OpenFileStream(IndexEntry entry)
    {
        EnsureUnlocked();
        ArgumentNullException.ThrowIfNull(entry);

        var reader = new ChunkReader(_stream, _encryption.SecureModeKey, _encryption.ObfuscationKey, _rsCodec, _streamLock);
        return new VaultFileStream(entry, reader);
    }

    /// <summary>
    /// Reads all decrypted bytes of a file directly into memory (C16).
    /// </summary>
    public async Task<byte[]> ReadAllBytesAsync(IndexEntry entry, CancellationToken cancellationToken = default)
    {
        using var fileStream = OpenFileStream(entry);
        using var ms = new MemoryStream((int)entry.OriginalSize);
        await fileStream.CopyToAsync(ms, cancellationToken);
        return ms.ToArray();
    }

    /// <summary>
    /// Marks a file as deleted in the index (C08).
    /// </summary>
    public bool DeleteFile(Guid fileGuid)
    {
        EnsureUnlocked();

        _streamLock.Wait();
        try
        {
            bool removed = FileDeleteOperation.Execute(_index, fileGuid);
            if (removed)
            {
                SaveIndexAndFooter();
            }

            return removed;
        }
        finally
        {
            _streamLock.Release();
        }
    }

    private void SaveIndexAndFooter()
    {
        ulong indexOffset = _header.PrimaryIndexOffset > 0 ? _header.PrimaryIndexOffset : (ulong)_stream.Position;
        _stream.Seek((long)indexOffset, SeekOrigin.Begin);

        // Write updated dual index
        var (pOff, pLen, bOff, bLen) = _index.WriteToVault(_stream, _encryption, _rsCodec);
        _header.PrimaryIndexOffset = pOff;
        _header.PrimaryIndexLength = pLen;
        _header.BackupIndexOffset = bOff;
        _header.BackupIndexLength = bLen;
        _header.UpdateHmac(_masterKey);

        // Write updated footer
        var footer = new VaultFooter
        {
            PrimaryIndexOffset = pOff,
            PrimaryIndexLength = pLen,
            BackupIndexOffset = bOff,
            BackupIndexLength = bLen,
            VaultDataSize = (ulong)_stream.Position + VaultFooter.FooterSize
        };
        footer.UpdateHmac(_masterKey);
        footer.WriteTo(_stream);
        _stream.SetLength(_stream.Position);

        // Rewrite header with updated index pointers
        _stream.Seek(0, SeekOrigin.Begin);
        _header.WriteTo(_stream);
        _stream.Flush(flushToDisk: true);
    }

    private void EnsureUnlocked()
    {
        if (_disposed)
        {
            throw new VaultLockedException();
        }
    }

    /// <summary>
    /// Locks the vault and zeros all cryptographic keys from memory (A03, A21, M04, M05).
    /// </summary>
    public void Lock() => Dispose();

    public void Dispose()
    {
        if (_disposed)
            return;

        _streamLock.Wait();
        try
        {
            if (_disposed) return;

            try
            {
                _stream.Flush(flushToDisk: true);
            }
            catch { }

            _cache.Dispose();
            _encryption.Dispose();
            _masterKey.Dispose();
            _stream.Dispose();
            _fileLock.Dispose();

            _disposed = true;
        }
        finally
        {
            _streamLock.Release();
            _streamLock.Dispose();
        }
    }
}
