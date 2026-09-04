using System.Security.Cryptography;
using SecureVault.Core.Exceptions;
using SecureVault.Core.Format;

namespace SecureVault.Core.Backup;

/// <summary>
/// Splits single vault files and multi-vault chains into arbitrary part sizes (default 50GB) (G02-G05).
/// Generates verified split parts joinable with binary concatenation and a comprehensive backup manifest.
/// </summary>
public static class SplitBackupService
{
    public const long DefaultSplitSizeBytes = 50L * 1024 * 1024 * 1024; // 50 GB
    private const int BufferSize = 65536; // 64 KB

    /// <summary>
    /// Splits an entire vault chain into split parts in the destination folder, writing a unified BackupManifest.
    /// </summary>
    public static async Task<BackupManifest> BackupSplitChainAsync(
        string masterVaultPath,
        string destinationFolder,
        long splitSizeBytes = DefaultSplitSizeBytes,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(masterVaultPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFolder);
        if (splitSizeBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(splitSizeBytes), "Split size must be positive.");

        if (!File.Exists(masterVaultPath))
            throw new FileNotFoundException($"Master vault file not found: '{masterVaultPath}'", masterVaultPath);

        Directory.CreateDirectory(destinationFolder);

        var chainFiles = BackupService.DiscoverChainFiles(masterVaultPath);
        long totalBytesToSplit = chainFiles.Sum(f => new FileInfo(f.Path).Length);
        long totalBytesProcessed = 0;

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
            TotalSizeBytes = totalBytesToSplit,
            IsSplit = true,
            SplitSizeBytes = splitSizeBytes
        };

        using var chainHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        foreach (var chainFile in chainFiles)
        {
            ct.ThrowIfCancellationRequested();

            var chainPartEntry = new BackupChainPartEntry
            {
                PartIndex = chainFile.PartIndex,
                VaultFileName = chainFile.FileName
            };

            var splits = await SplitSingleVaultFileAsync(
                chainFile.Path,
                destinationFolder,
                splitSizeBytes,
                b =>
                {
                    long cur = totalBytesProcessed + b;
                    if (totalBytesToSplit > 0) progress?.Report((double)cur / totalBytesToSplit);
                },
                ct);

            chainPartEntry.VaultFileSizeBytes = splits.Sum(s => s.SizeBytes);
            totalBytesProcessed += chainPartEntry.VaultFileSizeBytes;
            chainPartEntry.Splits.AddRange(splits);

            // Whole-file SHA-256 for this vault file
            chainPartEntry.VaultFileSha256 = await HashVerifier.ComputeFileHashAsync(chainFile.Path, null, ct);
            chainHasher.AppendData(Convert.FromHexString(chainPartEntry.VaultFileSha256));

            manifest.ChainParts.Add(chainPartEntry);
        }

        manifest.ChainSha256 = Convert.ToHexString(chainHasher.GetHashAndReset()).ToLowerInvariant();

        // Write backup manifest
        string manifestPath = BackupManifest.GetManifestPath(destinationFolder, vaultName);
        manifest.SaveToFile(manifestPath);

        return manifest;
    }

    private static async Task<List<BackupSplitPartEntry>> SplitSingleVaultFileAsync(
        string sourceFilePath,
        string destinationFolder,
        long splitSizeBytes,
        Action<long> progressBytes,
        CancellationToken ct)
    {
        var splits = new List<BackupSplitPartEntry>();
        string baseFileName = Path.GetFileName(sourceFilePath);
        long fileLen = new FileInfo(sourceFilePath).Length;

        using var inStream = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, BufferSize, useAsync: true);
        byte[] buffer = new byte[BufferSize];

        int splitIndex = 0;
        long totalRead = 0;

        while (totalRead < fileLen || splitIndex == 0)
        {
            ct.ThrowIfCancellationRequested();

            string splitFileName = $"{baseFileName}.part{(splitIndex + 1):D3}";
            string splitFilePath = Path.Combine(destinationFolder, splitFileName);

            long splitOffset = inStream.Position;
            long bytesRemainingInThisSplit = splitSizeBytes;
            long thisSplitWritten = 0;

            using (var outStream = new FileStream(splitFilePath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
            using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                while (bytesRemainingInThisSplit > 0 && inStream.Position < fileLen)
                {
                    int toRead = (int)Math.Min(buffer.Length, bytesRemainingInThisSplit);
                    int read = await inStream.ReadAsync(buffer.AsMemory(0, toRead), ct);
                    if (read == 0) break;

                    await outStream.WriteAsync(buffer.AsMemory(0, read), ct);
                    hasher.AppendData(buffer, 0, read);

                    thisSplitWritten += read;
                    bytesRemainingInThisSplit -= read;
                    totalRead += read;
                    progressBytes(totalRead);
                }

                await outStream.FlushAsync(ct);
                string splitHash = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();

                splits.Add(new BackupSplitPartEntry
                {
                    FileName = splitFileName,
                    Index = splitIndex,
                    Offset = splitOffset,
                    SizeBytes = thisSplitWritten,
                    Sha256 = splitHash
                });
            }

            splitIndex++;
            if (inStream.Position >= fileLen)
                break;
        }

        return splits;
    }
}
