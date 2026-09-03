using SecureVault.Core.Crypto;

namespace SecureVault.Core.Tests;

public class KeyDerivationTests
{
    [Fact]
    public void DeriveFromPassword_DeterministicWithSameSalt()
    {
        byte[] salt = new byte[32];
        salt.AsSpan().Fill(0x42);

        using var key1 = KeyDerivation.DeriveFromPassword("MySecretPass123!", salt, memoryCostKb: 65536, iterations: 2, parallelism: 2).DerivedKey;
        using var key2 = KeyDerivation.DeriveFromPassword("MySecretPass123!", salt, memoryCostKb: 65536, iterations: 2, parallelism: 2).DerivedKey;

        Assert.Equal(32, key1.Length);
        Assert.Equal(32, key2.Length);
        Assert.True(key1.AsReadOnlySpan().SequenceEqual(key2.AsReadOnlySpan()));
    }

    [Fact]
    public void DeriveFromPassword_DifferentSalts_ProduceDifferentKeys()
    {
        using var key1 = KeyDerivation.DeriveFromPassword("SamePassword", memoryCostKb: 65536, iterations: 2, parallelism: 2).DerivedKey;
        using var key2 = KeyDerivation.DeriveFromPassword("SamePassword", memoryCostKb: 65536, iterations: 2, parallelism: 2).DerivedKey;

        Assert.False(key1.AsReadOnlySpan().SequenceEqual(key2.AsReadOnlySpan()));
    }

    [Fact]
    public void DeriveSubkey_ProducesDeterministicDistinctSubkeys()
    {
        using var parentKey = new SecureBuffer(32);
        parentKey.AsSpan().Fill(0x01);

        using var subkeyA = KeyDerivation.DeriveSubkey(parentKey, "Info-A", 32);
        using var subkeyB = KeyDerivation.DeriveSubkey(parentKey, "Info-B", 32);
        using var subkeyA2 = KeyDerivation.DeriveSubkey(parentKey, "Info-A", 32);

        Assert.True(subkeyA.AsReadOnlySpan().SequenceEqual(subkeyA2.AsReadOnlySpan()));
        Assert.False(subkeyA.AsReadOnlySpan().SequenceEqual(subkeyB.AsReadOnlySpan()));
    }
}
