using System.Runtime.InteropServices;
using LibVLCSharp.Shared;
using SecureVault.Core.IO;

namespace SecureVault.Core.Media;

/// <summary>
/// Bridges a decrypted, seekable VaultFileStream directly into LibVLC's engine (I01-I04).
/// Streams audio and video data on-demand across 1MB chunk boundaries with zero disk writes.
/// </summary>
public sealed class VaultMediaInput : MediaInput
{
    private readonly Stream _stream;
    private readonly byte[] _readBuffer = new byte[65536];

    public VaultMediaInput(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _stream = stream;
        CanSeek = true;
    }

    public override bool Open(out ulong size)
    {
        size = (ulong)_stream.Length;
        return true;
    }

    public override int Read(IntPtr buf, uint len)
    {
        int bytesToRead = (int)Math.Min(len, (uint)_readBuffer.Length);
        int bytesRead = _stream.Read(_readBuffer, 0, bytesToRead);
        if (bytesRead > 0)
        {
            Marshal.Copy(_readBuffer, 0, buf, bytesRead);
        }
        return bytesRead;
    }

    public override bool Seek(ulong offset)
    {
        try
        {
            _stream.Seek((long)offset, SeekOrigin.Begin);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public override void Close()
    {
        _stream.Dispose();
    }
}
