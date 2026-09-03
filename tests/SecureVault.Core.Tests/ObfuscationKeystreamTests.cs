using SecureVault.Core.Crypto;

namespace SecureVault.Core.Tests;

public class ObfuscationKeystreamTests
{
    [Fact]
    public void ApplyInPlace_IsBidirectional_AndRestoresOriginalData()
    {
        using var masterKey = new SecureBuffer(32);
        masterKey.AsSpan().Fill(0x88);

        Guid fileGuid = Guid.NewGuid();
        byte[] salt = new byte[16];
        salt.AsSpan().Fill(0x12);

        byte[] original = new byte[512];
        for (int i = 0; i < original.Length; i++)
        {
            original[i] = (byte)(i * 3 + 7);
        }

        byte[] buffer = original.ToArray();

        using var keystream1 = new ObfuscationKeystream(masterKey, fileGuid, salt);
        keystream1.ApplyInPlace(buffer, 0);

        // Ciphertext should not match original
        Assert.False(original.AsSpan().SequenceEqual(buffer));

        // Applying keystream a second time reverses the XOR obfuscation
        using var keystream2 = new ObfuscationKeystream(masterKey, fileGuid, salt);
        keystream2.ApplyInPlace(buffer, 0);

        Assert.True(original.AsSpan().SequenceEqual(buffer));
    }

    [Fact]
    public void DifferentSalts_ProduceDifferentCiphertexts_ReviewerFixVerification()
    {
        // REVIEWER REQUIREMENT: Salt change must produce fresh keystream to prevent known-plaintext attacks
        using var masterKey = new SecureBuffer(32);
        masterKey.AsSpan().Fill(0x88);

        Guid fileGuid = Guid.NewGuid();
        byte[] saltA = new byte[16]; saltA.AsSpan().Fill(0x01);
        byte[] saltB = new byte[16]; saltB.AsSpan().Fill(0x02);

        byte[] bufferA = new byte[256]; bufferA.AsSpan().Fill(0x55);
        byte[] bufferB = new byte[256]; bufferB.AsSpan().Fill(0x55);

        using (var ksA = new ObfuscationKeystream(masterKey, fileGuid, saltA))
        {
            ksA.ApplyInPlace(bufferA, 0);
        }

        using (var ksB = new ObfuscationKeystream(masterKey, fileGuid, saltB))
        {
            ksB.ApplyInPlace(bufferB, 0);
        }

        Assert.False(bufferA.AsSpan().SequenceEqual(bufferB));
    }
}
