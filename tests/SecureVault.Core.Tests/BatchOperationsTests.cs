using System.Security.Cryptography;
using System.Text;
using SecureVault.Core.Format;
using SecureVault.Core.Operations;
using SecureVault.Core.Organization;

namespace SecureVault.Core.Tests;

public class BatchOperationsTests
{
    [Fact]
    public async Task BatchAdd_And_FileManagement_RoundTrip_WithVerifiedCopyAndExport()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"batch_ops_test_{Guid.NewGuid():N}");
        string vaultPath = Path.Combine(tempDir, "test.vault");
        Directory.CreateDirectory(tempDir);

        try
        {
            // 1. Create Vault
            var (vault, _) = await VaultManager.CreateAsync(vaultPath, "Password123!", memoryCostKb: 65536, iterations: 2, parallelism: 2);
            var folderService = new VirtualFolderService(new VaultIndex()); // local service
            var batchAdd = new BatchFileAddOperation(vault);
            var fileOps = new FileManagementOperations(vault, new VirtualFolderService(new VaultIndex()));

            // 2. Create sample source files on disk
            string file1 = Path.Combine(tempDir, "document.pdf");
            string file2 = Path.Combine(tempDir, "photo.jpg");
            byte[] file1Bytes = Encoding.UTF8.GetBytes("Important Contract Data - " + new string('X', 4000));
            byte[] file2Bytes = Encoding.UTF8.GetBytes("JPEG Image Data Bytes - " + new string('Y', 8000));
            File.WriteAllBytes(file1, file1Bytes);
            File.WriteAllBytes(file2, file2Bytes);

            // 3. Batch Add Files with progress
            var reportedProgress = new List<FileAddProgress>();
            var progress = new Progress<FileAddProgress>(reportedProgress.Add);

            await batchAdd.AddFilesAsync(new[] { file1, file2 }, "/Inbox", ProtectionMode.SecureMode, progress);

            Assert.Equal(2, vault.Files.Count);
            var docEntry = vault.Files.First(f => f.FileName == "document.pdf");
            var photoEntry = vault.Files.First(f => f.FileName == "photo.jpg");

            Assert.Equal((byte)FileCategory.Documents, docEntry.Category);
            Assert.Equal((byte)FileCategory.Photos, photoEntry.Category);

            // 4. Rename
            fileOps.Rename(docEntry.FileGuid, "contract_signed.pdf");
            Assert.Equal("contract_signed.pdf", docEntry.FileName);

            // 5. Move
            Guid archiveFolderGuid = Guid.NewGuid();
            fileOps.Move(docEntry.FileGuid, archiveFolderGuid);
            Assert.Equal(archiveFolderGuid, docEntry.ParentFolderGuid);

            // 6. Copy (REVIEWER REQUIREMENT: independent duplicate write without fragile chunk sharing)
            var copiedEntry = await fileOps.CopyAsync(photoEntry.FileGuid, null, "photo_copy.jpg");
            Assert.Equal(3, vault.Files.Count);
            Assert.NotEqual(photoEntry.FileGuid, copiedEntry.FileGuid);
            Assert.Equal("photo_copy.jpg", copiedEntry.FileName);
            Assert.Equal(photoEntry.OriginalSize, copiedEntry.OriginalSize);

            // Chunks must be physically distinct
            Assert.NotEqual(photoEntry.FirstChunkOffset, copiedEntry.FirstChunkOffset);

            // 7. Export with SHA-256 integrity verification
            string exportPath = Path.Combine(tempDir, "exported_photo.jpg");
            await fileOps.ExportFileAsync(photoEntry.FileGuid, exportPath);

            Assert.True(File.Exists(exportPath));
            byte[] exportedBytes = File.ReadAllBytes(exportPath);
            Assert.True(file2Bytes.AsSpan().SequenceEqual(exportedBytes));

            // Verify SHA-256 matches
            byte[] computedSha = SHA256.HashData(exportedBytes);
            Assert.True(photoEntry.PlaintextSHA256.AsSpan().SequenceEqual(computedSha));

            vault.Lock();
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
