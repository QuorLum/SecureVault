using System.Text;
using SecureVault.Core.Security;

namespace SecureVault.Core.Tests;

public class SecureTempFileTests
{
    [Fact]
    public void SecureTempFile_WipesAndDeletesOnDisposal()
    {
        string filePath;
        using (var temp = new SecureTempFile(".dat"))
        {
            filePath = temp.FilePath;
            Assert.True(File.Exists(filePath));

            byte[] confidential = Encoding.UTF8.GetBytes("Super secret ephemeral payload for viewer: " + new string('X', 5000));
            temp.Stream.Write(confidential);
            temp.Stream.Flush();
        }

        // Assert file was purged from disk
        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public async Task SecureTempFile_DisposeAsync_WipesAndDeletes()
    {
        string filePath;
        var temp = SecureTempFile.Create(".tmp");
        filePath = temp.FilePath;
        Assert.True(File.Exists(filePath));

        byte[] confidential = Encoding.UTF8.GetBytes("Async secret payload: " + new string('Y', 3000));
        temp.Stream.Write(confidential);
        temp.Stream.Flush();

        await temp.DisposeAsync();

        Assert.False(File.Exists(filePath));
    }
}
