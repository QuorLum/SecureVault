using SecureVault.Core.Format;

namespace SecureVault.Core.Operations;

/// <summary>
/// Converts files between Fast Obfuscation Mode and Secure AES-256-GCM Mode (A17, A18).
/// Preserves identical plaintext SHA-256 and metadata.
/// </summary>
public sealed class ProtectionModeOperation
{
    private readonly VaultManager _vault;

    public ProtectionModeOperation(VaultManager vault)
    {
        ArgumentNullException.ThrowIfNull(vault);
        _vault = vault;
    }

    /// <summary>
    /// Changes the protection mode of an individual file (A18).
    /// Re-encrypts all chunks with new nonces and AEAD tags.
    /// </summary>
    public async Task<IndexEntry> ChangeProtectionModeAsync(
        Guid fileGuid,
        ProtectionMode newMode,
        IProgress<long>? progress = null,
        CancellationToken ct = default)
    {
        var entry = _vault.Files.FirstOrDefault(f => f.FileGuid == fileGuid)
            ?? throw new FileNotFoundException($"File with GUID '{fileGuid}' was not found in vault.");

        if (entry.ProtectionMode == newMode)
        {
            return entry;
        }

        using var oldStream = _vault.OpenFileStream(entry);
        using var memoryStream = new MemoryStream();
        await oldStream.CopyToAsync(memoryStream, ct);
        memoryStream.Seek(0, SeekOrigin.Begin);

        byte[] originalSha = (byte[])entry.PlaintextSHA256.Clone();
        string originalFileName = entry.FileName;
        string originalPath = entry.VirtualFolderPath;
        var originalTags = new List<string>(entry.Tags);
        string originalNotes = entry.Notes;
        bool originalFav = entry.IsFavorite;

        // Ingest file under new protection mode
        var newEntry = await _vault.AddFileAsync(
            memoryStream,
            originalFileName,
            originalPath,
            newMode,
            cancellationToken: ct);

        // Copy over tags and metadata
        newEntry.Tags = originalTags.ToArray();
        newEntry.Notes = originalNotes;
        newEntry.IsFavorite = originalFav;

        // Validate plaintext SHA-256 integrity
        if (!originalSha.AsSpan().SequenceEqual(newEntry.PlaintextSHA256))
        {
            _vault.DeleteFile(newEntry.FileGuid);
            throw new InvalidOperationException("Integrity check failed: Plaintext SHA-256 changed after re-encryption.");
        }

        // Safe delete old entry
        _vault.DeleteFile(fileGuid);

        return newEntry;
    }

    /// <summary>
    /// "Encrypt Everything": Converts all Fast Obfuscation files to Secure Mode (A17).
    /// </summary>
    public async Task<int> EncryptAllAsync(
        IProgress<FileAddProgress>? progress = null,
        CancellationToken ct = default)
    {
        var fastFiles = _vault.Files
            .Where(f => !f.IsFolder && f.ProtectionMode == ProtectionMode.FastObfuscation)
            .ToList();

        if (fastFiles.Count == 0) return 0;

        int convertedCount = 0;
        long totalBytes = fastFiles.Sum(f => (long)f.OriginalSize);
        long bytesProcessed = 0;

        foreach (var file in fastFiles)
        {
            ct.ThrowIfCancellationRequested();

            progress?.Report(new FileAddProgress(
                file.FileName,
                bytesProcessed,
                totalBytes,
                convertedCount,
                fastFiles.Count,
                0,
                TimeSpan.Zero));

            await ChangeProtectionModeAsync(file.FileGuid, ProtectionMode.SecureMode, null, ct);

            convertedCount++;
            bytesProcessed += (long)file.OriginalSize;
        }

        progress?.Report(new FileAddProgress(
            "Complete",
            totalBytes,
            totalBytes,
            convertedCount,
            fastFiles.Count,
            0,
            TimeSpan.Zero));

        return convertedCount;
    }
}
