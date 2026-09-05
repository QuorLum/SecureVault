using System.Security.Cryptography;
using SecureVault.Core.Crypto;
using SecureVault.Core.Exceptions;
using SecureVault.Core.Format;

namespace SecureVault.Core.Tests;

public class AadBindingReproTests
{
    [Fact]
    public void TransplantChunk_AcrossFiles_UnpatchedV1_DecryptsSilentlyWithoutFailure()
    {
        // VULNERABILITY PROOF (A16 / M02):
        // In the unpatched format (v1), AES-GCM chunk encryption does not bind the file GUID
        // or chunk sequence in Additional Authenticated Data (AAD).
        // An attacker can transplant a chunk from File A into File B, and the decryption engine
        // verifies the AES-GCM auth tag and silently decrypts File A's plaintext.

        using var secureKey = new SecureBuffer(32); secureKey.AsSpan().Fill(0x55);
        using var obfKey = new SecureBuffer(32); obfKey.AsSpan().Fill(0x66);
        var rs = new ReedSolomonCodec();

        using var ms = new MemoryStream();
        // Legacy v1 writer and reader without AAD
        var writerV1 = new ChunkWriter(ms, secureKey, obfKey, rs, ProtectionMode.SecureMode, formatVersion: 1);
        var readerV1 = new ChunkReader(ms, secureKey, obfKey, rs, formatVersion: 1);

        Guid fileGuidA = Guid.NewGuid();
        Guid fileGuidB = Guid.NewGuid();
        byte[] saltA = new byte[16]; saltA.AsSpan().Fill(0x11);
        byte[] saltB = new byte[16]; saltB.AsSpan().Fill(0x22);

        byte[] plaintextA = "SECRET CONFIDENTIAL DATA OF FILE A"u8.ToArray();
        byte[] plaintextB = "PUBLIC BENIGN DATA OF FILE B"u8.ToArray();

        var entryA = writerV1.WriteChunk(plaintextA, 0, fileGuidA, saltA);
        var entryB = writerV1.WriteChunk(plaintextB, 0, fileGuidB, saltB);

        // In v1, transplanting entryA into fileGuidB succeeds silently!
        byte[] decryptedTransplant = readerV1.ReadChunk(entryA, fileGuidB, saltB);
        Assert.True(plaintextA.AsSpan().SequenceEqual(decryptedTransplant));
    }

    [Fact]
    public void TransplantChunk_AcrossFiles_PatchedV2_ThrowsCorruptedChunkException()
    {
        // VERIFICATION OF PATCH P-01:
        // In format v2, AAD binds FileGuid (16 bytes) || ChunkSeq (4 bytes) || FormatVersion (2 bytes).
        // Attempting to decrypt File A's chunk under File B's identity MUST fail AEAD authentication.

        using var secureKey = new SecureBuffer(32); secureKey.AsSpan().Fill(0x55);
        using var obfKey = new SecureBuffer(32); obfKey.AsSpan().Fill(0x66);
        var rs = new ReedSolomonCodec();

        using var ms = new MemoryStream();
        var writerV2 = new ChunkWriter(ms, secureKey, obfKey, rs, ProtectionMode.SecureMode, formatVersion: 2);
        var readerV2 = new ChunkReader(ms, secureKey, obfKey, rs, formatVersion: 2);

        Guid fileGuidA = Guid.NewGuid();
        Guid fileGuidB = Guid.NewGuid();
        byte[] saltA = new byte[16]; saltA.AsSpan().Fill(0x11);
        byte[] saltB = new byte[16]; saltB.AsSpan().Fill(0x22);

        byte[] plaintextA = "SECRET CONFIDENTIAL DATA OF FILE A"u8.ToArray();
        byte[] plaintextB = "PUBLIC BENIGN DATA OF FILE B"u8.ToArray();

        var entryA = writerV2.WriteChunk(plaintextA, 0, fileGuidA, saltA);
        var entryB = writerV2.WriteChunk(plaintextB, 0, fileGuidB, saltB);

        // 1. Decrypting under correct identity succeeds
        byte[] legitimateA = readerV2.ReadChunk(entryA, fileGuidA, saltA);
        Assert.True(plaintextA.AsSpan().SequenceEqual(legitimateA));

        // 2. Transplanting entryA into fileGuidB MUST throw CorruptedChunkException due to AAD mismatch
        var ex = Assert.Throws<CorruptedChunkException>(() => readerV2.ReadChunk(entryA, fileGuidB, saltB));
        Assert.Contains("AES-GCM authentication tag verification failed", ex.Message);
    }

