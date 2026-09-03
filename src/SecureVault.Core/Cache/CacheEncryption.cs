using System.Security.Cryptography;
using SecureVault.Core.Crypto;
using SecureVault.Core.Exceptions;

namespace SecureVault.Core.Cache;

/// <summary>
/// Cryptographic operations for encrypted local cache storage (E01).
/// Guarantees that a fresh 12-byte random nonce is generated on every single write.
/// </summary>
public static class CacheEncryption
{
    public const int NonceSize = 12;
    public const int TagSize = 16;
    public const string CacheKeyInfo = "SecureVault-CacheKey-v1";

    public static SecureBuffer DeriveCacheKey(SecureBuffer masterKey)
    {
        return KeyDerivation.DeriveSubkey(masterKey, CacheKeyInfo, 32);
    }

    /// <summary>
    /// Encrypts cache payload using AES-256-GCM with a newly generated 12-byte random nonce per write.
    /// Output layout: [Nonce (12B)][AuthTag (16B)][Ciphertext]
    /// </summary>
    public static byte[] Encrypt(ReadOnlySpan<byte> plaintext, SecureBuffer cacheKey)
    {
        byte[] nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[TagSize];

        using (var aes = new AesGcm(cacheKey.AsReadOnlySpan(), TagSize))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        byte[] result = new byte[NonceSize + TagSize + ciphertext.Length];
        nonce.CopyTo(result, 0);
        tag.CopyTo(result, NonceSize);
        ciphertext.CopyTo(result, NonceSize + TagSize);

        return result;
    }

    /// <summary>
    /// Decrypts cache payload previously encrypted with Encrypt.
    /// </summary>
    public static byte[] Decrypt(ReadOnlySpan<byte> encryptedData, SecureBuffer cacheKey)
    {
        if (encryptedData.Length < NonceSize + TagSize)
        {
            throw new CorruptedVaultException("Encrypted cache file is too small or invalid.");
        }

        ReadOnlySpan<byte> nonce = encryptedData.Slice(0, NonceSize);
        ReadOnlySpan<byte> tag = encryptedData.Slice(NonceSize, TagSize);
        ReadOnlySpan<byte> ciphertext = encryptedData.Slice(NonceSize + TagSize);

        byte[] plaintext = new byte[ciphertext.Length];

        try
        {
            using var aes = new AesGcm(cacheKey.AsReadOnlySpan(), TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return plaintext;
        }
        catch (CryptographicException ex)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new CorruptedVaultException("Cache authentication tag verification failed. Cache is corrupt or tampered.", ex);
        }
    }
}
