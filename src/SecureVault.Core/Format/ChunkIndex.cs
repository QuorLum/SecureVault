using MessagePack;

namespace SecureVault.Core.Format;

/// <summary>
/// Represents the index metadata for an individual chunk stored on disk.
/// Contains byte offset, lengths, CRC32, and AEAD authentication material.
/// </summary>
[MessagePackObject]
public sealed record ChunkIndexEntry
{
    [Key(0)]
    public required uint ChunkSequence { get; init; }

    [Key(1)]
    public required ulong AbsoluteOffset { get; set; }

    [Key(2)]
    public required uint ChunkDataLength { get; init; }

    [Key(3)]
    public required uint CRC32 { get; init; }

    [Key(4)]
    public required byte[] Nonce { get; init; }

    [Key(5)]
    public required byte[] AuthTag { get; init; }

    [Key(6)]
    public required uint RSParityLength { get; init; }
}
