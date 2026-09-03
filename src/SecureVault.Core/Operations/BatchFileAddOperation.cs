using System.Diagnostics;
using SecureVault.Core.Format;
using SecureVault.Core.Organization;

namespace SecureVault.Core.Operations;

public record FileAddProgress(
    string FileName,
    long BytesProcessed,
    long TotalBytes,
    int FileIndex,
    int TotalFiles,
    double SpeedBytesPerSec,
    TimeSpan EstimatedTimeRemaining);

/// <summary>
/// Executes multi-file additions and recursive folder ingestion with real-time progress reporting (C02, C03, C07).
/// </summary>
public sealed class BatchFileAddOperation
{
    private readonly VaultManager _vault;

    public BatchFileAddOperation(VaultManager vault)
    {
        ArgumentNullException.ThrowIfNull(vault);
        _vault = vault;
    }

    /// <summary>
    /// Adds multiple files to the vault sequentially, reporting progress after each chunk and file (C02, C07).
    /// </summary>
    public async Task AddFilesAsync(
        IReadOnlyList<string> filePaths,
        string virtualFolderPath = "/",
        ProtectionMode mode = ProtectionMode.SecureMode,
        IProgress<FileAddProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePaths);

        long totalBytesAcrossAllFiles = 0;
        foreach (var p in filePaths)
        {
            if (File.Exists(p))
            {
                totalBytesAcrossAllFiles += new FileInfo(p).Length;
            }
        }

        long totalBytesProcessed = 0;
        var stopwatch = Stopwatch.StartNew();

        for (int i = 0; i < filePaths.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string path = filePaths[i];
            if (!File.Exists(path))
                continue;

            string fileName = Path.GetFileName(path);
            long fileLength = new FileInfo(path).Length;

            using var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);

            var fileChunkProgress = new Progress<double>(fileFraction =>
            {
                long currentFileBytes = (long)(fileLength * fileFraction);
                long overallBytes = totalBytesProcessed + currentFileBytes;
                double elapsedSec = stopwatch.Elapsed.TotalSeconds;
                double speed = elapsedSec > 0.05 ? overallBytes / elapsedSec : 0;
                long bytesRemaining = Math.Max(0, totalBytesAcrossAllFiles - overallBytes);
                TimeSpan eta = speed > 0 ? TimeSpan.FromSeconds(bytesRemaining / speed) : TimeSpan.Zero;

                progress?.Report(new FileAddProgress(
                    fileName,
                    overallBytes,
                    totalBytesAcrossAllFiles,
                    i + 1,
                    filePaths.Count,
                    speed,
                    eta));
            });

            var entry = await _vault.AddFileAsync(
                fileStream,
                fileName,
                virtualFolderPath,
                mode,
                fileChunkProgress,
                cancellationToken);

            // Auto-categorize
            entry.Category = (byte)AutoCategorizer.Categorize(fileName);

            totalBytesProcessed += fileLength;
        }
    }

    /// <summary>
    /// Recursively enumerates a local disk directory and adds all files into matching virtual folders (C03).
    /// </summary>
    public async Task AddFolderAsync(
        string localFolderPath,
        string targetVirtualPath = "/",
        ProtectionMode mode = ProtectionMode.SecureMode,
        bool recursive = true,
        IProgress<FileAddProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localFolderPath);
        if (!Directory.Exists(localFolderPath))
            throw new DirectoryNotFoundException($"Directory '{localFolderPath}' was not found.");

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var allFiles = Directory.GetFiles(localFolderPath, "*", searchOption);

        long totalBytes = 0;
        foreach (var f in allFiles)
        {
            totalBytes += new FileInfo(f).Length;
        }

        long totalProcessed = 0;
        var stopwatch = Stopwatch.StartNew();

        string normalizedBase = Path.GetFullPath(localFolderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        for (int i = 0; i < allFiles.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string fullFilePath = allFiles[i];
            string fileName = Path.GetFileName(fullFilePath);
            long fileLength = new FileInfo(fullFilePath).Length;

            // Map directory structure relative to source folder
            string relativeDir = Path.GetDirectoryName(fullFilePath)!;
            string relativeSubpath = relativeDir.Length > normalizedBase.Length
                ? relativeDir.Substring(normalizedBase.Length).Replace('\\', '/')
                : "";

            string folderVirtualPath = targetVirtualPath.TrimEnd('/');
            if (!string.IsNullOrEmpty(relativeSubpath))
            {
                folderVirtualPath += relativeSubpath;
            }
            if (string.IsNullOrEmpty(folderVirtualPath))
            {
                folderVirtualPath = "/";
            }

            using var fileStream = new FileStream(fullFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);

            var fileChunkProgress = new Progress<double>(fraction =>
            {
                long currentFileBytes = (long)(fileLength * fraction);
                long overallBytes = totalProcessed + currentFileBytes;
                double elapsedSec = stopwatch.Elapsed.TotalSeconds;
                double speed = elapsedSec > 0.05 ? overallBytes / elapsedSec : 0;
                long bytesRemaining = Math.Max(0, totalBytes - overallBytes);
                TimeSpan eta = speed > 0 ? TimeSpan.FromSeconds(bytesRemaining / speed) : TimeSpan.Zero;

                progress?.Report(new FileAddProgress(
                    fileName,
                    overallBytes,
                    totalBytes,
                    i + 1,
                    allFiles.Length,
                    speed,
                    eta));
            });

            var entry = await _vault.AddFileAsync(
                fileStream,
                fileName,
                folderVirtualPath,
                mode,
                fileChunkProgress,
                cancellationToken);

            entry.Category = (byte)AutoCategorizer.Categorize(fileName);
            totalProcessed += fileLength;
        }
    }
}
