using System.Buffers.Binary;
using System.Security.Cryptography;
using SecureVault.Core.Crypto;
using SecureVault.Core.Exceptions;
using SecureVault.Core.Format;

namespace SecureVault.Core.MultiVault;

/// <summary>
/// Minimal 128-byte header for secondary vault files (.vault2, .vault3, etc.) in a vault chain (O04, B24).
/// Reuses the exact same HMAC subkey derivation ("SecureVault-HeaderHMAC-v1") as the primary vault header.
/// </summary>
public sealed class SecondaryVaultHeader
{
    public const int Size = VaultConstants.SecondaryHeaderSize; // 128 bytes
    private const string HeaderHmacInfo = "SecureVault-HeaderHMAC-v1";

    public byte[] RandomPrefix { get; set; } = new byte[32];
    public byte[] MaskedMagic { get; set; } = new byte[8];
    public ushort FormatVersion { get; set; } = VaultConstants.CurrentFormatVersion;
    public Guid MasterVaultUUID { get; set; }
    public int PartIndex { get; set; }
    public ulong LocalIndexOffset { get; set; }
    public ulong LocalIndexLength { get; set; }
    public byte[] Reserved { get; set; } = new byte[18];
    public byte[] HeaderHMAC { get; set; } = new byte[32];

    public static SecondaryVaultHeader Create(SecureBuffer masterKey, Guid masterVaultUuid, int partIndex)
    {
        var header = new SecondaryVaultHeader
        {
            MasterVaultUUID = masterVaultUuid,
            PartIndex = partIndex
        };

        RandomNumberGenerator.Fill(header.RandomPrefix);
        header.MaskedMagic = ComputeMaskedMagic(header.RandomPrefix);
        header.UpdateHmac(masterKey);
        return header;
    }

    public static byte[] ComputeMaskedMagic(ReadOnlySpan<byte> randomPrefix)
    {
        byte[] hash = SHA256.HashData(randomPrefix);
        byte[] masked = new byte[8];
        for (int i = 0; i < 8; i++)
        {
            masked[i] = (byte)(VaultConstants.SecondaryRawMagic[i] ^ hash[i]);
        }
        return masked;
    }

    public bool VerifyMagic()
    {
        byte[] expected = ComputeMaskedMagic(RandomPrefix);
        return CryptographicOperations.FixedTimeEquals(MaskedMagic, expected);
    }

    public void UpdateHmac(SecureBuffer masterKey)
    {
        using var hmacKey = KeyDerivation.DeriveSubkey(masterKey, HeaderHmacInfo, 32);
        byte[] serialized = SerializeWithoutHmac();
        using var hmac = new HMACSHA256(hmacKey.AsReadOnlySpan().ToArray());
        HeaderHMAC = hmac.ComputeHash(serialized);
    }

    public bool VerifyHmac(SecureBuffer masterKey)
    {
        using var hmacKey = KeyDerivation.DeriveSubkey(masterKey, HeaderHmacInfo, 32);
        byte[] serialized = SerializeWithoutHmac();
        using var hmac = new HMACSHA256(hmacKey.AsReadOnlySpan().ToArray());
        byte[] computed = hmac.ComputeHash(serialized);
        return CryptographicOperations.FixedTimeEquals(HeaderHMAC, computed);
    }

    private byte[] SerializeWithoutHmac()
    {
        byte[] buffer = new byte[0x0060]; // 96 bytes
        Span<byte> span = buffer;

        RandomPrefix.CopyTo(span[0x0000..0x0020]);
        MaskedMagic.CopyTo(span[0x0020..0x0028]);
        BinaryPrimitives.WriteUInt16LittleEndian(span[0x0028..0x002A], FormatVersion);
        MasterVaultUUID.ToByteArray().CopyTo(span[0x002A..0x003A]);
        BinaryPrimitives.WriteInt32LittleEndian(span[0x003A..0x003E], PartIndex);
        BinaryPrimitives.WriteUInt64LittleEndian(span[0x003E..0x0046], LocalIndexOffset);
        BinaryPrimitives.WriteUInt64LittleEndian(span[0x0046..0x004E], LocalIndexLength);
        Reserved.CopyTo(span[0x004E..0x0060]);

        return buffer;
    }

    public void WriteTo(Stream stream)
    {
        byte[] fullHeader = new byte[Size];
        byte[] preHmac = SerializeWithoutHmac();
        preHmac.CopyTo(fullHeader, 0);
        HeaderHMAC.CopyTo(fullHeader, 0x0060);

        stream.Write(fullHeader);
    }

    public static SecondaryVaultHeader ReadFrom(Stream stream)
    {
        byte[] buffer = new byte[Size];
        int read = stream.ReadAtLeast(buffer, Size, throwOnEndOfStream: false);
        if (read < Size)
        {
            throw new CorruptedVaultException($"Secondary vault header is truncated (expected {Size} bytes, got {read}).");
        }

        ReadOnlySpan<byte> span = buffer;
        var header = new SecondaryVaultHeader
        {
            RandomPrefix = span[0x0000..0x0020].ToArray(),
            MaskedMagic = span[0x0020..0x0028].ToArray(),
            FormatVersion = BinaryPrimitives.ReadUInt16LittleEndian(span[0x0028..0x002A]),
            MasterVaultUUID = new Guid(span[0x002A..0x003A]),
            PartIndex = BinaryPrimitives.ReadInt32LittleEndian(span[0x003A..0x003E]),
            LocalIndexOffset = BinaryPrimitives.ReadUInt64LittleEndian(span[0x003E..0x0046]),
            LocalIndexLength = BinaryPrimitives.ReadUInt64LittleEndian(span[0x0046..0x004E]),
            Reserved = span[0x004E..0x0060].ToArray(),
            HeaderHMAC = span[0x0060..0x0080].ToArray()
        };

        if (!header.VerifyMagic())
        {
            throw new CorruptedVaultException("File signature verification failed for secondary vault part.");
        }

        return header;
    }
}
