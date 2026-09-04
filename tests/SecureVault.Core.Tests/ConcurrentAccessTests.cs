using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using SecureVault.Core.Format;
using SecureVault.Core.Integrity;
using SecureVault.Core.Operations;
using SecureVault.Core.Organization;
using Xunit.Abstractions;

namespace SecureVault.Core.Tests;

public class ConcurrentAccessTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _tempVaultPath;
    private VaultManager? _vault;

    public ConcurrentAccessTests(ITestOutputHelper output)
    {
        _output = output;
        _tempVaultPath = Path.Combine(Path.GetTempPath(), $"concurrent_test_{Guid.NewGuid():N}.vault");
    }

    [Fact]
    public async Task ConcurrentForegroundAndBackgroundRepair_DoesNotCorruptStreamOrDeadlock()
    {
        var totalSw = Stopwatch.StartNew();

        // 1. Create a vault and seed initial files
        var (vault, _) = await VaultManager.CreateAsync(
            _tempVaultPath,
            "ConcurrentPass123!",
            memoryCostKb: 65536,
            iterations: 2,
            parallelism: 2);
        _vault = vault;

        byte[] fileABytes = Encoding.UTF8.GetBytes("Initial File A content - " + new string('A', 30000));
        byte[] fileBBytes = Encoding.UTF8.GetBytes("Initial File B content - " + new string('B', 45000));

        IndexEntry entryA;
        using (var msA = new MemoryStream(fileABytes))
            entryA = await vault.AddFileAsync(msA, "FileA.txt", "/", ProtectionMode.SecureMode);

        IndexEntry entryB;
        using (var msB = new MemoryStream(fileBBytes))
            entryB = await vault.AddFileAsync(msB, "FileB.txt", "/", ProtectionMode.SecureMode);

        // 2. Start BackgroundRepairService with high frequency (10ms interval)
        int backgroundScansCount = 0;
        using var repairService = new BackgroundRepairService(vault);
        repairService.HealthScanCompleted += (s, r) =>
        {
            Interlocked.Increment(ref backgroundScansCount);
            Assert.True(r.OverallHealthScore >= 99.9, $"Background scan encountered issues: {r.Summary}");
        };

        repairService.Start(TimeSpan.FromMilliseconds(10));
        Assert.True(repairService.IsRunning, "BackgroundRepairService must be running.");

        var folderService = new VirtualFolderService(vault.Index);
        var fileOps = new FileManagementOperations(vault, folderService);
        var replacer = new FileReplaceOperation(vault);

        int task1Ops = 0;
        int task2Ops = 0;
        int task3Ops = 0;

        long task1ElapsedMs = 0;
        long task2ElapsedMs = 0;
        long task3ElapsedMs = 0;

        const int Task1Target = 8;
        const int Task2Target = 15;
        const int Task3Target = 6;

        // 3. Launch concurrent foreground worker tasks
        var task1 = Task.Run(async () =>
        {
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < Task1Target; i++)
            {
                var copyEntry = await fileOps.CopyAsync(entryA.FileGuid, null, $"FileA_Copy_{i}.txt");
                Assert.NotNull(copyEntry);
                Assert.NotEqual(entryA.FileGuid, copyEntry.FileGuid);

                byte[] readCopy = await vault.ReadAllBytesAsync(copyEntry);
                Assert.Equal(fileABytes.Length, readCopy.Length);
                Assert.True(fileABytes.AsSpan().SequenceEqual(readCopy), $"Task 1 Copy {i} content mismatch!");

                Interlocked.Increment(ref task1Ops);
                await Task.Delay(15);
            }
            sw.Stop();
            Interlocked.Exchange(ref task1ElapsedMs, sw.ElapsedMilliseconds);
        });

        var task2 = Task.Run(async () =>
        {
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < Task2Target; i++)
            {
                var currentB = vault.Files.First(f => f.FileGuid == entryB.FileGuid);
                byte[] readB = await vault.ReadAllBytesAsync(currentB);
                Assert.Equal(fileBBytes.Length, readB.Length);
                Assert.True(fileBBytes.AsSpan().SequenceEqual(readB), $"Task 2 Read {i} content mismatch!");

                Interlocked.Increment(ref task2Ops);
                await Task.Delay(10);
            }
            sw.Stop();
            Interlocked.Exchange(ref task2ElapsedMs, sw.ElapsedMilliseconds);
        });

        var task3 = Task.Run(async () =>
        {
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < Task3Target; i++)
            {
                byte[] newFileData = Encoding.UTF8.GetBytes($"Dynamic File {i} Payload - " + new string('D', 15000));
                IndexEntry dynEntry;
                using (var ms = new MemoryStream(newFileData))
                {
                    dynEntry = await vault.AddFileAsync(ms, $"dynamic_{i}.txt", "/Dynamic");
                }
                Assert.NotNull(dynEntry);

                // Replace data immediately
                byte[] repData = Encoding.UTF8.GetBytes($"Replaced Dynamic {i} Payload - " + new string('R', 20000));
                using (var repMs = new MemoryStream(repData))
                {
                    await replacer.ReplaceFileDataAsync(dynEntry.FileGuid, repMs);
                }

                byte[] readRep = await vault.ReadAllBytesAsync(dynEntry);
                Assert.Equal(repData.Length, readRep.Length);
                Assert.True(repData.AsSpan().SequenceEqual(readRep), $"Task 3 Replace {i} content mismatch!");

                Interlocked.Increment(ref task3Ops);
                await Task.Delay(20);
            }
            sw.Stop();
            Interlocked.Exchange(ref task3ElapsedMs, sw.ElapsedMilliseconds);
        });

        // Wait for all 3 concurrent worker tasks to complete
        await Task.WhenAll(task1, task2, task3);

        totalSw.Stop();

        // 4. Assert all worker tasks executed every single iteration
        Assert.Equal(Task1Target, task1Ops);
        Assert.Equal(Task2Target, task2Ops);
        Assert.Equal(Task3Target, task3Ops);

        // 5. Stop background repair service and verify it actually scanned while tasks ran
        repairService.Stop();
        Assert.False(repairService.IsRunning);

        _output.WriteLine($"[Concurrency Audit] Total Test Duration: {totalSw.ElapsedMilliseconds} ms");
        _output.WriteLine($"[Concurrency Audit] Task 1 (Deep Copy & Verify): {task1Ops} ops in {task1ElapsedMs} ms");
        _output.WriteLine($"[Concurrency Audit] Task 2 (Concurrent Reads): {task2Ops} ops in {task2ElapsedMs} ms");
        _output.WriteLine($"[Concurrency Audit] Task 3 (Add & Replace Chunks): {task3Ops} ops in {task3ElapsedMs} ms");
        _output.WriteLine($"[Concurrency Audit] Background Scans Completed Concurrently: {backgroundScansCount} scans");

        Assert.True(backgroundScansCount > 0, $"BackgroundRepairService should have performed at least 1 scan, but performed {backgroundScansCount}.");
        Assert.True(totalSw.ElapsedMilliseconds >= 100, $"Concurrency test completed suspiciously fast ({totalSw.ElapsedMilliseconds} ms).");

        // 6. Final deep verification: whole-vault cryptographic audit
        var finalHealth = await IntegrityChecker.CheckVaultAsync(vault);
        Assert.Equal(100.0, finalHealth.OverallHealthScore);
        Assert.Empty(finalHealth.Issues);
    }

    public void Dispose()
    {
        _vault?.Dispose();
        if (File.Exists(_tempVaultPath))
        {
            try { File.Delete(_tempVaultPath); } catch { }
        }
    }
}
