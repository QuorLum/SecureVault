using SecureVault.Core.Crypto;
using SecureVault.Core.Format;

namespace SecureVault.Core.Tests;

public class VaultIndexTests
{
    [Fact]
    public void Index_SerializesAndDeserializesEntries()
    {
        var index = new VaultIndex();
        index.Entries.Add(new IndexEntry
        {
            FileGuid = Guid.NewGuid(),
            FileName = "document.pdf",
            OriginalSize = 1048576,
            ProtectionMode = ProtectionMode.SecureMode,
            VirtualFolderPath = "/Work"
        });

        byte[] serialized = index.Serialize();
        var deserialized = VaultIndex.Deserialize(serialized);

        Assert.Single(deserialized.Entries);
        Assert.Equal("document.pdf", deserialized.Entries[0].FileName);
        Assert.Equal("/Work", deserialized.Entries[0].VirtualFolderPath);
    }

    [Fact]
    public void CorruptedPrimaryIndex_FallsBackToBackupIndex()
    {
        using var masterKey = new SecureBuffer(32); masterKey.AsSpan().Fill(0xEF);
        using var encryption = new EncryptionService(masterKey);
        var rs = new ReedSolomonCodec();

        var index = new VaultIndex();
        index.Entries.Add(new IndexEntry
        {
            FileGuid = Guid.NewGuid(),
            FileName = "critical_backup.txt",
            OriginalSize = 256
        });

        using var ms = new MemoryStream();
        var (pOff, pLen, bOff, bLen) = index.WriteToVault(ms, encryption, rs);

        var header = new VaultHeader
        {
            PrimaryIndexOffset = pOff,
            PrimaryIndexLength = pLen,
            BackupIndexOffset = bOff,
            BackupIndexLength = bLen
        };

        // Corrupt primary index payload on disk completely
        ms.Seek((long)pOff + 35, SeekOrigin.Begin);
        ms.Write(new byte[50]); // zero out ciphertext and parity

        // ReadFromVault should detect primary corruption and recover from backup index
        var recovered = VaultIndex.ReadFromVault(ms, encryption, rs, header);
        Assert.Single(recovered.Entries);
        Assert.Equal("critical_backup.txt", recovered.Entries[0].FileName);
    }
}
