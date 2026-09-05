using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using SecureVault.App.Views;
using SecureVault.Core;
using SecureVault.Core.Security;

namespace SecureVault.App.Services;

/// <summary>
/// Central coordinator for the active unlocked vault session in the WinUI 3 shell (A08/M08).
/// Maintains idle and system lock detectors active across all pages, handles emergency closing
/// of all viewers/dialogs, auto-saving notes or zeroing plaintext on lock, and navigation to LoginPage.
/// </summary>
public sealed class VaultSessionManager : IDisposable
{
    private static VaultSessionManager? _instance;
    public static VaultSessionManager Instance => _instance ??= new VaultSessionManager();

    private VaultManager? _vault;
    private VaultSessionCoordinator? _coordinator;
    private IdleLockService? _idleLockService;
    private SystemLockDetector? _systemLockDetector;
    private Frame? _rootFrame;
    private readonly HashSet<ContentDialog> _openDialogs = new();
    private readonly object _lock = new();
    private bool _isLocking;

    public VaultManager? CurrentVault => _vault;
    public VaultSessionCoordinator? Coordinator => _coordinator;
    public bool IsActive => _vault != null && !_vault.IsDisposed;

    public void StartSession(VaultManager vault, Frame rootFrame, TimeSpan? idleTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(rootFrame);

        lock (_lock)
        {
            EndSessionInternal();

            _vault = vault;
            _coordinator = new VaultSessionCoordinator(vault);
            _rootFrame = rootFrame;
            _isLocking = false;

            var timeout = idleTimeout ?? TimeSpan.FromMinutes(5);
            _idleLockService = new IdleLockService(timeout, () =>
            {
                _ = TriggerLockAsync(LockTriggerReason.IdleTimeout);
            });
            _idleLockService.Start();

            _systemLockDetector = new SystemLockDetector(() =>
            {
                _ = TriggerLockAsync(LockTriggerReason.WorkstationLock);
            });
        }
    }

    public void RegisterDialog(ContentDialog dialog)
    {
        if (dialog == null) return;
        lock (_lock)
        {
            _openDialogs.Add(dialog);
        }
    }

    public void UnregisterDialog(ContentDialog dialog)
    {
        if (dialog == null) return;
        lock (_lock)
        {
            _openDialogs.Remove(dialog);
        }
    }

    public async Task TriggerLockAsync(LockTriggerReason reason)
    {
        lock (_lock)
        {
            if (_isLocking || _vault == null) return;
            _isLocking = true;
        }

        var dq = _rootFrame?.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
        if (dq != null && !dq.HasThreadAccess)
        {
            var tcs = new TaskCompletionSource();
            dq.TryEnqueue(async () =>
            {
                try
                {
                    await ExecuteLockOnUIThreadAsync(reason);
                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            await tcs.Task;
        }
        else
        {
            await ExecuteLockOnUIThreadAsync(reason);
        }
    }

    private async Task ExecuteLockOnUIThreadAsync(LockTriggerReason reason)
    {
        // 1. Close any open ContentDialogs
        List<ContentDialog> dialogsToHide;
        lock (_lock)
        {
            dialogsToHide = _openDialogs.ToList();
            _openDialogs.Clear();
        }
        foreach (var dialog in dialogsToHide)
        {
            try { dialog.Hide(); } catch { }
        }

        // 2. Handle active viewer page if currently displayed
        if (_rootFrame?.Content is Page currentPage)
        {
            if (currentPage is NotesEditorPage notesPage && notesPage.ViewModel != null)
            {
                try
                {
                    // Encrypted auto-save unsaved note to vault before locking
                    await notesPage.ViewModel.SaveAsync();
                }
                catch
                {
                    // If auto-save fails (e.g. disk full), note content must NOT survive in plaintext anywhere!
                    notesPage.ViewModel.Title = string.Empty;
                    notesPage.ViewModel.Content = string.Empty;
                }
                notesPage.ViewModel.Dispose();
            }
            else if (currentPage is MediaPlayerPage mediaPage && mediaPage.ViewModel != null)
            {
                mediaPage.ViewModel.Stop();
                mediaPage.ViewModel.Dispose();
            }
            else if (currentPage is PdfViewerPage pdfPage && pdfPage.ViewModel != null)
            {
                pdfPage.ViewModel.Dispose();
            }
            else if (currentPage is PhotoViewerPage photoPage && photoPage.ViewModel != null)
            {
                photoPage.ViewModel.DisplayImage = null;
            }
        }

        // 3. Delegate to coordinator to close any registered viewer handles and dispose vault
        if (_coordinator != null)
        {
            await _coordinator.TriggerLockAsync(reason);
        }
        else
        {
            _vault?.Dispose();
        }

        // 4. Clear frame navigation backstack and navigate to LoginPage
        if (_rootFrame != null)
        {
            _rootFrame.BackStack.Clear();
            _rootFrame.Navigate(typeof(LoginPage));
        }

        lock (_lock)
        {
            EndSessionInternal();
        }

        // 5. Force garbage collection to reclaim decrypted memory
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private void EndSessionInternal()
    {
        _idleLockService?.Dispose();
        _idleLockService = null;
        _systemLockDetector?.Dispose();
        _systemLockDetector = null;
        _vault = null;
        _coordinator = null;
        _openDialogs.Clear();
        _isLocking = false;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            EndSessionInternal();
        }
    }
}
