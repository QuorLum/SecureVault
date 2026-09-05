using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SecureVault.App.Diagnostics;

/// <summary>
/// Thread-safe ring buffer capturing the last 50 user actions for crash analysis and diagnostics.
/// Automatically redacts sensitive patterns such as keys, passwords, or raw vault contents.
/// </summary>
public static class UiActionTrace
{
    private const int Capacity = 50;
    private static readonly Queue<TraceEntry> _buffer = new(Capacity);
    private static readonly object _lock = new();

    public static string CurrentView { get; set; } = "Startup";

    public record TraceEntry(DateTime TimestampUtc, string View, string Action, string? Details);

    public static void Record(string action, string? details = null)
    {
        lock (_lock)
        {
            if (_buffer.Count >= Capacity)
            {
                _buffer.Dequeue();
            }

            string sanitizedAction = Redact(action);
            string? sanitizedDetails = details != null ? Redact(details) : null;
            _buffer.Enqueue(new TraceEntry(DateTime.UtcNow, CurrentView, sanitizedAction, sanitizedDetails));
        }
    }

    public static IReadOnlyList<TraceEntry> GetRecentActions()
    {
        lock (_lock)
        {
            return _buffer.ToArray();
        }
    }

    public static string FormatHistory()
    {
        var actions = GetRecentActions();
        if (actions.Count == 0) return "  (No UI actions recorded)";

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < actions.Count; i++)
        {
            var entry = actions[i];
            sb.AppendLine($"  {i + 1:D2}. [{entry.TimestampUtc:yyyy-MM-dd HH:mm:ss.fffZ}] [{entry.View}] {entry.Action}{(string.IsNullOrEmpty(entry.Details) ? "" : $" ({entry.Details})")}");
        }
        return sb.ToString().TrimEnd();
    }

    private static string Redact(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        // Redact potential 64-character hex keys, recovery keys, or password fields
        string redacted = Regex.Replace(input, @"[0-9a-fA-F]{64}", "[REDACTED_HEX_KEY]");
        redacted = Regex.Replace(redacted, @"(password|pwd|passphrase|recoveryKey|secret)=[^;\s&]+", "$1=[REDACTED]", RegexOptions.IgnoreCase);
        redacted = Regex.Replace(redacted, @"([A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4})", "[REDACTED_RECOVERY_KEY]");
        return redacted;
    }
}
