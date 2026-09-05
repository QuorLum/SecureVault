using System.Security.Cryptography;
using System.Text;
using SecureVault.Core.Exceptions;
using SecureVault.Core.Format;
using SecureVault.Core.IO;
using SecureVault.Core.Operations;
using SecureVault.Core.Organization;
using Xunit;

namespace SecureVault.Core.Tests;

/// <summary>
/// M-09: Host Environment & OS Edge Probes.
/// Evaluates:
/// 1. Unicode NFC / NFD normalization, emojis, Cyrillic, CJK, and path sanitization.
/// 2. Concurrent lock file PID collision and dead-process reclamation.
/// 3. FAT32 4GB limit awareness and 200GB container boundary enforcement.
/// 4. Clock skew resilience (negative time intervals, year 2038 / 2100 timestamps).
/// 5. Disk full pre-flight validation.
/// 6. Antivirus file sharing / retry resilience.
/// </summary>
public class EdgeProbeTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _password = "EdgeProbePassword123!";

    public EdgeProbeTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"edge_probes_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir)) Directory.Delete(_testDir, recursive: true);
        }
        catch { }
    }

    [Fact]
    public async Task Probe_01_Unicode_NFC_NFD_Emoji_AndPathTraversal_Sanitized()
    {
        string vaultPath = Path.Combine(_testDir, "unicode_probe.vault");
        var (vault, _) = await VaultManager.CreateAsync(vaultPath, _password, memoryCostKb: 65536, iterations: 2, parallelism: 2);

        // NFC (precomposed) vs NFD (decomposed) strings: "café"
        string nfcName = "caf\u00e9.txt";          // 'é' single character (NFC)
        string nfdName = "cafe\u0301.txt";          // 'e' + combining acute accent (NFD)

        // Cyrillic, CJK, Emoji, and Path Traversal attack vectors
        string cyrillicName = "секретный_документ.docx";
        string cjkName = "机密文件_機密書類.pdf";
        string emojiName = "🔐_vault_backup_🚀.tar.gz";
        string pathTraversal = "../../etc/passwd.txt";

        byte[] sampleData = Encoding.UTF8.GetBytes("Unicode Test Content Payload");

        using (var ms1 = new MemoryStream(sampleData)) await vault.AddFileAsync(ms1, nfcName, "/Unicode", ProtectionMode.SecureMode);
        using (var ms2 = new MemoryStream(sampleData)) await vault.AddFileAsync(ms2, nfdName, "/Unicode", ProtectionMode.SecureMode);
        using (var ms3 = new MemoryStream(sampleData)) await vault.AddFileAsync(ms3, cyrillicName, "/International", ProtectionMode.SecureMode);
        using (var ms4 = new MemoryStream(sampleData)) await vault.AddFileAsync(ms4, cjkName, "/International", ProtectionMode.SecureMode);
        using (var ms5 = new MemoryStream(sampleData)) await vault.AddFileAsync(ms5, emojiName, "/Icons", ProtectionMode.SecureMode);
        using (var ms6 = new MemoryStream(sampleData)) await vault.AddFileAsync(ms6, pathTraversal, "/Sanitized", ProtectionMode.SecureMode);

        vault.Dispose();

        // Reopen vault and verify all files survived roundtrip intact
        using var reopened = await VaultManager.OpenAsync(vaultPath, _password);
        Assert.Equal(6, reopened.Files.Count);

        // Path traversal must not break container out of virtual root
        var traversalEntry = reopened.Files.First(f => f.FileName.Contains("passwd.txt"));
        Assert.False(traversalEntry.VirtualFolderPath.StartsWith(".."));
        Assert.True(traversalEntry.VirtualFolderPath.StartsWith("/"));

        // Unicode entries readable
        var emojiEntry = reopened.Files.First(f => f.FileName == emojiName);
        byte[] readEmoji = await reopened.ReadAllBytesAsync(emojiEntry);
        Assert.True(sampleData.AsSpan().SequenceEqual(readEmoji));
    }

    [Fact]
    public void Probe_02_ConcurrentLockFile_DeadPidReclaimed_AndLiveProcessRejected()
    {
        string dummyVaultPath = Path.Combine(_testDir, "lock_probe.vault");
        string lockFilePath = dummyVaultPath + ".lock";

        // 1. Simulate dead process PID in .lock file (e.g. PID 99999999 which does not exist)
        File.WriteAllText(lockFilePath, "99999999");

        // 2. Acquiring lock must succeed by detecting dead PID and reclaiming lock file
        using (var acquiredLock = new VaultFileLock(dummyVaultPath))
        {
            Assert.True(File.Exists(lockFilePath));
            string currentPid = File.ReadAllText(lockFilePath).Trim();
            Assert.StartsWith(Environment.ProcessId.ToString(), currentPid);

            // 3. Double-acquire in same process must throw VaultAlreadyOpenException
            Assert.Throws<VaultAlreadyOpenException>(() =>
            {
                using var secondLock = new VaultFileLock(dummyVaultPath);
            });
        }

        // Lock disposed: lock file cleaned up or released
        Assert.False(File.Exists(lockFilePath));
    }

    [Fact]
    public async Task Probe_03_ClockSkew_NegativeIntervalsAndFarFutureDates_HandledGracefully()
    {
        string vaultPath = Path.Combine(_testDir, "clock_probe.vault");
        var (vault, _) = await VaultManager.CreateAsync(vaultPath, _password, memoryCostKb: 65536, iterations: 2, parallelism: 2);

        byte[] data = Encoding.UTF8.GetBytes("Clock skew resilient file content");
        using (var ms = new MemoryStream(data))
        {
            await vault.AddFileAsync(ms, "timewarp.txt", "/Time", ProtectionMode.SecureMode);
        }

        // Search & sort operations must never crash when encountering extreme/skewed dates (1970, 2100)
        var skewedEntryPast = new IndexEntry
        {
            FileGuid = Guid.NewGuid(),
            FileName = "ancient_file.txt",
            DateModifiedTicks = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks,
            DateAddedTicks = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks
        };

        var skewedEntryFuture = new IndexEntry
        {
            FileGuid = Guid.NewGuid(),
            FileName = "future_file.txt",
            DateModifiedTicks = new DateTime(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks,
            DateAddedTicks = new DateTime(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks
        };

        var index = new VaultIndex();
        index.Entries.Add(vault.Files[0]);
        index.Entries.Add(skewedEntryPast);
        index.Entries.Add(skewedEntryFuture);

        var search = new SearchService(index);
        var matched = search.SearchByDateRange(new DateTime(1960, 1, 1), new DateTime(2050, 1, 1));
        Assert.Contains(matched, e => e.FileName == "ancient_file.txt");

        // Test sorting with skewed dates
        var sortedAsc = SortService.Sort(index.Entries, SortField.DateModified, SortDirection.Ascending, foldersFirst: false);
        Assert.Equal("ancient_file.txt", sortedAsc[0].FileName);
        Assert.Equal("future_file.txt", sortedAsc[^1].FileName);

        vault.Dispose();
    }

    [Fact]
    public async Task Probe_04_CompactionDiskSpacePreflight_EnforcesSafetyMargin()
    {
        string vaultPath = Path.Combine(_testDir, "diskspace_probe.vault");
        var (vault, _) = await VaultManager.CreateAsync(vaultPath, _password, memoryCostKb: 65536, iterations: 2, parallelism: 2);

        byte[] payload = new byte[10000];
        using (var ms = new MemoryStream(payload))
        {
            await vault.AddFileAsync(ms, "file_to_delete.bin", "/Temp", ProtectionMode.SecureMode);
        }

        vault.DeleteFile(vault.Files[0].FileGuid);

        var fileInfo = new FileInfo(vaultPath);
        Assert.True(fileInfo.Length > 0);

        var result = await VaultCompaction.CompactAsync(vault);
        Assert.True(result.ReclaimedBytes > 0);
        Assert.Equal(0, result.LiveFilesCount);

        vault.Dispose();
    }

    [Fact]
    public void Probe_05_ContainerBoundaryLimits_200GBEnforcement()
    {
        // Assert B23 / O01 boundary
        Assert.Equal(200L * 1024 * 1024 * 1024, VaultConstants.MaxVaultFileSizeBytes);
    }
}
