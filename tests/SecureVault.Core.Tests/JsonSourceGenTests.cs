using System.Text.Json;
using SecureVault.Core.Backup;
using SecureVault.Core.IO;
using SecureVault.Core.MultiVault;
using SecureVault.Core.Notes;
using Xunit;

namespace SecureVault.Core.Tests;

public class JsonSourceGenTests
{
    [Fact]
    public void SettingsData_SourceGen_RoundTripsSuccessfully()
    {
        var settings = new SettingsData
        {
            LastVaultPath = @"C:\Users\Test\Documents\Personal.vault",
            HasCompletedFirstRun = true,
            RecentVaults = new List<string>
            {
                @"C:\Users\Test\Documents\Personal.vault",
                @"D:\Vaults\Work.vault"
            }
        };

        byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(settings, SecureVaultJsonContext.Default.SettingsData);
        Assert.NotEmpty(jsonBytes);

        var deserialized = JsonSerializer.Deserialize(jsonBytes, SecureVaultJsonContext.Default.SettingsData);
        Assert.NotNull(deserialized);
        Assert.Equal(settings.LastVaultPath, deserialized.LastVaultPath);
        Assert.True(deserialized.HasCompletedFirstRun);
        Assert.Equal(2, deserialized.RecentVaults.Count);
        Assert.Equal(@"C:\Users\Test\Documents\Personal.vault", deserialized.RecentVaults[0]);
    }

    [Fact]
    public void NoteDocument_SourceGen_RoundTripsSuccessfully()
    {
        var note = new NoteDocument
        {
            Title = "Classified Project",
            Content = "# Secret\nThis is a highly classified note.",
            Format = NoteFormat.Markdown,
            CreatedUtc = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            ModifiedUtc = new DateTime(2026, 1, 2, 15, 30, 0, DateTimeKind.Utc)
        };

        byte[] bytes = note.Serialize();
        Assert.NotEmpty(bytes);

        var deserialized = NoteDocument.Deserialize(bytes);
        Assert.NotNull(deserialized);
        Assert.Equal("Classified Project", deserialized.Title);
        Assert.Equal("# Secret\nThis is a highly classified note.", deserialized.Content);
        Assert.Equal(NoteFormat.Markdown, deserialized.Format);
        Assert.Equal(note.CreatedUtc, deserialized.CreatedUtc);
        Assert.Equal(note.ModifiedUtc, deserialized.ModifiedUtc);
    }

    [Fact]
    public void BackupManifest_SourceGen_RoundTripsSuccessfully()
    {
        var manifest = new BackupManifest
        {
            VaultName = "Personal",
            VaultUUID = Guid.NewGuid(),
            FormatVersion = 1,
            TotalSizeBytes = 1048576,
            IsSplit = true,
            SplitSizeBytes = 524288,
            ChainSha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            ChainParts = new List<BackupChainPartEntry>
            {
                new()
                {
                    PartIndex = 0,
                    VaultFileName = "Personal.vault",
                    VaultFileSizeBytes = 1048576,
                    VaultFileSha256 = "abc123def456",
                    Splits = new List<BackupSplitPartEntry>
                    {
                        new() { FileName = "Personal.vault.part001", Index = 0, Offset = 0, SizeBytes = 524288, Sha256 = "hash1" },
                        new() { FileName = "Personal.vault.part002", Index = 1, Offset = 524288, SizeBytes = 524288, Sha256 = "hash2" }
                    }
                }
            }
        };

        string tempFile = Path.GetTempFileName();
        try
        {
            manifest.SaveToFile(tempFile);
            var loaded = BackupManifest.LoadFromFile(tempFile);

            Assert.NotNull(loaded);
            Assert.Equal(manifest.VaultName, loaded.VaultName);
            Assert.Equal(manifest.VaultUUID, loaded.VaultUUID);
            Assert.True(loaded.IsSplit);
            Assert.Single(loaded.ChainParts);
            Assert.Equal(2, loaded.ChainParts[0].Splits.Count);
            Assert.Equal("Personal.vault.part001", loaded.ChainParts[0].Splits[0].FileName);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void VaultChainManifest_SourceGen_RoundTripsSuccessfully()
    {
        var chain = new VaultChainManifest
        {
            VaultName = "LargeVault",
            VaultUUID = Guid.NewGuid(),
            FormatVersion = 1,
            TotalFiles = 42,
            TotalSizeBytes = 50000000,
            Parts = new List<VaultChainPartInfo>
            {
                new() { PartIndex = 0, FileName = "LargeVault.vault", FileSizeBytes = 25000000, FileSha256 = "hashA" },
                new() { PartIndex = 1, FileName = "LargeVault.vault2", FileSizeBytes = 25000000, FileSha256 = "hashB" }
            }
        };

        string tempFile = Path.GetTempFileName();
        try
        {
            chain.SaveToFile(tempFile);
            var loaded = VaultChainManifest.LoadFromFile(tempFile);

            Assert.NotNull(loaded);
            Assert.Equal("LargeVault", loaded.VaultName);
            Assert.Equal(42, loaded.TotalFiles);
            Assert.Equal(2, loaded.Parts.Count);
            Assert.Equal("LargeVault.vault2", loaded.Parts[1].FileName);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
