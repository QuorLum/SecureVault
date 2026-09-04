using System.Security.Cryptography;
using System.Text;
using SecureVault.Core.Format;
using SecureVault.Core.Operations;

namespace SecureVault.Core.Tests;

public class FileReplaceOperationTests : IDisposable
{
    private readonly string _tempVaultPath;
    private VaultManager? _vault;

    public FileReplaceOperationTests()
    {
        _tempVaultPath = Path.Combine(Path.GetTempPath(), $"replace_test_{Guid.NewGuid():N}.vault");
    }

    [Fact]
    public async Task ReplaceFileDataAsync_ReplacesContentWithFreshNoncesAndUpdatesSha256()
    {
        var (vault, _) = await VaultManager.CreateAsync(
            _tempVaultPath,
            "Password123!",
            memoryCostKb: 65536,
            iterations: 2,
            parallelism: 2);
        _vault = vault;

        byte[] originalBytes = Encoding.UTF8.GetBytes("Original confidential contents before edit.");
        IndexEntry addedEntry;
        using (var ms = new MemoryStream(originalBytes))
        {
            addedEntry = await vault.AddFileAsync(ms, "confidential.txt", "/Docs", ProtectionMode.SecureMode);
        }

        byte[] initialNonce = addedEntry.Chunks[0].Nonce.ToArray();
        byte[] initialSha = addedEntry.PlaintextSHA256.ToArray();

        // Perform in-place replacement with new larger data
        byte[] replacementBytes = Encoding.UTF8.GetBytes("Newly edited replacement content with sensitive additions: " + new string('Z', 8000));
        var replacer = new FileReplaceOperation(vault);

        using (var repStream = new MemoryStream(replacementBytes))
        {
            var updatedEntry = await replacer.ReplaceFileDataAsync(addedEntry.FileGuid, repStream);

            Assert.Equal((ulong)replacementBytes.Length, updatedEntry.OriginalSize);
            Assert.False(initialSha.SequenceEqual(updatedEntry.PlaintextSHA256));
            Assert.False(initialNonce.SequenceEqual(updatedEntry.Chunks[0].Nonce)); // Fresh random nonce generated
        }

        // Verify decrypted reading from vault matches replacement bytes
        byte[] decrypted = await vault.ReadAllBytesAsync(addedEntry);
        Assert.True(replacementBytes.AsSpan().SequenceEqual(decrypted));

        // Reopen vault to assert index persistence
        vault.Lock();
        _vault = await VaultManager.OpenAsync(_tempVaultPath, "Password123!");

        var reopenedEntry = _vault.Files.First(f => f.FileGuid == addedEntry.FileGuid);
        Assert.Equal((ulong)replacementBytes.Length, reopenedEntry.OriginalSize);
        byte[] reopenedDecrypted = await _vault.ReadAllBytesAsync(reopenedEntry);
        Assert.True(replacementBytes.AsSpan().SequenceEqual(reopenedDecrypted));
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
