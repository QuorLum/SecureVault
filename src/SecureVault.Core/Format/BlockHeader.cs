using System.Buffers.Binary;
using SecureVault.Core.Exceptions;

namespace SecureVault.Core.Format;

public sealed class BlockHeader
{
    public const int Size = 66; // 0x0042 bytes
    public const uint ExpectedMagic = 0x424C4B48; // "BLKH"

    public uint BlockMagic { get; set; } = ExpectedMagic;
    public Guid FileGuid { get; set; }
    public uint ChunkCount { get; set; }
    public ulong OriginalFileSize { get; set; }
    public ProtectionMode ProtectionMode { get; set; } = ProtectionMode.SecureMode;
    public CompressionType CompressionType { get; set; } = CompressionType.None;
    public byte[] PlaintextSHA256 { get; set; } = new byte[32];

    public void WriteTo(Stream stream)
    {
        byte[] buffer = new byte[Size];
        Span<byte> span = buffer;

        BinaryPrimitives.WriteUInt32LittleEndian(span[0x0000..0x0004], BlockMagic);
        FileGuid.ToByteArray().CopyTo(span[0x0004..0x0014]);
        BinaryPrimitives.WriteUInt32LittleEndian(span[0x0014..0x0018], ChunkCount);
        BinaryPrimitives.WriteUInt64LittleEndian(span[0x0018..0x0020], OriginalFileSize);
        span[0x0020] = (byte)ProtectionMode;
        span[0x0021] = (byte)CompressionType;
        PlaintextSHA256.CopyTo(span[0x0022..0x0042]);

        stream.Write(buffer);
    }

    public static BlockHeader ReadFrom(Stream stream)
    {
        byte[] buffer = new byte[Size];
        int read = stream.ReadAtLeast(buffer, Size, throwOnEndOfStream: false);
        if (read < Size)
        {
            throw new CorruptedVaultException($"Block header truncated (expected {Size} bytes, got {read}).");
        }

        ReadOnlySpan<byte> span = buffer;
        var header = new BlockHeader
        {
            BlockMagic = BinaryPrimitives.ReadUInt32LittleEndian(span[0x0000..0x0004]),
            FileGuid = new Guid(span[0x0004..0x0014]),
            ChunkCount = BinaryPrimitives.ReadUInt32LittleEndian(span[0x0014..0x0018]),
            OriginalFileSize = BinaryPrimitives.ReadUInt64LittleEndian(span[0x0018..0x0020]),
            ProtectionMode = (ProtectionMode)span[0x0020],
            CompressionType = (CompressionType)span[0x0021],
            PlaintextSHA256 = span[0x0022..0x0042].ToArray()
        };

        if (header.BlockMagic != ExpectedMagic)
        {
            throw new CorruptedVaultException("Block header magic mismatch.");
        }

        return header;
    }
}
