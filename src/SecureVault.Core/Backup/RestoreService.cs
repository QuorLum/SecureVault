using System.Security.Cryptography;
using SecureVault.Core.Exceptions;
using SecureVault.Core.Format;
using SecureVault.Core.MultiVault;

namespace SecureVault.Core.Backup;

public sealed class MissingOrCorruptedPartDetail
{
    public string VaultFileName { get; set; } = string.Empty;
    public string? SplitFileName { get; set; }
    public string Reason { get; set; } = string.Empty;

    public override string ToString() =>
        string.IsNullOrEmpty(SplitFileName)
            ? $"{VaultFileName}: {Reason}"
            : $"{VaultFileName} ({SplitFileName}): {Reason}";
}

public sealed class ChainMissingPartsReport
{
    public bool IsCompleteAndValid => FailedParts.Count == 0;
    public int TotalPartsChecked { get; set; }
    public int ValidPartsCount { get; set; }
    public List<MissingOrCorruptedPartDetail> FailedParts { get; set; } = new();
}

/// <summary>
/// Restores single-file backups and multi-part vault chains with pre-restore validation (G07-G09, G15).
/// Reports exact vault chain part and split filenames for missing or corrupted archives.
/// </summary>
public static class RestoreService
{
    private const int BufferSize = 65536; // 64 KB

    /// <summary>
    /// Checks all chain files and split parts against the BackupManifest (G04, G08, G09).
    /// </summary>
    public static async Task<ChainMissingPartsReport> CheckPartsAsync(string manifestPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"Backup manifest not found: '{manifestPath}'.", manifestPath);

        var manifest = BackupManifest.LoadFromFile(manifestPath);
        string folder = Path.GetDirectoryName(manifestPath) ?? string.Empty;
        var report = new ChainMissingPartsReport();

        foreach (var chainPart in manifest.ChainParts)
        {
            if (manifest.IsSplit)
            {
                foreach (var split in chainPart.Splits)
                {
                    report.TotalPartsChecked++;
                    string splitPath = Path.Combine(folder, split.FileName);

                    if (!File.Exists(splitPath))
                    {
                        report.FailedParts.Add(new MissingOrCorruptedPartDetail
                        {
                            VaultFileName = chainPart.VaultFileName,
                            SplitFileName = split.FileName,
                            Reason = "Missing part file"
                        });
                        continue;
                    }

                    string hash = await HashVerifier.ComputeFileHashAsync(splitPath, null, ct);
                    if (!string.Equals(hash, split.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        report.FailedParts.Add(new MissingOrCorruptedPartDetail
                        {
                            VaultFileName = chainPart.VaultFileName,
                            SplitFileName = split.FileName,
                            Reason = $"SHA-256 mismatch (Expected: {split.Sha256}, Actual: {hash})"
                        });
                        continue;
                    }

                    report.ValidPartsCount++;
                }
            }
            else
            {
                report.TotalPartsChecked++;
                string partPath = Path.Combine(folder, chainPart.VaultFileName);

                if (!File.Exists(partPath))
                {
                    report.FailedParts.Add(new MissingOrCorruptedPartDetail
                    {
                        VaultFileName = chainPart.VaultFileName,
                        Reason = "Missing vault file"
                    });
                    continue;
                }

                string hash = await HashVerifier.ComputeFileHashAsync(partPath, null, ct);
                if (!string.Equals(hash, chainPart.VaultFileSha256, StringComparison.OrdinalIgnoreCase))
                {
                    report.FailedParts.Add(new MissingOrCorruptedPartDetail
                    {
                        VaultFileName = chainPart.VaultFileName,
                        Reason = $"SHA-256 mismatch (Expected: {chainPart.VaultFileSha256}, Actual: {hash})"
                    });
                    continue;
                }

                report.ValidPartsCount++;
            }
        }

        return report;
    }

    /// <summary>
    /// Restores a full vault chain from a BackupManifest into the target directory (G08, G15).
    /// </summary>
    public static async Task<string> RestoreChainAsync(
        string manifestPath,
        string targetDirectory,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);

        var report = await CheckPartsAsync(manifestPath, ct);
        if (!report.IsCompleteAndValid)
        {
            var errors = report.FailedParts.Select(f => f.ToString()).ToList();
            throw new IncompleteBackupException(
                $"Cannot restore vault: {report.FailedParts.Count} parts are missing or corrupted.",
                errors);
        }

        Directory.CreateDirectory(targetDirectory);
        var manifest = BackupManifest.LoadFromFile(manifestPath);
        string sourceFolder = Path.GetDirectoryName(manifestPath) ?? string.Empty;

