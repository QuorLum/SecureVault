using System.Collections.Concurrent;

namespace SecureVault.Core.Notes;

public record NoteVersion(
    int VersionNumber,
    DateTime SavedUtc,
    string Title,
    string Content,
    NoteFormat Format);

/// <summary>
/// Maintains rolling in-vault version history for encrypted notes (J09, J10).
/// Retains up to 10 historical snapshots per note with FIFO eviction.
/// </summary>
public sealed class NoteVersionHistory
{
    private const int MaxVersionsPerNote = 10;
    private readonly ConcurrentDictionary<Guid, List<NoteVersion>> _history = new();

    public void SaveVersion(Guid noteGuid, NoteDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        _history.AddOrUpdate(noteGuid,
            _ => new List<NoteVersion>
            {
                new(1, DateTime.UtcNow, document.Title, document.Content, document.Format)
            },
            (_, list) =>
            {
                lock (list)
                {
                    int nextNum = (list.Count > 0 ? list[^1].VersionNumber : 0) + 1;
                    list.Add(new NoteVersion(nextNum, DateTime.UtcNow, document.Title, document.Content, document.Format));

                    // Truncate to keep at most 10 versions
                    while (list.Count > MaxVersionsPerNote)
                    {
                        list.RemoveAt(0);
                    }
                }
                return list;
            });
    }

    public IReadOnlyList<NoteVersion> GetHistory(Guid noteGuid)
    {
        if (_history.TryGetValue(noteGuid, out var list))
        {
            lock (list)
            {
                return list.ToList();
            }
        }
        return Array.Empty<NoteVersion>();
    }

    public NoteDocument RestoreVersion(Guid noteGuid, int versionNumber)
    {
        if (!_history.TryGetValue(noteGuid, out var list))
            throw new KeyNotFoundException($"No history found for note '{noteGuid}'.");

        lock (list)
        {
            var version = list.FirstOrDefault(v => v.VersionNumber == versionNumber)
                ?? throw new KeyNotFoundException($"Version {versionNumber} was not found.");

            return new NoteDocument
            {
                Title = version.Title,
                Content = version.Content,
                Format = version.Format,
                ModifiedUtc = DateTime.UtcNow
            };
        }
    }
}
