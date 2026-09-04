using System.Text;
using SecureVault.Core;
using SecureVault.Core.Backup;
using SecureVault.Core.MultiVault;
using Xunit;

namespace SecureVault.Core.Tests;

public class SplitBackupTests : IDisposable
{
    private readonly string _testDir;

    public SplitBackupTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "SV_SplitBackupTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, true); } catch { }
    }

    [Fact]
    public async Task BackupSplitChainAsync_SplitsIntoMultiplePartsWithCorrectNamingAndHashes()
    {
        string vaultPath = Path.Combine(_testDir, "split_test.vault");
        string splitFolder = Path.Combine(_testDir, "splits");

        var (vault, _) = await VaultManager.CreateAsync(vaultPath, "Password123!");
        byte[] payload = new byte[300 * 1024]; // 300 KB
        Array.Fill(payload, (byte)0x42);
        using (var ms = new MemoryStream(payload))
        {
            await vault.AddFileAsync(ms, "large.bin");
        }
        vault.Lock();

        long splitSize = 100 * 1024; // 100 KB splits
        var manifest = await SplitBackupService.BackupSplitChainAsync(vaultPath, splitFolder, splitSize);

        Assert.NotNull(manifest);
        Assert.True(manifest.IsSplit);
        Assert.Equal(splitSize, manifest.SplitSizeBytes);
        Assert.Single(manifest.ChainParts);

        var part0 = manifest.ChainParts[0];
        Assert.True(part0.Splits.Count >= 3);

        foreach (var split in part0.Splits)
        {
            string splitFilePath = Path.Combine(splitFolder, split.FileName);
            Assert.True(File.Exists(splitFilePath));

            // Verify part filename pattern .part001, .part002, etc.
            Assert.Matches(@"\.part\d{3}$", split.FileName);

            // Verify per-part SHA-256
            string actualHash = await HashVerifier.ComputeFileHashAsync(splitFilePath);
            Assert.Equal(split.Sha256, actualHash);
        }

        // Test raw binary concatenation produces original file
        string joinedFile = Path.Combine(_testDir, "joined.vault");
        using (var outFs = new FileStream(joinedFile, FileMode.Create, FileAccess.Write))
        {
            foreach (var split in part0.Splits.OrderBy(s => s.Index))
            {
                byte[] splitBytes = await File.ReadAllBytesAsync(Path.Combine(splitFolder, split.FileName));
                outFs.Write(splitBytes);
            }
        }

        string originalHash = await HashVerifier.ComputeFileHashAsync(vaultPath);
        string joinedHash = await HashVerifier.ComputeFileHashAsync(joinedFile);
        Assert.Equal(originalHash, joinedHash);
    }
}
