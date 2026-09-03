using SecureVault.Core.Exceptions;
using SecureVault.Core.IO;

namespace SecureVault.Core.Tests;

public class VaultFileLockTests
{
    [Fact]
    public void Acquire_CreatesLockFile_AndDisposesCleanly()
    {
        string tempVaultPath = Path.Combine(Path.GetTempPath(), $"test_lock_{Guid.NewGuid():N}.vault");
        string lockFilePath = tempVaultPath + ".lock";

        try
        {
            using (var fileLock = new VaultFileLock(tempVaultPath))
            {
                Assert.True(File.Exists(lockFilePath));
                string content = File.ReadAllText(lockFilePath);
                Assert.Contains(Environment.ProcessId.ToString(), content);
            }

            Assert.False(File.Exists(lockFilePath));
        }
        finally
        {
            if (File.Exists(lockFilePath)) File.Delete(lockFilePath);
            if (File.Exists(tempVaultPath)) File.Delete(tempVaultPath);
        }
    }

    [Fact]
    public void DoubleAcquire_InSameProcess_ThrowsVaultAlreadyOpenException()
    {
        string tempVaultPath = Path.Combine(Path.GetTempPath(), $"test_lock_double_{Guid.NewGuid():N}.vault");

        try
        {
            using var lock1 = new VaultFileLock(tempVaultPath);
            Assert.Throws<VaultAlreadyOpenException>(() => new VaultFileLock(tempVaultPath));
        }
        finally
        {
            if (File.Exists(tempVaultPath + ".lock")) File.Delete(tempVaultPath + ".lock");
        }
    }

    [Fact]
    public void StaleLockFromCrashedProcess_IsCleanlyReclaimed()
    {
        // REVIEWER REQUIREMENT: Automated test verifying that a dead PID in .vault.lock is cleaned up and lock acquired
        string tempVaultPath = Path.Combine(Path.GetTempPath(), $"test_crash_lock_{Guid.NewGuid():N}.vault");
        string lockFilePath = tempVaultPath + ".lock";

        try
        {
            // Simulate a previous session where the process crashed (using a definitely non-existent PID like 999999)
            int deadPid = 999999;
            while (VaultFileLock.IsProcessAlive(deadPid))
            {
                deadPid++;
            }

            File.WriteAllText(lockFilePath, $"{deadPid}:{DateTime.UtcNow:o}:CRASHED_MACHINE");
            Assert.True(File.Exists(lockFilePath));

            // Acquiring should recognize dead PID, purge stale lock file, and succeed
            using (var fileLock = new VaultFileLock(tempVaultPath))
            {
                Assert.True(File.Exists(lockFilePath));
                string content = File.ReadAllText(lockFilePath);
                Assert.Contains(Environment.ProcessId.ToString(), content);
            }

            Assert.False(File.Exists(lockFilePath));
        }
        finally
        {
            if (File.Exists(lockFilePath)) File.Delete(lockFilePath);
        }
    }
}
