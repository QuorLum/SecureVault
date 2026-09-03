using SecureVault.Core.Format;

namespace SecureVault.Core.Operations;

/// <summary>
/// Executes soft-delete operations on files within the vault (C08).
/// Marks the index entry as deleted without requiring an expensive full-file compaction.
/// </summary>
public static class FileDeleteOperation
{
    public static bool Execute(VaultIndex index, Guid fileGuid)
    {
        ArgumentNullException.ThrowIfNull(index);

        var entry = index.Entries.FirstOrDefault(e => e.FileGuid == fileGuid && !e.IsDeleted);
        if (entry == null)
        {
            return false;
        }

        entry.IsDeleted = true;
        entry.DateModifiedTicks = DateTime.UtcNow.Ticks;
        return true;
    }
}
