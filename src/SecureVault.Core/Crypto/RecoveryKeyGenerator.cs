using System.Reflection;
using System.Security.Cryptography;
using SecureVault.Core.Exceptions;

namespace SecureVault.Core.Crypto;

/// <summary>
/// Implements 24-word BIP-39 recovery phrase generation and validation.
/// Encodes 256 bits of cryptographic entropy with an 8-bit SHA-256 checksum.
/// </summary>
public static class RecoveryKeyGenerator
{
    private static readonly string[] WordList;
    private static readonly Dictionary<string, int> WordToIndex;

    static RecoveryKeyGenerator()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("SecureVault.Core.Resources.english.txt")
            ?? throw new InvalidOperationException("Embedded BIP-39 wordlist resource not found.");

        using var reader = new StreamReader(stream);
        var words = new List<string>(2048);
        while (reader.ReadLine() is { } line)
        {
            string trimmed = line.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                words.Add(trimmed);
            }
        }

        if (words.Count != 2048)
        {
            throw new InvalidOperationException($"Expected 2048 BIP-39 words, found {words.Count}.");
        }

        WordList = words.ToArray();
        WordToIndex = new Dictionary<string, int>(2048, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < WordList.Length; i++)
        {
            WordToIndex[WordList[i]] = i;
        }
    }

    public static IReadOnlyList<string> AllWords => WordList;

    /// <summary>
    /// Generates a new 24-word recovery phrase and returns the corresponding 32-byte seed.
    /// </summary>
    public static (string[] Words, byte[] Seed) Generate()
    {
        byte[] seed = new byte[32];
        RandomNumberGenerator.Fill(seed);

        string[] words = SeedToWords(seed);
        return (words, seed);
    }

    /// <summary>
    /// Encodes a 32-byte entropy seed into a 24-word BIP-39 phrase.
    /// </summary>
    public static string[] SeedToWords(ReadOnlySpan<byte> seed)
    {
        if (seed.Length != 32)
        {
            throw new ArgumentException("Seed must be exactly 32 bytes (256 bits).", nameof(seed));
        }

        byte checksum = SHA256.HashData(seed)[0];

        // 33 bytes: 32 bytes entropy + 1 byte checksum (264 bits total)
        byte[] buffer = new byte[33];
        seed.CopyTo(buffer.AsSpan(0, 32));
        buffer[32] = checksum;

        string[] result = new string[24];
        for (int i = 0; i < 24; i++)
        {
            int bitIndex = i * 11;
            int byteIndex = bitIndex / 8;
            int bitInByte = bitIndex % 8;

            int b0 = buffer[byteIndex];
            int b1 = buffer[byteIndex + 1];
            int b2 = (byteIndex + 2 < 33) ? buffer[byteIndex + 2] : 0;

            int combined = (b0 << 16) | (b1 << 8) | b2;
            int index = (combined >> (24 - 11 - bitInByte)) & 0x7FF;

            result[i] = WordList[index];
        }

        return result;
    }

    /// <summary>
    /// Validates a 24-word recovery phrase and decodes it back to the original 32-byte seed.
    /// Throws InvalidRecoveryKeyException if words or checksum are invalid.
    /// </summary>
    public static byte[] WordsToSeed(string[] words)
    {
        ArgumentNullException.ThrowIfNull(words);

        if (words.Length != 24)
        {
            throw new InvalidRecoveryKeyException($"Recovery phrase must have exactly 24 words (got {words.Length}).");
        }

        byte[] buffer = new byte[33];

        for (int i = 0; i < 24; i++)
        {
            string word = words[i].Trim();
            if (!WordToIndex.TryGetValue(word, out int index))
            {
                throw new InvalidRecoveryKeyException($"Word '{word}' at position {i + 1} is not a valid BIP-39 word.");
            }

            int bitIndex = i * 11;
            for (int bit = 0; bit < 11; bit++)
            {
                int bitVal = (index >> (10 - bit)) & 1;
                int targetBit = bitIndex + bit;
                buffer[targetBit / 8] |= (byte)(bitVal << (7 - (targetBit % 8)));
            }
        }

        byte[] seed = new byte[32];
        Array.Copy(buffer, 0, seed, 0, 32);

        byte expectedChecksum = SHA256.HashData(seed)[0];
        if (buffer[32] != expectedChecksum)
        {
            throw new InvalidRecoveryKeyException("Recovery phrase checksum verification failed. Please check for typos.");
        }

        return seed;
    }
}
