using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SecureVault.App.Diagnostics;

namespace SecureVault.App;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// Incorporates global crash logging, binding error diagnostics, and unhandled exception reporting.
/// </summary>
public partial class App : Application
{
    public static Window CurrentWindow { get; private set; } = null!;
    public static string? StartupVaultPath { get; set; }
    private Window? _window;

    public App()
    {
        // 1. AppDomain Unhandled Exceptions
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            string report = CrashLog.Write("AppDomain.UnhandledException", ex);
            CrashReportWindow.ShowModal("AppDomain.UnhandledException", ex, report);
        };

        // 2. TaskScheduler Unobserved Task Exceptions
        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            CrashLog.Write("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

        // 3. WinUI Xaml Unhandled Exceptions
        this.UnhandledException += (s, e) =>
        {
            string report = CrashLog.Write("Xaml.UnhandledException", e.Exception);
            CrashReportWindow.ShowModal("Xaml.UnhandledException", e.Exception, report);
            e.Handled = true;
        };

        // 4. First-chance exception tracing for active diagnostics
#if DEBUG
        AppDomain.CurrentDomain.FirstChanceException += (s, e) =>
        {
            // Skip noisy COM/RPC internal probes
            var exType = e.Exception.GetType().FullName ?? "";
            if (!exType.StartsWith("System.Runtime.InteropServices.COMException") &&
                !exType.StartsWith("System.IO.FileNotFoundException"))
            {
                CrashLog.Trace("FirstChance", e.Exception);
            }
        };
#endif

        // 5. WinUI Binding Failures
        try
        {
            DebugSettings.BindingFailed += (s, e) =>
            {
                CrashLog.LogBindingError(e.Message);
            };
        }
        catch { }

        CrashLog.LogInfo("App ctor: Initializing components...");
        InitializeComponent();
    }

    private void ValidateResources()
    {
        try
        {
            if (Resources?.MergedDictionaries != null)
            {
                CrashLog.LogInfo($"MergedDictionaries count: {Resources.MergedDictionaries.Count}");
                foreach (var md in Resources.MergedDictionaries)
                {
                    CrashLog.LogInfo($"MergedDictionary source: {md.Source?.ToString() ?? "inline"}, Count: {md.Count}");
                }
            }
        }
        catch (Exception ex)
        {
            CrashLog.LogError("Failed to enumerate MergedDictionaries", ex);
        }
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        CrashLog.LogInfo("OnLaunched: Initializing MainWindow...");
        UiActionTrace.Record("AppLaunched");

        _window = new MainWindow();
        CurrentWindow = _window;
        _window.Activate();
        CrashLog.LogInfo("OnLaunched: MainWindow activated.");
        ValidateResources();

        // Non-blocking background shell registration when run in production
        if (!Services.ShellIntegrationService.IsRunningInDevelopmentEnvironment())
        {
            Task.Run(() =>
            {
                try
                {
                    Services.ShellIntegrationService.RegisterFileAssociation();
                }
                catch (Exception ex)
                {
                    CrashLog.LogWarning($"Shell file association registration failed: {ex.Message}");
                }
            });
        }
    }
}
