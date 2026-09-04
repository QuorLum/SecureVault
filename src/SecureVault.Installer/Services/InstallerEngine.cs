using System.Diagnostics;

namespace SecureVault.Installer.Services;

public sealed class InstallOptions
{
    public string DestinationDirectory { get; set; } = InstallerEngine.DefaultInstallDirectory;
    public bool CreateDesktopShortcut { get; set; } = true;
    public bool CreateStartMenuShortcut { get; set; } = true;
    public bool RegisterFileAssociation { get; set; } = true;
    public bool LaunchAfterInstall { get; set; } = true;
}

/// <summary>
/// Execution engine for SecureVault installation and uninstallation.
/// </summary>
public static class InstallerEngine
{
    public static string DefaultInstallDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs", "SecureVault");

    public static async Task InstallAsync(InstallOptions options, IProgress<(int Percent, string Status)> progress)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(progress);

        string targetDir = Path.GetFullPath(options.DestinationDirectory);

        progress.Report((10, "Preparing installation directory..."));
        await Task.Delay(100);

        if (!Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        // 1. Locate source payload
        progress.Report((20, "Locating SecureVault application payload..."));
        await Task.Delay(100);

        string currentDir = AppDomain.CurrentDomain.BaseDirectory;
        string? sourceExePath = FindPayloadExe(currentDir);

        if (sourceExePath == null)
        {
            throw new FileNotFoundException(
                "SecureVault.exe payload could not be found. Please ensure SecureVault.exe is in the same directory as the installer.");
        }

        // 2. Copy main executable
        string targetExePath = Path.Combine(targetDir, "SecureVault.exe");
        progress.Report((40, "Installing SecureVault executable..."));

        await CopyFileWithProgressAsync(sourceExePath, targetExePath, progress, 40, 70);

        // 3. Copy Icon and assets if present
        string targetIconPath = Path.Combine(targetDir, "AppIcon.ico");
        string sourceIconPath = Path.Combine(currentDir, "AppIcon.ico");
        if (!File.Exists(sourceIconPath))
        {
            // Try parent / Assets
            string devIcon = Path.Combine(currentDir, "..", "..", "..", "SecureVault.App", "Assets", "AppIcon.ico");
            if (File.Exists(devIcon)) sourceIconPath = devIcon;
        }

        if (File.Exists(sourceIconPath))
        {
            try { File.Copy(sourceIconPath, targetIconPath, true); } catch { }
        }
        else
        {
            targetIconPath = targetExePath; // Fallback to executable embedded icon
        }

        // 4. Install Uninstaller
        progress.Report((75, "Setting up uninstaller..."));
        string targetUninstallExe = Path.Combine(targetDir, "Uninstall.exe");
        string currentInstallerExe = Environment.ProcessPath ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(currentInstallerExe) && File.Exists(currentInstallerExe))
        {
            try { File.Copy(currentInstallerExe, targetUninstallExe, true); } catch { }
        }

        // 5. Desktop Shortcut
        if (options.CreateDesktopShortcut)
        {
            progress.Report((85, "Creating Desktop shortcut..."));
            ShortcutService.CreateDesktopShortcut(targetExePath, targetIconPath);
        }

        // 6. Start Menu Shortcut
        if (options.CreateStartMenuShortcut)
        {
            progress.Report((90, "Creating Start Menu shortcut..."));
            ShortcutService.CreateStartMenuShortcut(targetExePath, targetIconPath);
        }

        // 7. File Association & Windows Add/Remove Programs
        if (options.RegisterFileAssociation)
        {
            progress.Report((95, "Registering .vault file extension..."));
            RegistryService.RegisterFileAssociation(targetExePath, targetIconPath);
        }

        RegistryService.RegisterUninstall(targetDir, targetExePath, targetUninstallExe, targetIconPath);

        progress.Report((100, "Installation complete!"));
        await Task.Delay(200);
    }

    private static string? FindPayloadExe(string baseDir)
    {
        // 1. Same directory
        string direct = Path.Combine(baseDir, "SecureVault.exe");
        if (File.Exists(direct)) return direct;

        // 2. publish folder relative
        string publish = Path.Combine(baseDir, "publish", "SecureVault.exe");
        if (File.Exists(publish)) return publish;

        // 3. Check solution publish directory if running in development
        string devPublish = Path.Combine(baseDir, "..", "..", "..", "..", "publish", "SecureVault.exe");
        if (File.Exists(devPublish)) return Path.GetFullPath(devPublish);

        // 4. Look inside App bin output
        string devBin = Path.Combine(baseDir, "..", "..", "..", "SecureVault.App", "bin", "x64", "Release", "net8.0-windows10.0.26100.0", "win-x64", "SecureVault.exe");
        if (File.Exists(devBin)) return Path.GetFullPath(devBin);

        return null;
    }

    private static async Task CopyFileWithProgressAsync(
        string sourcePath,
        string destPath,
        IProgress<(int Percent, string Status)> progress,
        int startPercent,
        int endPercent)
    {
        const int bufferSize = 1024 * 1024; // 1MB buffer
        byte[] buffer = new byte[bufferSize];

        using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, true);
        using var dest = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, true);

        long totalBytes = source.Length;
        long copied = 0;
        int bytesRead;

        while ((bytesRead = await source.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            await dest.WriteAsync(buffer, 0, bytesRead);
            copied += bytesRead;

            double ratio = totalBytes > 0 ? (double)copied / totalBytes : 1.0;
            int currentPercent = startPercent + (int)(ratio * (endPercent - startPercent));
            progress.Report((currentPercent, $"Copying application files ({copied / (1024 * 1024)}MB / {totalBytes / (1024 * 1024)}MB)..."));
        }

        await dest.FlushAsync();
    }

    public static async Task UninstallAsync(string installDir)
    {
        ShortcutService.RemoveDesktopShortcut();
        ShortcutService.RemoveStartMenuShortcut();
        RegistryService.UnregisterAll();

        // Self-deleting cleanup script executed asynchronously via CMD after current process exits
        if (Directory.Exists(installDir))
        {
            string batchScript = Path.Combine(Path.GetTempPath(), $"securevault_uninstall_{Guid.NewGuid():N}.bat");
            string cmdContent = $@"@echo off
timeout /t 2 /nobreak > nul
rmdir /s /q ""{installDir}""
del ""%~f0""
";
            await File.WriteAllTextAsync(batchScript, cmdContent);

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{batchScript}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            Process.Start(psi);
        }
    }
}
