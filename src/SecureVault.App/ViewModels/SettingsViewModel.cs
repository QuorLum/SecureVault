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
            var settings = ApplicationData.Current.LocalSettings.Values;

            IsScreenProtectionEnabled = settings.TryGetValue("ScreenProtection", out var sp) && sp is bool bsp && bsp;
            AutoLockMinutes = settings.TryGetValue("AutoLockMinutes", out var alm) && alm is int ialm ? ialm : 10;
            AutoLockOnSystemLock = !settings.TryGetValue("LockOnSystemLock", out var lsl) || (lsl is bool blsl && blsl);
            DefaultProtectionModeIndex = settings.TryGetValue("DefaultProtectionMode", out var dpm) && dpm is int idpm ? idpm : 0;

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
        SaveSetting("ScreenProtection", value);
        StatusMessage = value ? "Screen capture protection activated." : "Screen capture protection disabled.";
    }

    partial void OnAutoLockMinutesChanged(int value)
    {
        SaveSetting("AutoLockMinutes", value);
    }

    partial void OnAutoLockOnSystemLockChanged(bool value)
    {
        SaveSetting("LockOnSystemLock", value);
    }

    partial void OnDefaultProtectionModeIndexChanged(int value)
    {
        SaveSetting("DefaultProtectionMode", value);
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

    private static void SaveSetting(string key, object value)
    {
        try
        {
            ApplicationData.Current.LocalSettings.Values[key] = value;
        }
        catch { }
    }
}
