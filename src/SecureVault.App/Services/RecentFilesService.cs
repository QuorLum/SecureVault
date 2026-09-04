namespace SecureVault.App.Services;

/// <summary>
/// Tracks recent files opened or viewed within the vault session (D20).
/// Enforces a FIFO maximum capacity of 20 items.
/// </summary>
public sealed class RecentFilesService
{
    private const int MaxRecentFiles = 20;
    private readonly List<Guid> _recentGuids = new();
    private readonly object _lock = new();

    public event EventHandler? RecentFilesChanged;

    public IReadOnlyList<Guid> RecentGuids
    {
        get
        {
            lock (_lock)
            {
                return _recentGuids.ToList();
            }
        }
    }

    public void LoadFrom(IEnumerable<Guid> guids)
    {
        lock (_lock)
        {
            _recentGuids.Clear();
            if (guids != null)
            {
                _recentGuids.AddRange(guids.Take(MaxRecentFiles));
            }
        }
        RecentFilesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RecordAccess(Guid fileGuid)
    {
        if (fileGuid == Guid.Empty) return;

        lock (_lock)
        {
            _recentGuids.Remove(fileGuid);
            _recentGuids.Insert(0, fileGuid);

            while (_recentGuids.Count > MaxRecentFiles)
            {
                _recentGuids.RemoveAt(_recentGuids.Count - 1);
            }
        }

        RecentFilesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        lock (_lock)
        {
            _recentGuids.Clear();
        }
        RecentFilesChanged?.Invoke(this, EventArgs.Empty);
    }
}
