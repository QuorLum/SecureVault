using SecureVault.Core.Format;
using SecureVault.Core.MultiVault;

namespace SecureVault.Core.Backup;

public sealed class BackupHealthReport
{
    public bool IsHealthy { get; set; }
    public bool IsComplete { get; set; }
    public int FormatVersion { get; set; }
    public Guid VaultUUID { get; set; }
    public int TotalParts { get; set; }
    public long TotalSizeBytes { get; set; }
    public List<string> Issues { get; set; } = new();
}

/// <summary>
/// Pre-flight offline verification of backup integrity and header structure without requiring vault password (G10).
/// </summary>
public static class BackupVerifier
{
    public static async Task<BackupHealthReport> VerifyBackupAsync(string pathOrManifest, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pathOrManifest);
        if (!File.Exists(pathOrManifest))
        {
            return new BackupHealthReport
            {
                IsHealthy = false,
                Issues = new() { $"Backup target '{pathOrManifest}' not found." }
            };
        }

        if (pathOrManifest.EndsWith(".backup.manifest", StringComparison.OrdinalIgnoreCase) ||
            pathOrManifest.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase))
        {
            return await VerifyManifestBackupAsync(pathOrManifest, ct);
        }

        return await VerifySingleFileBackupAsync(pathOrManifest, ct);
    }

    private static async Task<BackupHealthReport> VerifyManifestBackupAsync(string manifestPath, CancellationToken ct)
    {
        var report = new BackupHealthReport();

        try
        {
            var manifest = BackupManifest.LoadFromFile(manifestPath);
            report.VaultUUID = manifest.VaultUUID;
            report.FormatVersion = manifest.FormatVersion;
            report.TotalSizeBytes = manifest.TotalSizeBytes;
            report.TotalParts = manifest.ChainParts.Count;

            var checkResult = await RestoreService.CheckPartsAsync(manifestPath, ct);
            report.IsComplete = checkResult.IsCompleteAndValid;

            if (!checkResult.IsCompleteAndValid)
            {
                foreach (var fail in checkResult.FailedParts)
                {
                    report.Issues.Add(fail.ToString());
                }
            }

            // Check master header without password
            string sourceFolder = Path.GetDirectoryName(manifestPath) ?? string.Empty;
            var masterEntry = manifest.ChainParts.FirstOrDefault(p => p.PartIndex == 0);
            if (masterEntry != null)
            {
                string masterPath = manifest.IsSplit
                    ? Path.Combine(sourceFolder, masterEntry.Splits.FirstOrDefault()?.FileName ?? "")
                    : Path.Combine(sourceFolder, masterEntry.VaultFileName);

                if (File.Exists(masterPath))
                {
                    try
                    {
                        using var fs = new FileStream(masterPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        var header = VaultHeader.ReadFrom(fs);
                        if (!header.VerifyMagic())
                        {
                            report.Issues.Add("Master vault header magic check failed.");
                        }
                    }
                    catch (Exception ex)
                    {
                        report.Issues.Add($"Header read error: {ex.Message}");
                    }
                }
            }

            report.IsHealthy = report.IsComplete && report.Issues.Count == 0;
        }
        catch (Exception ex)
        {
            report.IsHealthy = false;
            report.Issues.Add($"Manifest read failure: {ex.Message}");
        }

        return report;
    }

    private static async Task<BackupHealthReport> VerifySingleFileBackupAsync(string filePath, CancellationToken ct)
    {
        var report = new BackupHealthReport
        {
            TotalParts = 1
        };

        try
        {
            report.TotalSizeBytes = new FileInfo(filePath).Length;

            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var header = VaultHeader.ReadFrom(fs);
                report.FormatVersion = header.FormatVersion;
                report.VaultUUID = header.VaultUUID;
                if (!header.VerifyMagic())
                {
                    report.Issues.Add("Vault magic check failed.");
                }
            }

            string companion = filePath + ".sha256";
            if (File.Exists(companion))
            {
                bool hashMatch = await HashVerifier.VerifySha256CompanionFileAsync(companion, ct);
                if (!hashMatch)
                {
                    report.Issues.Add("Companion .sha256 hash mismatch.");
                }
            }

            report.IsComplete = true;
            report.IsHealthy = report.Issues.Count == 0;
        }
        catch (Exception ex)
        {
            report.IsHealthy = false;
            report.Issues.Add($"Verification error: {ex.Message}");
        }

        return report;
    }
}