        long totalBytesToRestore = manifest.TotalSizeBytes;
        long totalBytesRestored = 0;
        string? masterVaultDestPath = null;

        foreach (var chainPart in manifest.ChainParts)
        {
            ct.ThrowIfCancellationRequested();
            string destFilePath = Path.Combine(targetDirectory, chainPart.VaultFileName);

            if (chainPart.PartIndex == 0)
            {
                masterVaultDestPath = destFilePath;
            }

            if (manifest.IsSplit)
            {
                // Join split parts in order
                using var outStream = new FileStream(destFilePath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
                using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] buffer = new byte[BufferSize];

                foreach (var split in chainPart.Splits.OrderBy(s => s.Index))
                {
                    string splitPath = Path.Combine(sourceFolder, split.FileName);
                    using var inStream = new FileStream(splitPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
                    int bytesRead;

                    while ((bytesRead = await inStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
                    {
                        await outStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                        hasher.AppendData(buffer, 0, bytesRead);
                        totalBytesRestored += bytesRead;
                        if (totalBytesToRestore > 0) progress?.Report((double)totalBytesRestored / totalBytesToRestore);
                    }
                }

                await outStream.FlushAsync(ct);
                string wholeHash = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();

                if (!string.Equals(wholeHash, chainPart.VaultFileSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new CorruptedVaultException(
                        $"Restored vault part '{chainPart.VaultFileName}' failed whole-file SHA-256 verification.");
                }
            }
            else
            {
                // Single-file copy
                string srcFilePath = Path.Combine(sourceFolder, chainPart.VaultFileName);
                using var inStream = new FileStream(srcFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
                using var outStream = new FileStream(destFilePath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
                using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] buffer = new byte[BufferSize];
                int bytesRead;

                while ((bytesRead = await inStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
                {
                    await outStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    hasher.AppendData(buffer, 0, bytesRead);
                    totalBytesRestored += bytesRead;
                    if (totalBytesToRestore > 0) progress?.Report((double)totalBytesRestored / totalBytesToRestore);
                }

                await outStream.FlushAsync(ct);
                string wholeHash = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();

                if (!string.Equals(wholeHash, chainPart.VaultFileSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new CorruptedVaultException(
                        $"Restored vault part '{chainPart.VaultFileName}' failed whole-file SHA-256 verification.");
                }
            }
        }

        // Also reconstruct live chain manifest if multiple parts exist
        if (manifest.ChainParts.Count > 1 && masterVaultDestPath != null)
        {
            var chainManifest = new VaultChainManifest
            {
                VaultName = manifest.VaultName,
                VaultUUID = manifest.VaultUUID,
                FormatVersion = manifest.FormatVersion,
                TotalFiles = 0,
                TotalSizeBytes = manifest.TotalSizeBytes,
                LastModifiedUtc = DateTime.UtcNow
            };

            foreach (var p in manifest.ChainParts)
            {
                chainManifest.Parts.Add(new VaultChainPartInfo
                {
                    PartIndex = p.PartIndex,
                    FileName = p.VaultFileName,
                    FileSizeBytes = p.VaultFileSizeBytes,
                    FileSha256 = p.VaultFileSha256
                });
            }

            chainManifest.SaveToFile(VaultChainManifest.GetManifestPath(masterVaultDestPath));
        }

        return masterVaultDestPath!;
    }

    /// <summary>
    /// Restores a single vault file, verifying companion .sha256 if present (G07).
    /// </summary>
    public static async Task RestoreSingleFileAsync(
        string backupFilePath,
        string destPath,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destPath);

        // Check companion hash if present
        string companion = backupFilePath + ".sha256";
        if (File.Exists(companion))
        {
            bool companionValid = await HashVerifier.VerifySha256CompanionFileAsync(companion, ct);
            if (!companionValid)
            {
                throw new CorruptedVaultException("Source backup file failed companion .sha256 hash verification.");
            }
        }

        string sourceHash = await HashVerifier.ComputeFileHashAsync(backupFilePath, null, ct);

        string? destDir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        using (var inStream = new FileStream(backupFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true))
        using (var outStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
        {
            byte[] buffer = new byte[BufferSize];
            long totalCopied = 0;
            long fileLen = inStream.Length;
            int bytesRead;

            while ((bytesRead = await inStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
            {
                await outStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                totalCopied += bytesRead;
                if (fileLen > 0) progress?.Report((double)totalCopied / fileLen);
            }
            await outStream.FlushAsync(ct);
        }

        string destHash = await HashVerifier.ComputeFileHashAsync(destPath, null, ct);
        if (!string.Equals(sourceHash, destHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new CorruptedVaultException("Restored file hash mismatch with source backup.");
        }
    }
}
