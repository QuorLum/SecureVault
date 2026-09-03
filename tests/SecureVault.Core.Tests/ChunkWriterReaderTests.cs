using SecureVault.Core.Crypto;
using SecureVault.Core.Exceptions;
using SecureVault.Core.Format;

namespace SecureVault.Core.Tests;

public class ChunkWriterReaderTests
{
    [Fact]
    public void SecureMode_RoundTrips_AndGeneratesRandomNonces_ReviewerFixVerification()
    {
        using var secureKey = new SecureBuffer(32); secureKey.AsSpan().Fill(0x11);
        using var obfKey = new SecureBuffer(32); obfKey.AsSpan().Fill(0x22);
        var rs = new ReedSolomonCodec();

        using var ms = new MemoryStream();
        var writer = new ChunkWriter(ms, secureKey, obfKey, rs, ProtectionMode.SecureMode);
        var reader = new ChunkReader(ms, secureKey, obfKey, rs);

        Guid fileGuid = Guid.NewGuid();
        byte[] salt = new byte[16]; salt.AsSpan().Fill(0x99);
        byte[] plaintext = new byte[1024]; plaintext.AsSpan().Fill(0x42);

        // Write chunk 1
        var entry1 = writer.WriteChunk(plaintext, 0, fileGuid, salt);

        // Write identical chunk 2
        var entry2 = writer.WriteChunk(plaintext, 1, fileGuid, salt);

        // REVIEWER REQUIREMENT: Assert that nonces are randomly generated and distinct across writes
        Assert.False(entry1.Nonce.AsSpan().SequenceEqual(entry2.Nonce));

        // Read chunk 1
        byte[] readPlaintext1 = reader.ReadChunk(entry1, fileGuid, salt);
        Assert.True(plaintext.AsSpan().SequenceEqual(readPlaintext1));

        // Read chunk 2
        byte[] readPlaintext2 = reader.ReadChunk(entry2, fileGuid, salt);
        Assert.True(plaintext.AsSpan().SequenceEqual(readPlaintext2));
    }

    [Fact]
    public void FastObfuscationMode_RoundTripsAccurately()
    {
        using var secureKey = new SecureBuffer(32); secureKey.AsSpan().Fill(0x11);
        using var obfKey = new SecureBuffer(32); obfKey.AsSpan().Fill(0x22);
        var rs = new ReedSolomonCodec();

        using var ms = new MemoryStream();
        var writer = new ChunkWriter(ms, secureKey, obfKey, rs, ProtectionMode.FastObfuscation);
        var reader = new ChunkReader(ms, secureKey, obfKey, rs);

        Guid fileGuid = Guid.NewGuid();
        byte[] salt = new byte[16]; salt.AsSpan().Fill(0x88);
        byte[] plaintext = new byte[2048];
        for (int i = 0; i < plaintext.Length; i++) plaintext[i] = (byte)(i % 256);

        var entry = writer.WriteChunk(plaintext, 0, fileGuid, salt);
        byte[] readBack = reader.ReadChunk(entry, fileGuid, salt);

        Assert.True(plaintext.AsSpan().SequenceEqual(readBack));
    }

    [Fact]
    public void CorruptedCiphertext_IsAutoRepaired_ByReedSolomon()
    {
        using var secureKey = new SecureBuffer(32); secureKey.AsSpan().Fill(0x11);
        using var obfKey = new SecureBuffer(32); obfKey.AsSpan().Fill(0x22);
        var rs = new ReedSolomonCodec();

        using var ms = new MemoryStream();
        var writer = new ChunkWriter(ms, secureKey, obfKey, rs, ProtectionMode.SecureMode);
        var reader = new ChunkReader(ms, secureKey, obfKey, rs);

        Guid fileGuid = Guid.NewGuid();
        byte[] salt = new byte[16]; salt.AsSpan().Fill(0x77);
        byte[] plaintext = new byte[512]; plaintext.AsSpan().Fill(0xAB);

        var entry = writer.WriteChunk(plaintext, 0, fileGuid, salt);

        // Intentionally corrupt 2 bytes in the chunk payload on disk (within RS repair capacity)
        long payloadOffset = (long)entry.AbsoluteOffset + VaultConstants.ChunkHeaderSize;
        ms.Seek(payloadOffset + 5, SeekOrigin.Begin);
        ms.WriteByte(0x00);
        ms.Seek(payloadOffset + 15, SeekOrigin.Begin);
        ms.WriteByte(0x00);

        // Reader should auto-repair the bit rot via Reed-Solomon before AES-GCM decryption
        byte[] recovered = reader.ReadChunk(entry, fileGuid, salt);
        Assert.True(plaintext.AsSpan().SequenceEqual(recovered));
    }
}
