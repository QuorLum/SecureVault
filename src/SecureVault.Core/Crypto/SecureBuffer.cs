using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace SecureVault.Core.Crypto;

/// <summary>
/// Manages a pinned byte buffer in memory that is guaranteed to be zeroed on disposal.
/// Prevents the GC from moving key material before it can be scrubbed.
/// </summary>
public sealed class SecureBuffer : IDisposable
{
    private byte[]? _buffer;
    private GCHandle _handle;
    private readonly int _length;
    private bool _disposed;

    public int Length => _length;

    public SecureBuffer(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);

        _length = length;
        _buffer = new byte[length];
        _handle = GCHandle.Alloc(_buffer, GCHandleType.Pinned);
    }

    public SecureBuffer(ReadOnlySpan<byte> source) : this(source.Length)
    {
        source.CopyTo(AsSpan());
    }

    public Span<byte> AsSpan()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _buffer.AsSpan(0, _length);
    }

    public ReadOnlySpan<byte> AsReadOnlySpan()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _buffer.AsSpan(0, _length);
    }

    /// <summary>
    /// For unit tests to inspect the raw underlying byte array post-disposal to assert zeroing.
    /// </summary>
    internal byte[]? DangerousGetRawBuffer() => _buffer;

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_buffer != null)
        {
            CryptographicOperations.ZeroMemory(_buffer);
            if (_handle.IsAllocated)
            {
                _handle.Free();
            }
        }

        _disposed = true;
    }
}
