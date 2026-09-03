using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using SecureVault.Core.Exceptions;

namespace SecureVault.Core.IO;

/// <summary>
/// Enforces single-writer access to a vault using a Windows Named Mutex combined with
/// a crash-resilient .vault.lock file containing PID and acquisition metadata.
/// </summary>
public sealed class VaultFileLock : IDisposable
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> ActiveProcessLocks = new(StringComparer.OrdinalIgnoreCase);

    private readonly string _fullPath;
    private readonly string _lockFilePath;
    private readonly Mutex? _mutex;
    private bool _hasMutex;
    private bool _hasProcessLock;
    private bool _disposed;

    public string LockFilePath => _lockFilePath;

    public VaultFileLock(string vaultFilePath, Guid? vaultGuid = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultFilePath);

        _fullPath = Path.GetFullPath(vaultFilePath);
        _lockFilePath = _fullPath + ".lock";

        // Step 0: In-process single-writer guard
        if (!ActiveProcessLocks.TryAdd(_fullPath, 0))
        {
            throw new VaultAlreadyOpenException($"Vault is already open in this process: '{_fullPath}'.");
        }
        _hasProcessLock = true;

        // Generate deterministic mutex identifier based on path or UUID
        string mutexName = vaultGuid.HasValue && vaultGuid.Value != Guid.Empty
            ? $"Local\\SecureVault_{vaultGuid.Value:N}"
            : $"Local\\SecureVault_{ComputePathHash(_fullPath)}";

        // Step 1: Check for stale lock file from a previous crashed instance
        CheckAndCleanStaleLockFile();

        // Step 2: Acquire OS-level mutex
        try
        {
            _mutex = new Mutex(initiallyOwned: true, name: mutexName, out bool createdNew);
            if (!createdNew)
            {
                // Mutex already exists and is held by another running process
                _hasMutex = _mutex.WaitOne(millisecondsTimeout: 100);
                if (!_hasMutex)
                {
                    int holderPid = ReadHolderPid();
                    ActiveProcessLocks.TryRemove(_fullPath, out _);
                    _hasProcessLock = false;
                    throw new VaultAlreadyOpenException(
                        $"Vault is already open in another process{(holderPid > 0 ? $" (PID {holderPid})" : "")}.");
                }
            }
            else
            {
                _hasMutex = true;
            }

            // Step 3: Write lock metadata to file
            WriteLockMetadata();
        }
        catch (AbandonedMutexException)
        {
            // Previous owner crashed without releasing mutex; OS gives ownership to us
            _hasMutex = true;
            WriteLockMetadata();
        }
        catch
        {
            if (_hasProcessLock)
            {
                ActiveProcessLocks.TryRemove(_fullPath, out _);
                _hasProcessLock = false;
            }
            throw;
        }
    }

    private void CheckAndCleanStaleLockFile()
    {
        if (!File.Exists(_lockFilePath))
            return;

        try
        {
            int pid = ReadHolderPid();
            if (pid > 0)
            {
                if (!IsProcessAlive(pid))
                {
                    // The process that created the lock is no longer running - safe to reclaim
                    File.Delete(_lockFilePath);
                }
            }
        }
        catch
        {
            // Ignore inspection exceptions and proceed to mutex test
        }
    }

    private int ReadHolderPid()
    {
        if (!File.Exists(_lockFilePath))
            return -1;

        try
        {
            string content = File.ReadAllText(_lockFilePath).Trim();
            string[] parts = content.Split(':', 3);
            if (parts.Length >= 1 && int.TryParse(parts[0], out int pid))
            {
                return pid;
            }
        }
        catch
        {
            // Lock file might be temporarily locked
        }

        return -1;
    }

    private void WriteLockMetadata()
    {
        int pid = Environment.ProcessId;
        string timestamp = DateTime.UtcNow.ToString("o");
        string machine = Environment.MachineName;
        string content = $"{pid}:{timestamp}:{machine}";

        File.WriteAllText(_lockFilePath, content);
    }

    public static bool IsProcessAlive(int pid)
    {
        if (pid <= 0) return false;
        try
        {
            var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            // Process does not exist
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static string ComputePathHash(string path)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant()));
        return Convert.ToHexString(hash)[..16];
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_hasMutex)
        {
            try
            {
                if (File.Exists(_lockFilePath))
                {
                    File.Delete(_lockFilePath);
                }
            }
            catch
            {
                // Suppress file delete failure on shutdown
            }

            try
            {
                _mutex?.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Mutex wasn't owned by this thread
            }
            finally
            {
                _mutex?.Dispose();
                _hasMutex = false;
            }
        }

        if (_hasProcessLock)
        {
            ActiveProcessLocks.TryRemove(_fullPath, out _);
            _hasProcessLock = false;
        }

        _disposed = true;
    }
}
