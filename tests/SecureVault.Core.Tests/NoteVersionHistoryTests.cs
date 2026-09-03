using SecureVault.Core.Notes;
using Xunit;

namespace SecureVault.Core.Tests;

public class NoteVersionHistoryTests
{
    [Fact]
    public void SaveVersion_TruncatesTo10Snapshots_AndRestoresTargetVersion()
    {
        var history = new NoteVersionHistory();
        var noteGuid = Guid.NewGuid();

        // Save 12 consecutive versions
        for (int i = 1; i <= 12; i++)
        {
            var doc = new NoteDocument
            {
                Title = $"Version {i} Title",
                Content = $"This is the content for version {i}",
                Format = NoteFormat.Markdown
            };
            history.SaveVersion(noteGuid, doc);
        }

        var list = history.GetHistory(noteGuid);
        Assert.Equal(10, list.Count);
        // Oldest version kept should be version 3 (1 and 2 evicted)
        Assert.Equal(3, list[0].VersionNumber);
        Assert.Equal(12, list[^1].VersionNumber);

        // Restore version 7
        var restored = history.RestoreVersion(noteGuid, 7);
        Assert.Equal("Version 7 Title", restored.Title);
        Assert.Equal("This is the content for version 7", restored.Content);
    }
}
