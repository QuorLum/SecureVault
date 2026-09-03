using System.Security.Cryptography;
using SecureVault.Core.Exceptions;

namespace SecureVault.Core.Crypto;

/// <summary>
/// Represents the dual-wrapped master key slots stored in the vault header.
/// Slot 0: Wrapped with Argon2id-derived key from user password.
/// Slot 1: Wrapped with HKDF-derived key from 24-word recovery phrase seed.
/// </summary>
public sealed record WrappedKeyPair
{
    public const int SaltSize = 32;
    public const int NonceSize = 12;
    public const int MasterKeySize = 32;
    public const int TagSize = 16;
    public const int WrappedKeySize = MasterKeySize + TagSize; // 48 bytes
    public const int TotalSize = (SaltSize + WrappedKeySize + NonceSize) * 2; // 184 bytes

    public required byte[] PasswordSalt { get; init; }
    public required byte[] PasswordWrappedKey { get; init; }
    public required byte[] PasswordWrappedKeyNonce { get; init; }

    public required byte[] RecoverySalt { get; init; }
    public required byte[] RecoveryWrappedKey { get; init; }
    public required byte[] RecoveryWrappedKeyNonce { get; init; }
}

public static class KeyWrapping
{
    private const string RecoveryInfo = "SecureVault-Recovery-v1";

    public static WrappedKeyPair WrapMasterKey(
        SecureBuffer masterKey,
        string password,
        ReadOnlySpan<byte> recoveryKeySeed,
        int memoryCostKb = KeyDerivation.DefaultMemoryCostKb,
        int iterations = KeyDerivation.DefaultIterations,
        int parallelism = KeyDerivation.DefaultParallelism)
    {
        ArgumentNullException.ThrowIfNull(masterKey);
        ArgumentNullException.ThrowIfNull(password);

        if (masterKey.Length != WrappedKeyPair.MasterKeySize)
        {
            throw new ArgumentException($"Master key must be exactly {WrappedKeyPair.MasterKeySize} bytes.", nameof(masterKey));
        }

        if (recoveryKeySeed.Length != 32)
        {
            throw new ArgumentException("Recovery key seed must be exactly 32 bytes.", nameof(recoveryKeySeed));
        }

        // Slot 0: Password wrap via Argon2id
        var (passwordDerivedKey, passwordSalt) = KeyDerivation.DeriveFromPassword(
            password,
            memoryCostKb: memoryCostKb,
            iterations: iterations,
            parallelism: parallelism);

        byte[] passwordNonce = new byte[WrappedKeyPair.NonceSize];
        RandomNumberGenerator.Fill(passwordNonce);
        byte[] passwordCiphertext = new byte[WrappedKeyPair.MasterKeySize];
        byte[] passwordTag = new byte[WrappedKeyPair.TagSize];

        try
        {
            using var aes = new AesGcm(passwordDerivedKey.AsReadOnlySpan(), WrappedKeyPair.TagSize);
            aes.Encrypt(passwordNonce, masterKey.AsReadOnlySpan(), passwordCiphertext, passwordTag);
        }
        finally
        {
            passwordDerivedKey.Dispose();
        }

        byte[] passwordWrappedBlob = new byte[WrappedKeyPair.WrappedKeySize];
        passwordCiphertext.CopyTo(passwordWrappedBlob.AsSpan(0, WrappedKeyPair.MasterKeySize));
        passwordTag.CopyTo(passwordWrappedBlob.AsSpan(WrappedKeyPair.MasterKeySize, WrappedKeyPair.TagSize));

        // Slot 1: Recovery key wrap via HKDF
        byte[] recoverySalt = new byte[WrappedKeyPair.SaltSize];
        RandomNumberGenerator.Fill(recoverySalt);

        using var seedBuffer = new SecureBuffer(recoveryKeySeed);
        using var recoveryDerivedKey = KeyDerivation.DeriveSubkey(seedBuffer, RecoveryInfo, 32, recoverySalt);

        byte[] recoveryNonce = new byte[WrappedKeyPair.NonceSize];
        RandomNumberGenerator.Fill(recoveryNonce);
        byte[] recoveryCiphertext = new byte[WrappedKeyPair.MasterKeySize];
        byte[] recoveryTag = new byte[WrappedKeyPair.TagSize];

        using (var aes = new AesGcm(recoveryDerivedKey.AsReadOnlySpan(), WrappedKeyPair.TagSize))
        {
            aes.Encrypt(recoveryNonce, masterKey.AsReadOnlySpan(), recoveryCiphertext, recoveryTag);
        }

        byte[] recoveryWrappedBlob = new byte[WrappedKeyPair.WrappedKeySize];
        recoveryCiphertext.CopyTo(recoveryWrappedBlob.AsSpan(0, WrappedKeyPair.MasterKeySize));
        recoveryTag.CopyTo(recoveryWrappedBlob.AsSpan(WrappedKeyPair.MasterKeySize, WrappedKeyPair.TagSize));

        return new WrappedKeyPair
        {
            PasswordSalt = passwordSalt,
            PasswordWrappedKey = passwordWrappedBlob,
            PasswordWrappedKeyNonce = passwordNonce,
            RecoverySalt = recoverySalt,
            RecoveryWrappedKey = recoveryWrappedBlob,
            RecoveryWrappedKeyNonce = recoveryNonce
        };
    }

