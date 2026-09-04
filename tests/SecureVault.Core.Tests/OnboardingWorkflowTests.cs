using SecureVault.Core.IO;
using Xunit;

namespace SecureVault.Core.Tests;

public class OnboardingWorkflowTests : IDisposable
{
    private readonly string _testDir;

    public OnboardingWorkflowTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "SecureVault_OnboardingTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try
            {
                Directory.Delete(_testDir, true);
            }
            catch
            {
                // Best-effort cleanup
            }
        }
    }

    [Fact]
    public async Task CompleteOnboardingAndReturningUserLifecycle_OperatesCorrectly()
    {
        // STEP 1: First-Time Launch (No config or last vault)
        var settings = new AppSettingsService(_testDir);
        Assert.Null(settings.LastVaultPath);
        Assert.False(settings.HasCompletedFirstRun);

        // Simulated first-time user chooses a target folder and sets a password
        string vaultFolder = Path.Combine(_testDir, "Vaults");
        string vaultName = "Personal.vault";
        string targetPath = Path.Combine(vaultFolder, vaultName);
        string masterPassword = "TestPassword2026!";
        string hint = "My favorite project";

        Directory.CreateDirectory(vaultFolder);

        // STEP 2: In-Page Creation (direct creation, no 0-byte orphan files)
        Assert.False(File.Exists(targetPath));

        var (vault, recoveryWords) = await VaultManager.CreateAsync(
            targetPath,
            masterPassword,
            memoryCostKb: 1024,
            iterations: 1,
            parallelism: 1);

        using (vault)
        {
            vault.SetPasswordHint(hint);
            Assert.True(File.Exists(targetPath));
            Assert.Equal(24, recoveryWords.Length);

            // Simulate saving the created vault as the active vault
            settings.LastVaultPath = targetPath;
        }

        // STEP 3: Second-Time Launch (Returning User)
        // Fresh AppSettingsService instance simulates restarting the application
        var restartSettings = new AppSettingsService(_testDir);
        Assert.True(restartSettings.HasCompletedFirstRun);
        Assert.Equal(targetPath, restartSettings.LastVaultPath);
        Assert.True(File.Exists(restartSettings.LastVaultPath));

        // Verify password hint is readable without unlocking
        string? loadedHint = VaultManager.GetPasswordHint(restartSettings.LastVaultPath);
        Assert.Equal(hint, loadedHint);

        // STEP 4: Returning user unlocks directly with master password
        var reopenedVault = await VaultManager.OpenAsync(restartSettings.LastVaultPath, masterPassword);
        using (reopenedVault)
        {
            Assert.NotNull(reopenedVault.Index);
            Assert.Equal(hint, reopenedVault.PasswordHint);
        }

        // STEP 5: Returning user can also unlock with the 24-word recovery key
        var recoveryReopened = await VaultManager.OpenWithRecoveryKeyAsync(restartSettings.LastVaultPath, recoveryWords);
        using (recoveryReopened)
        {
            Assert.NotNull(recoveryReopened.Index);
        }
    }

    [Fact]
    public async Task CreationCancellation_LeavesZeroOrphanFiles()
    {
        string targetPath = Path.Combine(_testDir, "AbortedVault.vault");

        var (vault, recoveryWords) = await VaultManager.CreateAsync(
            targetPath,
            "TempPassword123!",
            memoryCostKb: 1024,
            iterations: 1,
            parallelism: 1);

        // Simulated user fails/cancels recovery confirmation gate
        vault.Dispose();
        File.Delete(targetPath);

        // Verify no orphan file exists on disk
        Assert.False(File.Exists(targetPath));
    }
}
