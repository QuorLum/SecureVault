using SecureVault.Core.Crypto;
using SecureVault.Core.Exceptions;

namespace SecureVault.Core.Tests;

public class KeyWrappingTests
{
    [Fact]
    public void DualKeyWrap_CanBeUnwrappedByPassword_OrRecoveryKey()
    {
        using var masterKey = new SecureBuffer(32);
        masterKey.AsSpan().Fill(0x77);

        var (words, seed) = RecoveryKeyGenerator.Generate();
        string password = "CorrectPassword456!";

        var wrapped = KeyWrapping.WrapMasterKey(
            masterKey,
            password,
            seed,
            memoryCostKb: 65536,
            iterations: 2,
            parallelism: 2);

        // 1. Unwrap with Password
        using var unwrappedViaPassword = KeyWrapping.UnwrapWithPassword(
            wrapped,
            password,
            memoryCostKb: 65536,
            iterations: 2,
            parallelism: 2);
        Assert.True(masterKey.AsReadOnlySpan().SequenceEqual(unwrappedViaPassword.AsReadOnlySpan()));

        // 2. Unwrap with Recovery Key
        using var unwrappedViaRecovery = KeyWrapping.UnwrapWithRecoveryKey(wrapped, seed);
        Assert.True(masterKey.AsReadOnlySpan().SequenceEqual(unwrappedViaRecovery.AsReadOnlySpan()));

        // 3. Wrong password throws InvalidPasswordException
        Assert.Throws<InvalidPasswordException>(() => KeyWrapping.UnwrapWithPassword(
            wrapped,
            "WrongPassword!",
            memoryCostKb: 65536,
            iterations: 2,
            parallelism: 2));

        // 4. Wrong recovery seed throws InvalidRecoveryKeyException
        byte[] wrongSeed = new byte[32];
        wrongSeed.AsSpan().Fill(0x99);
        Assert.Throws<InvalidRecoveryKeyException>(() => KeyWrapping.UnwrapWithRecoveryKey(wrapped, wrongSeed));
    }

    [Fact]
    public void RewrapPasswordOnly_UpdatesPasswordSlot_KeepsRecoverySlotIntact()
    {
        using var masterKey = new SecureBuffer(32);
        masterKey.AsSpan().Fill(0x33);

        var (_, seed) = RecoveryKeyGenerator.Generate();

        var wrapped = KeyWrapping.WrapMasterKey(
            masterKey,
            "OldPassword",
            seed,
            memoryCostKb: 65536,
            iterations: 2,
            parallelism: 2);

        var updated = KeyWrapping.RewrapPasswordOnly(
            wrapped,
            masterKey,
            "NewPassword",
            memoryCostKb: 65536,
            iterations: 2,
            parallelism: 2);

        // Recovery slot is preserved bit-for-bit
        Assert.True(wrapped.RecoverySalt.AsSpan().SequenceEqual(updated.RecoverySalt));
        Assert.True(wrapped.RecoveryWrappedKey.AsSpan().SequenceEqual(updated.RecoveryWrappedKey));

        // Old password fails
        Assert.Throws<InvalidPasswordException>(() => KeyWrapping.UnwrapWithPassword(
            updated,
            "OldPassword",
            memoryCostKb: 65536,
            iterations: 2,
            parallelism: 2));

        // New password works
        using var unwrapped = KeyWrapping.UnwrapWithPassword(
            updated,
            "NewPassword",
            memoryCostKb: 65536,
            iterations: 2,
            parallelism: 2);
        Assert.True(masterKey.AsReadOnlySpan().SequenceEqual(unwrapped.AsReadOnlySpan()));

        // Recovery key still works
        using var recoveryUnwrapped = KeyWrapping.UnwrapWithRecoveryKey(updated, seed);
        Assert.True(masterKey.AsReadOnlySpan().SequenceEqual(recoveryUnwrapped.AsReadOnlySpan()));
    }
}