    [Fact]
    public void ReorderChunks_WithinFile_PatchedV2_ThrowsCorruptedChunkException()
    {
        // VERIFICATION OF PATCH P-01: Chunk reordering within the same file.
        // Chunk 1 placed at Chunk 0 sequence index MUST fail AEAD authentication because
        // chunk sequence is bound in the AAD.

        using var secureKey = new SecureBuffer(32); secureKey.AsSpan().Fill(0x55);
        using var obfKey = new SecureBuffer(32); obfKey.AsSpan().Fill(0x66);
        var rs = new ReedSolomonCodec();

        using var ms = new MemoryStream();
        var writerV2 = new ChunkWriter(ms, secureKey, obfKey, rs, ProtectionMode.SecureMode, formatVersion: 2);
        var readerV2 = new ChunkReader(ms, secureKey, obfKey, rs, formatVersion: 2);

        Guid fileGuid = Guid.NewGuid();
        byte[] salt = new byte[16]; salt.AsSpan().Fill(0x33);

        byte[] chunk0Data = "FIRST CHUNK BLOCK (SEQUENCE 0)"u8.ToArray();
        byte[] chunk1Data = "SECOND CHUNK BLOCK (SEQUENCE 1)"u8.ToArray();

        var entry0 = writerV2.WriteChunk(chunk0Data, 0, fileGuid, salt);
        var entry1 = writerV2.WriteChunk(chunk1Data, 1, fileGuid, salt);

        // Valid reads succeed
        Assert.True(chunk0Data.AsSpan().SequenceEqual(readerV2.ReadChunk(entry0, fileGuid, salt)));
        Assert.True(chunk1Data.AsSpan().SequenceEqual(readerV2.ReadChunk(entry1, fileGuid, salt)));

        // Create a forged index entry pointing to chunk 1's physical bytes but claiming sequence 0
        var forgedEntry = new ChunkIndexEntry
        {
            ChunkSequence = 0, // Claim sequence 0
            AbsoluteOffset = entry1.AbsoluteOffset, // Point to chunk 1's on-disk ciphertext
            ChunkDataLength = entry1.ChunkDataLength,
            CRC32 = entry1.CRC32,
            Nonce = entry1.Nonce,
            AuthTag = entry1.AuthTag,
            RSParityLength = entry1.RSParityLength
        };

        // Decrypting chunk 1 bytes with sequence 0 in AAD MUST fail authentication
        var ex = Assert.Throws<CorruptedChunkException>(() => readerV2.ReadChunk(forgedEntry, fileGuid, salt));
        Assert.Contains("AES-GCM authentication tag verification failed", ex.Message);
    }

    [Fact]
    public void ShortLastChunk_PatchedV2_RoundTripsAndAuthenticatesWithAad()
    {
        // VERIFICATION: AAD covers variable-length and short last chunks properly.
        using var secureKey = new SecureBuffer(32); secureKey.AsSpan().Fill(0x55);
        using var obfKey = new SecureBuffer(32); obfKey.AsSpan().Fill(0x66);
        var rs = new ReedSolomonCodec();

        using var ms = new MemoryStream();
        var writerV2 = new ChunkWriter(ms, secureKey, obfKey, rs, ProtectionMode.SecureMode, formatVersion: 2);
        var readerV2 = new ChunkReader(ms, secureKey, obfKey, rs, formatVersion: 2);

        Guid fileGuid = Guid.NewGuid();
        byte[] salt = new byte[16]; salt.AsSpan().Fill(0x44);

        // 37 bytes (short chunk)
        byte[] shortChunk = "SHORT TRAILING CHUNK WITH 37 BYTES..."u8.ToArray();
        var entry = writerV2.WriteChunk(shortChunk, 99, fileGuid, salt);

        byte[] decrypted = readerV2.ReadChunk(entry, fileGuid, salt);
        Assert.True(shortChunk.AsSpan().SequenceEqual(decrypted));

        // Tampering GUID fails
        Assert.Throws<CorruptedChunkException>(() => readerV2.ReadChunk(entry, Guid.NewGuid(), salt));
    }

    [Fact]
    public void ZeroLengthChunk_PatchedV2_RoundTripsAndAuthenticatesWithAad()
    {
        // VERIFICATION: AES-GCM AEAD over zero-length plaintext binds AAD and produces a 16-byte auth tag.
        using var secureKey = new SecureBuffer(32); secureKey.AsSpan().Fill(0x55);
        using var obfKey = new SecureBuffer(32); obfKey.AsSpan().Fill(0x66);
        var rs = new ReedSolomonCodec();

        using var ms = new MemoryStream();
        var writerV2 = new ChunkWriter(ms, secureKey, obfKey, rs, ProtectionMode.SecureMode, formatVersion: 2);
        var readerV2 = new ChunkReader(ms, secureKey, obfKey, rs, formatVersion: 2);

        Guid fileGuid = Guid.NewGuid();
        byte[] salt = new byte[16]; salt.AsSpan().Fill(0x77);

        var entry = writerV2.WriteChunk(ReadOnlySpan<byte>.Empty, 0, fileGuid, salt);
        Assert.Equal(0u, entry.ChunkDataLength);
        Assert.Equal(16, entry.AuthTag.Length);

        // Reading under matching identity succeeds and yields 0 bytes
        byte[] decrypted = readerV2.ReadChunk(entry, fileGuid, salt);
        Assert.Empty(decrypted);

        // Transplanting to another FileGuid fails authentication even with zero-length payload
        Assert.Throws<CorruptedChunkException>(() => readerV2.ReadChunk(entry, Guid.NewGuid(), salt));
    }

    [Fact]
    public async Task ZeroLengthFile_InVault_RoundTripsAccurately()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), "zero_len_vault_" + Guid.NewGuid().ToString("N") + ".vault");
        try
        {
            var (vault, _) = await VaultManager.CreateAsync(tempPath, "Password123!");
            using (vault)
            {
                using var emptyStream = new MemoryStream();
                var entry = await vault.AddFileAsync(emptyStream, "empty.txt", "/", ProtectionMode.SecureMode);
                Assert.Equal(0UL, entry.OriginalSize);

                byte[] readBack = await vault.ReadAllBytesAsync(entry);
                Assert.Empty(readBack);
            }
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}
