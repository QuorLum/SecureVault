using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;

namespace SecureVault.App.Services;

public enum NotificationSeverity
{
    Informational,
    Success,
    Warning,
    Error
}

public sealed record AppNotification
{
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public NotificationSeverity Severity { get; init; } = NotificationSeverity.Informational;
    public TimeSpan Duration { get; init; } = TimeSpan.FromSeconds(4);
}

/// <summary>
/// Provides application-wide in-app notifications and banner alerts (N18).
/// </summary>
public sealed class NotificationService
{
    private static readonly NotificationService _instance = new();
    public static NotificationService Shared => _instance;

    public event EventHandler<AppNotification>? NotificationPosted;

    public void Show(string title, string message, NotificationSeverity severity = NotificationSeverity.Informational, int durationSeconds = 4)
    {
        var notification = new AppNotification
        {
            Title = title,
            Message = message,
            Severity = severity,
            Duration = TimeSpan.FromSeconds(durationSeconds)
        };

        NotificationPosted?.Invoke(this, notification);
    }

    public void ShowSuccess(string title, string message) => Show(title, message, NotificationSeverity.Success);
    public void ShowWarning(string title, string message) => Show(title, message, NotificationSeverity.Warning, durationSeconds: 6);
    public void ShowError(string title, string message) => Show(title, message, NotificationSeverity.Error, durationSeconds: 8);
}
