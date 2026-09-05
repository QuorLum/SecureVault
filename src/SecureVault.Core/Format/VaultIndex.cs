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

    [Key(19)]
    public bool IsFolder { get; set; }

    [Key(20)]
    public Guid? ParentFolderGuid { get; set; }

    [Key(21)]
    public int PartIndex { get; set; } = 0;

    [IgnoreMember]
    public bool IsAvailable { get; set; } = true;

    public void ClearAndZero()
    {
        VaultIndex.WipeString(FileName);
        FileName = string.Empty;

        VaultIndex.WipeString(VirtualFolderPath);
        VirtualFolderPath = string.Empty;

        VaultIndex.WipeString(Notes);
        Notes = string.Empty;

        if (Tags != null)
        {
            for (int i = 0; i < Tags.Length; i++)
            {
                VaultIndex.WipeString(Tags[i]);
            }
            Tags = Array.Empty<string>();
        }

        if (PlaintextSHA256 != null)
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(PlaintextSHA256);
        }

        if (FileSalt != null)
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(FileSalt);
        }

        if (Chunks != null)
        {
            foreach (var chunk in Chunks)
            {
                chunk.ClearAndZero();
            }
            Chunks.Clear();
        }
    }
}

[MessagePackObject]
public sealed class VaultIndexData
{
    [Key(0)]
    public List<IndexEntry> Entries { get; set; } = new();

    [Key(1)]
    public ulong IndexVersion { get; set; } = 1;
}

public sealed class VaultIndex
{
    private readonly List<IndexEntry> _entries = new();

    public List<IndexEntry> Entries => _entries;
    public ulong Version { get; set; } = 1;

    public void ClearAndZero()
    {
        foreach (var entry in _entries)
        {
            entry.ClearAndZero();
        }
        _entries.Clear();
        Version = 0;
    }

    internal static void WipeString(string? str)
    {
        if (string.IsNullOrEmpty(str) || object.ReferenceEquals(str, string.Empty)) return;

        var interned = string.IsInterned(str);
        if (interned != null && object.ReferenceEquals(interned, str))
        {
            return;
        }

        unsafe
        {
            fixed (char* ptr = str)
            {
                for (int i = 0; i < str.Length; i++)
                {
                    ptr[i] = '\0';
                }
            }
        }
    }

    public byte[] Serialize()
    {
        using var writer = new ZeroingBufferWriter();
        var data = new VaultIndexData { Entries = Entries, IndexVersion = Version };
        MessagePackSerializer.Serialize(writer, data);
        return writer.WrittenSpan.ToArray();
    }

    public static VaultIndex Deserialize(ReadOnlyMemory<byte> bytes)
    {
        var data = MessagePackSerializer.Deserialize<VaultIndexData>(bytes);
        var index = new VaultIndex();
        if (data != null)
        {
            if (data.Entries != null)
            {
                index.Entries.AddRange(data.Entries);
            }
            index.Version = data.IndexVersion;
        }
        return index;
    }

    /// <summary>
    /// Writes the index to disk encrypted and RS-encoded.
    /// Dual write: primary index and backup index.
    /// </summary>
    public (ulong primaryOffset, uint primaryLength, ulong backupOffset, uint backupLength) WriteToVault(
        Stream stream,
        EncryptionService encryption,
        ReedSolomonCodec rsCodec)
    {
        Version++;
        byte[] ciphertext;
        byte[] nonce;
        byte[] tag;
        using (var writer = new ZeroingBufferWriter())
        {
            var data = new VaultIndexData { Entries = Entries, IndexVersion = Version };
            MessagePackSerializer.Serialize(writer, data);
            (ciphertext, nonce, tag) = encryption.EncryptIndex(writer.WrittenSpan);
        }

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
        uint primaryLength = (uint)payload.Length;

        // Backup Index write
        ulong backupOffset = (ulong)stream.Position;
        stream.Write(payload);
        uint backupLength = (uint)payload.Length;

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
        try
        {
            return Deserialize(plaintext.AsMemory());
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}

internal sealed class ZeroingBufferWriter : System.Buffers.IBufferWriter<byte>, IDisposable
{
    private byte[] _buffer;
    private int _written;

    public ZeroingBufferWriter(int initialCapacity = 4096)
    {
        _buffer = new byte[initialCapacity];
    }

    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _written);
    public int WrittenCount => _written;

    public void Advance(int count)
    {
        _written += count;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer.AsMemory(_written);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer.AsSpan(_written);
    }

    private void EnsureCapacity(int sizeHint)
    {
        int needed = sizeHint <= 0 ? 1024 : sizeHint;
        if (_written + needed > _buffer.Length)
        {
            int newCap = Math.Max(_buffer.Length * 2, _written + needed);
            byte[] newBuf = new byte[newCap];
            Buffer.BlockCopy(_buffer, 0, newBuf, 0, _written);
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(_buffer);
            _buffer = newBuf;
        }
    }

    public void Dispose()
    {
        if (_buffer != null)
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(_buffer);
            _buffer = Array.Empty<byte>();
            _written = 0;
        }
    }
}
