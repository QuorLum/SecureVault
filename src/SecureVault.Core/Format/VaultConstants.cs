using System.Text;

namespace SecureVault.Core.Format;

public enum ProtectionMode : byte
{
    FastObfuscation = 0,
    SecureMode = 1
}

public enum CompressionType : byte
{
    None = 0,
    LZ4 = 1,
    Brotli = 2
}

public static class VaultConstants
{
    public const int HeaderSize = 572; // 0x023C bytes
    public const ushort CurrentFormatVersion = 1;
    public const int DefaultChunkSize = 1024 * 1024; // 1 MB
    public const int ChunkHeaderSize = 39; // 0x0027 bytes
    public const int BlockHeaderSize = 66; // 0x0042 bytes
    public const int BlockFooterSize = 52; // 0x0034 bytes
    public const int RandomPrefixSize = 32;
    public const int MagicSize = 8;
    public const int HmacSize = 32;
    public const long MaxVaultFileSizeBytes = 200L * 1024 * 1024 * 1024; // 200 GB (B23, O01)
    public const int SecondaryHeaderSize = 128; // 0x0080 bytes

    public static readonly byte[] RawMagic = Encoding.ASCII.GetBytes("SVAULT01");
    public static readonly byte[] SecondaryRawMagic = Encoding.ASCII.GetBytes("SVAULT02");
    public static readonly byte[] BlockHeaderMagic = Encoding.ASCII.GetBytes("BLKH");
    public static readonly byte[] BlockFooterMagic = Encoding.ASCII.GetBytes("BLKF");
}
