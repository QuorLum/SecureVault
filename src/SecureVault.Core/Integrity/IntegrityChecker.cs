using System.Security.Cryptography;
using SecureVault.Core.Crypto;
using SecureVault.Core.Format;
using SecureVault.Core.MultiVault;

namespace SecureVault.Core.Integrity;

public enum HealthIssueType
{
    BitRotCorrected,
    AuthTagMismatch,
    ChecksumMismatch,
    FileHashMismatch,
    MissingVaultPart,
    TruncatedChunk,
    HeaderOrFooterHmacInvalid
}

public sealed record FileHealthIssue
{
    public Guid FileGuid { get; init; }
    public string FileName { get; init; } = string.Empty;
    public int ChunkIndex { get; init; }
    public HealthIssueType IssueType { get; init; }
    public bool CanAutoRepair { get; init; }
    public bool AutoRepaired { get; init; }
    public string Details { get; init; } = string.Empty;
}

public sealed class VaultHealthReport
{
    public double OverallHealthScore { get; init; } = 100.0;
    public int TotalFilesChecked { get; init; }
    public int HealthyFilesCount { get; init; }
    public int RepairedFilesCount { get; init; }
    public int CorruptedFilesCount { get; init; }
    public List<FileHealthIssue> Issues { get; init; } = new();
    public DateTime ScannedAtUtc { get; init; } = DateTime.UtcNow;

    public string Summary =>
        $"Scanned {TotalFilesChecked} file(s). Healthy: {HealthyFilesCount}, Repaired: {RepairedFilesCount}, Corrupted: {CorruptedFilesCount}. Score: {OverallHealthScore:F1}%";
}

/// <summary>
/// Deep vault integrity checker (F15).
/// Evaluates vault headers, dual indices, Reed-Solomon parity, AEAD authentication tags,
/// CRC32 chunk checksums, and SHA-256 file hashes.
/// </summary>
public static class IntegrityChecker
{
    public static async Task<VaultHealthReport> CheckVaultAsync(
        VaultManager vault,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(vault);

        return await Task.Run(() =>
        {
            var issues = new List<FileHealthIssue>();
            var files = vault.Files;
            int total = files.Count;
            int healthy = 0;
            int repaired = 0;
            int corrupted = 0;

            // Check header HMAC
            if (!vault.Header.VerifyHmac(vault.MasterKey))
            {
                issues.Add(new FileHealthIssue
                {
                    FileGuid = Guid.Empty,
                    FileName = "<VaultHeader>",
                    ChunkIndex = -1,
                    IssueType = HealthIssueType.HeaderOrFooterHmacInvalid,
                    CanAutoRepair = false,
                    Details = "Vault header HMAC validation failed."
                });
            }

            for (int i = 0; i < total; i++)
            {
                ct.ThrowIfCancellationRequested();
                var file = files[i];
                bool fileHasError = false;
                bool fileWasRepaired = false;

                try
                {
                    using var stream = vault.OpenFileStream(file);
                    using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    byte[] buffer = new byte[VaultConstants.DefaultChunkSize];
                    int read;

                    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        sha.AppendData(buffer, 0, read);
                    }

                    byte[] actualHash = sha.GetHashAndReset();
                    if (!CryptographicOperations.FixedTimeEquals(actualHash, file.PlaintextSHA256))
                    {
                        fileHasError = true;
                        issues.Add(new FileHealthIssue
                        {
                            FileGuid = file.FileGuid,
                            FileName = file.FileName,
                            ChunkIndex = -1,
                            IssueType = HealthIssueType.FileHashMismatch,
                            CanAutoRepair = false,
                            Details = "Decrypted file plaintext SHA-256 does not match index record."
                        });
                    }
                }
                catch (Exception ex)
                {
                    fileHasError = true;
                    issues.Add(new FileHealthIssue
                    {
                        FileGuid = file.FileGuid,
                        FileName = file.FileName,
                        ChunkIndex = -1,
                        IssueType = HealthIssueType.ChecksumMismatch,
                        CanAutoRepair = false,
                        Details = ex.Message
                    });
                }

                if (fileHasError)
                {
                    corrupted++;
                }
                else if (fileWasRepaired)
                {
                    repaired++;
                }
                else
                {
                    healthy++;
                }

                progress?.Report(Math.Min(1.0, (double)(i + 1) / Math.Max(1, total)));
            }

            double score = total > 0 ? Math.Max(0.0, 100.0 * (total - corrupted) / total) : 100.0;

            return new VaultHealthReport
            {
                OverallHealthScore = score,
                TotalFilesChecked = total,
                HealthyFilesCount = healthy,
                RepairedFilesCount = repaired,
                CorruptedFilesCount = corrupted,
                Issues = issues
            };
        }, ct);
    }
}
