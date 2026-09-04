using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SecureVault.Installer.Services;

/// <summary>
/// Manages user-level (HKCU) registry configuration: .vault file association,
/// icon registration, and Windows Add/Remove Programs entry.
/// </summary>
public static class RegistryService
{
    private const string VaultProgId = "SecureVault.VaultContainer";
    private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SecureVault";

    [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    private const uint SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;

    public static void RegisterFileAssociation(string exePath, string? iconPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exePath);

        string effectiveIcon = !string.IsNullOrWhiteSpace(iconPath) ? iconPath : exePath;

        // 1. HKCU\Software\Classes\.vault
        using (var extKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\.vault"))
        {
            extKey.SetValue(string.Empty, VaultProgId);
        }

        // 2. HKCU\Software\Classes\SecureVault.VaultContainer
        using (var progIdKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{VaultProgId}"))
        {
            progIdKey.SetValue(string.Empty, "SecureVault Encrypted Container");

            using (var iconKey = progIdKey.CreateSubKey("DefaultIcon"))
            {
                iconKey.SetValue(string.Empty, $"\"{effectiveIcon}\",0");
            }

            using (var commandKey = progIdKey.CreateSubKey(@"shell\open\command"))
            {
                commandKey.SetValue(string.Empty, $"\"{exePath}\" \"%1\"");
            }
        }

        NotifyShell();
    }

    public static void RegisterUninstall(
        string installDir,
        string exePath,
        string uninstallExePath,
        string? iconPath = null,
        string version = "1.0.0")
    {
        string effectiveIcon = !string.IsNullOrWhiteSpace(iconPath) ? iconPath : exePath;

        using var key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath);
        key.SetValue("DisplayName", "SecureVault");
        key.SetValue("DisplayVersion", version);
        key.SetValue("Publisher", "SecureVault Contributors");
        key.SetValue("DisplayIcon", effectiveIcon);
        key.SetValue("InstallLocation", installDir);
        key.SetValue("UninstallString", $"\"{uninstallExePath}\" /uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", 450000, RegistryValueKind.DWord); // ~450MB in KB
    }

    public static void UnregisterAll()
    {
        try
        {
            // Remove uninstall entry
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, throwOnMissingSubKey: false);
        }
        catch { }

        try
        {
            // Check if .vault is mapped to SecureVaultProgId before deleting
            using (var extKey = Registry.CurrentUser.OpenSubKey(@"Software\Classes\.vault"))
            {
                if (extKey?.GetValue(string.Empty)?.ToString() == VaultProgId)
                {
                    Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\.vault", throwOnMissingSubKey: false);
                }
            }
        }
        catch { }

        try
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{VaultProgId}", throwOnMissingSubKey: false);
        }
        catch { }

        NotifyShell();
    }

    public static void NotifyShell()
    {
        try
        {
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        }
        catch { }
    }
}
