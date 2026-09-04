using System.Security.Cryptography;
using System.Text;
using SecureVault.Core.Format;
using SecureVault.Core.MultiVault;
using SecureVault.Core.Operations;

namespace SecureVault.Core.Tests;

public class VaultCompactionTests : IDisposable
{
    private readonly string _tempVaultPath;
    private VaultManager? _vault;
    private VaultChainManager? _chain;

    public VaultCompactionTests()
    {
        _tempVaultPath = Path.Combine(Path.GetTempPath(), $"compact_test_{Guid.NewGuid():N}.vault");
    }

    [Fact]
    public async Task CompactAsync_SingleVault_ReclaimsDeletedSpace_PreservesDataIntegrity()
    {
        var (vault, _) = await VaultManager.CreateAsync(
            _tempVaultPath,
            "Password123!",
            memoryCostKb: 65536,
            iterations: 2,
            parallelism: 2);
        _vault = vault;

        byte[] dataA = Encoding.UTF8.GetBytes("File A - " + new string('A', 20000));
        byte[] dataB = Encoding.UTF8.GetBytes("File B - " + new string('B', 40000));
        byte[] dataC = Encoding.UTF8.GetBytes("File C - " + new string('C', 15000));

        using (var msA = new MemoryStream(dataA)) await vault.AddFileAsync(msA, "fileA.txt");
        using (var msB = new MemoryStream(dataB)) await vault.AddFileAsync(msB, "fileB.txt");
        using (var msC = new MemoryStream(dataC)) await vault.AddFileAsync(msC, "fileC.txt");

        long preDeleteSize = new FileInfo(_tempVaultPath).Length;

        // Delete File B (40KB)
        var fileB = vault.Files.First(f => f.FileName == "fileB.txt");
        vault.DeleteFile(fileB.FileGuid);

        // Run Compaction
        var result = await VaultCompaction.CompactAsync(vault);

        Assert.Equal(2, result.LiveFilesCount);
        Assert.True(result.ReclaimedBytes > 30000, $"Expected > 30KB reclaimed, got {result.ReclaimedBytes}");
        Assert.True(result.NewSizeBytes < preDeleteSize);

        // Ensure .pre-compact was cleaned up
        Assert.False(File.Exists($"{_tempVaultPath}.pre-compact"));
        Assert.False(File.Exists($"{_tempVaultPath}.compact.tmp"));

        // Verify remaining files decrypt cleanly
        var entryA = vault.Files.First(f => f.FileName == "fileA.txt");
        var entryC = vault.Files.First(f => f.FileName == "fileC.txt");

        byte[] readA = await vault.ReadAllBytesAsync(entryA);
        byte[] readC = await vault.ReadAllBytesAsync(entryC);

        Assert.True(dataA.AsSpan().SequenceEqual(readA));
        Assert.True(dataC.AsSpan().SequenceEqual(readC));
    }

    [Fact]
    public async Task CompactChainAsync_SecondaryPart_UpdatesLocalIndexAndMasterGlobalIndex()
    {
        var (vault, _) = await VaultManager.CreateAsync(
            _tempVaultPath,
            "Password123!",
            memoryCostKb: 65536,
            iterations: 2,
            parallelism: 2);
        _vault = vault;

        // Use low threshold to force secondary part creation
        _chain = new VaultChainManager(vault, maxPartSizeBytes: 60000);

        byte[] data1 = Encoding.UTF8.GetBytes("Part0 File - " + new string('0', 10000));
        byte[] data2 = Encoding.UTF8.GetBytes("Part1 File To Keep - " + new string('K', 15000));
        byte[] data3 = Encoding.UTF8.GetBytes("Part1 File To Delete - " + new string('D', 20000));

        using (var ms1 = new MemoryStream(data1)) await _chain.AddFileAsync(ms1, "p0_file.txt");

        // Allocate secondary part for data2 and data3
        _chain.AllocateNextPart();

        using (var ms2 = new MemoryStream(data2)) await _chain.AddFileAsync(ms2, "p1_keep.txt");
        using (var ms3 = new MemoryStream(data3)) await _chain.AddFileAsync(ms3, "p1_delete.txt");

        // Verify Part 1 exists
        Assert.Contains(1, _chain.SecondaryParts.Keys);
        string part1Path = _chain.SecondaryParts[1].FilePath;
        long part1PreSize = new FileInfo(part1Path).Length;

        // Delete file 3 from chain
        var file3 = _chain.GlobalFiles.First(f => f.FileName == "p1_delete.txt");
        vault.DeleteFile(file3.FileGuid);
        _chain.SecondaryParts[1].LocalIndex.Entries.First(e => e.FileGuid == file3.FileGuid).IsDeleted = true;

        // Compact part 1
        var result = await VaultCompaction.CompactChainAsync(_chain, specificPartIndex: 1);

        Assert.True(result.ReclaimedBytes > 10000);
        long part1PostSize = new FileInfo(part1Path).Length;
        Assert.True(part1PostSize < part1PreSize);

        // Verify remaining file in Part 1 can still be read seamlessly through VaultChainManager
        var keepFile = _chain.GlobalFiles.First(f => f.FileName == "p1_keep.txt");
        byte[] readKeep = await _chain.ReadAllBytesAsync(keepFile);
        Assert.True(data2.AsSpan().SequenceEqual(readKeep));
    }

    public void Dispose()
    {
        _chain?.Dispose();
        _vault?.Dispose();

        for (int i = 0; i < 5; i++)
        {
            string p = VaultChainManager.GetPartPath(_tempVaultPath, i);
            if (File.Exists(p))
            {
                try { File.Delete(p); } catch { }
            }
        }
    }
}
