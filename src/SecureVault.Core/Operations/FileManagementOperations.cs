using System.Security.Cryptography;
using SecureVault.Core.Exceptions;
using SecureVault.Core.Format;
using SecureVault.Core.Organization;

namespace SecureVault.Core.Operations;

/// <summary>
/// Executes file management operations: rename, move, deep copy, and verified export (C10-C15).
/// </summary>
public sealed class FileManagementOperations
{
    private readonly VaultManager _vault;
    private readonly VirtualFolderService _folderService;

    public FileManagementOperations(VaultManager vault, VirtualFolderService folderService)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(folderService);
        _vault = vault;
        _folderService = folderService;
    }

    /// <summary>
    /// Renames a file entry in the index without moving chunk data (C10).
    /// </summary>
    public void Rename(Guid fileGuid, string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        var entry = _vault.Files.FirstOrDefault(f => f.FileGuid == fileGuid);
        if (entry == null)
            throw new KeyNotFoundException($"File with GUID {fileGuid} was not found.");

        entry.FileName = newName.Trim();
        entry.Category = (byte)AutoCategorizer.Categorize(entry.FileName);
        entry.DateModifiedTicks = DateTime.UtcNow.Ticks;
    }

    /// <summary>
    /// Moves a file to another virtual folder without moving chunk data (C11).
    /// </summary>
    public void Move(Guid fileGuid, Guid? targetFolderGuid)
    {
        var entry = _vault.Files.FirstOrDefault(f => f.FileGuid == fileGuid);
        if (entry == null)
            throw new KeyNotFoundException($"File with GUID {fileGuid} was not found.");

        entry.ParentFolderGuid = (targetFolderGuid == null || targetFolderGuid == Guid.Empty) ? null : targetFolderGuid;
        entry.VirtualFolderPath = _folderService.GetFullPath(entry.ParentFolderGuid);
        entry.DateModifiedTicks = DateTime.UtcNow.Ticks;
    }

    /// <summary>
    /// Deep copies a file, writing an independent set of encrypted chunks with a new FileGuid and fresh nonces (C12).
    /// REVIEWER REQUIREMENT: Avoids fragile chunk pointer sharing to prevent divergence bugs on edits and compaction.
    /// </summary>
    public async Task<IndexEntry> CopyAsync(
        Guid fileGuid,
        Guid? targetFolderGuid,
        string? newFileName = null,
        CancellationToken cancellationToken = default)
    {
        var sourceEntry = _vault.Files.FirstOrDefault(f => f.FileGuid == fileGuid);
        if (sourceEntry == null)
            throw new KeyNotFoundException($"File with GUID {fileGuid} was not found.");

        string copyName = !string.IsNullOrWhiteSpace(newFileName) ? newFileName : "Copy of " + sourceEntry.FileName;
        string targetPath = _folderService.GetFullPath(targetFolderGuid);

        using var sourceStream = _vault.OpenFileStream(sourceEntry);

        var copyEntry = await _vault.AddFileAsync(
            sourceStream,
            copyName,
            targetPath,
            sourceEntry.ProtectionMode,
            null,
            cancellationToken);

        copyEntry.ParentFolderGuid = (targetFolderGuid == null || targetFolderGuid == Guid.Empty) ? null : targetFolderGuid;
        copyEntry.Tags = sourceEntry.Tags?.ToArray() ?? Array.Empty<string>();
        copyEntry.Notes = sourceEntry.Notes;
        copyEntry.Category = sourceEntry.Category;

        return copyEntry;
    }

    /// <summary>
    /// Decrypts and streams a file to local disk, verifying its plaintext SHA-256 after export (C13).
    /// </summary>
    public async Task ExportFileAsync(
        Guid fileGuid,
        string destinationFilePath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFilePath);

        var entry = _vault.Files.FirstOrDefault(f => f.FileGuid == fileGuid);
        if (entry == null)
            throw new KeyNotFoundException($"File with GUID {fileGuid} was not found.");

        string destDir = Path.GetDirectoryName(destinationFilePath)!;
        if (!string.IsNullOrEmpty(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        string tempExportPath = destinationFilePath + $".tmp_{Guid.NewGuid():N}";
        try
        {
            using (var inStream = _vault.OpenFileStream(entry))
            using (var outStream = new FileStream(tempExportPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, useAsync: true))
            using (var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                byte[] buffer = new byte[65536];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await inStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    await outStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    sha256.AppendData(buffer, 0, bytesRead);

                    totalRead += bytesRead;
                    if (entry.OriginalSize > 0)
                    {
                        progress?.Report((double)totalRead / entry.OriginalSize);
                    }
                }

                await outStream.FlushAsync(cancellationToken);

                byte[] computedHash = sha256.GetHashAndReset();
                if (!computedHash.AsSpan().SequenceEqual(entry.PlaintextSHA256))
                {
                    throw new CorruptedVaultException(
                        $"Exported file '{entry.FileName}' failed plaintext SHA-256 integrity verification.");
                }
            }

            // Atomic move to final destination
            if (File.Exists(destinationFilePath))
            {
                File.Delete(destinationFilePath);
            }
            File.Move(tempExportPath, destinationFilePath);
        }
        finally
        {
            if (File.Exists(tempExportPath))
            {
                try { File.Delete(tempExportPath); } catch { }
            }
        }
    }

    /// <summary>
    /// Exports multiple files into a destination folder (C14).
    /// </summary>
    public async Task ExportMultipleAsync(
        IReadOnlyList<Guid> fileGuids,
        string destinationFolder,
        IProgress<FileAddProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileGuids);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFolder);

        Directory.CreateDirectory(destinationFolder);

        for (int i = 0; i < fileGuids.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entry = _vault.Files.FirstOrDefault(f => f.FileGuid == fileGuids[i]);
            if (entry == null)
                continue;

            string targetPath = Path.Combine(destinationFolder, entry.FileName);
            await ExportFileAsync(entry.FileGuid, targetPath, null, cancellationToken);

            progress?.Report(new FileAddProgress(
                entry.FileName,
                i + 1,
                fileGuids.Count,
                i + 1,
                fileGuids.Count,
                0,
                TimeSpan.Zero));
        }
    }

    /// <summary>
    /// Recursively exports all files within a virtual folder, recreating the folder structure on disk (C15).
    /// </summary>
    public async Task ExportFolderAsync(
        Guid folderGuid,
        string destinationFolder,
        IProgress<FileAddProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFolder);

        var targetFolder = _folderService.GetFolder(folderGuid);
        string folderName = targetFolder?.Name ?? "Export";
        string rootDest = Path.Combine(destinationFolder, folderName);
        Directory.CreateDirectory(rootDest);

        await ExportFolderRecursiveInternal(folderGuid, rootDest, progress, cancellationToken);
    }

    private async Task ExportFolderRecursiveInternal(
        Guid folderGuid,
        string currentDiskDir,
        IProgress<FileAddProgress>? progress,
        CancellationToken cancellationToken)
    {
        var files = _folderService.GetFiles(folderGuid);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string destFile = Path.Combine(currentDiskDir, file.FileName);
            await ExportFileAsync(file.FileGuid, destFile, null, cancellationToken);
        }

        var subfolders = _folderService.GetSubfolders(folderGuid);
        foreach (var sub in subfolders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string subDir = Path.Combine(currentDiskDir, sub.Name);
            Directory.CreateDirectory(subDir);
            await ExportFolderRecursiveInternal(sub.FolderGuid, subDir, progress, cancellationToken);
        }
    }
}
