using System.Text;
using SecureVault.Core.Crypto;
using SecureVault.Core.Format;

namespace SecureVault.Core.Tests;

public class VaultHeaderTests
{
    [Fact]
    public void Header_HasExact572ByteLayout_AndValidMaskedMagic()
    {
        using var masterKey = new SecureBuffer(32);
        masterKey.AsSpan().Fill(0x99);

        var (_, recoverySeed) = RecoveryKeyGenerator.Generate();
        var header = VaultHeader.Create(
            masterKey,
            "HeaderTestPassword",
            recoverySeed,
            memoryCostKb: 65536,
            iterations: 2,
            parallelism: 2);

        using var ms = new MemoryStream();
        header.WriteTo(ms);

        // Header size assertion: exactly 572 bytes
        Assert.Equal(VaultConstants.HeaderSize, ms.Length);
        Assert.Equal(572, ms.Length);

        // Verification checklist: raw string "SVAULT01" should NOT appear in serialized binary
        string asciiDump = Encoding.ASCII.GetString(ms.ToArray());
        Assert.DoesNotContain("SVAULT01", asciiDump);

        // Read back header
        ms.Seek(0, SeekOrigin.Begin);
        var readHeader = VaultHeader.ReadFrom(ms);

        Assert.True(readHeader.VerifyMagic());
        Assert.True(readHeader.VerifyHmac(masterKey));
        Assert.Equal(header.VaultUUID, readHeader.VaultUUID);
    }

    [Fact]
    public void Header_TamperingFailsHmacVerification()
    {
        using var masterKey = new SecureBuffer(32);
        masterKey.AsSpan().Fill(0x99);

        var (_, recoverySeed) = RecoveryKeyGenerator.Generate();
        var header = VaultHeader.Create(
            masterKey,
            "HeaderTestPassword",
            recoverySeed,
            memoryCostKb: 65536,
            iterations: 2,
            parallelism: 2);

        using var ms = new MemoryStream();
        header.WriteTo(ms);

        byte[] bytes = ms.ToArray();
        // Flip one byte in the header body (e.g. index offset area)
        bytes[0x01FC] ^= 0xFF;

        using var tamperedMs = new MemoryStream(bytes);
        var readHeader = VaultHeader.ReadFrom(tamperedMs);

        Assert.False(readHeader.VerifyHmac(masterKey));
    }
}
