using System.Buffers.Binary;
using System.IO.Hashing;
using System.Security.Cryptography;
using SecureVault.Core.Crypto;
using SecureVault.Core.Exceptions;

namespace SecureVault.Core.Format;

/// <summary>
/// Reads chunk blocks from the vault, performs Reed-Solomon auto-repair if bit rot is present,
/// verifies AES-GCM AEAD tags, deobfuscates or decrypts, and verifies CRC32 plaintext integrity.
/// </summary>
public sealed class ChunkReader
{
    private readonly Stream _stream;
    private readonly SecureBuffer _secureModeKey;
    private readonly SecureBuffer _obfuscationKey;
    private readonly ReedSolomonCodec _rsCodec;

    public ChunkReader(
        Stream vaultStream,
        SecureBuffer secureModeKey,
        SecureBuffer obfuscationKey,
        ReedSolomonCodec rsCodec)
    {
        _stream = vaultStream ?? throw new ArgumentNullException(nameof(vaultStream));
        _secureModeKey = secureModeKey ?? throw new ArgumentNullException(nameof(secureModeKey));
        _obfuscationKey = obfuscationKey ?? throw new ArgumentNullException(nameof(obfuscationKey));
        _rsCodec = rsCodec ?? throw new ArgumentNullException(nameof(rsCodec));
    }

    public byte[] ReadChunk(
        ChunkIndexEntry entry,
        Guid fileGuid,
        ReadOnlySpan<byte> fileSalt)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _stream.Seek((long)entry.AbsoluteOffset, SeekOrigin.Begin);

        byte[] header = new byte[VaultConstants.ChunkHeaderSize];
        int headerRead = _stream.ReadAtLeast(header, VaultConstants.ChunkHeaderSize, throwOnEndOfStream: false);
        if (headerRead < VaultConstants.ChunkHeaderSize)
        {
            throw new CorruptedChunkException((int)entry.ChunkSequence, $"Chunk header truncated at offset {entry.AbsoluteOffset}.");
        }

        ReadOnlySpan<byte> headerSpan = header;
        uint chunkDataLength = BinaryPrimitives.ReadUInt32LittleEndian(headerSpan[0x0000..0x0004]);
        uint expectedCrc = BinaryPrimitives.ReadUInt32LittleEndian(headerSpan[0x0004..0x0008]);
        var protectionMode = (ProtectionMode)headerSpan[0x0008];
        byte[] nonce = headerSpan[0x0009..0x0015].ToArray();
        byte[] authTag = headerSpan[0x0015..0x0025].ToArray();
        ushort headerParityLen = BinaryPrimitives.ReadUInt16LittleEndian(headerSpan[0x0025..0x0027]);

        if (chunkDataLength != entry.ChunkDataLength)
        {
            throw new CorruptedChunkException((int)entry.ChunkSequence, "Chunk data length mismatch between index and on-disk header.");
        }

        byte[] payload = new byte[chunkDataLength];
        int payloadRead = _stream.ReadAtLeast(payload, (int)chunkDataLength, throwOnEndOfStream: false);
        if (payloadRead < chunkDataLength)
        {
            throw new CorruptedChunkException((int)entry.ChunkSequence, "Chunk payload truncated.");
        }

        // Determine actual RS parity length:
        // RS(255, 223) requires ceil(payload.Length / 223) * 32 parity bytes when enabled
        int blockCount = ((int)chunkDataLength + ReedSolomonCodec.DataBlockSize - 1) / ReedSolomonCodec.DataBlockSize;
        int expectedParityLen = blockCount * ReedSolomonCodec.ParityBlockSize;
        int rsParityLength = (headerParityLen > 0 || entry.RSParityLength > 0)
            ? (entry.RSParityLength > 0 ? (int)entry.RSParityLength : expectedParityLen)
            : 0;

        byte[] parity = new byte[rsParityLength];
        int parityRead = _stream.ReadAtLeast(parity, rsParityLength, throwOnEndOfStream: false);
        if (parityRead < rsParityLength)
        {
            throw new CorruptedChunkException((int)entry.ChunkSequence, "Chunk RS parity truncated.");
        }

        // Auto-repair payload via Reed-Solomon if bit rot occurred
        byte[] repairedPayload = payload;
        if (rsParityLength > 0)
        {
            try
            {
                var (decoded, _) = _rsCodec.Decode(payload, parity);
                repairedPayload = decoded;
            }
            catch (UncorrectableCorruptionException)
            {
                throw new CorruptedChunkException((int)entry.ChunkSequence, "Uncorrectable chunk corruption in parity layer.");
            }
        }

        byte[] plaintext = new byte[chunkDataLength];

        if (protectionMode == ProtectionMode.SecureMode)
        {
            try
            {
                using var aes = new AesGcm(_secureModeKey.AsReadOnlySpan(), 16);
                aes.Decrypt(nonce, repairedPayload, authTag, plaintext);
            }
            catch (CryptographicException ex)
            {
                CryptographicOperations.ZeroMemory(plaintext);
                throw new CorruptedChunkException((int)entry.ChunkSequence, "AES-GCM authentication tag verification failed. Chunk is corrupt or tampered.", ex);
            }
        }
        else
        {
            repairedPayload.CopyTo(plaintext, 0);
            using var keystream = new ObfuscationKeystream(_obfuscationKey, fileGuid, fileSalt);
            long streamOffset = (long)entry.ChunkSequence * VaultConstants.DefaultChunkSize;
            keystream.ApplyInPlace(plaintext, streamOffset);
        }

        // Verify plaintext CRC32
        uint actualCrc = Crc32.HashToUInt32(plaintext);
        if (actualCrc != expectedCrc)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new CorruptedChunkException((int)entry.ChunkSequence, $"CRC32 checksum mismatch on decrypted plaintext (expected {expectedCrc:X8}, got {actualCrc:X8}).");
        }

        return plaintext;
    }
}
