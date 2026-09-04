using System.Security.Cryptography;

namespace SecureVault.Core.Security;

/// <summary>
/// Manages temporary files with cryptographic multi-pass overwrite upon disposal (M07).
/// 
/// NOTICE ON SSD WEAR-LEVELING AND PHYSICAL ERASURE:
/// Multi-pass overwriting significantly mitigates forensic file recovery on magnetic storage
/// and conventional filesystems. However, due to flash controller wear-leveling and out-of-place
/// write allocation algorithms on modern SSDs/NVMe media, complete physical block destruction
/// cannot be guaranteed at the hardware cell level. Zero-disk-write memory streaming remains
/// the primary protection barrier against local forensic data recovery.
/// </summary>
public sealed class SecureTempFile : IDisposable, IAsyncDisposable
{
    private readonly string _filePath;
    private FileStream? _stream;
    private bool _disposed;

    public string FilePath => _filePath;
    public FileStream Stream => _stream ?? throw new ObjectDisposedException(nameof(SecureTempFile));

    public SecureTempFile(string? extension = null)
    {
        string fileName = $"sv_{Guid.NewGuid():N}_{(extension != null ? extension.TrimStart('.') : "tmp")}";
        _filePath = Path.Combine(Path.GetTempPath(), fileName);
        _stream = new FileStream(_filePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
    }

    public static SecureTempFile Create(string? extension = null) => new(extension);

    public void Dispose()
    {
        if (_disposed) return;

        WipeAndPurge();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        await Task.Run(WipeAndPurge);
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void WipeAndPurge()
    {
        try
        {
            if (_stream != null)
            {
                long length = _stream.Length;
                if (length > 0)
                {
                    // Pass 1: Overwrite entire file with CSPRNG random bytes
                    _stream.Seek(0, SeekOrigin.Begin);
                    byte[] randomBuffer = new byte[Math.Min(65536, length)];
                    long remaining = length;
                    while (remaining > 0)
                    {
                        int toWrite = (int)Math.Min(randomBuffer.Length, remaining);
                        RandomNumberGenerator.Fill(randomBuffer.AsSpan(0, toWrite));
                        _stream.Write(randomBuffer, 0, toWrite);
                        remaining -= toWrite;
                    }
                    _stream.Flush(flushToDisk: true);

                    // Pass 2: Overwrite with all zeroes (0x00)
                    _stream.Seek(0, SeekOrigin.Begin);
                    Array.Clear(randomBuffer, 0, randomBuffer.Length);
                    remaining = length;
                    while (remaining > 0)
                    {
                        int toWrite = (int)Math.Min(randomBuffer.Length, remaining);
                        _stream.Write(randomBuffer, 0, toWrite);
                        remaining -= toWrite;
                    }
                    _stream.Flush(flushToDisk: true);
                }

                _stream.Dispose();
                _stream = null;
            }
        }
        catch
        {
            // Suppress wipe exceptions to ensure file deletion attempt continues
        }

        try
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }
        }
        catch
        {
            // Best effort deletion
        }
    }
}
