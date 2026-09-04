using System.Security.Cryptography;
using System.Text;
using SecureVault.Core.Format;
using SecureVault.Core.Integrity;

namespace SecureVault.Core.Tests;

public class IntegrityCheckerTests : IDisposable
{
    private readonly string _tempVaultPath;
    private VaultManager? _vault;

    public IntegrityCheckerTests()
    {
        _tempVaultPath = Path.Combine(Path.GetTempPath(), $"integrity_test_{Guid.NewGuid():N}.vault");
    }

    [Fact]
    public async Task CheckVaultAsync_Returns100PercentOnHealthyVault()
    {
        var (vault, _) = await VaultManager.CreateAsync(
            _tempVaultPath,
            "Password123!",
            memoryCostKb: 65536,
            iterations: 2,
            parallelism: 2);
        _vault = vault;

        byte[] payload1 = Encoding.UTF8.GetBytes("Healthy File 1 Payload " + new string('H', 1000));
        byte[] payload2 = Encoding.UTF8.GetBytes("Healthy File 2 Payload " + new string('H', 2000));

        using (var ms1 = new MemoryStream(payload1)) await vault.AddFileAsync(ms1, "healthy1.txt");
        using (var ms2 = new MemoryStream(payload2)) await vault.AddFileAsync(ms2, "healthy2.txt");

        var report = await IntegrityChecker.CheckVaultAsync(vault);

        Assert.Equal(100.0, report.OverallHealthScore);
        Assert.Equal(2, report.TotalFilesChecked);
        Assert.Equal(2, report.HealthyFilesCount);
        Assert.Equal(0, report.CorruptedFilesCount);
        Assert.Empty(report.Issues);
    }

    [Fact]
    public async Task CheckVaultAsync_DetectsTamperedChunkAndDecreasesScore()
    {
        var (vault, _) = await VaultManager.CreateAsync(
            _tempVaultPath,
            "Password123!",
            memoryCostKb: 65536,
            iterations: 2,
            parallelism: 2);
        _vault = vault;

        byte[] payload = Encoding.UTF8.GetBytes("Tamper Target File Payload " + new string('T', 4000));
        using (var ms = new MemoryStream(payload)) await vault.AddFileAsync(ms, "target.txt");

        var file = vault.Files.First();
        ulong chunkOffset = file.Chunks[0].AbsoluteOffset;

        // Tamper 50 bytes of payload in the file stream directly (exceeding RS correctable threshold)
        vault.Stream.Seek((long)chunkOffset + VaultConstants.ChunkHeaderSize + 10, SeekOrigin.Begin);
        byte[] badBytes = new byte[50];
        Array.Fill<byte>(badBytes, 0xFF);
        vault.Stream.Write(badBytes);
        vault.Stream.Flush(flushToDisk: true);

        var report = await IntegrityChecker.CheckVaultAsync(vault);

        Assert.Equal(1, report.CorruptedFilesCount);
        Assert.Equal(0, report.HealthyFilesCount);
        Assert.True(report.OverallHealthScore < 100.0);
        Assert.NotEmpty(report.Issues);
    }

    public void Dispose()
    {
        _vault?.Dispose();
        if (File.Exists(_tempVaultPath))
        {
            try { File.Delete(_tempVaultPath); } catch { }
        }
    }
}
