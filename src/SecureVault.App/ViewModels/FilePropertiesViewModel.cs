using CommunityToolkit.Mvvm.ComponentModel;
using SecureVault.Core.Format;
using SecureVault.Core.MultiVault;

namespace SecureVault.App.ViewModels;

public sealed record ChunkDisplayItem
{
    public uint Sequence { get; init; }
    public string OffsetHex { get; init; } = string.Empty;
    public string SizeFormatted { get; init; } = string.Empty;
    public string Crc32Hex { get; init; } = string.Empty;
    public string AuthTagHex { get; init; } = string.Empty;
    public string ParityFormatted { get; init; } = string.Empty;
}

/// <summary>
/// ViewModel presenting cryptographic and structural metadata for an individual file (N13, C22).
/// </summary>
public partial class FilePropertiesViewModel : ObservableObject
{
    public IndexEntry Entry { get; }

    public string FileName => Entry.FileName;
    public string VirtualPath => Entry.VirtualFolderPath;
    public string ProtectionModeText => Entry.ProtectionMode == ProtectionMode.SecureMode ? "Secure Mode (AES-256-GCM + RS)" : "Fast Mode (Keystream XOR + RS)";
    public string FormattedOriginalSize => FormatBytes(Entry.OriginalSize);
    public string FormattedCompressedSize => FormatBytes(Entry.CompressedSize);
    public string PlaintextSha256Hex => Convert.ToHexString(Entry.PlaintextSHA256);
    public string FileGuidString => Entry.FileGuid.ToString("D");
    public string DateAddedText => new DateTime(Entry.DateAddedTicks, DateTimeKind.Utc).ToLocalTime().ToString("F");
    public string DateModifiedText => new DateTime(Entry.DateModifiedTicks, DateTimeKind.Utc).ToLocalTime().ToString("F");

    public int PartIndex => Entry.PartIndex;
    public string PartLocation => Entry.PartIndex == 0 ? "Master Container (.vault)" : $"Secondary Part {Entry.PartIndex} (.vault{Entry.PartIndex + 1})";

    public uint ChunkCount => (uint)Entry.Chunks.Count;
    public string FirstChunkOffsetHex => $"0x{Entry.FirstChunkOffset:X8}";

    public IReadOnlyList<ChunkDisplayItem> Chunks { get; }

    public FilePropertiesViewModel(IndexEntry entry)
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));

        Chunks = entry.Chunks.Select(c => new ChunkDisplayItem
        {
            Sequence = c.ChunkSequence,
            OffsetHex = $"0x{c.AbsoluteOffset:X8}",
            SizeFormatted = FormatBytes(c.ChunkDataLength),
            Crc32Hex = $"0x{c.CRC32:X8}",
            AuthTagHex = c.AuthTag.Length > 0 ? Convert.ToHexString(c.AuthTag)[..16] + "..." : "N/A",
            ParityFormatted = $"{c.RSParityLength} bytes"
        }).ToList();
    }

    private static string FormatBytes(ulong bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int i = 0;
        double d = bytes;
        while (d >= 1024 && i < suffixes.Length - 1)
        {
            d /= 1024;
            i++;
        }
        return $"{d:0.##} {suffixes[i]}";
    }
}