    public static SecureBuffer UnwrapWithPassword(
        WrappedKeyPair wrapped,
        string password,
        int memoryCostKb = KeyDerivation.DefaultMemoryCostKb,
        int iterations = KeyDerivation.DefaultIterations,
        int parallelism = KeyDerivation.DefaultParallelism)
    {
        ArgumentNullException.ThrowIfNull(wrapped);
        ArgumentNullException.ThrowIfNull(password);

        var (passwordDerivedKey, _) = KeyDerivation.DeriveFromPassword(
            password,
            wrapped.PasswordSalt,
            memoryCostKb: memoryCostKb,
            iterations: iterations,
            parallelism: parallelism);

        var masterKey = new SecureBuffer(WrappedKeyPair.MasterKeySize);
        try
        {
            using var aes = new AesGcm(passwordDerivedKey.AsReadOnlySpan(), WrappedKeyPair.TagSize);
            ReadOnlySpan<byte> ciphertext = wrapped.PasswordWrappedKey.AsSpan(0, WrappedKeyPair.MasterKeySize);
            ReadOnlySpan<byte> tag = wrapped.PasswordWrappedKey.AsSpan(WrappedKeyPair.MasterKeySize, WrappedKeyPair.TagSize);

            aes.Decrypt(wrapped.PasswordWrappedKeyNonce, ciphertext, tag, masterKey.AsSpan());
            return masterKey;
        }
        catch (CryptographicException ex)
        {
            masterKey.Dispose();
            throw new InvalidPasswordException("Failed to unlock vault: invalid password.", ex);
        }
        finally
        {
            passwordDerivedKey.Dispose();
        }
    }

    public static SecureBuffer UnwrapWithRecoveryKey(
        WrappedKeyPair wrapped,
        ReadOnlySpan<byte> recoveryKeySeed)
    {
        ArgumentNullException.ThrowIfNull(wrapped);

        if (recoveryKeySeed.Length != 32)
        {
            throw new ArgumentException("Recovery key seed must be 32 bytes.", nameof(recoveryKeySeed));
        }

        using var seedBuffer = new SecureBuffer(recoveryKeySeed);
        using var recoveryDerivedKey = KeyDerivation.DeriveSubkey(seedBuffer, RecoveryInfo, 32, wrapped.RecoverySalt);

        var masterKey = new SecureBuffer(WrappedKeyPair.MasterKeySize);
        try
        {
            using var aes = new AesGcm(recoveryDerivedKey.AsReadOnlySpan(), WrappedKeyPair.TagSize);
            ReadOnlySpan<byte> ciphertext = wrapped.RecoveryWrappedKey.AsSpan(0, WrappedKeyPair.MasterKeySize);
            ReadOnlySpan<byte> tag = wrapped.RecoveryWrappedKey.AsSpan(WrappedKeyPair.MasterKeySize, WrappedKeyPair.TagSize);

            aes.Decrypt(wrapped.RecoveryWrappedKeyNonce, ciphertext, tag, masterKey.AsSpan());
            return masterKey;
        }
        catch (CryptographicException ex)
        {
            masterKey.Dispose();
            throw new InvalidRecoveryKeyException("Failed to unlock vault: invalid recovery key.", ex);
        }
    }

    public static WrappedKeyPair RewrapPasswordOnly(
        WrappedKeyPair existing,
        SecureBuffer masterKey,
        string newPassword,
        int memoryCostKb = KeyDerivation.DefaultMemoryCostKb,
        int iterations = KeyDerivation.DefaultIterations,
        int parallelism = KeyDerivation.DefaultParallelism)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(masterKey);
        ArgumentNullException.ThrowIfNull(newPassword);

        var (newPasswordDerivedKey, newSalt) = KeyDerivation.DeriveFromPassword(
            newPassword,
            memoryCostKb: memoryCostKb,
            iterations: iterations,
            parallelism: parallelism);

        byte[] newNonce = new byte[WrappedKeyPair.NonceSize];
        RandomNumberGenerator.Fill(newNonce);
        byte[] ciphertext = new byte[WrappedKeyPair.MasterKeySize];
        byte[] tag = new byte[WrappedKeyPair.TagSize];

        try
        {
            using var aes = new AesGcm(newPasswordDerivedKey.AsReadOnlySpan(), WrappedKeyPair.TagSize);
            aes.Encrypt(newNonce, masterKey.AsReadOnlySpan(), ciphertext, tag);
        }
        finally
        {
            newPasswordDerivedKey.Dispose();
        }

        byte[] newWrappedBlob = new byte[WrappedKeyPair.WrappedKeySize];
        ciphertext.CopyTo(newWrappedBlob.AsSpan(0, WrappedKeyPair.MasterKeySize));
        tag.CopyTo(newWrappedBlob.AsSpan(WrappedKeyPair.MasterKeySize, WrappedKeyPair.TagSize));

        return new WrappedKeyPair
        {
            PasswordSalt = newSalt,
            PasswordWrappedKey = newWrappedBlob,
            PasswordWrappedKeyNonce = newNonce,
            RecoverySalt = existing.RecoverySalt,
            RecoveryWrappedKey = existing.RecoveryWrappedKey,
            RecoveryWrappedKeyNonce = existing.RecoveryWrappedKeyNonce
        };
    }
}
