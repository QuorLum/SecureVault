namespace SecureVault.Core.Format;

/// <summary>
/// Handles vault container format upgrades with automatic rollback backup (G13, G14).
/// </summary>
public static class FormatUpgrader
{
    public static bool NeedsUpgrade(VaultHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        return header.FormatVersion < VaultConstants.CurrentFormatVersion;
    }

    public static async Task<string> CreateUpgradeBackupAsync(string vaultPath, ushort currentVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultPath);
        string backupPath = $"{vaultPath}.backup-v{currentVersion}";
        await Task.Run(() => File.Copy(vaultPath, backupPath, overwrite: true));
        return backupPath;
    }
}
