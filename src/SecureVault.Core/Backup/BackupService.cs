using System.Security.Cryptography;
using SecureVault.Core.Exceptions;
using SecureVault.Core.Format;
using SecureVault.Core.MultiVault;

namespace SecureVault.Core.Backup;

/// <summary>
/// Executes full, zero-decryption backups of vault files and multi-part chains (G01, G06, G15).
/// Copies raw encrypted bytes without requiring password or exposing plaintext to memory/disk.
/// </summary>
public static class BackupService
{
    private const int BufferSize = 65536; // 64 KB

    /// <summary>
    /// Backs up a complete vault chain to a destination folder, generating companion .sha256 files and a backup manifest.
    /// </summary>
    public static async Task<BackupManifest> BackupChainAsync(
        string masterVaultPath,
        string destinationFolder,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(masterVaultPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFolder);

        if (!File.Exists(masterVaultPath))
            throw new FileNotFoundException($"Master vault file not found: '{masterVaultPath}'", masterVaultPath);

        Directory.CreateDirectory(destinationFolder);

        // Discover all chain parts (.vault, .vault2, .vault3...)
        var chainFiles = DiscoverChainFiles(masterVaultPath);
        long totalBytesToCopy = chainFiles.Sum(f => new FileInfo(f.Path).Length);
        long totalBytesCopiedSoFar = 0;

        string vaultName = Path.GetFileNameWithoutExtension(masterVaultPath);
        Guid vaultUuid;
        using (var fs = new FileStream(masterVaultPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            var header = VaultHeader.ReadFrom(fs);
            vaultUuid = header.VaultUUID;
        }

        var manifest = new BackupManifest
        {
            VaultName = vaultName,
            VaultUUID = vaultUuid,
            FormatVersion = VaultConstants.CurrentFormatVersion,
            CreatedUtc = DateTime.UtcNow,
            TotalSizeBytes = totalBytesToCopy,
            IsSplit = false,
            SplitSizeBytes = 0
        };

        using var chainCumulativeHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        for (int i = 0; i < chainFiles.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var part = chainFiles[i];
            string destFilePath = Path.Combine(destinationFolder, part.FileName);

            var (partHash, bytesCopied) = await CopyAndHashFileAsync(
                part.Path,
                destFilePath,
                b =>
                {
                    long currentTotal = totalBytesCopiedSoFar + b;
                    if (totalBytesToCopy > 0) progress?.Report((double)currentTotal / totalBytesToCopy);
                },
                ct);

            totalBytesCopiedSoFar += bytesCopied;
            chainCumulativeHasher.AppendData(Convert.FromHexString(partHash));

            // Write companion .sha256 file
            await HashVerifier.WriteSha256CompanionFileAsync(destFilePath, partHash);

            manifest.ChainParts.Add(new BackupChainPartEntry
            {
                PartIndex = part.PartIndex,
                VaultFileName = part.FileName,
                VaultFileSizeBytes = bytesCopied,
                VaultFileSha256 = partHash
            });

            // Re-read and verify copied file
            string verifiedHash = await HashVerifier.ComputeFileHashAsync(destFilePath, null, ct);
            if (!string.Equals(partHash, verifiedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new CorruptedVaultException($"Backup copy of '{part.FileName}' failed post-write hash verification.");
            }
        }

        manifest.ChainSha256 = Convert.ToHexString(chainCumulativeHasher.GetHashAndReset()).ToLowerInvariant();

        // Save backup manifest
        string manifestPath = BackupManifest.GetManifestPath(destinationFolder, vaultName);
        manifest.SaveToFile(manifestPath);

        // Also copy live chain manifest if it exists
        string liveChainManifest = VaultChainManifest.GetManifestPath(masterVaultPath);
        if (File.Exists(liveChainManifest))
        {
            string destChainManifest = Path.Combine(destinationFolder, Path.GetFileName(liveChainManifest));
            File.Copy(liveChainManifest, destChainManifest, overwrite: true);
        }

        return manifest;
    }

    /// <summary>
    /// Copies a single vault file to destPath and generates companion .sha256 file (G01, G06).
    /// </summary>
    public static async Task<string> BackupSingleFileAsync(
        string sourceVaultPath,
        string destPath,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceVaultPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destPath);

        string? destDir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        var (hash, _) = await CopyAndHashFileAsync(sourceVaultPath, destPath, bytes =>
        {
            long fileLen = new FileInfo(sourceVaultPath).Length;
            if (fileLen > 0) progress?.Report((double)bytes / fileLen);
        }, ct);

        await HashVerifier.WriteSha256CompanionFileAsync(destPath, hash);

        // Verification step
        string verifiedHash = await HashVerifier.ComputeFileHashAsync(destPath, null, ct);
        if (!string.Equals(hash, verifiedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new CorruptedVaultException($"Backup verification failed: Hash mismatch for '{destPath}'.");
        }

        return hash;
    }

    private static async Task<(string Sha256, long BytesCopied)> CopyAndHashFileAsync(
        string sourcePath,
        string destPath,
        Action<long> progressBytes,
        CancellationToken ct)
    {
        using var inStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, BufferSize, useAsync: true);
        using var outStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        byte[] buffer = new byte[BufferSize];
        long totalCopied = 0;
        int bytesRead;

        while ((bytesRead = await inStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
        {
            await outStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            hasher.AppendData(buffer, 0, bytesRead);
            totalCopied += bytesRead;
            progressBytes(totalCopied);
        }

        await outStream.FlushAsync(ct);
        string hash = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
        return (hash, totalCopied);
    }

    public static List<(int PartIndex, string FileName, string Path)> DiscoverChainFiles(string masterVaultPath)
    {
        var list = new List<(int PartIndex, string FileName, string Path)>
        {
            (0, Path.GetFileName(masterVaultPath), Path.GetFullPath(masterVaultPath))
        };

        string liveChainManifest = VaultChainManifest.GetManifestPath(masterVaultPath);
        if (File.Exists(liveChainManifest))
        {
            try
            {
                var manifest = VaultChainManifest.LoadFromFile(liveChainManifest);
                foreach (var p in manifest.Parts.Where(p => p.PartIndex > 0).OrderBy(p => p.PartIndex))
                {
                    string partPath = VaultChainManager.GetPartPath(masterVaultPath, p.PartIndex);
                    if (File.Exists(partPath))
                    {
                        list.Add((p.PartIndex, Path.GetFileName(partPath), partPath));
                    }
                }
                return list;
            }
            catch { }
        }

        // Probing fallback
        for (int i = 1; i <= 100; i++)
        {
            string partPath = VaultChainManager.GetPartPath(masterVaultPath, i);
            if (File.Exists(partPath))
            {
                list.Add((i, Path.GetFileName(partPath), partPath));
            }
            else
            {
                break;
            }
        }

        return list;
    }
}
