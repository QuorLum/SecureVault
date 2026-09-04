using System.Runtime.InteropServices.ComTypes;
using SecureVault.Installer.Interop;

namespace SecureVault.Installer.Services;

/// <summary>
/// Manages creation and deletion of Windows Shell Shortcuts (.lnk) on Desktop and Start Menu.
/// </summary>
public static class ShortcutService
{
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

    public static void CreateShortcut(
        string shortcutPath,
        string targetPath,
        string workingDirectory,
        string? iconPath = null,
        string? description = "SecureVault - Zero-Knowledge Encrypted Storage")
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

    public static void CreateDesktopShortcut(string targetExePath, string? iconPath = null)
    {
        string workDir = Path.GetDirectoryName(targetExePath) ?? string.Empty;
        CreateShortcut(DesktopShortcutPath, targetExePath, workDir, iconPath);
    }

    public static void CreateStartMenuShortcut(string targetExePath, string? iconPath = null)
    {
        string workDir = Path.GetDirectoryName(targetExePath) ?? string.Empty;
        CreateShortcut(StartMenuShortcutPath, targetExePath, workDir, iconPath);
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
}
