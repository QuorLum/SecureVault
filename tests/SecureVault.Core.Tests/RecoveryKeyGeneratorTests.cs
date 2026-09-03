using SecureVault.Core.Crypto;
using SecureVault.Core.Exceptions;

namespace SecureVault.Core.Tests;

public class RecoveryKeyGeneratorTests
{
    [Fact]
    public void StandardBip39_AllZerosVector_MatchesKnownMnemonic()
    {
        byte[] allZeros = new byte[32];
        string[] words = RecoveryKeyGenerator.SeedToWords(allZeros);

        Assert.Equal(24, words.Length);
        for (int i = 0; i < 23; i++)
        {
            Assert.Equal("abandon", words[i]);
        }
        Assert.Equal("art", words[23]);

        byte[] recoveredSeed = RecoveryKeyGenerator.WordsToSeed(words);
        Assert.True(allZeros.AsSpan().SequenceEqual(recoveredSeed));
    }

    [Fact]
    public void Generate_Produces24ValidWords_RoundTripsAccurately()
    {
        var (words, seed) = RecoveryKeyGenerator.Generate();

        Assert.Equal(24, words.Length);
        Assert.Equal(32, seed.Length);

        byte[] decodedSeed = RecoveryKeyGenerator.WordsToSeed(words);
        Assert.True(seed.AsSpan().SequenceEqual(decodedSeed));
    }

    [Fact]
    public void CorruptedWord_ThrowsInvalidRecoveryKeyException()
    {
        var (words, _) = RecoveryKeyGenerator.Generate();
        words[0] = "nonexistentwordxyz";

        Assert.Throws<InvalidRecoveryKeyException>(() => RecoveryKeyGenerator.WordsToSeed(words));
    }

    [Fact]
    public void CorruptedChecksum_ThrowsInvalidRecoveryKeyException()
    {
        var (words, _) = RecoveryKeyGenerator.Generate();
        // Swap last word to a different valid word to invalidate checksum
        words[23] = words[23] == "zoo" ? "zone" : "zoo";

        Assert.Throws<InvalidRecoveryKeyException>(() => RecoveryKeyGenerator.WordsToSeed(words));
    }
}
