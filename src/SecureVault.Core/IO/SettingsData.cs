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
}
