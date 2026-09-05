using System.Buffers.Binary;
using System.IO.Hashing;
using System.Security.Cryptography;
using SecureVault.Core.Crypto;

namespace SecureVault.Core.Format;

/// <summary>
/// Writes 1MB chunk blocks to the vault stream, handling AEAD encryption,
/// random nonce generation, CRC32 integrity verification, and Reed-Solomon parity.
/// </summary>
public sealed class ChunkWriter
{
    private readonly Stream _stream;
    private readonly SecureBuffer _secureModeKey;
    private readonly SecureBuffer _obfuscationKey;
    private readonly ReedSolomonCodec _rsCodec;
    private readonly ProtectionMode _protectionMode;
    private readonly ushort _formatVersion;

    public ChunkWriter(
        Stream vaultStream,
        SecureBuffer secureModeKey,
        SecureBuffer obfuscationKey,
        ReedSolomonCodec rsCodec,
        ProtectionMode protectionMode = ProtectionMode.SecureMode,
        ushort formatVersion = VaultConstants.CurrentFormatVersion)
    {
        _stream = vaultStream ?? throw new ArgumentNullException(nameof(vaultStream));
        _secureModeKey = secureModeKey ?? throw new ArgumentNullException(nameof(secureModeKey));
        _obfuscationKey = obfuscationKey ?? throw new ArgumentNullException(nameof(obfuscationKey));
        _rsCodec = rsCodec ?? throw new ArgumentNullException(nameof(rsCodec));
        _protectionMode = protectionMode;
        _formatVersion = formatVersion;
    }

    public const int AadSize = 22;

    public static byte[] ComputeAssociatedData(Guid fileGuid, uint chunkSequence, ushort formatVersion)
    {
        byte[] aad = new byte[AadSize];
        BuildAssociatedData(aad, fileGuid, chunkSequence, formatVersion);
        return aad;
    }

