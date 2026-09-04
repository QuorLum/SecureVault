using SecureVault.Core.Format;

namespace SecureVault.Core.IO;

/// <summary>
/// A read-only, seekable Stream providing on-demand chunk decryption directly from the vault (C18).
/// Allows VLC, PDFium, or Image decoders to stream content in-memory without disk extraction.
/// </summary>
public sealed class VaultFileStream : Stream
{
    private readonly IndexEntry _entry;
    private readonly ChunkReader _chunkReader;
    private long _position;
    private int _cachedChunkIndex = -1;
    private byte[]? _cachedChunkData;
    private bool _disposed;

    public VaultFileStream(IndexEntry entry, ChunkReader chunkReader)
    {
        _entry = entry ?? throw new ArgumentNullException(nameof(entry));
        _chunkReader = chunkReader ?? throw new ArgumentNullException(nameof(chunkReader));
    }

    internal Stream UnderlyingStream => _chunkReader.UnderlyingStream;

    public override bool CanRead => !_disposed;
    public override bool CanSeek => !_disposed;
    public override bool CanWrite => false;
    public override long Length => (long)_entry.OriginalSize;

    public override long Position
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _position;
        }
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, Length);
            _position = value;
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset + count > buffer.Length)
        {
            throw new ArgumentException("Offset and count exceed destination buffer length.");
        }

        if (_position >= Length || count == 0)
        {
            return 0;
        }

        int totalRead = 0;
        int remaining = (int)Math.Min(count, Length - _position);

        while (remaining > 0)
        {
            int chunkIndex = (int)(_position / VaultConstants.DefaultChunkSize);
            int offsetInChunk = (int)(_position % VaultConstants.DefaultChunkSize);

            if (_cachedChunkIndex != chunkIndex)
            {
                if (chunkIndex >= _entry.Chunks.Count)
                {
                    break;
                }

                _cachedChunkData = _chunkReader.ReadChunk(_entry.Chunks[chunkIndex], _entry.FileGuid, _entry.FileSalt);
                _cachedChunkIndex = chunkIndex;
            }

            int availableInChunk = _cachedChunkData!.Length - offsetInChunk;
            if (availableInChunk <= 0)
            {
                break;
            }

            int bytesToCopy = Math.Min(remaining, availableInChunk);
            Buffer.BlockCopy(_cachedChunkData, offsetInChunk, buffer, offset + totalRead, bytesToCopy);

            totalRead += bytesToCopy;
            _position += bytesToCopy;
            remaining -= bytesToCopy;
        }

        return totalRead;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        long newPosition = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };

        if (newPosition < 0 || newPosition > Length)
        {
            throw new IOException($"Seek offset {newPosition} is out of bounds (0..{Length}).");
        }

        _position = newPosition;
        return _position;
    }

    public override void Flush() { }

    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _cachedChunkData = null;
            _disposed = true;
        }
        base.Dispose(disposing);
    }
}
