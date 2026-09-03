using System.Security.Cryptography;
using System.Text;
using SecureVault.Core.Exceptions;
using SecureVault.Core.Format;

namespace SecureVault.Core.Tests;

public class VaultLifecycleTests
{
    [Fact]
    public async Task CompleteVaultLifecycle_Create_AddFiles_Seek_ChangePassword_Recover()
    {
        string tempVaultPath = Path.Combine(Path.GetTempPath(), $"lifecycle_test_{Guid.NewGuid():N}.vault");
        string initialPassword = "VaultPassword2026!";
        string updatedPassword = "UpdatedVaultPassword2026!";

        try
        {
            // 1. Create Vault
            var (vault, recoveryWords) = await VaultManager.CreateAsync(
                tempVaultPath,
                initialPassword,
                memoryCostKb: 65536,
                iterations: 2,
                parallelism: 2);

            Assert.Equal(24, recoveryWords.Length);
            Assert.Empty(vault.Files);

            // 2. Add File A (SecureMode)
            byte[] fileABytes = Encoding.UTF8.GetBytes("Secret Secure Data Content - " + new string('A', 5000));
            using (var streamA = new MemoryStream(fileABytes))
            {
                await vault.AddFileAsync(streamA, "fileA.txt", "/Notes", ProtectionMode.SecureMode);
            }

            // 3. Add File B (FastObfuscation)
            byte[] fileBBytes = Encoding.UTF8.GetBytes("Fast Obfuscated Media Bytes - " + new string('B', 15000));
            using (var streamB = new MemoryStream(fileBBytes))
            {
                await vault.AddFileAsync(streamB, "fileB.bin", "/Media", ProtectionMode.FastObfuscation);
            }

            Assert.Equal(2, vault.Files.Count);

            // 4. Verify Read Stream & Bit-for-bit SHA-256
            var entryA = vault.Files.First(f => f.FileName == "fileA.txt");
            byte[] readA = await vault.ReadAllBytesAsync(entryA);
            Assert.True(fileABytes.AsSpan().SequenceEqual(readA));

            var entryB = vault.Files.First(f => f.FileName == "fileB.bin");
            byte[] readB = await vault.ReadAllBytesAsync(entryB);
            Assert.True(fileBBytes.AsSpan().SequenceEqual(readB));

            // 5. Test Seekable VaultFileStream
            using (var fileStream = vault.OpenFileStream(entryA))
            {
                Assert.Equal(fileABytes.Length, fileStream.Length);
                fileStream.Seek(10, SeekOrigin.Begin);
                byte[] seekBuf = new byte[20];
                int readCount = fileStream.Read(seekBuf, 0, 20);
                Assert.Equal(20, readCount);
                Assert.True(fileABytes.AsSpan(10, 20).SequenceEqual(seekBuf));
            }

            // 6. Change Password
            vault.ChangePassword(updatedPassword);

            // 7. Lock Vault
            vault.Lock();
            Assert.True(vault.IsLocked);
            Assert.Throws<VaultLockedException>(() => vault.DeleteFile(entryA.FileGuid));

            // 8. Reopen with NEW password
            using (var reopenedVault = await VaultManager.OpenAsync(tempVaultPath, updatedPassword))
            {
                Assert.Equal(2, reopenedVault.Files.Count);
                byte[] readAfterChange = await reopenedVault.ReadAllBytesAsync(reopenedVault.Files[0]);
                Assert.True(fileABytes.AsSpan().SequenceEqual(readAfterChange));
            }

            // 9. Reopen with 24-word recovery key
            using (var recoveredVault = await VaultManager.OpenWithRecoveryKeyAsync(tempVaultPath, recoveryWords))
            {
                Assert.Equal(2, recoveredVault.Files.Count);

                // 10. Soft delete file
                bool deleted = recoveredVault.DeleteFile(entryA.FileGuid);
                Assert.True(deleted);
                Assert.Single(recoveredVault.Files);
                Assert.Equal("fileB.bin", recoveredVault.Files[0].FileName);
            }
        }
        finally
        {
            if (File.Exists(tempVaultPath)) File.Delete(tempVaultPath);
            if (File.Exists(tempVaultPath + ".lock")) File.Delete(tempVaultPath + ".lock");
        }
    }
}
