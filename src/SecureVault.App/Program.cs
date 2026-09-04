using System;
using System.Threading;

namespace SecureVault.App;

/// <summary>
/// Custom entry point enabling seamless single-file unpackaged Windows App SDK runtime resolution.
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
                System.IO.File.WriteAllText("startup_crash.log", $"[{DateTime.Now:HH:mm:ss.fff}] UNHANDLED: {e.ExceptionObject}\n");
            }
            catch { }
        };

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