    public static void BuildAssociatedData(Span<byte> destination, Guid fileGuid, uint chunkSequence, ushort formatVersion)
    {
        fileGuid.TryWriteBytes(destination[..16]);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(16, 4), chunkSequence);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(20, 2), formatVersion);
    }

    public ChunkIndexEntry WriteChunk(
        ReadOnlySpan<byte> plaintext,
        uint chunkSequence,
        Guid fileGuid,
        ReadOnlySpan<byte> fileSalt)
    {
        uint crc32 = Crc32.HashToUInt32(plaintext);

        byte[] nonce = new byte[12];
        byte[] authTag = new byte[16];
        byte[] payload = new byte[plaintext.Length];

        if (_protectionMode == ProtectionMode.SecureMode)
        {
            // CRITICAL FIX: Fresh 12-byte random nonce generated on every write to avoid nonce reuse
            RandomNumberGenerator.Fill(nonce);
            using var aes = new AesGcm(_secureModeKey.AsReadOnlySpan(), 16);
            if (_formatVersion >= 2)
            {
                Span<byte> aad = stackalloc byte[AadSize];
                BuildAssociatedData(aad, fileGuid, chunkSequence, _formatVersion);
                aes.Encrypt(nonce, plaintext, payload, authTag, aad);
            }
            else
            {
                aes.Encrypt(nonce, plaintext, payload, authTag);
            }
        }
        else
        {
            // Fast Obfuscation Mode: XOR keystream
            plaintext.CopyTo(payload);
            using var keystream = new ObfuscationKeystream(_obfuscationKey, fileGuid, fileSalt);
            long streamOffset = (long)chunkSequence * VaultConstants.DefaultChunkSize;
            keystream.ApplyInPlace(payload, streamOffset);
        }

        // Compute Reed-Solomon parity for the payload
        byte[] rsParity = _rsCodec.Encode(payload);

        ulong absoluteOffset = (ulong)_stream.Position;

        // Write Chunk Header (39 bytes = 0x0027)
        byte[] header = new byte[VaultConstants.ChunkHeaderSize];
        Span<byte> span = header;
        BinaryPrimitives.WriteUInt32LittleEndian(span[0x0000..0x0004], (uint)payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span[0x0004..0x0008], crc32);
        span[0x0008] = (byte)_protectionMode;
        nonce.CopyTo(span[0x0009..0x0015]);
        authTag.CopyTo(span[0x0015..0x0025]);
        BinaryPrimitives.WriteUInt16LittleEndian(span[0x0025..0x0027], (ushort)Math.Min((int)ushort.MaxValue, rsParity.Length));

        _stream.Write(header);
        _stream.Write(payload);
        _stream.Write(rsParity);

        return new ChunkIndexEntry
        {
            ChunkSequence = chunkSequence,
            AbsoluteOffset = absoluteOffset,
            ChunkDataLength = (uint)payload.Length,
            CRC32 = crc32,
            Nonce = nonce,
            AuthTag = authTag,
            RSParityLength = (uint)rsParity.Length
        };
    }

    public static ProcessedChunk ProcessChunk(
        ReadOnlySpan<byte> plaintext,
        uint chunkSequence,
        Guid fileGuid,
        ReadOnlySpan<byte> fileSalt,
        SecureBuffer secureModeKey,
        SecureBuffer obfuscationKey,
        ReedSolomonCodec rsCodec,
        ProtectionMode protectionMode,
        ushort formatVersion = VaultConstants.CurrentFormatVersion)
    {
        uint crc32 = Crc32.HashToUInt32(plaintext);

        byte[] nonce = new byte[12];
        byte[] authTag = new byte[16];
        byte[] payload = new byte[plaintext.Length];

        if (protectionMode == ProtectionMode.SecureMode)
        {
            RandomNumberGenerator.Fill(nonce);
            using var aes = new AesGcm(secureModeKey.AsReadOnlySpan(), 16);
            if (formatVersion >= 2)
            {
                Span<byte> aad = stackalloc byte[AadSize];
                BuildAssociatedData(aad, fileGuid, chunkSequence, formatVersion);
                aes.Encrypt(nonce, plaintext, payload, authTag, aad);
            }
            else
            {
                aes.Encrypt(nonce, plaintext, payload, authTag);
            }
        }
        else
        {
            plaintext.CopyTo(payload);
            using var keystream = new ObfuscationKeystream(obfuscationKey, fileGuid, fileSalt);
            long streamOffset = (long)chunkSequence * VaultConstants.DefaultChunkSize;
            keystream.ApplyInPlace(payload, streamOffset);
        }

        byte[] rsParity = rsCodec.Encode(payload);

        byte[] header = new byte[VaultConstants.ChunkHeaderSize];
        Span<byte> span = header;
        BinaryPrimitives.WriteUInt32LittleEndian(span[0x0000..0x0004], (uint)payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span[0x0004..0x0008], crc32);
        span[0x0008] = (byte)protectionMode;
        nonce.CopyTo(span[0x0009..0x0015]);
        authTag.CopyTo(span[0x0015..0x0025]);
        BinaryPrimitives.WriteUInt16LittleEndian(span[0x0025..0x0027], (ushort)Math.Min((int)ushort.MaxValue, rsParity.Length));

        byte[] fullChunk = new byte[header.Length + payload.Length + rsParity.Length];
        header.CopyTo(fullChunk, 0);
        payload.CopyTo(fullChunk, header.Length);
        rsParity.CopyTo(fullChunk, header.Length + payload.Length);

        return new ProcessedChunk(
            fullChunk,
            chunkSequence,
            (uint)payload.Length,
            crc32,
            nonce,
            authTag,
            (uint)rsParity.Length);
    }
}

public record ProcessedChunk(
    byte[] FullChunkBytes,
    uint ChunkSequence,
    uint ChunkDataLength,
    uint CRC32,
    byte[] Nonce,
    byte[] AuthTag,
    uint RSParityLength);
