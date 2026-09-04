using System.Text;
using SecureVault.Core;
using SecureVault.Core.Backup;
using SecureVault.Core.Exceptions;
using SecureVault.Core.MultiVault;
using Xunit;

namespace SecureVault.Core.Tests;

public class RestoreServiceTests : IDisposable
{
    private readonly string _testDir;

    public RestoreServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "SV_RestoreTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, true); } catch { }
    }

    [Fact]
    public async Task RestoreChainAsync_RestoresSplitBackupAndOpensSuccessfully()
    {
        string vaultPath = Path.Combine(_testDir, "orig.vault");
        string splitFolder = Path.Combine(_testDir, "split_backup");
        string restoreFolder = Path.Combine(_testDir, "restored");

        var (vault, _) = await VaultManager.CreateAsync(vaultPath, "VaultPassword456!");
        byte[] originalContent = Encoding.UTF8.GetBytes("Secret content to restore from split backup parts");
        using (var ms = new MemoryStream(originalContent))
        {
            await vault.AddFileAsync(ms, "secret.txt");
        }
        vault.Lock();

        // Create split backup with 50KB parts
        var manifest = await SplitBackupService.BackupSplitChainAsync(vaultPath, splitFolder, 50 * 1024);
        string manifestPath = Path.Combine(splitFolder, "orig.backup.manifest");

        // Verify pre-restore check
        var checkResult = await RestoreService.CheckPartsAsync(manifestPath);
        Assert.True(checkResult.IsCompleteAndValid);
        Assert.Empty(checkResult.FailedParts);

        // Execute restore
        string restoredMasterPath = await RestoreService.RestoreChainAsync(manifestPath, restoreFolder);
        Assert.True(File.Exists(restoredMasterPath));

        // Unlock restored vault
        var restoredVault = await VaultManager.OpenAsync(restoredMasterPath, "VaultPassword456!");
        var restoredFile = restoredVault.Files.First(f => f.FileName == "secret.txt");
        byte[] restoredBytes = await restoredVault.ReadAllBytesAsync(restoredFile);
        Assert.Equal(originalContent, restoredBytes);
        restoredVault.Lock();
    }

    [Fact]
    public async Task CheckPartsAsync_ReportsMissingSplitPart()
    {
        string vaultPath = Path.Combine(_testDir, "missing_test.vault");
        string splitFolder = Path.Combine(_testDir, "missing_splits");

        var (vault, _) = await VaultManager.CreateAsync(vaultPath, "Password!");
        byte[] data = new byte[150 * 1024];
        using (var ms = new MemoryStream(data))
        {
            await vault.AddFileAsync(ms, "dummy.bin");
        }
        vault.Lock();

        await SplitBackupService.BackupSplitChainAsync(vaultPath, splitFolder, 40 * 1024);
        string manifestPath = Path.Combine(splitFolder, "missing_test.backup.manifest");

        // Delete part 2
        string part2Path = Path.Combine(splitFolder, "missing_test.vault.part002");
        Assert.True(File.Exists(part2Path));
        File.Delete(part2Path);

        // Check parts
        var report = await RestoreService.CheckPartsAsync(manifestPath);
        Assert.False(report.IsCompleteAndValid);
        Assert.Contains(report.FailedParts, f => f.SplitFileName == "missing_test.vault.part002" && f.Reason.Contains("Missing"));

        // Attempting to restore throws IncompleteBackupException
        var ex = await Assert.ThrowsAsync<IncompleteBackupException>(() =>
            RestoreService.RestoreChainAsync(manifestPath, Path.Combine(_testDir, "fail_restore")));

        Assert.Contains(ex.MissingOrCorruptParts, s => s.Contains("missing_test.vault.part002"));
    }

    [Fact]
    public async Task CheckPartsAsync_ReportsCorruptedSplitPartAndRecoversAfterReplacement()
    {
        string vaultPath = Path.Combine(_testDir, "corrupt_test.vault");
        string splitFolder = Path.Combine(_testDir, "corrupt_splits");

        var (vault, _) = await VaultManager.CreateAsync(vaultPath, "Password!");
        byte[] data = new byte[150 * 1024];
        using (var ms = new MemoryStream(data))
        {
            await vault.AddFileAsync(ms, "dummy.bin");
        }
        vault.Lock();

        await SplitBackupService.BackupSplitChainAsync(vaultPath, splitFolder, 40 * 1024);
        string manifestPath = Path.Combine(splitFolder, "corrupt_test.backup.manifest");

        string part2Path = Path.Combine(splitFolder, "corrupt_test.vault.part002");
        byte[] backupCopyOfPart2 = await File.ReadAllBytesAsync(part2Path);

        // Corrupt part 2 by flipping bytes
        byte[] corruptedBytes = (byte[])backupCopyOfPart2.Clone();
        corruptedBytes[10] ^= 0xFF;
        await File.WriteAllBytesAsync(part2Path, corruptedBytes);

        // Check parts should fail with hash mismatch
        var report = await RestoreService.CheckPartsAsync(manifestPath);
        Assert.False(report.IsCompleteAndValid);
        Assert.Contains(report.FailedParts, f => f.SplitFileName == "corrupt_test.vault.part002" && f.Reason.Contains("SHA-256 mismatch"));

        // Replace corrupted part with healthy copy (simulating re-download G09)
        await File.WriteAllBytesAsync(part2Path, backupCopyOfPart2);

        // Re-check parts should now succeed
        var fixedReport = await RestoreService.CheckPartsAsync(manifestPath);
        Assert.True(fixedReport.IsCompleteAndValid);

        // Restore succeeds
        string restoredVaultPath = await RestoreService.RestoreChainAsync(manifestPath, Path.Combine(_testDir, "recovered"));
        Assert.True(File.Exists(restoredVaultPath));
    }
}
