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

    public ChunkWriter(
        Stream vaultStream,
        SecureBuffer secureModeKey,
        SecureBuffer obfuscationKey,
        ReedSolomonCodec rsCodec,
        ProtectionMode protectionMode = ProtectionMode.SecureMode)
    {
        _stream = vaultStream ?? throw new ArgumentNullException(nameof(vaultStream));
        _secureModeKey = secureModeKey ?? throw new ArgumentNullException(nameof(secureModeKey));
        _obfuscationKey = obfuscationKey ?? throw new ArgumentNullException(nameof(obfuscationKey));
        _rsCodec = rsCodec ?? throw new ArgumentNullException(nameof(rsCodec));
        _protectionMode = protectionMode;
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
            aes.Encrypt(nonce, plaintext, payload, authTag);
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
        BinaryPrimitives.WriteUInt16LittleEndian(span[0x0025..0x0027], (ushort)rsParity.Length);

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
            RSParityLength = (ushort)rsParity.Length
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
        ProtectionMode protectionMode)
    {
        uint crc32 = Crc32.HashToUInt32(plaintext);

        byte[] nonce = new byte[12];
        byte[] authTag = new byte[16];
        byte[] payload = new byte[plaintext.Length];

        if (protectionMode == ProtectionMode.SecureMode)
        {
            RandomNumberGenerator.Fill(nonce);
            using var aes = new AesGcm(secureModeKey.AsReadOnlySpan(), 16);
            aes.Encrypt(nonce, plaintext, payload, authTag);
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
        BinaryPrimitives.WriteUInt16LittleEndian(span[0x0025..0x0027], (ushort)rsParity.Length);

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
            (ushort)rsParity.Length);
    }
}

public record ProcessedChunk(
    byte[] FullChunkBytes,
    uint ChunkSequence,
    uint ChunkDataLength,
    uint CRC32,
    byte[] Nonce,
    byte[] AuthTag,
    ushort RSParityLength);
