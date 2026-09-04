using SecureVault.Core.Exceptions;
using SecureVault.Core.Format;

namespace SecureVault.Core.MultiVault;

public sealed class VaultPartHealthStatus
{
    public int PartIndex { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public bool IsPresent { get; set; }
    public bool IsIntegrityValid { get; set; }
    public string? StatusMessage { get; set; }
}

public sealed class VaultChainHealthReport
{
    public bool IsHealthy { get; set; }
    public int TotalParts { get; set; }
    public int PresentParts { get; set; }
    public int MissingPartsCount { get; set; }
    public int TotalFiles { get; set; }
    public int AvailableFiles { get; set; }
    public int UnavailableFiles { get; set; }
    public long TotalSizeBytes { get; set; }
    public List<VaultPartHealthStatus> PartStatuses { get; set; } = new();
    public List<string> MissingPartFileNames { get; set; } = new();
}

/// <summary>
/// Health monitoring, missing vault detection, graceful degradation, and integrity verification for vault chains (O08-O12, B25).
/// </summary>
public static class VaultChainHealth
{
    /// <summary>
    /// Checks availability of all parts in the chain based on the manifest or disk presence (O08).
    /// </summary>
    public static VaultChainHealthReport CheckHealth(VaultChainManager chain)
    {
        var report = new VaultChainHealthReport
        {
            TotalFiles = chain.GlobalFiles.Count
        };

        string masterPath = chain.MasterVault.VaultPath;
        string manifestPath = VaultChainManifest.GetManifestPath(masterPath);

        List<int> expectedParts = new() { 0 };
        if (File.Exists(manifestPath))
        {
            try
            {
                var manifest = VaultChainManifest.LoadFromFile(manifestPath);
                foreach (var p in manifest.Parts)
                {
                    if (!expectedParts.Contains(p.PartIndex))
                        expectedParts.Add(p.PartIndex);
                }
            }
            catch { }
        }

        // Also check any currently loaded secondary parts
        foreach (var key in chain.SecondaryParts.Keys)
        {
            if (!expectedParts.Contains(key))
                expectedParts.Add(key);
        }

        expectedParts.Sort();
        report.TotalParts = expectedParts.Count;

        foreach (int partIndex in expectedParts)
        {
            string partPath = VaultChainManager.GetPartPath(masterPath, partIndex);
            string partFileName = Path.GetFileName(partPath);

            var status = new VaultPartHealthStatus
            {
                PartIndex = partIndex,
                FileName = partFileName,
                FilePath = partPath,
                IsPresent = File.Exists(partPath)
            };

            if (status.IsPresent)
            {
                report.PresentParts++;
                status.FileSizeBytes = new FileInfo(partPath).Length;
                report.TotalSizeBytes += status.FileSizeBytes;

                // Validate header integrity
                try
                {
                    if (partIndex == 0)
                    {
                        status.IsIntegrityValid = chain.MasterVault.Header.VerifyMagic() &&
                                                  chain.MasterVault.Header.VerifyHmac(chain.MasterVault.MasterKey);
                    }
                    else if (chain.SecondaryParts.TryGetValue(partIndex, out var secondaryPart))
                    {
                        status.IsIntegrityValid = secondaryPart.Header.VerifyMagic() &&
                                                  secondaryPart.Header.VerifyHmac(chain.MasterVault.MasterKey);
                    }
                    else
                    {
                        using var fs = new FileStream(partPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        var secHeader = SecondaryVaultHeader.ReadFrom(fs);
                        status.IsIntegrityValid = secHeader.VerifyMagic() && secHeader.VerifyHmac(chain.MasterVault.MasterKey);
                    }
                    status.StatusMessage = status.IsIntegrityValid ? "Healthy" : "Header verification failed";
                }
                catch (Exception ex)
                {
                    status.IsIntegrityValid = false;
                    status.StatusMessage = ex.Message;
                }
            }
            else
            {
                report.MissingPartsCount++;
                report.MissingPartFileNames.Add(partFileName);
                status.StatusMessage = "File missing from disk";
            }

            report.PartStatuses.Add(status);
        }

        report.AvailableFiles = chain.GlobalFiles.Count(f => f.IsAvailable);
        report.UnavailableFiles = chain.GlobalFiles.Count - report.AvailableFiles;
        report.IsHealthy = report.MissingPartsCount == 0 && report.PartStatuses.All(p => p.IsIntegrityValid);

        return report;
    }

    /// <summary>
    /// Gets all files whose physical chunks are present in accessible vault parts (O09).
    /// </summary>
    public static IReadOnlyList<IndexEntry> GetAvailableFiles(VaultChainManager chain)
    {
        return chain.GlobalFiles.Where(f => f.IsAvailable).ToList();
    }

    /// <summary>
    /// Gets all files whose physical chunks are in missing vault parts (O09).
    /// UI renders these with clear unavailable indicator and part name.
    /// </summary>
    public static IReadOnlyList<IndexEntry> GetUnavailableFiles(VaultChainManager chain)
    {
        return chain.GlobalFiles.Where(f => !f.IsAvailable).ToList();
    }
}
