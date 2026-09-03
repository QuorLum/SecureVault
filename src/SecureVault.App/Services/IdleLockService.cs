using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace SecureVault.App.Services;

/// <summary>
/// Monitors system-wide keyboard and mouse inactivity and triggers auto-lock on timeout (A08).
/// </summary>
public sealed class IdleLockService : IDisposable
{
    private readonly TimeSpan _timeout;
    private readonly Action _onLockAction;
    private DispatcherQueueTimer? _timer;
    private bool _disposed;
    private bool _isLocked;

    public bool IsEnabled { get; set; } = true;

    public IdleLockService(TimeSpan timeout, Action onLockAction)
    {
        _timeout = timeout;
        _onLockAction = onLockAction ?? throw new ArgumentNullException(nameof(onLockAction));
    }

    public void Start()
    {
        var dq = DispatcherQueue.GetForCurrentThread();
        if (dq == null) return;

        _timer = dq.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(10);
        _timer.Tick += (s, e) => CheckIdleStatus();
        _timer.Start();
    }

    private void CheckIdleStatus()
    {
        if (!IsEnabled || _isLocked) return;

        uint idleMs = GetIdleTimeMilliseconds();
        if (idleMs >= _timeout.TotalMilliseconds)
        {
            _isLocked = true;
            _onLockAction();
        }
    }

    public void ResetLockState()
    {
        _isLocked = false;
    }

    public static uint GetIdleTimeMilliseconds()
    {
        var lii = new LASTINPUTINFO();
        lii.cbSize = (uint)Marshal.SizeOf(lii);

        if (GetLastInputInfo(ref lii))
        {
            return (uint)Environment.TickCount - lii.dwTime;
        }

        return 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _timer?.Stop();
        _disposed = true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
}
