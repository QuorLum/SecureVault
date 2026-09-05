using System.Text;
using SecureVault.Core;
using SecureVault.Core.Backup;
using SecureVault.Core.Format;
using Xunit;

namespace SecureVault.Core.Tests;

public class BackupVerifierTests : IDisposable
{
    private readonly string _testDir;

    public BackupVerifierTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "SV_VerifierTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, true); } catch { }
    }

    [Fact]
    public async Task VerifyBackupAsync_VerifiesManifestBackupWithoutPassword()
    {
        string vaultPath = Path.Combine(_testDir, "verifier.vault");
        string splitFolder = Path.Combine(_testDir, "splits");

        var (vault, _) = await VaultManager.CreateAsync(vaultPath, "Password99!");
        byte[] data = Encoding.UTF8.GetBytes("Data to verify offline without password");
        using (var ms = new MemoryStream(data))
        {
            await vault.AddFileAsync(ms, "test.txt");
        }
        vault.Lock();

        await SplitBackupService.BackupSplitChainAsync(vaultPath, splitFolder, 50 * 1024);
        string manifestPath = Path.Combine(splitFolder, "verifier.backup.manifest");

        var report = await BackupVerifier.VerifyBackupAsync(manifestPath);

        Assert.True(report.IsHealthy);
        Assert.True(report.IsComplete);
        Assert.Equal(VaultConstants.CurrentFormatVersion, report.FormatVersion);
        Assert.NotEqual(Guid.Empty, report.VaultUUID);
        Assert.Empty(report.Issues);
    }

    [Fact]
    public async Task VerifyBackupAsync_VerifiesSingleFileWithCompanionSha256()
    {
        string vaultPath = Path.Combine(_testDir, "single_v.vault");
        string backupPath = Path.Combine(_testDir, "single_v_backup.vault");

        var (vault, _) = await VaultManager.CreateAsync(vaultPath, "Password!");
        vault.Lock();

        await BackupService.BackupSingleFileAsync(vaultPath, backupPath);

        var report = await BackupVerifier.VerifyBackupAsync(backupPath);

        Assert.True(report.IsHealthy);
        Assert.True(report.IsComplete);
        Assert.Empty(report.Issues);
    }
}
