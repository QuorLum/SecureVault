using System.Text;
using SecureVault.Core.Format;
using SecureVault.Core.Operations;
using Xunit;

namespace SecureVault.Core.Tests;

public class ProtectionModeOperationTests : IDisposable
{
    private readonly string _testVaultPath;

    public ProtectionModeOperationTests()
    {
        _testVaultPath = Path.Combine(Path.GetTempPath(), $"securevault_mode_{Guid.NewGuid():N}.vault");
    }

    [Fact]
    public async Task ChangeProtectionMode_PreservesPlaintextSha256AndPayload()
    {
        var (vault, _) = await VaultManager.CreateAsync(
            _testVaultPath,
            "Password123!",
            memoryCostKb: 1024,
            iterations: 1,
            parallelism: 1);

        using (vault)
        {
            byte[] fileData = Encoding.UTF8.GetBytes("Secret document content for protection mode conversion test.");
            using var sourceStream = new MemoryStream(fileData);

            var entry = await vault.AddFileAsync(sourceStream, "document.txt", "/", ProtectionMode.FastObfuscation);
            Assert.Equal(ProtectionMode.FastObfuscation, entry.ProtectionMode);

            byte[] originalSha = (byte[])entry.PlaintextSHA256.Clone();

            var operation = new ProtectionModeOperation(vault);
            var converted = await operation.ChangeProtectionModeAsync(entry.FileGuid, ProtectionMode.SecureMode);

            Assert.Equal(ProtectionMode.SecureMode, converted.ProtectionMode);
            Assert.Equal(originalSha, converted.PlaintextSHA256);

            // Read back decrypted content and verify
            using var readStream = vault.OpenFileStream(converted);
            using var ms = new MemoryStream();
            await readStream.CopyToAsync(ms);
            Assert.Equal(fileData, ms.ToArray());
        }
    }

    [Fact]
    public async Task EncryptAll_ConvertsAllFastFilesToSecureMode()
    {
        var (vault, _) = await VaultManager.CreateAsync(
            _testVaultPath,
            "Password123!",
            memoryCostKb: 1024,
            iterations: 1,
            parallelism: 1);

        using (vault)
        {
            using var stream1 = new MemoryStream(Encoding.UTF8.GetBytes("File 1"));
            using var stream2 = new MemoryStream(Encoding.UTF8.GetBytes("File 2"));

            await vault.AddFileAsync(stream1, "file1.txt", "/", ProtectionMode.FastObfuscation);
            await vault.AddFileAsync(stream2, "file2.txt", "/", ProtectionMode.FastObfuscation);

            Assert.Equal(2, vault.Files.Count(f => f.ProtectionMode == ProtectionMode.FastObfuscation));

            var op = new ProtectionModeOperation(vault);
            int count = await op.EncryptAllAsync();

            Assert.Equal(2, count);
            Assert.Equal(0, vault.Files.Count(f => f.ProtectionMode == ProtectionMode.FastObfuscation));
            Assert.Equal(2, vault.Files.Count(f => f.ProtectionMode == ProtectionMode.SecureMode));
        }
    }

    public void Dispose()
    {
        if (File.Exists(_testVaultPath))
        {
            try { File.Delete(_testVaultPath); } catch { }
        }
    }
}
