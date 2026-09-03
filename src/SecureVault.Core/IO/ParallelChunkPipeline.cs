using System.Threading.Channels;
using SecureVault.Core.Crypto;
using SecureVault.Core.Format;

namespace SecureVault.Core.IO;

/// <summary>
/// High-throughput parallel chunk encryption and processing pipeline (E20).
/// Uses producer-consumer channels to parallelize cryptography across cores while
/// strictly enforcing sequential chunk write order on disk.
/// </summary>
public sealed class ParallelChunkPipeline
{
    private readonly SecureBuffer _secureKey;
    private readonly SecureBuffer _obfuscationKey;
    private readonly ReedSolomonCodec _rsCodec;
    private readonly int _parallelism;

    public ParallelChunkPipeline(
        SecureBuffer secureKey,
        SecureBuffer obfuscationKey,
        ReedSolomonCodec? rsCodec = null,
        int? parallelism = null)
    {
        ArgumentNullException.ThrowIfNull(secureKey);
        ArgumentNullException.ThrowIfNull(obfuscationKey);

        _secureKey = secureKey;
        _obfuscationKey = obfuscationKey;
        _rsCodec = rsCodec ?? new ReedSolomonCodec();
        _parallelism = Math.Max(1, parallelism ?? Environment.ProcessorCount);
    }

    /// <summary>
    /// Processes and writes all chunks from a source stream sequentially into the vault file stream.
    /// </summary>
    public async Task<List<ChunkIndexEntry>> ProcessStreamParallelAsync(
        Stream sourceStream,
        Stream vaultStream,
        Guid fileGuid,
        byte[] fileSalt,
        ProtectionMode protectionMode,
        IProgress<long>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sourceStream);
        ArgumentNullException.ThrowIfNull(vaultStream);
        ArgumentNullException.ThrowIfNull(fileSalt);

        var chunkEntries = new List<ChunkIndexEntry>();
        int chunkIndex = 0;
        long bytesProcessed = 0;

        var inputChannel = Channel.CreateBounded<RawChunkItem>(new BoundedChannelOptions(_parallelism * 2)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true,
            SingleReader = false
        });

        var processedChannel = Channel.CreateBounded<ProcessedChunkItem>(new BoundedChannelOptions(_parallelism * 2)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = false,
            SingleReader = true
        });

        // 1. Producer: Read 1MB chunks from source stream
        var producerTask = Task.Run(async () =>
        {
            try
            {
                byte[] readBuffer = new byte[VaultConstants.DefaultChunkSize];
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    int bytesRead = await sourceStream.ReadAtLeastAsync(readBuffer, readBuffer.Length, throwOnEndOfStream: false, ct);
                    if (bytesRead == 0) break;

                    byte[] chunkBytes = new byte[bytesRead];
                    Array.Copy(readBuffer, chunkBytes, bytesRead);

                    await inputChannel.Writer.WriteAsync(new RawChunkItem(chunkIndex++, chunkBytes), ct);
                }
            }
            finally
            {
                inputChannel.Writer.Complete();
            }
        }, ct);

        // 2. Workers: Parallel compression, encryption, auth tag, RS parity calculation
        var workerTasks = Enumerable.Range(0, _parallelism).Select(_ => Task.Run(async () =>
        {
            await foreach (var item in inputChannel.Reader.ReadAllAsync(ct))
            {
                var processed = ChunkWriter.ProcessChunk(
                    item.Payload,
                    (uint)item.Index,
                    fileGuid,
                    fileSalt,
                    _secureKey,
                    _obfuscationKey,
                    _rsCodec,
                    protectionMode);

                await processedChannel.Writer.WriteAsync(new ProcessedChunkItem(item.Index, processed, item.Payload.Length), ct);
            }
        }, ct)).ToArray();

        _ = Task.WhenAll(workerTasks).ContinueWith(_ => processedChannel.Writer.Complete(), ct);

        // 3. Consumer: Collect processed chunks in strict ascending sequence and write to disk
        var pendingChunks = new Dictionary<int, ProcessedChunkItem>();
        int nextExpectedIndex = 0;

        await foreach (var item in processedChannel.Reader.ReadAllAsync(ct))
        {
            pendingChunks[item.Index] = item;

            while (pendingChunks.Remove(nextExpectedIndex, out var readyChunk))
            {
                ulong offset = (ulong)vaultStream.Position;
                await vaultStream.WriteAsync(readyChunk.Processed.FullChunkBytes, ct);

                var entry = new ChunkIndexEntry
                {
                    ChunkSequence = readyChunk.Processed.ChunkSequence,
                    AbsoluteOffset = offset,
                    ChunkDataLength = readyChunk.Processed.ChunkDataLength,
                    CRC32 = readyChunk.Processed.CRC32,
                    Nonce = readyChunk.Processed.Nonce,
                    AuthTag = readyChunk.Processed.AuthTag,
                    RSParityLength = readyChunk.Processed.RSParityLength
                };
                chunkEntries.Add(entry);

                bytesProcessed += readyChunk.PlaintextLength;
                progress?.Report(bytesProcessed);

                nextExpectedIndex++;
            }
        }

        await producerTask;

        return chunkEntries;
    }

    private sealed record RawChunkItem(int Index, byte[] Payload);
    private sealed record ProcessedChunkItem(int Index, ProcessedChunk Processed, int PlaintextLength);
}
