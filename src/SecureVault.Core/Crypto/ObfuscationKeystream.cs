using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace SecureVault.Core.Crypto;

/// <summary>
/// Generates position-dependent XOR keystreams for Fast Obfuscation Mode (A15 / M14).
/// Uses HKDF-derived AES-CTR counter blocks with a per-file unique random salt.
/// Note: Fast Obfuscation protects against casual hex viewing only; use Secure Mode (AES-GCM)
/// for cryptographic confidentiality.
/// </summary>
public sealed class ObfuscationKeystream : IDisposable
{
    private readonly Aes _aes;
    private readonly ICryptoTransform _encryptor;
    private bool _disposed;

    public ObfuscationKeystream(SecureBuffer masterKey, Guid fileId, ReadOnlySpan<byte> fileSalt)
    {
        ArgumentNullException.ThrowIfNull(masterKey);

        if (fileSalt.Length < 16)
        {
            throw new ArgumentException("File salt must be at least 16 bytes.", nameof(fileSalt));
        }

        // Derive unique 256-bit keystream key for this file version
        byte[] info = Encoding.UTF8.GetBytes($"SecureVault-XOR-Keystream-v1:{fileId:N}");
        byte[] keyBytes = new byte[32];

        try
        {
            HKDF.DeriveKey(
                HashAlgorithmName.SHA256,
                masterKey.AsReadOnlySpan(),
                keyBytes,
                fileSalt,
                info);

            _aes = Aes.Create();
            _aes.Key = keyBytes;
            _aes.Mode = CipherMode.ECB;
            _aes.Padding = PaddingMode.None;
            _encryptor = _aes.CreateEncryptor();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
        }
    }

    /// <summary>
    /// Applies XOR keystream in-place to the target buffer, starting from the given byte offset.
    /// Calling this method again with the same offset reverses the obfuscation.
    /// </summary>
    public void ApplyInPlace(Span<byte> data, long streamOffset)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (data.IsEmpty)
            return;

        Span<byte> counterBlock = stackalloc byte[16];
        Span<byte> keystreamBlock = stackalloc byte[16];

        long currentOffset = streamOffset;
        int remaining = data.Length;
        int bufferIndex = 0;

        while (remaining > 0)
        {
            long blockIndex = currentOffset / 16;
            int offsetInBlock = (int)(currentOffset % 16);

            // Construct 16-byte counter block (Big-endian block index)
            counterBlock.Clear();
            BinaryPrimitives.WriteInt64BigEndian(counterBlock[8..], blockIndex);

            // Encrypt counter block using ECB to generate 16 bytes of keystream
            _encryptor.TransformBlock(counterBlock.ToArray(), 0, 16, keystreamBlock.ToArray(), 0);

            // Re-encrypt directly into keystream buffer
            byte[] inArr = counterBlock.ToArray();
            byte[] outArr = new byte[16];
            _encryptor.TransformBlock(inArr, 0, 16, outArr, 0);

            int bytesToXor = Math.Min(remaining, 16 - offsetInBlock);
            for (int i = 0; i < bytesToXor; i++)
            {
                data[bufferIndex + i] ^= outArr[offsetInBlock + i];
            }

            remaining -= bytesToXor;
            bufferIndex += bytesToXor;
            currentOffset += bytesToXor;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _encryptor.Dispose();
        _aes.Dispose();
        _disposed = true;
    }
}
