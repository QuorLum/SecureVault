using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using SecureVault.Core.Crypto;
using SecureVault.Core.Exceptions;

namespace SecureVault.Core.Format;

public sealed class VaultHeader
{
    private const string HeaderHmacInfo = "SecureVault-HeaderHMAC-v1";

    public byte[] RandomPrefix { get; set; } = new byte[32];
    public byte[] MaskedMagic { get; set; } = new byte[8];
    public ushort FormatVersion { get; set; } = VaultConstants.CurrentFormatVersion;
    public Guid VaultUUID { get; set; }
    public uint HeaderLength { get; set; } = VaultConstants.HeaderSize;

    public uint Argon2MemoryKb { get; set; } = KeyDerivation.DefaultMemoryCostKb;
    public byte Argon2Iterations { get; set; } = KeyDerivation.DefaultIterations;
    public byte Argon2Parallelism { get; set; } = KeyDerivation.DefaultParallelism;

    public WrappedKeyPair KeyData { get; set; } = null!;

    public byte PasswordHintLength { get; set; }
    public byte[] PasswordHintBytes { get; set; } = new byte[255];

    public ulong PrimaryIndexOffset { get; set; }
    public ulong PrimaryIndexLength { get; set; }
    public ulong BackupIndexOffset { get; set; }
    public ulong BackupIndexLength { get; set; }

    public byte[] HeaderHMAC { get; set; } = new byte[32];

    public static VaultHeader Create(
        SecureBuffer masterKey,
        string password,
        ReadOnlySpan<byte> recoveryKeySeed,
        Guid? vaultGuid = null,
        int memoryCostKb = KeyDerivation.DefaultMemoryCostKb,
        int iterations = KeyDerivation.DefaultIterations,
        int parallelism = KeyDerivation.DefaultParallelism)
    {
        var header = new VaultHeader
        {
            VaultUUID = vaultGuid ?? Guid.NewGuid(),
            Argon2MemoryKb = (uint)memoryCostKb,
            Argon2Iterations = (byte)iterations,
            Argon2Parallelism = (byte)parallelism,
            KeyData = KeyWrapping.WrapMasterKey(
                masterKey,
                password,
                recoveryKeySeed,
                memoryCostKb,
                iterations,
                parallelism)
        };

        RandomNumberGenerator.Fill(header.RandomPrefix);
        header.MaskedMagic = ComputeMaskedMagic(header.RandomPrefix);

        // Compute HMAC over the 0x0000..0x021B portion
        header.UpdateHmac(masterKey);
        return header;
    }

