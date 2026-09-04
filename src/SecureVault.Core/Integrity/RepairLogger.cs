namespace SecureVault.Core.Integrity;

public sealed record RepairEvent
{
    public Guid FileGuid { get; init; }
    public string FileName { get; init; } = string.Empty;
    public uint ChunkSequence { get; init; }
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public int SymbolErrorsCorrected { get; init; }
    public bool ReVerificationPassed { get; init; }
    public string VerificationMethod { get; init; } = string.Empty;
    public string Details { get; init; } = string.Empty;
}

/// <summary>
/// Thread-safe auto-repair audit logger (F03).
/// Enforces the critical invariant: ONLY commit a repair to memory/disk if post-repair re-verification passes.
/// </summary>
public sealed class RepairLogger
{
    private static readonly RepairLogger _instance = new();
    public static RepairLogger Shared => _instance;

    private readonly List<RepairEvent> _events = new();
    private readonly object _lock = new();

    public event EventHandler<RepairEvent>? RepairLogged;

    public IReadOnlyList<RepairEvent> GetEvents()
    {
        lock (_lock)
        {
            return _events.ToList();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _events.Clear();
        }
    }

    /// <summary>
    /// Logs a repair event and asserts that re-verification passed.
    /// Returns true if repair is valid and verified, or false if re-verification failed.
    /// </summary>
    public bool AssertAndLogRepair(
        Guid fileGuid,
        string fileName,
        uint chunkSequence,
        int errorsCorrected,
        bool reVerificationPassed,
        string verificationMethod,
        string details)
    {
        var evt = new RepairEvent
        {
            FileGuid = fileGuid,
            FileName = fileName,
            ChunkSequence = chunkSequence,
            TimestampUtc = DateTime.UtcNow,
            SymbolErrorsCorrected = errorsCorrected,
            ReVerificationPassed = reVerificationPassed,
            VerificationMethod = verificationMethod,
            Details = details
        };

        lock (_lock)
        {
            _events.Add(evt);
        }

        RepairLogged?.Invoke(this, evt);
        return reVerificationPassed;
    }
}
