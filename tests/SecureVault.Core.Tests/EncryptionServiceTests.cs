using SecureVault.Core.Crypto;
using SecureVault.Core.Exceptions;

namespace SecureVault.Core.Tests;

public class EncryptionServiceTests
{
    [Fact]
    public void DerivesFourDistinctSubkeys_FromMasterKey()
    {
        using var masterKey = new SecureBuffer(32);
        masterKey.AsSpan().Fill(0xAB);

        using var service = new EncryptionService(masterKey);

        Assert.Equal(32, service.IndexKey.Length);
        Assert.Equal(32, service.SecureModeKey.Length);
        Assert.Equal(32, service.ObfuscationKey.Length);
        Assert.Equal(32, service.HmacKey.Length);

        // Assert all 4 keys are distinct
        Assert.False(service.IndexKey.AsReadOnlySpan().SequenceEqual(service.SecureModeKey.AsReadOnlySpan()));
        Assert.False(service.IndexKey.AsReadOnlySpan().SequenceEqual(service.ObfuscationKey.AsReadOnlySpan()));
        Assert.False(service.IndexKey.AsReadOnlySpan().SequenceEqual(service.HmacKey.AsReadOnlySpan()));
        Assert.False(service.SecureModeKey.AsReadOnlySpan().SequenceEqual(service.ObfuscationKey.AsReadOnlySpan()));
    }

    [Fact]
    public void IndexEncryption_RoundTrips_AndDetectsTampering()
    {
        using var masterKey = new SecureBuffer(32);
        masterKey.AsSpan().Fill(0xCD);

        using var service = new EncryptionService(masterKey);

        byte[] plainIndexData = new byte[1024];
        for (int i = 0; i < plainIndexData.Length; i++) plainIndexData[i] = (byte)(i % 256);

        var (ciphertext, nonce, tag) = service.EncryptIndex(plainIndexData);

        // Verify round trip
        byte[] decrypted = service.DecryptIndex(ciphertext, nonce, tag);
        Assert.True(plainIndexData.AsSpan().SequenceEqual(decrypted));

        // Tamper with tag -> must throw CorruptedIndexException
        tag[0] ^= 0xFF;
        Assert.Throws<CorruptedIndexException>(() => service.DecryptIndex(ciphertext, nonce, tag));
    }
}
