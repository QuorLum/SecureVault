using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace SecureVault.App.Diagnostics;

/// <summary>
/// Global crash and diagnostic logging service.
/// Outputs structured crash reports and rolling application logs to %LOCALAPPDATA%\SecureVault\logs.
/// </summary>
public static class CrashLog
{
    private static readonly object _logLock = new();
    private static string? _logDirectory;

    public static string LogDirectory
    {
        get
        {
            if (_logDirectory == null)
            {
                _logDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SecureVault", "logs");
                try
                {
                    Directory.CreateDirectory(_logDirectory);
                }
                catch { }
            }
            return _logDirectory;
        }
    }

    public static string AppLogFilePath => Path.Combine(LogDirectory, "app.log");

    public static string Write(string source, Exception? ex, string? extraContext = null)
    {
        lock (_logLock)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                var timestamp = DateTime.Now;
                string crashFileName = $"crash-{timestamp:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log";
                string crashFilePath = Path.Combine(LogDirectory, crashFileName);

                string report = BuildCrashReport(source, ex, extraContext, timestamp);

                File.WriteAllText(crashFilePath, report, Encoding.UTF8);
                AppendToRollingAppLog($"[CRASH] [{source}] Written to {crashFileName}:\n{report}\n");

                return report;
            }
            catch (Exception writeEx)
            {
                // Absolute fallback in case LocalAppData is inaccessible
                try
                {
                    File.WriteAllText($"emergency_crash_{DateTime.Now:yyyyMMdd_HHmmss}.log",
                        $"CrashLog failed: {writeEx}\nOriginal error ({source}): {ex}");
                }
                catch { }
                return ex?.ToString() ?? "Unknown error";
            }
        }
    }

    public static void Trace(string category, Exception? ex)
    {
        if (ex == null) return;
        AppendToRollingAppLog($"[TRACE-FIRSTCHANCE] [{category}] {ex.GetType().FullName}: {Redact(ex.Message)}");
    }

    public static void LogBindingError(string message)
    {
        AppendToRollingAppLog($"[XAML-BINDING-ERROR] {Redact(message)}");
    }

    public static void LogInfo(string message)
    {
        AppendToRollingAppLog($"[INFO] {message}");
    }

    public static void LogWarning(string message)
    {
        AppendToRollingAppLog($"[WARN] {message}");
    }

    public static void LogError(string message, Exception? ex = null)
    {
        string text = ex != null ? $"{message}\n{ex}" : message;
        AppendToRollingAppLog($"[ERROR] {Redact(text)}");
    }

    private static void AppendToRollingAppLog(string message)
    {
        lock (_logLock)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                string entry = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fffZ}] {message}\n";

                // Rolling limit ~5 MB
                var fileInfo = new FileInfo(AppLogFilePath);
                if (fileInfo.Exists && fileInfo.Length > 5 * 1024 * 1024)
                {
                    string oldLog = Path.Combine(LogDirectory, "app.old.log");
                    try
                    {
                        if (File.Exists(oldLog)) File.Delete(oldLog);
                        File.Move(AppLogFilePath, oldLog);
                    }
                    catch { }
                }

                File.AppendAllText(AppLogFilePath, entry, Encoding.UTF8);
            }
            catch { }
        }
    }

    public static string BuildCrashReport(string source, Exception? ex, string? extraContext, DateTime timestamp)
    {
        var sb = new StringBuilder();
        sb.AppendLine("================================================================================");
        sb.AppendLine("                         SECUREVAULT CRASH REPORT                                ");
        sb.AppendLine("================================================================================");
        sb.AppendLine($"Timestamp (Local) : {timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}");
        sb.AppendLine($"Timestamp (UTC)   : {timestamp.ToUniversalTime():yyyy-MM-dd HH:mm:ss.fffZ}");
        sb.AppendLine($"Crash Source      : {source}");
        sb.AppendLine($"Current View      : {UiActionTrace.CurrentView}");
        if (!string.IsNullOrEmpty(extraContext))
        {
            sb.AppendLine($"Extra Context     : {extraContext}");
        }

        sb.AppendLine();
        sb.AppendLine("--- ENVIRONMENT DETAILS ---");
        try
        {
            var appVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
            sb.AppendLine($"App Version       : {appVersion}");
            sb.AppendLine($"OS Version        : {Environment.OSVersion} ({RuntimeInformation.OSDescription})");
            sb.AppendLine($"OS Architecture   : {RuntimeInformation.OSArchitecture}");
            sb.AppendLine($"Process Arch      : {RuntimeInformation.ProcessArchitecture}");
            sb.AppendLine($".NET Runtime      : {RuntimeInformation.FrameworkDescription} (ClrVersion: {Environment.Version})");
            sb.AppendLine($"Working Set       : {Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024)} MB");
        }
        catch (Exception envEx)
        {
            sb.AppendLine($"Failed to read environment info: {envEx.Message}");
        }

        sb.AppendLine();
        sb.AppendLine("--- EXCEPTION DETAILS ---");
        if (ex == null)
        {
            sb.AppendLine("No exception object provided.");
        }
        else
        {
            int depth = 0;
            Exception? curr = ex;
            while (curr != null)
            {
                sb.AppendLine($"[Exception Level {depth}]");
                sb.AppendLine($"Type: {curr.GetType().FullName}");
                sb.AppendLine($"HResult: 0x{curr.HResult:X8}");
                sb.AppendLine($"Message: {Redact(curr.Message)}");
                sb.AppendLine($"Source: {curr.Source}");
                sb.AppendLine("Stack Trace:");
                sb.AppendLine(Redact(curr.StackTrace ?? "  (No stack trace available)"));
                sb.AppendLine();

                curr = curr.InnerException;
                depth++;
            }
        }

        sb.AppendLine("--- RECENT UI ACTIONS (RING BUFFER) ---");
        sb.AppendLine(UiActionTrace.FormatHistory());

        sb.AppendLine();
        sb.AppendLine("--- LOADED PROCESS MODULES ---");
        try
        {
            var process = Process.GetCurrentProcess();
            foreach (ProcessModule module in process.Modules)
            {
                try
                {
                    sb.AppendLine($"  {module.ModuleName} | {module.FileVersionInfo.FileVersion} | {module.FileName}");
                }
                catch
                {
                    sb.AppendLine($"  {module.ModuleName}");
                }
            }
        }
        catch (Exception modEx)
        {
            sb.AppendLine($"  Could not enumerate modules: {modEx.Message}");
        }

        sb.AppendLine();
        sb.AppendLine("================================================================================");
        sb.AppendLine("                             END OF REPORT                                      ");
        sb.AppendLine("================================================================================");

        return sb.ToString();
    }

    private static string Redact(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        // Redact potential 64-character hex keys, passwords, and recovery keys
        string redacted = Regex.Replace(input, @"[0-9a-fA-F]{64}", "[REDACTED_HEX_KEY]");
        redacted = Regex.Replace(redacted, @"(password|pwd|passphrase|recoveryKey|secret)=[^;\s&]+", "$1=[REDACTED]", RegexOptions.IgnoreCase);
        redacted = Regex.Replace(redacted, @"([A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4})", "[REDACTED_RECOVERY_KEY]");
        return redacted;
    }
}
