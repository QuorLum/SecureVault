using SecureVault.Core.Exceptions;
using SecureVault.Core.Format;

namespace SecureVault.Core.Tests;

public class ReedSolomonCodecTests
{
    [Fact]
    public void EncodeDecode_CleanData_ProducesExactMatch()
    {
        var codec = new ReedSolomonCodec();
        byte[] data = new byte[223];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i * 5 + 1);
        }

        byte[] parity = codec.Encode(data);
        Assert.Equal(32, parity.Length);

        var (repaired, errorsFixed) = codec.Decode(data, parity);
        Assert.Equal(0, errorsFixed);
        Assert.True(data.AsSpan().SequenceEqual(repaired));
    }

    [Fact]
    public void AutoRepair_SingleByteCorruption_IsCorrected()
    {
        var codec = new ReedSolomonCodec();
        byte[] data = new byte[223];
        for (int i = 0; i < data.Length; i++) data[i] = (byte)i;

        byte[] parity = codec.Encode(data);

        // Corrupt 1 byte in data
        byte[] corrupted = data.ToArray();
        corrupted[42] ^= 0xEE;

        var (repaired, errorsFixed) = codec.Decode(corrupted, parity);
        Assert.Equal(1, errorsFixed);
        Assert.True(data.AsSpan().SequenceEqual(repaired));
    }

    [Fact]
    public void AutoRepair_UpTo16ByteErrors_IsFullyRepaired()
    {
        var codec = new ReedSolomonCodec();
        byte[] data = new byte[223];
        for (int i = 0; i < data.Length; i++) data[i] = (byte)(i ^ 0x5A);

        byte[] parity = codec.Encode(data);

        // Corrupt 16 different bytes in data (maximum correctable for t=32/2=16)
        byte[] corrupted = data.ToArray();
        for (int i = 0; i < 16; i++)
        {
            corrupted[i * 10] ^= 0x7F;
        }

        var (repaired, errorsFixed) = codec.Decode(corrupted, parity);
        Assert.Equal(16, errorsFixed);
        Assert.True(data.AsSpan().SequenceEqual(repaired));
    }

    [Fact]
    public void BeyondRepairThreshold_ThrowsUncorrectableCorruptionException()
    {
        var codec = new ReedSolomonCodec();
        byte[] data = new byte[223];
        byte[] parity = codec.Encode(data);

        // Corrupt 20 bytes (exceeds t=16 error capacity)
        byte[] corrupted = data.ToArray();
        for (int i = 0; i < 20; i++)
        {
            corrupted[i * 5] ^= 0xFF;
        }

        Assert.Throws<UncorrectableCorruptionException>(() => codec.Decode(corrupted, parity));
    }
}
