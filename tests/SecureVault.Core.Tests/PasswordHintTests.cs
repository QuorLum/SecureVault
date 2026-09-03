using SecureVault.Core.Format;
using Xunit;

namespace SecureVault.Core.Tests;

public class PasswordHintTests : IDisposable
{
    private readonly string _testVaultPath;

    public PasswordHintTests()
    {
        _testVaultPath = Path.Combine(Path.GetTempPath(), $"securevault_hint_{Guid.NewGuid():N}.vault");
    }

    [Fact]
    public async Task GetPasswordHint_And_SetPasswordHint_OperateCorrectly()
    {
        var (vault, _) = await VaultManager.CreateAsync(
            _testVaultPath,
            "MasterPassword123!",
            memoryCostKb: 1024,
            iterations: 1,
            parallelism: 1);

        using (vault)
        {
            Assert.Null(vault.PasswordHint);

            vault.SetPasswordHint("First pet's name");
            Assert.Equal("First pet's name", vault.PasswordHint);
        }

        // Read hint on closed vault without password (A05)
        string? readHint = VaultManager.GetPasswordHint(_testVaultPath);
        Assert.Equal("First pet's name", readHint);

        // Re-open and verify HMAC is valid after hint update
        var reopened = await VaultManager.OpenAsync(_testVaultPath, "MasterPassword123!");
        using (reopened)
        {
            Assert.Equal("First pet's name", reopened.PasswordHint);

            // Clearing hint
            reopened.SetPasswordHint(null);
            Assert.Null(reopened.PasswordHint);
        }

        Assert.Null(VaultManager.GetPasswordHint(_testVaultPath));
    }

    [Fact]
    public void PasswordHint_Exceeding255Bytes_ThrowsArgumentException()
    {
        var header = new VaultHeader();
        string longHint = new string('A', 256);

        Assert.Throws<ArgumentException>(() => header.PasswordHint = longHint);
    }

    public void Dispose()
    {
        if (File.Exists(_testVaultPath))
        {
            try { File.Delete(_testVaultPath); } catch { }
        }
    }
}
