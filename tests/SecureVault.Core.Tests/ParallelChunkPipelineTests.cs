using System.Security.Cryptography;
using SecureVault.Core.Crypto;
using SecureVault.Core.Format;
using SecureVault.Core.IO;
using Xunit;

namespace SecureVault.Core.Tests;

public class ParallelChunkPipelineTests
{
    [Fact]
    public async Task ProcessStreamParallelAsync_WritesChunksSequentially_AndAllowsDecryption()
    {
        using var masterKey = new SecureBuffer(32);
        RandomNumberGenerator.Fill(masterKey.AsSpan());
        using var enc = new EncryptionService(masterKey);

        byte[] fileSalt = new byte[16];
        RandomNumberGenerator.Fill(fileSalt);
        var fileGuid = Guid.NewGuid();

        // 2.5 MB payload = 3 chunks (1MB, 1MB, 0.5MB)
        byte[] payload = new byte[(int)(2.5 * VaultConstants.DefaultChunkSize)];
        RandomNumberGenerator.Fill(payload);

        using var sourceStream = new MemoryStream(payload);
        using var vaultStream = new MemoryStream();

        var pipeline = new ParallelChunkPipeline(
            enc.SecureModeKey,
            enc.ObfuscationKey,
            parallelism: 4);

        var chunkEntries = await pipeline.ProcessStreamParallelAsync(
            sourceStream,
            vaultStream,
            fileGuid,
            fileSalt,
            ProtectionMode.SecureMode);

        Assert.Equal(3, chunkEntries.Count);
        Assert.Equal(0u, chunkEntries[0].ChunkSequence);
        Assert.Equal(1u, chunkEntries[1].ChunkSequence);
        Assert.Equal(2u, chunkEntries[2].ChunkSequence);

        // Verify sequential offsets
        Assert.True(chunkEntries[0].AbsoluteOffset < chunkEntries[1].AbsoluteOffset);
        Assert.True(chunkEntries[1].AbsoluteOffset < chunkEntries[2].AbsoluteOffset);
    }
}
