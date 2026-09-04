using System.Text;
using SecureVault.Core;
using SecureVault.Core.Backup;
using SecureVault.Core.Format;
using SecureVault.Core.MultiVault;
using Xunit;

namespace SecureVault.Core.Tests;

public class BackupServiceTests : IDisposable
{
    private readonly string _testDir;

    public BackupServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "SV_BackupTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, true); } catch { }
    }

    [Fact]
    public async Task BackupSingleFileAsync_CreatesIdenticalCopyAndSha256Companion()
    {
        string vaultPath = Path.Combine(_testDir, "single.vault");
        string backupPath = Path.Combine(_testDir, "backups", "single_backup.vault");

        var (vault, _) = await VaultManager.CreateAsync(vaultPath, "Password123!");
        byte[] sampleData = Encoding.UTF8.GetBytes("Data to be protected in backup test");
        using (var ms = new MemoryStream(sampleData))
        {
            await vault.AddFileAsync(ms, "sample.txt");
        }
        vault.Lock();

        // Perform single-file backup
        string computedHash = await BackupService.BackupSingleFileAsync(vaultPath, backupPath);

        Assert.True(File.Exists(backupPath));
        Assert.Equal(new FileInfo(vaultPath).Length, new FileInfo(backupPath).Length);

        // Verify .sha256 companion file exists and is valid
        string companionPath = backupPath + ".sha256";
        Assert.True(File.Exists(companionPath));
        bool isCompanionValid = await HashVerifier.VerifySha256CompanionFileAsync(companionPath);
        Assert.True(isCompanionValid);

        // Open backup vault directly to verify it functions identically
        var restoredVault = await VaultManager.OpenAsync(backupPath, "Password123!");
        var restoredFile = restoredVault.Files.First(f => f.FileName == "sample.txt");
        byte[] readBytes = await restoredVault.ReadAllBytesAsync(restoredFile);
        Assert.Equal(sampleData, readBytes);
        restoredVault.Lock();
    }

    [Fact]
    public async Task BackupChainAsync_BacksUpAllPartsInChain()
    {
        string masterVaultPath = Path.Combine(_testDir, "chain.vault");
        string backupDestFolder = Path.Combine(_testDir, "chain_backup");

        var (vault, _) = await VaultManager.CreateAsync(masterVaultPath, "Password123!");
        using (var chain = new VaultChainManager(vault, maxPartSizeBytes: 1024 * 1024)) // 1MB threshold
        {
            // Add file 1 to part 0
            byte[] data1 = new byte[600 * 1024]; // 600 KB
            Array.Fill(data1, (byte)0xAA);
            using (var ms1 = new MemoryStream(data1))
            {
                await chain.AddFileAsync(ms1, "file1.bin");
            }

            // Add file 2 (600 KB) -> rolls over to .vault2 (Part 1)
            byte[] data2 = new byte[600 * 1024];
            Array.Fill(data2, (byte)0xBB);
            using (var ms2 = new MemoryStream(data2))
            {
                await chain.AddFileAsync(ms2, "file2.bin");
            }

            Assert.Single(chain.SecondaryParts);
        }
        vault.Lock();

        // Back up the complete chain
        var manifest = await BackupService.BackupChainAsync(masterVaultPath, backupDestFolder);

        Assert.NotNull(manifest);
        Assert.Equal(2, manifest.ChainParts.Count);

        string backedUpMaster = Path.Combine(backupDestFolder, "chain.vault");
        string backedUpPart2 = Path.Combine(backupDestFolder, "chain.vault2");

        Assert.True(File.Exists(backedUpMaster));
        Assert.True(File.Exists(backedUpPart2));
        Assert.True(File.Exists(backedUpMaster + ".sha256"));
        Assert.True(File.Exists(backedUpPart2 + ".sha256"));

        // Verify companion files
        Assert.True(await HashVerifier.VerifySha256CompanionFileAsync(backedUpMaster + ".sha256"));
        Assert.True(await HashVerifier.VerifySha256CompanionFileAsync(backedUpPart2 + ".sha256"));
    }
}
