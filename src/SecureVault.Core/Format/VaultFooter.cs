using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using SecureVault.Core.Crypto;
using SecureVault.Core.Exceptions;

namespace SecureVault.Core.Format;

public sealed class VaultFooter
{
    public const int FooterSize = 76; // 0x004C bytes
    private const string FooterHmacInfo = "SecureVault-FooterHMAC-v1";

    public uint FooterMagic { get; set; } = 0x53564654; // "SVFT"
    public ulong PrimaryIndexOffset { get; set; }
    public ulong PrimaryIndexLength { get; set; }
    public ulong BackupIndexOffset { get; set; }
    public ulong BackupIndexLength { get; set; }
    public ulong VaultDataSize { get; set; }
    public byte[] FooterHMAC { get; set; } = new byte[32];

    public byte[] SerializeWithoutHmac()
    {
        byte[] buffer = new byte[0x002C]; // 44 bytes
        Span<byte> span = buffer;

        BinaryPrimitives.WriteUInt32LittleEndian(span[0x0000..0x0004], FooterMagic);
        BinaryPrimitives.WriteUInt64LittleEndian(span[0x0004..0x000C], PrimaryIndexOffset);
        BinaryPrimitives.WriteUInt64LittleEndian(span[0x000C..0x0014], PrimaryIndexLength);
        BinaryPrimitives.WriteUInt64LittleEndian(span[0x0014..0x001C], BackupIndexOffset);
        BinaryPrimitives.WriteUInt64LittleEndian(span[0x001C..0x0024], BackupIndexLength);
        BinaryPrimitives.WriteUInt64LittleEndian(span[0x0024..0x002C], VaultDataSize);

        return buffer;
    }

    public void UpdateHmac(SecureBuffer masterKey)
    {
        using var hmacKey = KeyDerivation.DeriveSubkey(masterKey, FooterHmacInfo, 32);
        byte[] serialized = SerializeWithoutHmac();
        using var hmac = new HMACSHA256(hmacKey.AsReadOnlySpan().ToArray());
        FooterHMAC = hmac.ComputeHash(serialized);
    }

    public bool VerifyHmac(SecureBuffer masterKey)
    {
        using var hmacKey = KeyDerivation.DeriveSubkey(masterKey, FooterHmacInfo, 32);
        byte[] serialized = SerializeWithoutHmac();
        using var hmac = new HMACSHA256(hmacKey.AsReadOnlySpan().ToArray());
        byte[] computed = hmac.ComputeHash(serialized);
        return CryptographicOperations.FixedTimeEquals(FooterHMAC, computed);
    }

    public void WriteTo(Stream stream)
    {
        byte[] fullFooter = new byte[FooterSize];
        byte[] preHmac = SerializeWithoutHmac();
        preHmac.CopyTo(fullFooter, 0);
        FooterHMAC.CopyTo(fullFooter, 0x002C);

        stream.Write(fullFooter);
    }

    public static VaultFooter ReadFrom(Stream stream)
    {
        byte[] buffer = new byte[FooterSize];
        int read = stream.ReadAtLeast(buffer, FooterSize, throwOnEndOfStream: false);
        if (read < FooterSize)
        {
            throw new CorruptedVaultException($"Vault footer is truncated (expected {FooterSize} bytes, got {read}).");
        }

        ReadOnlySpan<byte> span = buffer;
        var footer = new VaultFooter
        {
            FooterMagic = BinaryPrimitives.ReadUInt32LittleEndian(span[0x0000..0x0004]),
            PrimaryIndexOffset = BinaryPrimitives.ReadUInt64LittleEndian(span[0x0004..0x000C]),
            PrimaryIndexLength = BinaryPrimitives.ReadUInt64LittleEndian(span[0x000C..0x0014]),
            BackupIndexOffset = BinaryPrimitives.ReadUInt64LittleEndian(span[0x0014..0x001C]),
            BackupIndexLength = BinaryPrimitives.ReadUInt64LittleEndian(span[0x001C..0x0024]),
            VaultDataSize = BinaryPrimitives.ReadUInt64LittleEndian(span[0x0024..0x002C]),
            FooterHMAC = span[0x002C..0x004C].ToArray()
        };

        if (footer.FooterMagic != 0x53564654)
        {
            throw new CorruptedVaultException("Vault footer magic mismatch.");
        }

        return footer;
    }
}
