using System.Security.Cryptography;
using SecureVault.Core.Crypto;
using SecureVault.Core.Exceptions;
using SecureVault.Core.Format;

namespace SecureVault.Core.Operations;

/// <summary>
/// Replaces the data of an existing file in the vault (C20).
/// Generates fresh salts and random nonces per chunk to eliminate AES-GCM nonce reuse vulnerabilities.
/// Replaces chunk pointers in the index; orphaned chunks are safely reclaimed during compaction.
/// </summary>
public sealed class FileReplaceOperation
{
    private readonly VaultManager _vault;

    public FileReplaceOperation(VaultManager vault)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
    }

    /// <summary>
    /// Replaces file data with new content from sourceStream, updating size, modified time, SHA-256, and chunk pointers.
    /// </summary>
    public async Task<IndexEntry> ReplaceFileDataAsync(
        Guid fileGuid,
        Stream sourceStream,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sourceStream);

        var entry = _vault.Files.FirstOrDefault(f => f.FileGuid == fileGuid);
        if (entry == null)
            throw new KeyNotFoundException($"File with GUID '{fileGuid}' was not found in vault.");

        // Seek past existing files to append new file block (before primary index)
        ulong appendOffset = _vault.Header.PrimaryIndexOffset > 0 ? _vault.Header.PrimaryIndexOffset : (ulong)_vault.StreamLength;
        _vault.Stream.Seek((long)appendOffset, SeekOrigin.Begin);

        var addOp = new FileAddOperation(_vault.Stream, _vault.Encryption, _vault.RsCodec);
        var newFileRecord = await addOp.ExecuteAsync(
            sourceStream,
            entry.FileName,
            entry.VirtualFolderPath,
            entry.ProtectionMode,
            progress,
            ct);

        // Atomically transfer new chunks, size, salt, and hashes into existing entry
        entry.OriginalSize = newFileRecord.OriginalSize;
        entry.CompressedSize = newFileRecord.CompressedSize;
        entry.PlaintextSHA256 = newFileRecord.PlaintextSHA256;
        entry.FileSalt = newFileRecord.FileSalt;
        entry.ChunkCount = newFileRecord.ChunkCount;
        entry.FirstChunkOffset = newFileRecord.FirstChunkOffset;
        entry.Chunks = newFileRecord.Chunks;
        entry.DateModifiedTicks = DateTime.UtcNow.Ticks;

        // Remove the temporary entry added to _vault.Index by FileAddOperation if it was added
        _vault.Index.Entries.Remove(newFileRecord);

        // Update primary index pointer and save
        _vault.Header.PrimaryIndexOffset = (ulong)_vault.Stream.Position;
        _vault.PersistIndexAndFooter();

        return entry;
    }
}
