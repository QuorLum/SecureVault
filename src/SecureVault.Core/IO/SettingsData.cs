using System.Text.Json.Serialization;

namespace SecureVault.Core.IO;

/// <summary>
/// Serializable model for application settings and recent vaults history.
/// </summary>
public sealed class SettingsData
{
    [JsonPropertyName("LastVaultPath")]
    public string? LastVaultPath { get; set; }

    [JsonPropertyName("RecentVaults")]
    public List<string> RecentVaults { get; set; } = new();

    [JsonPropertyName("HasCompletedFirstRun")]
    public bool HasCompletedFirstRun { get; set; }

    [JsonPropertyName("ScreenProtection")]
    public bool ScreenProtection { get; set; }

    [JsonPropertyName("AutoLockMinutes")]
    public int AutoLockMinutes { get; set; } = 10;

    [JsonPropertyName("LockOnSystemLock")]
    public bool LockOnSystemLock { get; set; } = true;

    [JsonPropertyName("DefaultProtectionMode")]
    public int DefaultProtectionMode { get; set; }
}
