using System.Security.Cryptography;
using System.Text;

namespace SecureVault.Core.Backup;

/// <summary>
/// Incremental streaming SHA-256 calculator and sha256sum companion file generator/verifier (G04, G05, G06).
/// </summary>
public static class HashVerifier
{
    private const int BufferSize = 65536; // 64 KB

    /// <summary>
    /// Computes the SHA-256 hash of a file using buffered streaming, reporting progress in bytes read.
    /// </summary>
    public static async Task<string> ComputeFileHashAsync(
        string filePath,
        IProgress<long>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File to hash was not found: '{filePath}'.", filePath);

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, BufferSize, useAsync: true);
        return await ComputeStreamHashAsync(stream, progress, ct);
    }

    /// <summary>
    /// Computes the SHA-256 hash of a stream using buffered streaming.
    /// </summary>
    public static async Task<string> ComputeStreamHashAsync(
        Stream stream,
        IProgress<long>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[BufferSize];
        long totalBytesRead = 0;
        int bytesRead;

        while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
        {
            hasher.AppendData(buffer, 0, bytesRead);
            totalBytesRead += bytesRead;
            progress?.Report(totalBytesRead);
        }

        byte[] hash = hasher.GetHashAndReset();
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Writes a companion .sha256 file in sha256sum-compatible format: "<hex_hash>  <filename>\n" (G06).
    /// </summary>
    public static async Task<string> WriteSha256CompanionFileAsync(string targetFilePath, string hash)
    {
        string companionPath = targetFilePath + ".sha256";
        string fileName = Path.GetFileName(targetFilePath);
        string content = $"{hash}  {fileName}\n";
        await File.WriteAllTextAsync(companionPath, content, new UTF8Encoding(false));
        return companionPath;
    }

    /// <summary>
    /// Verifies a companion .sha256 file against the referenced file on disk (G06).
    /// </summary>
    public static async Task<bool> VerifySha256CompanionFileAsync(string companionFilePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(companionFilePath);
        if (!File.Exists(companionFilePath))
            return false;

        string content = await File.ReadAllTextAsync(companionFilePath, ct);
        string[] parts = content.Trim().Split("  ", StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            // Try single space or tab fallback
            parts = content.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return false;
        }

        string expectedHash = parts[0].Trim().ToLowerInvariant();
        string targetFileName = parts[1].Trim();
        string directory = Path.GetDirectoryName(companionFilePath) ?? string.Empty;
        string targetFilePath = Path.Combine(directory, targetFileName);

        if (!File.Exists(targetFilePath))
            return false;

        string actualHash = await ComputeFileHashAsync(targetFilePath, null, ct);
        return string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase);
    }
}
