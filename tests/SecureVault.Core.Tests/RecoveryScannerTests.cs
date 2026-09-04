using System.Security.Cryptography;
using System.Text;
using SecureVault.Core.Format;
using SecureVault.Core.Integrity;

namespace SecureVault.Core.Tests;

public class RecoveryScannerTests : IDisposable
{
    private readonly string _tempVaultPath;
    private VaultManager? _vault;

    public RecoveryScannerTests()
    {
        _tempVaultPath = Path.Combine(Path.GetTempPath(), $"recovery_test_{Guid.NewGuid():N}.vault");
    }

    [Fact]
    public async Task ScanAsync_RecoversFilesWhenIndexIsDestroyed_WithTieredConfidence()
    {
        var (vault, _) = await VaultManager.CreateAsync(
            _tempVaultPath,
            "Password123!",
            memoryCostKb: 65536,
            iterations: 2,
            parallelism: 2);
        _vault = vault;

        byte[] secureData = Encoding.UTF8.GetBytes("Critical Secret File Recoverable Payload - " + new string('S', 5000));
        byte[] fastData = Encoding.UTF8.GetBytes("Fast Obfuscated File Recoverable Payload - " + new string('F', 7000));

        using (var ms1 = new MemoryStream(secureData))
            await vault.AddFileAsync(ms1, "secret.txt", "/Docs", ProtectionMode.SecureMode);

        using (var ms2 = new MemoryStream(fastData))
            await vault.AddFileAsync(ms2, "media.bin", "/Media", ProtectionMode.FastObfuscation);

        var masterKey = vault.MasterKey;

        // Destroy the Primary & Backup index by overwriting the header pointers
        vault.Header.PrimaryIndexOffset = 0;
        vault.Header.BackupIndexOffset = 0;
        vault.Header.UpdateHmac(masterKey);

        vault.Stream.Seek(0, SeekOrigin.Begin);
        vault.Header.WriteTo(vault.Stream);
        vault.Stream.Flush(flushToDisk: true);

        // Scan raw container stream using RecoveryScanner
        vault.Stream.Seek(0, SeekOrigin.Begin);
        var recovered = await RecoveryScanner.ScanAsync(vault.Stream, masterKey);

        Assert.Equal(2, recovered.Count);

        var secureRecovered = recovered.First(r => r.ProtectionMode == ProtectionMode.SecureMode);
        Assert.Equal(RecoveryConfidenceLevel.CryptographicallyVerified, secureRecovered.Confidence);
        Assert.Equal((ulong)secureData.Length, secureRecovered.OriginalSize);

        var fastRecovered = recovered.First(r => r.ProtectionMode == ProtectionMode.FastObfuscation);
        Assert.Equal(RecoveryConfidenceLevel.StructuralAndParityVerified, fastRecovered.Confidence);
        Assert.Equal((ulong)fastData.Length, fastRecovered.OriginalSize);
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
