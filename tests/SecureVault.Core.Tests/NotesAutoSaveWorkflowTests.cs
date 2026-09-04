using System.Text;
using SecureVault.Core.Format;
using SecureVault.Core.Notes;
using Xunit;

namespace SecureVault.Core.Tests;

public class NotesAutoSaveWorkflowTests
{
    [Fact]
    public async Task RepeatedAutoSave_UpdatesExistingEntry_LeavesNoDuplicates()
    {
        string vaultPath = Path.Combine(Path.GetTempPath(), $"note_autosave_{Guid.NewGuid():N}.vault");
        string password = "AutoSavePassword123!";

        try
        {
            var (vault, _) = await VaultManager.CreateAsync(vaultPath, password);
            using (vault)
            {
                IndexEntry? currentEntry = null;

                // Simulate 5 consecutive auto-save cycles
                for (int i = 1; i <= 5; i++)
                {
                    var doc = new NoteDocument
                    {
                        Title = "My Living Note",
                        Content = $"Revision {i} content text."
                    };

                    byte[] serialized = doc.Serialize();
                    using var ms = new MemoryStream(serialized);
                    string fileName = "My Living Note.md";

                    if (currentEntry != null)
                    {
                        Guid oldGuid = currentEntry.FileGuid;
                        var newEntry = await vault.AddFileAsync(ms, fileName, currentEntry.VirtualFolderPath, currentEntry.ProtectionMode);
                        vault.DeleteFile(oldGuid);
                        currentEntry = newEntry;
                    }
                    else
                    {
                        var newEntry = await vault.AddFileAsync(ms, fileName, "/", ProtectionMode.SecureMode);
                        currentEntry = newEntry;
                    }

                    // Assert invariant: at each iteration, there is exactly 1 active file in the vault
                    Assert.Single(vault.Files);
                    Assert.Equal(currentEntry.FileGuid, vault.Files[0].FileGuid);
                }

                // Final verification: read content back from vault stream
                Assert.Single(vault.Files);
                using var readStream = vault.OpenFileStream(vault.Files[0]);
                using var readMs = new MemoryStream();
                readStream.CopyTo(readMs);

                var loadedDoc = NoteDocument.Deserialize(readMs.ToArray());
                Assert.Equal("My Living Note", loadedDoc.Title);
                Assert.Equal("Revision 5 content text.", loadedDoc.Content);
            }
        }
        finally
        {
            if (File.Exists(vaultPath)) File.Delete(vaultPath);
        }
    }
}
