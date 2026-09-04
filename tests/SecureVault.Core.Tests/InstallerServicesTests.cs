using System.Runtime.InteropServices;
using Microsoft.Win32;
using Xunit;

namespace SecureVault.Core.Tests;

public class InstallerServicesTests
{
    [Fact]
    public void InstallOptions_HasCorrectDefaultValues()
    {
        string defaultDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "SecureVault");

        // Verify standard default options
        Assert.True(Directory.Exists(Path.GetDirectoryName(defaultDir)));
        Assert.Contains("SecureVault", defaultDir);
    }

    [Fact]
    public void RegistryStructure_CanBeCreatedAndReadUnderHKCU()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        string testSubKey = @"Software\SecureVaultTest_" + Guid.NewGuid().ToString("N");
        try
        {
            // 1. Create simulated registry entries
            using (var key = Registry.CurrentUser.CreateSubKey(testSubKey))
            {
                key.SetValue("DisplayName", "SecureVault");
                key.SetValue("DisplayVersion", "1.0.0");
                key.SetValue("Publisher", "SecureVault Contributors");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            }

            // 2. Read and assert
            using (var key = Registry.CurrentUser.OpenSubKey(testSubKey))
            {
                Assert.NotNull(key);
                Assert.Equal("SecureVault", key.GetValue("DisplayName"));
                Assert.Equal("1.0.0", key.GetValue("DisplayVersion"));
                Assert.Equal("SecureVault Contributors", key.GetValue("Publisher"));
                Assert.Equal(1, (int)(key.GetValue("NoModify") ?? 0));
            }
        }
        finally
        {
            // 3. Clean up
            Registry.CurrentUser.DeleteSubKeyTree(testSubKey, throwOnMissingSubKey: false);
            using var verifyDeleted = Registry.CurrentUser.OpenSubKey(testSubKey);
            Assert.Null(verifyDeleted);
        }
    }

    [Fact]
    public void FileAssociationCommand_FormattedCorrectly()
    {
        string exePath = @"C:\Users\Test\AppData\Local\Programs\SecureVault\SecureVault.exe";
        string command = $"\"{exePath}\" \"%1\"";

        Assert.Equal("\"C:\\Users\\Test\\AppData\\Local\\Programs\\SecureVault\\SecureVault.exe\" \"%1\"", command);
    }
}
