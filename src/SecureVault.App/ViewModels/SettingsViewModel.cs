using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Windows.Storage;
using SecureVault.App.Services;
using SecureVault.Core;
using SecureVault.Core.Format;

namespace SecureVault.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly VaultManager? _vault;
    private readonly IntPtr _windowHandle;

    [ObservableProperty]
    private bool _isScreenProtectionEnabled;

    [ObservableProperty]
    private int _autoLockMinutes;

    [ObservableProperty]
    private bool _autoLockOnSystemLock;

    [ObservableProperty]
    private int _defaultProtectionModeIndex; // 0 = Secure, 1 = Fast

    [ObservableProperty]
    private string? _passwordHint;

    private readonly AppSettingsService _settings = AppSettingsService.Instance;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public SettingsViewModel(VaultManager? vault, IntPtr windowHandle)
    {
        _vault = vault;
        _windowHandle = windowHandle;

        LoadPreferences();
    }

    private void LoadPreferences()
    {
        try
        {
            IsScreenProtectionEnabled = _settings.ScreenProtection;
            AutoLockMinutes = _settings.AutoLockMinutes;
            AutoLockOnSystemLock = _settings.LockOnSystemLock;
            DefaultProtectionModeIndex = _settings.DefaultProtectionMode;

            if (_vault != null)
            {
                PasswordHint = _vault.PasswordHint;
            }
        }
        catch
        {
            AutoLockMinutes = 10;
            AutoLockOnSystemLock = true;
        }
    }

    partial void OnIsScreenProtectionEnabledChanged(bool value)
    {
        ScreenProtectionService.SetProtection(_windowHandle, value);
        _settings.ScreenProtection = value;
        StatusMessage = value ? "Screen capture protection activated." : "Screen capture protection disabled.";
    }

    partial void OnAutoLockMinutesChanged(int value)
    {
        _settings.AutoLockMinutes = value;
    }

    partial void OnAutoLockOnSystemLockChanged(bool value)
    {
        _settings.LockOnSystemLock = value;
    }

    partial void OnDefaultProtectionModeIndexChanged(int value)
    {
        _settings.DefaultProtectionMode = value;
    }

    [RelayCommand]
    public void SavePasswordHint()
    {
        if (_vault == null) return;

        try
        {
            _vault.SetPasswordHint(PasswordHint);
            StatusMessage = "Password hint updated successfully.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to save hint: {ex.Message}";
        }
    }

    [RelayCommand]
    public void CreateDesktopShortcut()
    {
        try
        {
            ShellIntegrationService.CreateDesktopShortcut();
            StatusMessage = "Desktop shortcut created successfully.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to create Desktop shortcut: {ex.Message}";
        }
    }

    [RelayCommand]
    public void CreateStartMenuShortcut()
    {
        try
        {
            ShellIntegrationService.CreateStartMenuShortcut();
            StatusMessage = "Start Menu shortcut created successfully.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to create Start Menu shortcut: {ex.Message}";
        }
    }

    [RelayCommand]
    public void RegisterFileAssociation()
    {
        try
        {
            ShellIntegrationService.RegisterFileAssociation();
            StatusMessage = ".vault file association registered with Windows.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to register file association: {ex.Message}";
        }
    }

    [RelayCommand]
    public void InstallToSystem()
    {
        try
        {
            bool ok = ShellIntegrationService.InstallToSystem(launchInstalled: false);
            StatusMessage = ok ? "SecureVault installed to system Programs folder successfully." : "Application already running from system directory.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Installation error: {ex.Message}";
        }
    }
}
