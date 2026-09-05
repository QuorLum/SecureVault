using System;
using System.IO;
using System.Threading;
using SecureVault.App.Services;

namespace SecureVault.App;

/// <summary>
/// Custom entry point enabling single-file unpackaged execution, CLI installation/uninstallation,
/// and automated shell integration.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            try
            {
                var ex = e.ExceptionObject as Exception;
                Diagnostics.CrashLog.Write("Program.Main.UnhandledException", ex);
            }
            catch { }
        };

        // Handle single-file CLI installation and setup commands
        if (args.Length > 0)
        {
            string first = args[0].Trim().ToLowerInvariant();
            if (first is "--install" or "--setup" or "/install" or "/setup")
            {
                ShellIntegrationService.InstallToSystem(launchInstalled: true);
                return;
            }
            if (first is "--uninstall" or "/uninstall")
            {
                ShellIntegrationService.Uninstall();
                return;
            }
            if (File.Exists(args[0]) && args[0].EndsWith(".vault", StringComparison.OrdinalIgnoreCase))
            {
                App.StartupVaultPath = Path.GetFullPath(args[0]);
            }
        }

        Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", AppContext.BaseDirectory);
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Microsoft.UI.Xaml.Application.Start((p) =>
        {
            var context = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
    }
}
