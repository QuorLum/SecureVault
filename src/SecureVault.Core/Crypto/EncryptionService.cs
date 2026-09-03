using System.Security.Cryptography;
using SecureVault.Core.Exceptions;

namespace SecureVault.Core.Crypto;

/// <summary>
/// Central cryptographic service deriving subkeys from the vault master key and handling
/// index encryption and AEAD verification.
/// </summary>
public sealed class EncryptionService : IDisposable
{
    private const string IndexKeyInfo = "SecureVault-IndexKey-v1";
    private const string SecureModeKeyInfo = "SecureVault-SecureModeKey-v1";
    private const string ObfuscationKeyInfo = "SecureVault-ObfuscationKey-v1";
    private const string HmacKeyInfo = "SecureVault-HMACKey-v1";

    public SecureBuffer IndexKey { get; }
    public SecureBuffer SecureModeKey { get; }
    public SecureBuffer ObfuscationKey { get; }
    public SecureBuffer HmacKey { get; }

    private bool _disposed;

    public EncryptionService(SecureBuffer masterKey)
    {
        ArgumentNullException.ThrowIfNull(masterKey);

        IndexKey = KeyDerivation.DeriveSubkey(masterKey, IndexKeyInfo, 32);
        SecureModeKey = KeyDerivation.DeriveSubkey(masterKey, SecureModeKeyInfo, 32);
        ObfuscationKey = KeyDerivation.DeriveSubkey(masterKey, ObfuscationKeyInfo, 32);
        HmacKey = KeyDerivation.DeriveSubkey(masterKey, HmacKeyInfo, 32);
    }

    public (byte[] Ciphertext, byte[] Nonce, byte[] Tag) EncryptIndex(ReadOnlySpan<byte> plaintext)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        byte[] nonce = new byte[12];
        RandomNumberGenerator.Fill(nonce);

        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[16];

        using var aes = new AesGcm(IndexKey.AsReadOnlySpan(), 16);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        return (ciphertext, nonce, tag);
    }

    public byte[] DecryptIndex(ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> tag)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        byte[] plaintext = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(IndexKey.AsReadOnlySpan(), 16);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return plaintext;
        }
        catch (CryptographicException ex)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new CorruptedIndexException("Failed to decrypt index: authentication tag mismatch or corrupted data.", ex);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        IndexKey.Dispose();
        SecureModeKey.Dispose();
        ObfuscationKey.Dispose();
        HmacKey.Dispose();

        _disposed = true;
    }
}
