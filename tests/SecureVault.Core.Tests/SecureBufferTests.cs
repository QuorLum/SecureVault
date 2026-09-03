using SecureVault.Core.Crypto;

namespace SecureVault.Core.Tests;

public class SecureBufferTests
{
    [Fact]
    public void CreateAndRead_WritesAndReadsCorrectBytes()
    {
        using var buffer = new SecureBuffer(32);
        buffer.AsSpan().Fill(0xAA);

        var span = buffer.AsReadOnlySpan();
        Assert.Equal(32, span.Length);
        for (int i = 0; i < 32; i++)
        {
            Assert.Equal(0xAA, span[i]);
        }
    }

    [Fact]
    public void Dispose_ZeroesUnderlyingMemory_AutomatedVerification()
    {
        // REVIEWER REQUIREMENT: Automated unit test verifying memory zeroing on dispose
        byte[]? rawUnderlyingBuffer;
        using (var buffer = new SecureBuffer(64))
        {
            buffer.AsSpan().Fill(0xFF);
            rawUnderlyingBuffer = buffer.DangerousGetRawBuffer();
            Assert.NotNull(rawUnderlyingBuffer);
            Assert.All(rawUnderlyingBuffer, b => Assert.Equal(0xFF, b));

            buffer.Dispose();
        }

        // Post-disposal, assert that CryptographicOperations.ZeroMemory cleared every single byte to 0x00
        Assert.NotNull(rawUnderlyingBuffer);
        Assert.All(rawUnderlyingBuffer, b => Assert.Equal(0x00, b));
    }

    [Fact]
    public void AccessAfterDispose_ThrowsObjectDisposedException()
    {
        var buffer = new SecureBuffer(16);
        buffer.Dispose();

        Assert.Throws<ObjectDisposedException>(() => buffer.AsSpan());
        Assert.Throws<ObjectDisposedException>(() => buffer.AsReadOnlySpan());
    }

    [Fact]
    public void DoubleDispose_IsSafe()
    {
        var buffer = new SecureBuffer(16);
        buffer.Dispose();
        buffer.Dispose(); // Should not throw
    }
}
