using System.Security.Cryptography;
using System.Text;
using SecureVault.Core.Format;
using SecureVault.Core.Operations;
using SecureVault.Core.Organization;

namespace SecureVault.Core.Tests;

public class InteractionAuditTests : IDisposable
{
    private readonly string _tempVaultPath1;
    private readonly string _tempVaultPath2;
    private VaultManager? _vault1;
    private VaultManager? _vault2;

    public InteractionAuditTests()
    {
        _tempVaultPath1 = Path.Combine(Path.GetTempPath(), $"audit_interaction_1_{Guid.NewGuid():N}.vault");
        _tempVaultPath2 = Path.Combine(Path.GetTempPath(), $"audit_interaction_2_{Guid.NewGuid():N}.vault");
    }

    [Fact]
    public async Task Test1_AddFileA_CopyToFileB_ReplaceFileA_AssertFileBUnchanged()
    {
        // 1. Create a vault, add File A
        var (vault, _) = await VaultManager.CreateAsync(
            _tempVaultPath1,
            "AuditPassword123!",
            memoryCostKb: 65536,
            iterations: 2,
            parallelism: 2);
        _vault1 = vault;

        byte[] originalContentA = Encoding.UTF8.GetBytes("Original File A content - " + new string('A', 5000));
        IndexEntry entryA;
        using (var msA = new MemoryStream(originalContentA))
        {
            entryA = await vault.AddFileAsync(msA, "FileA.txt", "/", ProtectionMode.SecureMode);
        }

        // 2. Copy File A to File B
        var folderService = new VirtualFolderService(vault.Index);
        var fileOps = new FileManagementOperations(vault, folderService);
        var entryB = await fileOps.CopyAsync(entryA.FileGuid, null, "FileB.txt");

        // 3. Call FileReplaceOperation on File A with new content
        byte[] replacementContentA = Encoding.UTF8.GetBytes("Replaced File A content - " + new string('Z', 8000));
        var replacer = new FileReplaceOperation(vault);
        using (var repStream = new MemoryStream(replacementContentA))
        {
            await replacer.ReplaceFileDataAsync(entryA.FileGuid, repStream);
        }

        // 4. Read File B and assert its content is byte-identical to the ORIGINAL File A content — unchanged
        byte[] readBytesB = await vault.ReadAllBytesAsync(entryB);
        Assert.True(
            originalContentA.AsSpan().SequenceEqual(readBytesB),
            "File B content must be byte-identical to the original File A content after File A was replaced.");

        // Also verify File A was indeed changed
        byte[] readBytesA = await vault.ReadAllBytesAsync(entryA);
        Assert.True(
            replacementContentA.AsSpan().SequenceEqual(readBytesA),
            "File A content must match the replacement content.");
    }

    [Fact]
    public async Task Test2_AddFileA_CopyToFileB_Compaction_AssertFileBUnchanged()
    {
        // 1. Create a vault, add File A
        var (vault, _) = await VaultManager.CreateAsync(
            _tempVaultPath2,
            "AuditPassword123!",
            memoryCostKb: 65536,
            iterations: 2,
            parallelism: 2);
        _vault2 = vault;

        byte[] originalContentA = Encoding.UTF8.GetBytes("Original File A content - " + new string('A', 5000));
        IndexEntry entryA;
        using (var msA = new MemoryStream(originalContentA))
        {
            entryA = await vault.AddFileAsync(msA, "FileA.txt", "/", ProtectionMode.SecureMode);
        }

        // 2. Copy File A to File B
        var folderService = new VirtualFolderService(vault.Index);
        var fileOps = new FileManagementOperations(vault, folderService);
        var entryB = await fileOps.CopyAsync(entryA.FileGuid, null, "FileB.txt");

        // 3. Substitute a full VaultCompaction() call for the FileReplaceOperation step
        var compactionResult = await VaultCompaction.CompactAsync(vault);
        Assert.Equal(2, compactionResult.LiveFilesCount);

        // 4. Read File B and assert its content is byte-identical to the ORIGINAL File A content — unchanged
        // Re-fetch entryB from vault.Files to ensure updated chunk offsets after compaction are used
        var refreshedEntryB = vault.Files.First(f => f.FileGuid == entryB.FileGuid);
        byte[] readBytesB = await vault.ReadAllBytesAsync(refreshedEntryB);
        Assert.True(
            originalContentA.AsSpan().SequenceEqual(readBytesB),
            "File B content must be byte-identical to the original File A content after VaultCompaction.");

        // Also assert File A is unchanged and byte-identical
        var refreshedEntryA = vault.Files.First(f => f.FileGuid == entryA.FileGuid);
        byte[] readBytesA = await vault.ReadAllBytesAsync(refreshedEntryA);
        Assert.True(
            originalContentA.AsSpan().SequenceEqual(readBytesA),
            "File A content must remain byte-identical to original File A content after VaultCompaction.");
    }

    public void Dispose()
    {
        _vault1?.Dispose();
        _vault2?.Dispose();

        if (File.Exists(_tempVaultPath1))
        {
            try { File.Delete(_tempVaultPath1); } catch { }
        }
        if (File.Exists(_tempVaultPath2))
        {
            try { File.Delete(_tempVaultPath2); } catch { }
        }
    }
}
