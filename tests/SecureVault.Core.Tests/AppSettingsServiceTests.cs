using SecureVault.Core.IO;
using Xunit;

namespace SecureVault.Core.Tests;

public class AppSettingsServiceTests : IDisposable
{
    private readonly string _testDir;

    public AppSettingsServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "SecureVault_SettingsTests_" + Guid.NewGuid().ToString("N"));
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
    public void InitialState_WhenNoConfigFile_HasExpectedDefaults()
    {
        var settings = new AppSettingsService(_testDir);

        Assert.Null(settings.LastVaultPath);
        Assert.False(settings.HasCompletedFirstRun);
        Assert.Empty(settings.RecentVaults);
    }

    [Fact]
    public void SettingLastVault_PersistsAndLoadsAcrossInstances()
    {
        var settings1 = new AppSettingsService(_testDir);
        string vaultPath = Path.Combine(_testDir, "TestVault.vault");

        settings1.LastVaultPath = vaultPath;

        Assert.True(settings1.HasCompletedFirstRun);
        Assert.Single(settings1.RecentVaults);
        Assert.Equal(vaultPath, settings1.RecentVaults[0]);

        // Load fresh instance from same directory
        var settings2 = new AppSettingsService(_testDir);

        Assert.Equal(vaultPath, settings2.LastVaultPath);
        Assert.True(settings2.HasCompletedFirstRun);
        Assert.Single(settings2.RecentVaults);
        Assert.Equal(vaultPath, settings2.RecentVaults[0]);
    }

    [Fact]
    public void RecentVaults_EnforcesFIFOAndDeduplication()
    {
        var settings = new AppSettingsService(_testDir);

        for (int i = 1; i <= 15; i++)
        {
            settings.AddRecentVault(Path.Combine(_testDir, $"Vault{i}.vault"));
        }

        // Must enforce maximum of 10 items
        Assert.Equal(10, settings.RecentVaults.Count);

        // Most recent should be Vault15
        Assert.Equal(Path.Combine(_testDir, "Vault15.vault"), settings.RecentVaults[0]);

        // Re-adding an existing path moves it to the front without duplicating
        settings.AddRecentVault(Path.Combine(_testDir, "Vault10.vault"));
        Assert.Equal(10, settings.RecentVaults.Count);
        Assert.Equal(Path.Combine(_testDir, "Vault10.vault"), settings.RecentVaults[0]);
    }

    [Fact]
    public void RemoveRecentVault_RemovesEntryAndUpdatesLastVaultIfMatched()
    {
        var settings = new AppSettingsService(_testDir);
        string vault1 = Path.Combine(_testDir, "Vault1.vault");
        string vault2 = Path.Combine(_testDir, "Vault2.vault");

        settings.AddRecentVault(vault1);
        settings.AddRecentVault(vault2);
        settings.LastVaultPath = vault2;

        settings.RemoveRecentVault(vault2);

        Assert.DoesNotContain(vault2, settings.RecentVaults);
        Assert.Equal(vault1, settings.LastVaultPath);
    }

    [Fact]
    public void CorruptedConfigFile_RecoversGracefullyWithoutCrashing()
    {
        string configFile = Path.Combine(_testDir, "config.json");
        File.WriteAllText(configFile, "{ invalid json corrupt content !!! }");

        var settings = new AppSettingsService(_testDir);

        Assert.Null(settings.LastVaultPath);
        Assert.False(settings.HasCompletedFirstRun);
        Assert.Empty(settings.RecentVaults);
    }
}
