using System.Security.Cryptography;
using SecureVault.Core.Crypto;
using SecureVault.Core.Format;
using SecureVault.Core.Organization;

namespace SecureVault.Core.Operations;

/// <summary>
/// Executes streaming file addition into the vault (C01, C05, C06).
/// Computes plaintext SHA-256 on the fly, divides file into 1MB chunks, writes block header/footer,
/// and returns the resulting index entry.
/// </summary>
public sealed class FileAddOperation
{
    private readonly Stream _vaultStream;
    private readonly EncryptionService _encryption;
    private readonly ReedSolomonCodec _rsCodec;

    public FileAddOperation(
        Stream vaultStream,
        EncryptionService encryption,
        ReedSolomonCodec rsCodec)
    {
        _vaultStream = vaultStream ?? throw new ArgumentNullException(nameof(vaultStream));
        _encryption = encryption ?? throw new ArgumentNullException(nameof(encryption));
        _rsCodec = rsCodec ?? throw new ArgumentNullException(nameof(rsCodec));
    }

    public async Task<IndexEntry> ExecuteAsync(
        Stream sourceStream,
        string fileName,
        string virtualPath = "/",
        ProtectionMode mode = ProtectionMode.SecureMode,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        Guid fileGuid = Guid.NewGuid();
        byte[] fileSalt = new byte[16];
        RandomNumberGenerator.Fill(fileSalt);

        long blockStartOffset = _vaultStream.Position;

        // Reserve space for BlockHeader (66 bytes)
        byte[] headerPlaceholder = new byte[BlockHeader.Size];
        await _vaultStream.WriteAsync(headerPlaceholder, cancellationToken);

        ulong firstChunkOffset = (ulong)_vaultStream.Position;
        long currentWriteOffset = _vaultStream.Position;
        var chunkWriter = new ChunkWriter(_vaultStream, _encryption.SecureModeKey, _encryption.ObfuscationKey, _rsCodec, mode);
        var chunkEntries = new List<ChunkIndexEntry>();

        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var blockHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        byte[] buffer = new byte[VaultConstants.DefaultChunkSize];
        ulong totalBytesRead = 0;
        uint chunkSequence = 0;
        long sourceLength = sourceStream.CanSeek ? sourceStream.Length : -1;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
            if (bytesRead <= 0)
            {
                break;
            }

            sha256.AppendData(buffer, 0, bytesRead);
            totalBytesRead += (ulong)bytesRead;

            // Restore write position in case sourceStream reading sought within the same vault stream (e.g. CopyAsync)
            _vaultStream.Seek(currentWriteOffset, SeekOrigin.Begin);

            var chunkEntry = chunkWriter.WriteChunk(
                buffer.AsSpan(0, bytesRead),
                chunkSequence,
                fileGuid,
                fileSalt);

            currentWriteOffset = _vaultStream.Position;
            chunkEntries.Add(chunkEntry);
            chunkSequence++;

            if (progress != null && sourceLength > 0)
            {
                progress.Report(Math.Min(1.0, (double)totalBytesRead / sourceLength));
            }
        }

        byte[] plaintextSha256 = sha256.GetHashAndReset();

        // Write BlockFooter (52 bytes)
        var footer = new BlockFooter
        {
            FileGuid = fileGuid,
            BlockSHA256 = plaintextSha256 // Block hash verification
        };
        _vaultStream.Seek(currentWriteOffset, SeekOrigin.Begin);
        footer.WriteTo(_vaultStream);

        long blockEndOffset = _vaultStream.Position;

        // Seek back and rewrite BlockHeader with finalized file information
        _vaultStream.Seek(blockStartOffset, SeekOrigin.Begin);
        var header = new BlockHeader
        {
            FileGuid = fileGuid,
            ChunkCount = (uint)chunkEntries.Count,
            OriginalFileSize = totalBytesRead,
            ProtectionMode = mode,
            CompressionType = CompressionType.None,
            PlaintextSHA256 = plaintextSha256
        };
        header.WriteTo(_vaultStream);

        // Restore stream position to end of block
        _vaultStream.Seek(blockEndOffset, SeekOrigin.Begin);

        return new IndexEntry
        {
            FileGuid = fileGuid,
            FileName = fileName,
            OriginalSize = totalBytesRead,
            CompressedSize = totalBytesRead,
            ProtectionMode = mode,
            CompressionType = CompressionType.None,
            PlaintextSHA256 = plaintextSha256,
            FileSalt = fileSalt,
            DateAddedTicks = DateTime.UtcNow.Ticks,
            DateModifiedTicks = DateTime.UtcNow.Ticks,
            Category = (byte)AutoCategorizer.Categorize(fileName),
            VirtualFolderPath = virtualPath,
            ChunkCount = (uint)chunkEntries.Count,
            FirstChunkOffset = firstChunkOffset,
            Chunks = chunkEntries,
            IsDeleted = false
        };
    }
}
