using System.Runtime.InteropServices;
using SecureVault.Core.Media;
using Xunit;

namespace SecureVault.Core.Tests;

public class VaultMediaInputTests
{
    [Fact]
    public void Open_ReturnsCorrectSizeAndCanSeekIsTrue()
    {
        byte[] testData = new byte[100_000];
        new Random(42).NextBytes(testData);
        using var stream = new MemoryStream(testData);

        var mediaInput = new VaultMediaInput(stream);

        Assert.True(mediaInput.CanSeek);
        Assert.True(mediaInput.Open(out ulong size));
        Assert.Equal((ulong)testData.Length, size);
    }

    [Fact]
    public void ReadAndSeek_OperatesSeamlesslyAcrossOffsets()
    {
        byte[] testData = new byte[200_000];
        for (int i = 0; i < testData.Length; i++)
        {
            testData[i] = (byte)(i % 256);
        }
        var stream = new MemoryStream(testData);
        var mediaInput = new VaultMediaInput(stream);
        mediaInput.Open(out _);

        IntPtr unmanagedBuf = Marshal.AllocHGlobal(1024);
        byte[] readBack = new byte[1024];

        try
        {
            // Initial read at offset 0
            int readCount = mediaInput.Read(unmanagedBuf, 1024);
            Assert.Equal(1024, readCount);
            Marshal.Copy(unmanagedBuf, readBack, 0, 1024);
            for (int i = 0; i < 1024; i++)
            {
                Assert.Equal(testData[i], readBack[i]);
            }

            // Seek to offset 50,000
            bool seekSuccess = mediaInput.Seek(50_000);
            Assert.True(seekSuccess);

            // Read at offset 50,000
            readCount = mediaInput.Read(unmanagedBuf, 512);
            Assert.Equal(512, readCount);
            Marshal.Copy(unmanagedBuf, readBack, 0, 512);
            for (int i = 0; i < 512; i++)
            {
                Assert.Equal(testData[50_000 + i], readBack[i]);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(unmanagedBuf);
            mediaInput.Close();
        }
    }
}
