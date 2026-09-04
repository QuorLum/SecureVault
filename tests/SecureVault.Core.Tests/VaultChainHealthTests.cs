using System.Text;
using SecureVault.Core;
using SecureVault.Core.Exceptions;
using SecureVault.Core.Format;
using SecureVault.Core.MultiVault;
using Xunit;

namespace SecureVault.Core.Tests;

public class VaultChainHealthTests : IDisposable
{
    private readonly string _testDir;

    public VaultChainHealthTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "SV_HealthTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, true); } catch { }
    }

    [Fact]
    public async Task GracefulDegradation_MissingVaultPart_PermitsReadingAvailableFilesAndReportsMissingPart()
    {
        string masterVaultPath = Path.Combine(_testDir, "health_test.vault");
        var (vault, _) = await VaultManager.CreateAsync(masterVaultPath, "PassHealth123!");

        byte[] content0 = Encoding.UTF8.GetBytes("Part 0 file content");
        byte[] content1 = Encoding.UTF8.GetBytes("Part 1 file content residing in secondary vault");
        Guid guid1;

        using (var chain = new VaultChainManager(vault, maxPartSizeBytes: 1024 * 1024))
        {
            // Add file to Part 0
            using (var ms0 = new MemoryStream(content0))
            {
                await chain.AddFileAsync(ms0, "file0.txt");
            }

            // Allocate Part 1 and add file
            chain.AllocateNextPart();
            using (var ms1 = new MemoryStream(content1))
            {
                var entry1 = await chain.AddFileAsync(ms1, "file1.txt");
                guid1 = entry1.FileGuid;
            }
        }
        vault.Lock();

        string part1Path = VaultChainManager.GetPartPath(masterVaultPath, 1);
        string part1BackupPath = part1Path + ".hidden";
        Assert.True(File.Exists(part1Path));

        // Simulate missing secondary vault part (e.g. unplugged external drive)
        File.Move(part1Path, part1BackupPath);

        // Re-open master vault — master vault MUST unlock cleanly without crashing (O08, O09)
        var reopenedVault = await VaultManager.OpenAsync(masterVaultPath, "PassHealth123!");
        using (var chain2 = new VaultChainManager(reopenedVault))
        {
            var healthReport = VaultChainHealth.CheckHealth(chain2);
            Assert.False(healthReport.IsHealthy);
            Assert.Equal(1, healthReport.MissingPartsCount);
            Assert.Contains(Path.GetFileName(part1Path), healthReport.MissingPartFileNames);

            var availableFiles = VaultChainHealth.GetAvailableFiles(chain2);
            var unavailableFiles = VaultChainHealth.GetUnavailableFiles(chain2);

            Assert.Single(availableFiles);
            Assert.Equal("file0.txt", availableFiles[0].FileName);
            Assert.Single(unavailableFiles);
            Assert.Equal("file1.txt", unavailableFiles[0].FileName);

            // Reading file in Part 0 succeeds
            byte[] read0 = await chain2.ReadAllBytesAsync(availableFiles[0]);
            Assert.Equal(content0, read0);

            // Reading file in missing Part 1 throws VaultPartMissingException
            var ex = await Assert.ThrowsAsync<VaultPartMissingException>(() =>
                chain2.ReadAllBytesAsync(unavailableFiles[0]));

            Assert.Equal(1, ex.PartIndex);
            Assert.Equal(Path.GetFileName(part1Path), ex.ExpectedFileName);

            // Re-attach part 1 (simulating drive plugged back in)
            File.Move(part1BackupPath, part1Path);
        }
        reopenedVault.Lock();

        // Re-open chain with re-attached part 1
        var reattachedVault = await VaultManager.OpenAsync(masterVaultPath, "PassHealth123!");
        using (var chain3 = new VaultChainManager(reattachedVault))
        {
            var healthReport = VaultChainHealth.CheckHealth(chain3);
            Assert.True(healthReport.IsHealthy);
            Assert.Equal(0, healthReport.MissingPartsCount);

            var entry1 = chain3.GlobalFiles.First(f => f.FileGuid == guid1);
            Assert.True(entry1.IsAvailable);
            byte[] read1 = await chain3.ReadAllBytesAsync(entry1);
            Assert.Equal(content1, read1);
        }
        reattachedVault.Lock();
    }
}
