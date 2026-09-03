using Microsoft.Win32;

namespace SecureVault.App.Services;

/// <summary>
/// Detects Windows workstation lock screen and triggers immediate vault lock (A08, M08).
/// </summary>
public sealed class SystemLockDetector : IDisposable
{
    private readonly Action _onLockAction;
    private bool _disposed;

    public SystemLockDetector(Action onLockAction)
    {
        _onLockAction = onLockAction ?? throw new ArgumentNullException(nameof(onLockAction));
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock || e.Reason == SessionSwitchReason.SessionLogoff)
        {
            _onLockAction();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _disposed = true;
    }
}
