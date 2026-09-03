using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace SecureVault.Core.Crypto;

public record Argon2idParameters(
    int MemoryCostKb = 262144, // 256 MB default
    int Iterations = 3,
    int Parallelism = 4,
    int SaltLength = 32,
    int OutputLength = 32);

/// <summary>
/// Handles Argon2id password-based key derivation and HKDF subkey derivation.
/// </summary>
public static class KeyDerivation
{
    public const int DefaultMemoryCostKb = 262144; // 256 MB
    public const int FallbackMemoryCostKb = 131072; // 128 MB fallback for low-RAM systems
    public const int DefaultIterations = 3;
    public const int DefaultParallelism = 4;
    public const int SaltLength = 32;
    public const int KeyLength = 32;

    /// <summary>
    /// Derives a 256-bit encryption key from a password using Argon2id.
    /// Intermediate sensitive buffers are pinned and scrubbed from memory.
    /// </summary>
    public static (SecureBuffer DerivedKey, byte[] Salt) DeriveFromPassword(
        string password,
        byte[]? existingSalt = null,
        int memoryCostKb = DefaultMemoryCostKb,
        int iterations = DefaultIterations,
        int parallelism = DefaultParallelism)
    {
        ArgumentNullException.ThrowIfNull(password);

        byte[] salt = existingSalt ?? new byte[SaltLength];
        if (existingSalt == null)
        {
            RandomNumberGenerator.Fill(salt);
        }

        byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
        byte[]? rawKey = null;

        try
        {
            using var argon2 = new Argon2id(passwordBytes)
            {
                MemorySize = memoryCostKb,
                Iterations = iterations,
                DegreeOfParallelism = parallelism,
                Salt = salt
            };

            rawKey = argon2.GetBytes(KeyLength);
            var secureBuffer = new SecureBuffer(rawKey);
            return (secureBuffer, salt);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            if (rawKey != null)
            {
                CryptographicOperations.ZeroMemory(rawKey);
            }
        }
    }

    /// <summary>
    /// Derives subkeys deterministically from a parent master key using HKDF-SHA256.
    /// </summary>
    public static SecureBuffer DeriveSubkey(
        SecureBuffer parentKey,
        string info,
        int outputLength = 32,
        byte[]? salt = null)
    {
        ArgumentNullException.ThrowIfNull(parentKey);
        ArgumentNullException.ThrowIfNull(info);

        byte[] infoBytes = Encoding.UTF8.GetBytes(info);
        byte[]? rawSubkey = null;

        try
        {
            rawSubkey = new byte[outputLength];
            HKDF.DeriveKey(
                HashAlgorithmName.SHA256,
                parentKey.AsReadOnlySpan(),
                rawSubkey,
                salt ?? ReadOnlySpan<byte>.Empty,
                infoBytes);

            return new SecureBuffer(rawSubkey);
        }
        finally
        {
            if (rawSubkey != null)
            {
                CryptographicOperations.ZeroMemory(rawSubkey);
            }
        }
    }
}
