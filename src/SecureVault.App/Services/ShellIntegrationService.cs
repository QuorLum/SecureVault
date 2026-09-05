using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Microsoft.Win32;

namespace SecureVault.App.Services;

/// <summary>
/// Provides single-executable self-installation, Windows Shell shortcut management (.lnk),
/// .vault file association in HKCU, and Windows Add/Remove Programs registration.
/// </summary>
public static class ShellIntegrationService
{
    private const string VaultProgId = "SecureVault.VaultContainer";
    private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SecureVault";

    [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    private const uint SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;

    public static string DefaultInstallDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs", "SecureVault");

    public static string InstalledExePath => Path.Combine(DefaultInstallDirectory, "SecureVault.exe");

    public static string DesktopShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        "SecureVault.lnk");

    public static string StartMenuShortcutPath
    {
        get
        {
            string startMenuDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                "SecureVault");
            return Path.Combine(startMenuDir, "SecureVault.lnk");
        }
    }

    public static bool IsInstalledInSystemDirectory()
    {
        string? currentExe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExe)) return false;
        return string.Equals(Path.GetFullPath(currentExe), Path.GetFullPath(InstalledExePath), StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsRunningInDevelopmentEnvironment()
    {
        string? currentExe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExe)) return false;
        return currentExe.Contains(@"\bin\Debug\", StringComparison.OrdinalIgnoreCase) ||
               currentExe.Contains(@"\bin\Release\", StringComparison.OrdinalIgnoreCase);
    }

    public static void CreateShortcut(
        string shortcutPath,
        string targetPath,
        string workingDirectory,
        string? iconPath = null,
        string? description = "SecureVault - Zero-Knowledge Encrypted Safe")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shortcutPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        string? dir = Path.GetDirectoryName(shortcutPath);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var link = (IShellLinkW)new ShellLink();
        link.SetPath(targetPath);
        link.SetWorkingDirectory(!string.IsNullOrWhiteSpace(workingDirectory) ? workingDirectory : Path.GetDirectoryName(targetPath) ?? string.Empty);

        if (!string.IsNullOrWhiteSpace(description))
        {
            link.SetDescription(description);
        }

        if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
        {
            link.SetIconLocation(iconPath, 0);
        }
        else
        {
            link.SetIconLocation(targetPath, 0);
        }

        var file = (IPersistFile)link;
        file.Save(shortcutPath, false);
    }

    public static void CreateDesktopShortcut(string? targetExe = null, string? iconPath = null)
    {
        string exe = targetExe ?? Environment.ProcessPath ?? InstalledExePath;
        string workDir = Path.GetDirectoryName(exe) ?? string.Empty;
        CreateShortcut(DesktopShortcutPath, exe, workDir, iconPath);
    }

    public static void CreateStartMenuShortcut(string? targetExe = null, string? iconPath = null)
    {
        string exe = targetExe ?? Environment.ProcessPath ?? InstalledExePath;
        string workDir = Path.GetDirectoryName(exe) ?? string.Empty;
        CreateShortcut(StartMenuShortcutPath, exe, workDir, iconPath);
    }

    public static void RemoveDesktopShortcut()
    {
        try
        {
            if (File.Exists(DesktopShortcutPath))
            {
                File.Delete(DesktopShortcutPath);
            }
        }
        catch { }
    }

    public static void RemoveStartMenuShortcut()
    {
        try
        {
            if (File.Exists(StartMenuShortcutPath))
            {
                File.Delete(StartMenuShortcutPath);
            }

            string? startMenuDir = Path.GetDirectoryName(StartMenuShortcutPath);
            if (!string.IsNullOrWhiteSpace(startMenuDir) && Directory.Exists(startMenuDir))
            {
                if (Directory.GetFiles(startMenuDir).Length == 0 && Directory.GetDirectories(startMenuDir).Length == 0)
                {
                    Directory.Delete(startMenuDir);
                }
            }
        }
        catch { }
    }

    public static void RegisterFileAssociation(string? targetExe = null, string? iconPath = null)
    {
        string exePath = targetExe ?? Environment.ProcessPath ?? InstalledExePath;
        string effectiveIcon = !string.IsNullOrWhiteSpace(iconPath) ? iconPath : exePath;

        try
        {
            using (var extKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\.vault"))
            {
                extKey.SetValue(string.Empty, VaultProgId);
            }

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
        catch { }
    }

    public static void RegisterUninstall(
        string installDir,
        string exePath,
        string? iconPath = null,
        string version = "1.0.0")
    {
        try
        {
            string effectiveIcon = !string.IsNullOrWhiteSpace(iconPath) ? iconPath : exePath;

            using var key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath);
            key.SetValue("DisplayName", "SecureVault");
            key.SetValue("DisplayVersion", version);
            key.SetValue("Publisher", "SecureVault");
            key.SetValue("DisplayIcon", effectiveIcon);
            key.SetValue("InstallLocation", installDir);
            key.SetValue("UninstallString", $"\"{exePath}\" --uninstall");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", 160000, RegistryValueKind.DWord);
        }
        catch { }
    }

    public static void UnregisterAll()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, throwOnMissingSubKey: false);
        }
        catch { }

        try
        {
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

    public static bool InstallToSystem(bool launchInstalled = true)
    {
        string? currentExe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExe) || !File.Exists(currentExe)) return false;

        string targetDir = DefaultInstallDirectory;
        if (!Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        string targetExe = InstalledExePath;

        // If not already running from installed location, copy self
        if (!string.Equals(Path.GetFullPath(currentExe), Path.GetFullPath(targetExe), StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                File.Copy(currentExe, targetExe, overwrite: true);
            }
            catch
            {
                return false;
            }
        }

        // Copy icon asset if present alongside exe
        string currentDir = AppDomain.CurrentDomain.BaseDirectory;
        string sourceIcon = Path.Combine(currentDir, "Assets", "AppIcon.ico");
        string targetIconDir = Path.Combine(targetDir, "Assets");
        string targetIcon = Path.Combine(targetIconDir, "AppIcon.ico");
        if (File.Exists(sourceIcon))
        {
            try
            {
                Directory.CreateDirectory(targetIconDir);
                File.Copy(sourceIcon, targetIcon, true);
            }
            catch { }
        }
        else
        {
            targetIcon = targetExe;
        }

        // Create shortcuts & associations
        CreateDesktopShortcut(targetExe, targetIcon);
        CreateStartMenuShortcut(targetExe, targetIcon);
        RegisterFileAssociation(targetExe, targetIcon);
        RegisterUninstall(targetDir, targetExe, targetIcon);

        if (launchInstalled && !string.Equals(Path.GetFullPath(currentExe), Path.GetFullPath(targetExe), StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = targetExe,
                    UseShellExecute = true
                });
                return true;
            }
            catch { }
        }

        return true;
    }

    public static void Uninstall()
    {
        RemoveDesktopShortcut();
        RemoveStartMenuShortcut();
        UnregisterAll();
    }
}

[ComImport]
[Guid("00021401-0000-0000-C000-000000000046")]
internal class ShellLink
{
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("000214F9-0000-0000-C000-000000000046")]
internal interface IShellLinkW
{
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, out IntPtr pfd, uint fFlags);
    void GetIDList(out IntPtr ppidl);
    void SetIDList(IntPtr pidl);
    void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
    void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
    void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
    void GetHotkey(out short pwHotkey);
    void SetHotkey(short wHotkey);
    void GetShowCmd(out int piShowCmd);
    void SetShowCmd(int iShowCmd);
    void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
    void Resolve(IntPtr hwnd, uint fFlags);
    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
}