    public static byte[] ComputeMaskedMagic(ReadOnlySpan<byte> randomPrefix)
    {
        byte[] hash = SHA256.HashData(randomPrefix);
        byte[] masked = new byte[8];
        for (int i = 0; i < 8; i++)
        {
            masked[i] = (byte)(VaultConstants.RawMagic[i] ^ hash[i]);
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
        byte[] buffer = new byte[0x021C];
        Span<byte> span = buffer;

        RandomPrefix.CopyTo(span[0x0000..0x0020]);
        MaskedMagic.CopyTo(span[0x0020..0x0028]);
        BinaryPrimitives.WriteUInt16LittleEndian(span[0x0028..0x002A], FormatVersion);
        VaultUUID.ToByteArray().CopyTo(span[0x002A..0x003A]);
        BinaryPrimitives.WriteUInt32LittleEndian(span[0x003A..0x003E], HeaderLength);

        BinaryPrimitives.WriteUInt32LittleEndian(span[0x003E..0x0042], Argon2MemoryKb);
        span[0x0042] = Argon2Iterations;
        span[0x0043] = Argon2Parallelism;

        KeyData.PasswordSalt.CopyTo(span[0x0044..0x0064]);
        KeyData.PasswordWrappedKeyNonce.CopyTo(span[0x0064..0x0070]);
        KeyData.PasswordWrappedKey.CopyTo(span[0x0070..0x00A0]);

        KeyData.RecoverySalt.CopyTo(span[0x00A0..0x00C0]);
        KeyData.RecoveryWrappedKeyNonce.CopyTo(span[0x00C0..0x00CC]);
        KeyData.RecoveryWrappedKey.CopyTo(span[0x00CC..0x00FC]);

        span[0x00FC] = PasswordHintLength;
        PasswordHintBytes.CopyTo(span[0x00FD..0x01FC]);

        BinaryPrimitives.WriteUInt64LittleEndian(span[0x01FC..0x0204], PrimaryIndexOffset);
        BinaryPrimitives.WriteUInt64LittleEndian(span[0x0204..0x020C], PrimaryIndexLength);
        BinaryPrimitives.WriteUInt64LittleEndian(span[0x020C..0x0214], BackupIndexOffset);
        BinaryPrimitives.WriteUInt64LittleEndian(span[0x0214..0x021C], BackupIndexLength);

        return buffer;
    }

    public void WriteTo(Stream stream)
    {
        byte[] fullHeader = new byte[VaultConstants.HeaderSize];
        byte[] preHmac = SerializeWithoutHmac();
        preHmac.CopyTo(fullHeader, 0);
        HeaderHMAC.CopyTo(fullHeader, 0x021C);

        stream.Write(fullHeader);
    }

    public static VaultHeader ReadFrom(Stream stream)
    {
        byte[] buffer = new byte[VaultConstants.HeaderSize];
        int read = stream.ReadAtLeast(buffer, VaultConstants.HeaderSize, throwOnEndOfStream: false);
        if (read < VaultConstants.HeaderSize)
        {
            throw new CorruptedVaultException($"Vault header is truncated (expected {VaultConstants.HeaderSize} bytes, got {read}).");
        }

        ReadOnlySpan<byte> span = buffer;
        var header = new VaultHeader
        {
            RandomPrefix = span[0x0000..0x0020].ToArray(),
            MaskedMagic = span[0x0020..0x0028].ToArray(),
            FormatVersion = BinaryPrimitives.ReadUInt16LittleEndian(span[0x0028..0x002A]),
            VaultUUID = new Guid(span[0x002A..0x003A]),
            HeaderLength = BinaryPrimitives.ReadUInt32LittleEndian(span[0x003A..0x003E]),
            Argon2MemoryKb = BinaryPrimitives.ReadUInt32LittleEndian(span[0x003E..0x0042]),
            Argon2Iterations = span[0x0042],
            Argon2Parallelism = span[0x0043],
            KeyData = new WrappedKeyPair
            {
                PasswordSalt = span[0x0044..0x0064].ToArray(),
                PasswordWrappedKeyNonce = span[0x0064..0x0070].ToArray(),
                PasswordWrappedKey = span[0x0070..0x00A0].ToArray(),
                RecoverySalt = span[0x00A0..0x00C0].ToArray(),
                RecoveryWrappedKeyNonce = span[0x00C0..0x00CC].ToArray(),
                RecoveryWrappedKey = span[0x00CC..0x00FC].ToArray()
            },
            PasswordHintLength = span[0x00FC],
            PasswordHintBytes = span[0x00FD..0x01FC].ToArray(),
            PrimaryIndexOffset = BinaryPrimitives.ReadUInt64LittleEndian(span[0x01FC..0x0204]),
            PrimaryIndexLength = BinaryPrimitives.ReadUInt64LittleEndian(span[0x0204..0x020C]),
            BackupIndexOffset = BinaryPrimitives.ReadUInt64LittleEndian(span[0x020C..0x0214]),
            BackupIndexLength = BinaryPrimitives.ReadUInt64LittleEndian(span[0x0214..0x021C]),
            HeaderHMAC = span[0x021C..0x023C].ToArray()
        };

        if (!header.VerifyMagic())
        {
            throw new CorruptedVaultException("File signature verification failed. Not a valid SecureVault file.");
        }

        return header;
    }
}
