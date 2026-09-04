using System.Text;
using SecureVault.Core;
using SecureVault.Core.Format;
using SecureVault.Core.MultiVault;
using Xunit;

namespace SecureVault.Core.Tests;

public class VaultChainManagerTests : IDisposable
{
    private readonly string _testDir;

    public VaultChainManagerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "SV_ChainTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, true); } catch { }
    }

    [Fact]
    public async Task AddFileAsync_EnforcesFileSpansPartRolloverRule_BeforeWritingChunks()
    {
        // Critical Reviewer Requirement:
        // When a file doesn't fit in the current part's remaining space, roll to a fresh part
        // BEFORE writing any of its chunks, guaranteeing no file chunks span across multiple parts.
        string masterVaultPath = Path.Combine(_testDir, "rollover.vault");
        var (vault, _) = await VaultManager.CreateAsync(masterVaultPath, "ChainPassword123!");

        // Set threshold to 1.5 MB (so after initial files, the next file exceeds remaining space)
        using var chain = new VaultChainManager(vault, maxPartSizeBytes: 1536 * 1024);

        // Add File 1 (600 KB) -> fits in .vault (Part 0)
        byte[] content1 = new byte[600 * 1024];
        Array.Fill(content1, (byte)0x11);
        IndexEntry entry1;
        using (var ms1 = new MemoryStream(content1))
        {
            entry1 = await chain.AddFileAsync(ms1, "file1.dat");
        }

        Assert.Equal(0, entry1.PartIndex);
        Assert.Empty(chain.SecondaryParts);

        // Current part (.vault) is now around ~650 KB (header + file chunks + index).
        // Remaining space in Part 0 is less than 1 MB.
        // Add File 2 (900 KB) -> exceeds remaining space of Part 0, but fits in new part (1.5 MB).
        // It MUST roll over to .vault2 BEFORE writing chunks of File 2.
        byte[] content2 = new byte[900 * 1024];
        Array.Fill(content2, (byte)0x22);
        IndexEntry entry2;
        using (var ms2 = new MemoryStream(content2))
        {
            entry2 = await chain.AddFileAsync(ms2, "file2.dat");
        }

        Assert.Equal(1, entry2.PartIndex);
        Assert.Single(chain.SecondaryParts);
        string part2Path = VaultChainManager.GetPartPath(masterVaultPath, 1);
        Assert.True(File.Exists(part2Path));

        // Confirm both files can be read transparently via the chain manager
        byte[] read1 = await chain.ReadAllBytesAsync(entry1);
        Assert.Equal(content1, read1);

        byte[] read2 = await chain.ReadAllBytesAsync(entry2);
        Assert.Equal(content2, read2);
    }

    [Fact]
    public async Task MoveFileBetweenParts_MovesChunksAndUpdatesIndicesWithoutCorruption()
    {
        string masterVaultPath = Path.Combine(_testDir, "move_file.vault");
        var (vault, _) = await VaultManager.CreateAsync(masterVaultPath, "Pass123!");

        using var chain = new VaultChainManager(vault, maxPartSizeBytes: 1024 * 1024);

        // Add File A (Part 0)
        byte[] fileAContent = Encoding.UTF8.GetBytes("File A content to move between vault parts");
        IndexEntry entryA;
        using (var ms = new MemoryStream(fileAContent))
        {
            entryA = await chain.AddFileAsync(ms, "fileA.txt");
        }
        Assert.Equal(0, entryA.PartIndex);

        // Allocate Part 1 (.vault2)
        var part1 = chain.AllocateNextPart();
        Assert.Equal(1, part1.PartIndex);

        // Move File A from Part 0 to Part 1
        await chain.MoveFileBetweenParts(entryA.FileGuid, targetPartIndex: 1);

        Assert.Equal(1, entryA.PartIndex);

        // Verify data integrity after moving
        byte[] readAfterMove = await chain.ReadAllBytesAsync(entryA);
        Assert.Equal(fileAContent, readAfterMove);
    }
}
