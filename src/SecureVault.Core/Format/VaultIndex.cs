using MessagePack;
using SecureVault.Core.Crypto;
using SecureVault.Core.Exceptions;

namespace SecureVault.Core.Format;

[MessagePackObject]
public sealed class IndexEntry
{
    [Key(0)]
    public Guid FileGuid { get; set; }

    [Key(1)]
    public string FileName { get; set; } = string.Empty;

    [Key(2)]
    public ulong OriginalSize { get; set; }

    [Key(3)]
    public ulong CompressedSize { get; set; }

    [Key(4)]
    public ProtectionMode ProtectionMode { get; set; } = ProtectionMode.SecureMode;

    [Key(5)]
    public CompressionType CompressionType { get; set; } = CompressionType.None;

    [Key(6)]
    public byte[] PlaintextSHA256 { get; set; } = new byte[32];

    [Key(7)]
    public byte[] FileSalt { get; set; } = new byte[16];

    [Key(8)]
    public long DateAddedTicks { get; set; } = DateTime.UtcNow.Ticks;

    [Key(9)]
    public long DateModifiedTicks { get; set; } = DateTime.UtcNow.Ticks;

    [Key(10)]
    public byte Category { get; set; }

    [Key(11)]
    public bool IsDeleted { get; set; }

    [Key(12)]
    public string VirtualFolderPath { get; set; } = "/";

    [Key(13)]
    public uint ChunkCount { get; set; }

    [Key(14)]
    public ulong FirstChunkOffset { get; set; }

    [Key(15)]
    public List<ChunkIndexEntry> Chunks { get; set; } = new();

    [Key(16)]
    public string[] Tags { get; set; } = Array.Empty<string>();

    [Key(17)]
    public string Notes { get; set; } = string.Empty;

    [Key(18)]
    public bool IsFavorite { get; set; }
}

[MessagePackObject]
public sealed class VaultIndexData
{
    [Key(0)]
    public List<IndexEntry> Entries { get; set; } = new();
}

public sealed class VaultIndex
{
    public List<IndexEntry> Entries { get; } = new();

    public byte[] Serialize()
    {
        var data = new VaultIndexData { Entries = Entries };
        return MessagePackSerializer.Serialize(data);
    }

    public static VaultIndex Deserialize(ReadOnlySpan<byte> bytes)
    {
        var data = MessagePackSerializer.Deserialize<VaultIndexData>(bytes.ToArray());
        var index = new VaultIndex();
        if (data?.Entries != null)
        {
            index.Entries.AddRange(data.Entries);
        }
        return index;
    }

    /// <summary>
    /// Writes the index to disk encrypted and RS-encoded.
    /// Dual write: primary index and backup index.
    /// </summary>
    public (ulong primaryOffset, ulong primaryLength, ulong backupOffset, ulong backupLength) WriteToVault(
        Stream stream,
        EncryptionService encryption,
        ReedSolomonCodec rsCodec)
    {
        byte[] rawBytes = Serialize();
        var (ciphertext, nonce, tag) = encryption.EncryptIndex(rawBytes);

        // Header for index payload on disk: 12-byte nonce + 16-byte tag + 4-byte ciphertext len + ciphertext + RS parity
        byte[] rsParity = rsCodec.Encode(ciphertext);

        byte[] payload = new byte[12 + 16 + 4 + ciphertext.Length + rsParity.Length];
        nonce.CopyTo(payload, 0);
        tag.CopyTo(payload, 12);
        BitConverter.GetBytes(ciphertext.Length).CopyTo(payload, 28);
        ciphertext.CopyTo(payload, 32);
        rsParity.CopyTo(payload, 32 + ciphertext.Length);

        // Primary Index write
        ulong primaryOffset = (ulong)stream.Position;
        stream.Write(payload);
        ulong primaryLength = (ulong)payload.Length;

        // Backup Index write
        ulong backupOffset = (ulong)stream.Position;
        stream.Write(payload);
        ulong backupLength = (ulong)payload.Length;

        return (primaryOffset, primaryLength, backupOffset, backupLength);
    }

    public static VaultIndex ReadFromVault(
        Stream stream,
        EncryptionService encryption,
        ReedSolomonCodec rsCodec,
        VaultHeader header)
    {
        // Try reading Primary Index first
        try
        {
            return ReadIndexBlock(stream, header.PrimaryIndexOffset, header.PrimaryIndexLength, encryption, rsCodec);
        }
        catch
        {
            // If primary index corrupted, fall back to Backup Index
            try
            {
                return ReadIndexBlock(stream, header.BackupIndexOffset, header.BackupIndexLength, encryption, rsCodec);
            }
            catch (Exception ex)
            {
                throw new CorruptedIndexException("Both primary and backup vault indices are corrupted or unreadable.", ex);
            }
        }
    }

    private static VaultIndex ReadIndexBlock(
        Stream stream,
        ulong offset,
        ulong length,
        EncryptionService encryption,
        ReedSolomonCodec rsCodec)
    {
        if (offset == 0 || length < 32)
        {
            throw new CorruptedIndexException("Invalid index offset or length in vault header.");
        }

        stream.Seek((long)offset, SeekOrigin.Begin);
        byte[] payload = new byte[length];
        int read = stream.ReadAtLeast(payload, (int)length, throwOnEndOfStream: false);
        if (read < (int)length)
        {
            throw new CorruptedIndexException("Index block truncated on disk.");
        }

        byte[] nonce = payload[0..12];
        byte[] tag = payload[12..28];
        int cipherLen = BitConverter.ToInt32(payload, 28);

        if (cipherLen <= 0 || 32 + cipherLen > payload.Length)
        {
            throw new CorruptedIndexException("Invalid index ciphertext length header.");
        }

        byte[] ciphertext = payload[32..(32 + cipherLen)];
        byte[] parity = payload[(32 + cipherLen)..];

        byte[] repairedCiphertext = ciphertext;
        if (parity.Length > 0)
        {
            var (repaired, _) = rsCodec.Decode(ciphertext, parity);
            repairedCiphertext = repaired;
        }

        byte[] plaintext = encryption.DecryptIndex(repairedCiphertext, nonce, tag);
        return Deserialize(plaintext);
    }
}
