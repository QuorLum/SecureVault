using System.Security.Cryptography;
using System.Text;
using SecureVault.Core.Format;
using SecureVault.Core.Operations;

namespace SecureVault.Core.Tests;

public class DuplicateDetectorTests : IDisposable
{
    private readonly string _tempVaultPath;
    private VaultManager? _vault;

    public DuplicateDetectorTests()
    {
        _tempVaultPath = Path.Combine(Path.GetTempPath(), $"dup_test_{Guid.NewGuid():N}.vault");
    }

    [Fact]
    public async Task FindDuplicates_DetectsIdenticalFiles_AndDistinguishesSharedChunks()
    {
        var (vault, _) = await VaultManager.CreateAsync(
            _tempVaultPath,
            "Password123!",
            memoryCostKb: 65536,
            iterations: 2,
            parallelism: 2);
        _vault = vault;

        byte[] payload1 = Encoding.UTF8.GetBytes("Identical file content payload - " + new string('D', 1000));
        byte[] payload2 = Encoding.UTF8.GetBytes("Unique file content - " + new string('U', 500));

        // File 1 and File 2: independent duplicate writes (waste disk space)
        using (var ms1 = new MemoryStream(payload1))
            await vault.AddFileAsync(ms1, "doc1.txt", "/Docs");

        using (var ms2 = new MemoryStream(payload1))
            await vault.AddFileAsync(ms2, "doc1_copy.txt", "/Backup");

        // File 3: distinct content
        using (var ms3 = new MemoryStream(payload2))
            await vault.AddFileAsync(ms3, "doc3.txt", "/Docs");

        var duplicates = DuplicateDetector.FindDuplicates(vault.Index);

        Assert.Single(duplicates);
        var group = duplicates[0];
        Assert.Equal(2, group.DuplicateCount);
        Assert.Equal((ulong)payload1.Length, group.WastedStorageBytes);

        // Test CoW shared copy: File 4 shares chunk offsets with File 1
        var file1 = vault.Files.First(f => f.FileName == "doc1.txt");
        var cowCopy = new IndexEntry
        {
            FileGuid = Guid.NewGuid(),
            FileName = "doc1_cow_share.txt",
            OriginalSize = file1.OriginalSize,
            CompressedSize = file1.CompressedSize,
            PlaintextSHA256 = file1.PlaintextSHA256.ToArray(),
            FileSalt = file1.FileSalt.ToArray(),
            Chunks = file1.Chunks.Select(c => new ChunkIndexEntry
            {
                ChunkSequence = c.ChunkSequence,
                AbsoluteOffset = c.AbsoluteOffset, // EXACT same physical chunk
                ChunkDataLength = c.ChunkDataLength,
                CRC32 = c.CRC32,
                Nonce = c.Nonce.ToArray(),
                AuthTag = c.AuthTag.ToArray(),
                RSParityLength = c.RSParityLength
            }).ToList()
        };
        vault.Index.Entries.Add(cowCopy);

        var duplicatesWithCow = DuplicateDetector.FindDuplicates(vault.Index);
        Assert.Single(duplicatesWithCow);
        Assert.Equal(3, duplicatesWithCow[0].DuplicateCount);
        // Wasted bytes remains payload1.Length because cowCopy shares physical chunks!
        Assert.Equal((ulong)payload1.Length, duplicatesWithCow[0].WastedStorageBytes);
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
