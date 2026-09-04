using System.Text.Json;
using System.Text.Json.Serialization;

namespace SecureVault.Core.MultiVault;

public sealed class VaultChainPartInfo
{
    [JsonPropertyName("part_index")]
    public int PartIndex { get; set; }

    [JsonPropertyName("filename")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("size_bytes")]
    public long FileSizeBytes { get; set; }

    [JsonPropertyName("sha256")]
    public string FileSha256 { get; set; } = string.Empty;
}

/// <summary>
/// Live vault chain linking manifest file (<vaultName>.chain.manifest) (O07, B26).
/// Non-sensitive metadata describing the vault chain parts on disk.
/// </summary>
public sealed class VaultChainManifest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    [JsonPropertyName("vault_name")]
    public string VaultName { get; set; } = string.Empty;

    [JsonPropertyName("vault_uuid")]
    public Guid VaultUUID { get; set; }

    [JsonPropertyName("format_version")]
    public int FormatVersion { get; set; } = 1;

    [JsonPropertyName("parts")]
    public List<VaultChainPartInfo> Parts { get; set; } = new();

    [JsonPropertyName("total_files")]
    public int TotalFiles { get; set; }

    [JsonPropertyName("total_size_bytes")]
    public long TotalSizeBytes { get; set; }

    [JsonPropertyName("last_modified_utc")]
    public DateTime LastModifiedUtc { get; set; } = DateTime.UtcNow;

    public static string GetManifestPath(string masterVaultPath)
    {
        string dir = Path.GetDirectoryName(masterVaultPath) ?? string.Empty;
        string name = Path.GetFileNameWithoutExtension(masterVaultPath);
        return Path.Combine(dir, $"{name}.chain.manifest");
    }

    public void SaveToFile(string path)
    {
        string json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(path, json);
    }

    public static VaultChainManifest LoadFromFile(string path)
    {
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<VaultChainManifest>(json, JsonOptions)
               ?? throw new InvalidOperationException($"Failed to deserialize vault chain manifest at '{path}'.");
    }
}
