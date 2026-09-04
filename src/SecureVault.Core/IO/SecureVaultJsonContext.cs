using System.Text.Json.Serialization;
using SecureVault.Core.Backup;
using SecureVault.Core.MultiVault;
using SecureVault.Core.Notes;

namespace SecureVault.Core.IO;

/// <summary>
/// High-performance compile-time trim-safe JSON source generator context.
/// Eliminates reflection-based serialization failures across all deployment modes.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(SettingsData))]
[JsonSerializable(typeof(NoteDocument))]
[JsonSerializable(typeof(NoteFormat))]
[JsonSerializable(typeof(VaultChainManifest))]
[JsonSerializable(typeof(VaultChainPartInfo))]
[JsonSerializable(typeof(BackupManifest))]
[JsonSerializable(typeof(BackupChainPartEntry))]
[JsonSerializable(typeof(BackupSplitPartEntry))]
[JsonSerializable(typeof(List<string>))]
public partial class SecureVaultJsonContext : JsonSerializerContext
{
}
