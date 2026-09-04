using System.Buffers.Binary;
using System.Security.Cryptography;
using SecureVault.Core.Crypto;
using SecureVault.Core.Exceptions;
using SecureVault.Core.Format;
using SecureVault.Core.IO;
using SecureVault.Core.MultiVault;

namespace SecureVault.Core.Operations;

public sealed class CompactionResult
{
    public long OldSizeBytes { get; init; }
    public long NewSizeBytes { get; init; }
    public long ReclaimedBytes => Math.Max(0, OldSizeBytes - NewSizeBytes);
    public int LiveFilesCount { get; init; }
}

/// <summary>
/// Executes chain-aware vault compaction, defragmenting physical chunk storage,
/// reclaiming orphaned chunk bytes from deleted/replaced files, updating local and master global indices,
/// and performing atomic two-phase swaps with .pre-compact rollback protection (C23).
/// </summary>
public static class VaultCompaction
{
    /// <summary>
    /// Compacts a single vault or master container (Part 0).
    /// </summary>
    public static async Task<CompactionResult> CompactAsync(
        VaultManager vault,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(vault);

        await vault.StreamLock.WaitAsync(ct);
        try
        {
            return await Task.Run(() =>
            {
            string vaultPath = vault.VaultPath;
            long oldSize = new FileInfo(vaultPath).Length;

            // Step 1: Disk space check (>= 2x file size)
            CheckDiskSpace(vaultPath, oldSize);

            string tempPath = $"{vaultPath}.compact.tmp";
            string preCompactPath = $"{vaultPath}.pre-compact";

            if (File.Exists(tempPath)) File.Delete(tempPath);
            if (File.Exists(preCompactPath)) File.Delete(preCompactPath);

            var liveEntries = vault.Index.Entries.Where(e => e.PartIndex == 0 && !e.IsDeleted).ToList();
            Dictionary<ulong, ulong> offsetMap = new();

            VaultHeader newHeader;
            ulong pOff, pLen, bOff, bLen;

            using (var tempStream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                // Write placeholder header
                byte[] headerPlaceholder = new byte[VaultConstants.HeaderSize];
                tempStream.Write(headerPlaceholder);

                int fileIndex = 0;
                foreach (var file in liveEntries)
                {
                    ct.ThrowIfCancellationRequested();

                    // Only write block header & chunks if chunks haven't been written
                    bool anyChunksWritten = false;
                    foreach (var chunk in file.Chunks)
                    {
                        if (!offsetMap.ContainsKey(chunk.AbsoluteOffset))
                        {
                            anyChunksWritten = true;
                            break;
                        }
                    }

                    if (anyChunksWritten || file.Chunks.Count == 0)
                    {
                        var blockHeader = new BlockHeader
                        {
                            FileGuid = file.FileGuid,
                            ChunkCount = (uint)file.Chunks.Count,
                            OriginalFileSize = file.OriginalSize,
                            ProtectionMode = file.ProtectionMode,
                            CompressionType = file.CompressionType,
                            PlaintextSHA256 = file.PlaintextSHA256
                        };
                        blockHeader.WriteTo(tempStream);

                        foreach (var chunk in file.Chunks)
                        {
                            if (!offsetMap.ContainsKey(chunk.AbsoluteOffset))
                            {
                                ulong newChunkOffset = (ulong)tempStream.Position;
                                offsetMap[chunk.AbsoluteOffset] = newChunkOffset;

                                // Copy physical chunk bytes
                                CopyChunkBytes(vault.Stream, tempStream, chunk);
                            }
                        }

                        var blockFooter = new BlockFooter
                        {
                            FileGuid = file.FileGuid,
                            BlockSHA256 = file.PlaintextSHA256
                        };
                        blockFooter.WriteTo(tempStream);
                    }

                    fileIndex++;
                    progress?.Report(0.7 * fileIndex / Math.Max(1, liveEntries.Count));
                }

                // Remap chunk offsets in live files
                foreach (var file in liveEntries)
                {
                    foreach (var chunk in file.Chunks)
                    {
                        if (offsetMap.TryGetValue(chunk.AbsoluteOffset, out ulong newOffset))
                        {
                            chunk.AbsoluteOffset = newOffset;
                        }
                    }
                    if (file.Chunks.Count > 0)
                    {
                        file.FirstChunkOffset = file.Chunks[0].AbsoluteOffset;
                    }
                }

                // Remove deleted files from master index
                vault.Index.Entries.RemoveAll(e => e.PartIndex == 0 && e.IsDeleted);

                // Write dual index
                (pOff, pLen, bOff, bLen) = vault.Index.WriteToVault(tempStream, vault.Encryption, vault.RsCodec);

                // Write footer
                var footer = new VaultFooter
                {
                    PrimaryIndexOffset = pOff,
                    PrimaryIndexLength = pLen,
                    BackupIndexOffset = bOff,
                    BackupIndexLength = bLen,
                    VaultDataSize = (ulong)tempStream.Position + VaultFooter.FooterSize
                };
                footer.UpdateHmac(vault.MasterKey);
                footer.WriteTo(tempStream);

                // Write finalized header
                newHeader = vault.Header;
                newHeader.PrimaryIndexOffset = pOff;
                newHeader.PrimaryIndexLength = pLen;
                newHeader.BackupIndexOffset = bOff;
                newHeader.BackupIndexLength = bLen;
                newHeader.UpdateHmac(vault.MasterKey);

                tempStream.Seek(0, SeekOrigin.Begin);
                newHeader.WriteTo(tempStream);
                tempStream.Flush(flushToDisk: true);
            }

            // Step 4: Verification before commit
            VerifyCompactedVault(tempPath, vault.MasterKey, liveEntries);
            progress?.Report(0.9);

            // Close current vault stream so OS allows file rename
            vault.Stream.Flush(flushToDisk: true);
            vault.Stream.Dispose();

            // Step 5: Two-phase atomic commit with rollback protection
            ExecuteTwoPhaseCommit(vaultPath, tempPath, preCompactPath, () =>
            {
                // Reopen vault stream in VaultManager
                var newStream = new FileStream(vaultPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                vault.UpdateStreamAfterCompaction(newStream, newHeader);
            }, () =>
            {
                var restoredStream = new FileStream(vaultPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                vault.UpdateStreamAfterCompaction(restoredStream, vault.Header);
            });

            progress?.Report(1.0);
            long newSize = new FileInfo(vaultPath).Length;

            return new CompactionResult
            {
                OldSizeBytes = oldSize,
                NewSizeBytes = newSize,
                LiveFilesCount = liveEntries.Count
            };
        }, ct);
        }
        finally
        {
            vault.StreamLock.Release();
        }
    }

    /// <summary>
    /// Compacts a multi-vault chain, updating secondary parts and syncing master Global Index entries.
    /// If specificPartIndex is null, compacts all parts in the chain.
    /// </summary>
    public static async Task<CompactionResult> CompactChainAsync(
        VaultChainManager chain,
        int? specificPartIndex = null,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(chain);

        if (specificPartIndex == null)
        {
            long totalOld = 0;
            long totalNew = 0;
            int totalFiles = 0;

            // Compact part 0
            var res0 = await CompactAsync(chain.MasterVault, progress, ct);
            totalOld += res0.OldSizeBytes;
            totalNew += res0.NewSizeBytes;
            totalFiles += res0.LiveFilesCount;

            // Compact secondary parts
            foreach (var kvp in chain.SecondaryParts.OrderBy(p => p.Key))
            {
                var res = await CompactSecondaryPartAsync(chain, kvp.Key, progress, ct);
                totalOld += res.OldSizeBytes;
                totalNew += res.NewSizeBytes;
                totalFiles += res.LiveFilesCount;
            }

            return new CompactionResult
            {
                OldSizeBytes = totalOld,
                NewSizeBytes = totalNew,
                LiveFilesCount = totalFiles
            };
        }

        if (specificPartIndex.Value == 0)
        {
            return await CompactAsync(chain.MasterVault, progress, ct);
        }

        return await CompactSecondaryPartAsync(chain, specificPartIndex.Value, progress, ct);
    }

    private static async Task<CompactionResult> CompactSecondaryPartAsync(
        VaultChainManager chain,
        int partIndex,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        if (!chain.SecondaryParts.TryGetValue(partIndex, out var part))
        {
            throw new ArgumentException($"Secondary part {partIndex} not found in chain.");
        }

        await part.StreamLock.WaitAsync(ct);
        try
        {
            return await Task.Run(() =>
            {
            string partPath = part.FilePath;
            long oldSize = new FileInfo(partPath).Length;
            CheckDiskSpace(partPath, oldSize);

            string tempPath = $"{partPath}.compact.tmp";
            string preCompactPath = $"{partPath}.pre-compact";

            if (File.Exists(tempPath)) File.Delete(tempPath);
            if (File.Exists(preCompactPath)) File.Delete(preCompactPath);

            var liveEntries = part.LocalIndex.Entries.Where(e => !e.IsDeleted).ToList();
            Dictionary<ulong, ulong> offsetMap = new();

            SecondaryVaultHeader newHeader;

            using (var tempStream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                // Write placeholder header
                byte[] headerPlaceholder = new byte[SecondaryVaultHeader.Size];
                tempStream.Write(headerPlaceholder);

                int fileIndex = 0;
                foreach (var file in liveEntries)
                {
                    ct.ThrowIfCancellationRequested();

                    bool anyChunksWritten = false;
                    foreach (var chunk in file.Chunks)
                    {
                        if (!offsetMap.ContainsKey(chunk.AbsoluteOffset))
                        {
                            anyChunksWritten = true;
                            break;
                        }
                    }

                    if (anyChunksWritten || file.Chunks.Count == 0)
                    {
                        var blockHeader = new BlockHeader
                        {
                            FileGuid = file.FileGuid,
                            ChunkCount = (uint)file.Chunks.Count,
                            OriginalFileSize = file.OriginalSize,
                            ProtectionMode = file.ProtectionMode,
                            CompressionType = file.CompressionType,
                            PlaintextSHA256 = file.PlaintextSHA256
                        };
                        blockHeader.WriteTo(tempStream);

                        foreach (var chunk in file.Chunks)
                        {
                            if (!offsetMap.ContainsKey(chunk.AbsoluteOffset))
                            {
                                ulong newChunkOffset = (ulong)tempStream.Position;
                                offsetMap[chunk.AbsoluteOffset] = newChunkOffset;
                                CopyChunkBytes(part.Stream, tempStream, chunk);
                            }
                        }

                        var blockFooter = new BlockFooter
                        {
                            FileGuid = file.FileGuid,
                            BlockSHA256 = file.PlaintextSHA256
                        };
                        blockFooter.WriteTo(tempStream);
                    }

                    fileIndex++;
                    progress?.Report(0.7 * fileIndex / Math.Max(1, liveEntries.Count));
                }

                // Remap chunk offsets in local index
                foreach (var file in liveEntries)
                {
                    foreach (var chunk in file.Chunks)
                    {
                        if (offsetMap.TryGetValue(chunk.AbsoluteOffset, out ulong newOffset))
                        {
                            chunk.AbsoluteOffset = newOffset;
                        }
                    }
                    if (file.Chunks.Count > 0)
                    {
                        file.FirstChunkOffset = file.Chunks[0].AbsoluteOffset;
                    }
                }

                // Remove deleted files from local index
                part.LocalIndex.Entries.RemoveAll(e => e.IsDeleted);

                // Write encrypted local index
                part.LocalIndex.Version++;
                byte[] serialized = part.LocalIndex.Serialize();
                var (ciphertext, nonce, tag) = chain.MasterVault.Encryption.EncryptIndex(serialized);
                byte[] parity = chain.MasterVault.RsCodec.Encode(ciphertext);

                byte[] payload = new byte[12 + 16 + 4 + ciphertext.Length + parity.Length];
                nonce.CopyTo(payload, 0);
                tag.CopyTo(payload, 12);
                BitConverter.GetBytes(ciphertext.Length).CopyTo(payload, 28);
                ciphertext.CopyTo(payload, 32);
                parity.CopyTo(payload, 32 + ciphertext.Length);

                ulong pOff = (ulong)tempStream.Position;
                tempStream.Write(payload);
                uint pLen = (uint)payload.Length;

                newHeader = part.Header;
                newHeader.LocalIndexOffset = pOff;
                newHeader.LocalIndexLength = pLen;
                newHeader.UpdateHmac(chain.MasterVault.MasterKey);

                tempStream.Seek(0, SeekOrigin.Begin);
                newHeader.WriteTo(tempStream);
                tempStream.Flush(flushToDisk: true);
            }

            // Sync master Global Index entries for this part
            foreach (var masterEntry in chain.MasterVault.Index.Entries.Where(e => e.PartIndex == partIndex).ToList())
            {
                if (masterEntry.IsDeleted)
                {
                    chain.MasterVault.Index.Entries.Remove(masterEntry);
                }
                else
                {
                    foreach (var chunk in masterEntry.Chunks)
                    {
                        if (offsetMap.TryGetValue(chunk.AbsoluteOffset, out ulong newOffset))
                        {
                            chunk.AbsoluteOffset = newOffset;
                        }
                    }
                    if (masterEntry.Chunks.Count > 0)
                    {
                        masterEntry.FirstChunkOffset = masterEntry.Chunks[0].AbsoluteOffset;
                    }
                }
            }
            chain.MasterVault.StreamLock.Wait(ct);
            try
            {
                chain.MasterVault.PersistIndexAndFooter();
            }
            finally
            {
                chain.MasterVault.StreamLock.Release();
            }

            // Verification
            VerifyCompactedSecondaryPart(tempPath, chain.MasterVault.MasterKey, liveEntries);

            // Close current part stream so OS allows file rename
            part.Stream.Flush(flushToDisk: true);
            part.Stream.Dispose();

            // Two-phase atomic commit with rollback
            ExecuteTwoPhaseCommit(partPath, tempPath, preCompactPath, () =>
            {
                var newStream = new FileStream(partPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                part.Stream = newStream;
                part.Header = newHeader;
            }, () =>
            {
                var restoredStream = new FileStream(partPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                part.Stream = restoredStream;
            });

            chain.UpdateChainManifest();

            long newSize = new FileInfo(partPath).Length;
            return new CompactionResult
            {
                OldSizeBytes = oldSize,
                NewSizeBytes = newSize,
                LiveFilesCount = liveEntries.Count
            };
        }, ct);
        }
        finally
        {
            part.StreamLock.Release();
        }
    }

    private static void CopyChunkBytes(Stream src, Stream dest, ChunkIndexEntry chunk)
    {
        src.Seek((long)chunk.AbsoluteOffset, SeekOrigin.Begin);
        byte[] headerBytes = new byte[VaultConstants.ChunkHeaderSize];
        int read = src.ReadAtLeast(headerBytes, VaultConstants.ChunkHeaderSize, throwOnEndOfStream: false);
        if (read < VaultConstants.ChunkHeaderSize)
        {
            throw new CorruptedChunkException((int)chunk.ChunkSequence, "Chunk header truncated during compaction.");
        }

        uint payloadLen = BinaryPrimitives.ReadUInt32LittleEndian(headerBytes.AsSpan(0, 4));
        ushort headerParityLen = BinaryPrimitives.ReadUInt16LittleEndian(headerBytes.AsSpan(0x25, 2));

        int blockCount = ((int)payloadLen + ReedSolomonCodec.DataBlockSize - 1) / ReedSolomonCodec.DataBlockSize;
        int expectedParityLen = blockCount * ReedSolomonCodec.ParityBlockSize;
        int rsParityLength = (headerParityLen > 0 || chunk.RSParityLength > 0)
            ? (chunk.RSParityLength > 0 ? (int)chunk.RSParityLength : expectedParityLen)
            : 0;

        dest.Write(headerBytes);

        byte[] payloadAndParity = new byte[payloadLen + (uint)rsParityLength];
        int bodyRead = src.ReadAtLeast(payloadAndParity, payloadAndParity.Length, throwOnEndOfStream: false);
        if (bodyRead < payloadAndParity.Length)
        {
            throw new CorruptedChunkException((int)chunk.ChunkSequence, "Chunk body truncated during compaction.");
        }

        dest.Write(payloadAndParity);
    }

    private static void VerifyCompactedVault(string tempPath, SecureBuffer masterKey, List<IndexEntry> entries)
    {
        using var stream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var header = VaultHeader.ReadFrom(stream);
        if (!header.VerifyHmac(masterKey))
        {
            throw new CorruptedVaultException("Compacted vault header HMAC failed verification.");
        }

        using var enc = new EncryptionService(masterKey);
        var rsCodec = new ReedSolomonCodec();
        var index = VaultIndex.ReadFromVault(stream, enc, rsCodec, header);

        if (index.Entries.Count(e => !e.IsDeleted) != entries.Count)
        {
            throw new CorruptedIndexException("Compacted vault live entries count mismatch.");
        }
    }

    private static void VerifyCompactedSecondaryPart(string tempPath, SecureBuffer masterKey, List<IndexEntry> entries)
    {
        using var stream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var header = SecondaryVaultHeader.ReadFrom(stream);
        if (!header.VerifyHmac(masterKey))
        {
            throw new CorruptedVaultException("Compacted secondary part header HMAC failed verification.");
        }
    }

    private static void CheckDiskSpace(string targetPath, long requiredBytes)
    {
        try
        {
            string root = Path.GetPathRoot(Path.GetFullPath(targetPath)) ?? string.Empty;
            if (!string.IsNullOrEmpty(root))
            {
                var drive = new DriveInfo(root);
                if (drive.AvailableFreeSpace < requiredBytes * 2)
                {
                    throw new InvalidOperationException(
                        $"Insufficient disk space for compaction. Required at least {requiredBytes * 2:N0} bytes free, available: {drive.AvailableFreeSpace:N0} bytes.");
                }
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch
        {
            // Ignore if drive info cannot be queried on non-standard paths
        }
    }

    private static void ExecuteTwoPhaseCommit(
        string targetPath,
        string tempPath,
        string preCompactPath,
        Action reloader,
        Action? rollbackReloader = null)
    {
        try
        {
            // Rename target to .pre-compact
            if (File.Exists(preCompactPath)) File.Delete(preCompactPath);
            File.Move(targetPath, preCompactPath);

            try
            {
                File.Move(tempPath, targetPath);
                reloader();

                // Post-commit: delete pre-compact
                if (File.Exists(preCompactPath))
                {
                    try { File.Delete(preCompactPath); } catch { }
                }
            }
            catch
            {
                // Rollback!
                if (!File.Exists(targetPath) && File.Exists(preCompactPath))
                {
                    File.Move(preCompactPath, targetPath);
                }

                if (rollbackReloader != null)
                {
                    try { rollbackReloader(); } catch { }
                }
                else
                {
                    try { reloader(); } catch { }
                }
                throw;
            }
        }
        catch
        {
            throw;
        }
    }
}
