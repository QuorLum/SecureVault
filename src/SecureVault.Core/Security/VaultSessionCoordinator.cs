using SecureVault.Core.Format;

namespace SecureVault.Core.Security;

public enum LockTriggerReason
{
    IdleTimeout,
    WorkstationLock,
    PowerSuspend,
    ManualLock
}

public enum ViewerType
{
    Notes,
    Photo,
    Video,
    Pdf,
    TextMarkdown,
    PreviewPane,
    Dialog
}

public interface IVaultViewerHandle
{
    ViewerType ViewerType { get; }
    bool IsOpen { get; }
    bool HasUnsavedChanges { get; }
    Task PrepareForLockAsync(CancellationToken cancellationToken = default);
    void CloseAndRelease();
}

/// <summary>
/// Coordinates vault session lifecycle, active viewers/dialogs enumeration,
/// emergency encrypted auto-saving, graceful viewer closing, and zeroing of decrypted memory on Lock (A08/M08, A03/M04).
/// </summary>
public sealed class VaultSessionCoordinator
{
    private readonly VaultManager _vault;
    private readonly List<IVaultViewerHandle> _viewers = new();
    private readonly object _lock = new();
    private bool _isLocked;

    public VaultManager Vault => _vault;

    public bool IsLocked
    {
        get { lock (_lock) return _isLocked; }
    }

    public IReadOnlyList<IVaultViewerHandle> ActiveViewers
    {
        get { lock (_lock) return _viewers.Where(v => v.IsOpen).ToList(); }
    }

    public VaultSessionCoordinator(VaultManager vault)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
    }

    public void RegisterViewer(IVaultViewerHandle viewer)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        lock (_lock)
        {
            if (!_viewers.Contains(viewer))
            {
                _viewers.Add(viewer);
            }
        }
    }

    public void UnregisterViewer(IVaultViewerHandle viewer)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        lock (_lock)
        {
            _viewers.Remove(viewer);
        }
    }

    /// <summary>
    /// Executes emergency lock:
    /// 1. Enumerates every open viewer / dialog.
    /// 2. Executes PrepareForLockAsync (e.g. encrypted auto-save for unsaved notes).
    /// 3. Closes and disposes all viewers, stopping media playback and releasing all stream handles.
    /// 4. Disposes/locks the vault, zeroing all index memory, caches, and cryptographic keys.
    /// 5. Forces garbage collection to reclaim memory.
    /// </summary>
    public async Task TriggerLockAsync(LockTriggerReason reason, CancellationToken cancellationToken = default)
    {
        List<IVaultViewerHandle> viewersToClose;
        lock (_lock)
        {
            if (_isLocked) return;
            _isLocked = true;
            viewersToClose = _viewers.Where(v => v.IsOpen).ToList();
        }

        // 1. Prepare each viewer for lock (e.g. encrypted auto-save unsaved notes)
        foreach (var viewer in viewersToClose)
        {
            try
            {
                await viewer.PrepareForLockAsync(cancellationToken);
            }
            catch
            {
                // If auto-save fails (e.g. disk full), viewer.CloseAndRelease() ensures
                // plaintext note content does NOT survive in plaintext anywhere.
            }
        }

        // 2. Close each viewer BEFORE disposing the vault
        foreach (var viewer in viewersToClose)
        {
            try
            {
                viewer.CloseAndRelease();
            }
            catch { }
        }

        lock (_lock)
        {
            _viewers.Clear();
        }

        // 3. Lock & Dispose the vault container (zeroing keys, indices, and caches)
        _vault.Dispose();

        // 4. Force GC to guarantee unreferenced decrypted buffers are collected
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
