using SecureVault.Core.Format;

namespace SecureVault.Core.Integrity;

/// <summary>
/// Background vault integrity verification and auto-repair service (F16).
/// Periodically scans vault chunks in the background with low resource footprint,
/// detecting and auto-repairing bit rot using Reed-Solomon parity without blocking user UI.
/// </summary>
public sealed class BackgroundRepairService : IDisposable
{
    private readonly VaultManager _vault;
    private CancellationTokenSource? _cts;
    private Task? _backgroundTask;
    private bool _disposed;

    public event EventHandler<VaultHealthReport>? HealthScanCompleted;
    public event EventHandler<RepairEvent>? RepairApplied;

    public bool IsRunning => _backgroundTask != null && !_backgroundTask.IsCompleted;

    public BackgroundRepairService(VaultManager vault)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        RepairLogger.Shared.RepairLogged += OnRepairLogged;
    }

    private void OnRepairLogged(object? sender, RepairEvent e)
    {
        RepairApplied?.Invoke(this, e);
    }

    public void Start(TimeSpan interval)
    {
        if (IsRunning) return;

        _cts = new CancellationTokenSource();
        _backgroundTask = Task.Run(() => WorkerLoopAsync(interval, _cts.Token));
    }

    public void Stop()
    {
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            _cts.Cancel();
            try { _backgroundTask?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        }
    }

    public async Task<VaultHealthReport> RunCheckNowAsync(CancellationToken ct = default)
    {
        var report = await IntegrityChecker.CheckVaultAsync(_vault, ct: ct);
        HealthScanCompleted?.Invoke(this, report);
        return report;
    }

    private async Task WorkerLoopAsync(TimeSpan interval, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, ct);
                if (ct.IsCancellationRequested || _vault.IsLocked) break;

                var report = await IntegrityChecker.CheckVaultAsync(_vault, ct: ct);
                HealthScanCompleted?.Invoke(this, report);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Background worker suppresses transient exceptions to maintain stability
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        RepairLogger.Shared.RepairLogged -= OnRepairLogged;
        _cts?.Dispose();
        _disposed = true;
    }
}
