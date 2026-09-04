using System.Text.Json;

namespace SecureVault.Core.IO;

/// <summary>
/// Persists application-level preferences, last opened vault path, and recent vaults history
/// in %LOCALAPPDATA%\SecureVault\config.json (or a custom directory).
/// Passwords, master keys, and decrypted credentials are NEVER stored here.
/// </summary>
public sealed class AppSettingsService
{
    private static readonly Lazy<AppSettingsService> _lazyInstance = new(() => new AppSettingsService());
    public static AppSettingsService Instance => _lazyInstance.Value;

    private readonly string _configDirectory;
    private readonly string _configFilePath;
    private readonly object _lock = new();

    private SettingsData _data = new();

    public AppSettingsService(string? customConfigDirectory = null)
    {
        _configDirectory = customConfigDirectory ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SecureVault");
        _configFilePath = Path.Combine(_configDirectory, "config.json");

        Load();
    }

    public string ConfigFilePath => _configFilePath;

    public string? LastVaultPath
    {
        get
        {
            lock (_lock)
            {
                return _data.LastVaultPath;
            }
        }
        set
        {
            lock (_lock)
            {
                _data.LastVaultPath = value;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    _data.HasCompletedFirstRun = true;
                    AddRecentInternal(value);
                }
            }
            Save();
        }
    }

    public bool HasCompletedFirstRun
    {
        get
        {
            lock (_lock)
            {
                return _data.HasCompletedFirstRun;
            }
        }
        set
        {
            lock (_lock)
            {
                _data.HasCompletedFirstRun = value;
            }
            Save();
        }
    }

    public IReadOnlyList<string> RecentVaults
    {
        get
        {
            lock (_lock)
            {
                return _data.RecentVaults.ToList();
            }
        }
    }

    public void AddRecentVault(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        lock (_lock)
        {
            AddRecentInternal(path);
        }
        Save();
    }

    private void AddRecentInternal(string path)
    {
        _data.RecentVaults.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        _data.RecentVaults.Insert(0, path);

        while (_data.RecentVaults.Count > 10)
        {
            _data.RecentVaults.RemoveAt(_data.RecentVaults.Count - 1);
        }
    }

    public void RemoveRecentVault(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        lock (_lock)
        {
            _data.RecentVaults.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            if (string.Equals(_data.LastVaultPath, path, StringComparison.OrdinalIgnoreCase))
            {
                _data.LastVaultPath = _data.RecentVaults.FirstOrDefault();
            }
        }
        Save();
    }

    public void Clear()
    {
        lock (_lock)
        {
            _data = new SettingsData();
        }
        Save();
    }

    public void Load()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    var json = File.ReadAllText(_configFilePath);
                    var loaded = JsonSerializer.Deserialize(json, SecureVaultJsonContext.Default.SettingsData);
                    if (loaded != null)
                    {
                        _data = loaded;
                    }
                }
            }
            catch
            {
                _data = new SettingsData();
            }
        }
    }

    public void Save()
    {
        lock (_lock)
        {
            try
            {
                if (!Directory.Exists(_configDirectory))
                {
                    Directory.CreateDirectory(_configDirectory);
                }

                var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(_data, SecureVaultJsonContext.Default.SettingsData);
                AtomicWriter.WriteAllBytes(_configFilePath, jsonBytes);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save AppSettings: {ex.Message}");
            }
        }
    }
}
