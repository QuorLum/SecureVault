using System.Text.Json;
using System.Text.Json.Serialization;

namespace SecureVault.Core.Backup;

public sealed class BackupSplitPartEntry
{
    [JsonPropertyName("filename")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("offset")]
    public long Offset { get; set; }

    [JsonPropertyName("size_bytes")]
    public long SizeBytes { get; set; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;
}

public sealed class BackupChainPartEntry
{
    [JsonPropertyName("part_index")]
    public int PartIndex { get; set; }

    [JsonPropertyName("vault_filename")]
    public string VaultFileName { get; set; } = string.Empty;

    [JsonPropertyName("vault_file_size_bytes")]
    public long VaultFileSizeBytes { get; set; }

    [JsonPropertyName("vault_file_sha256")]
    public string VaultFileSha256 { get; set; } = string.Empty;

    [JsonPropertyName("splits")]
    public List<BackupSplitPartEntry> Splits { get; set; } = new();
}

/// <summary>
/// Chain-aware backup manifest model (<vaultName>.backup.manifest) (G03, G15).
/// Holds metadata, whole-vault hashes, and per-split hashes for all vault parts in the chain.
/// </summary>
public sealed class BackupManifest
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

    [JsonPropertyName("created_utc")]
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("total_size_bytes")]
    public long TotalSizeBytes { get; set; }

    [JsonPropertyName("is_split")]
    public bool IsSplit { get; set; }

    [JsonPropertyName("split_size_bytes")]
    public long SplitSizeBytes { get; set; }

    [JsonPropertyName("chain_parts")]
    public List<BackupChainPartEntry> ChainParts { get; set; } = new();

    [JsonPropertyName("chain_sha256")]
    public string ChainSha256 { get; set; } = string.Empty;

    public static string GetManifestPath(string destFolder, string vaultName)
    {
        return Path.Combine(destFolder, $"{vaultName}.backup.manifest");
    }

    public void SaveToFile(string path)
    {
        string json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(path, json);
    }

    public static BackupManifest LoadFromFile(string path)
    {
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<BackupManifest>(json, JsonOptions)
               ?? throw new InvalidOperationException($"Failed to deserialize backup manifest at '{path}'.");
    }
}
