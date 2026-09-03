using System.Buffers.Binary;
using SecureVault.Core.Exceptions;

namespace SecureVault.Core.Format;

public sealed class BlockFooter
{
    public const int Size = 52; // 0x0034 bytes
    public const uint ExpectedMagic = 0x424C4B46; // "BLKF"

    public uint FooterMagic { get; set; } = ExpectedMagic;
    public Guid FileGuid { get; set; }
    public byte[] BlockSHA256 { get; set; } = new byte[32];

    public void WriteTo(Stream stream)
    {
        byte[] buffer = new byte[Size];
        Span<byte> span = buffer;

        BinaryPrimitives.WriteUInt32LittleEndian(span[0x0000..0x0004], FooterMagic);
        FileGuid.ToByteArray().CopyTo(span[0x0004..0x0014]);
        BlockSHA256.CopyTo(span[0x0014..0x0034]);

        stream.Write(buffer);
    }

    public static BlockFooter ReadFrom(Stream stream)
    {
        byte[] buffer = new byte[Size];
        int read = stream.ReadAtLeast(buffer, Size, throwOnEndOfStream: false);
        if (read < Size)
        {
            throw new CorruptedVaultException($"Block footer truncated (expected {Size} bytes, got {read}).");
        }

        ReadOnlySpan<byte> span = buffer;
        var footer = new BlockFooter
        {
            FooterMagic = BinaryPrimitives.ReadUInt32LittleEndian(span[0x0000..0x0004]),
            FileGuid = new Guid(span[0x0004..0x0014]),
            BlockSHA256 = span[0x0014..0x0034].ToArray()
        };

        if (footer.FooterMagic != ExpectedMagic)
        {
            throw new CorruptedVaultException("Block footer magic mismatch.");
        }

        return footer;
    }
}
