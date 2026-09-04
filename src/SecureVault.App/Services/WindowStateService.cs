using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using Windows.Storage;

namespace SecureVault.App.Services;

/// <summary>
/// Persists and restores window geometry and maximized state across application sessions (N20).
/// </summary>
public static class WindowStateService
{
    private const string WidthKey = "Window_Width";
    private const string HeightKey = "Window_Height";
    private const string XKey = "Window_X";
    private const string YKey = "Window_Y";
    private const string MaximizedKey = "Window_IsMaximized";

    private const int DefaultWidth = 1200;
    private const int DefaultHeight = 800;

    public static void RestoreWindowState(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        try
        {
            var appWindow = window.AppWindow;
            if (appWindow == null) return;

            var settings = ApplicationData.Current.LocalSettings.Values;

            int width = settings.TryGetValue(WidthKey, out var w) && w is int iw ? iw : DefaultWidth;
            int height = settings.TryGetValue(HeightKey, out var h) && h is int ih ? ih : DefaultHeight;
            bool isMaximized = settings.TryGetValue(MaximizedKey, out var m) && m is bool bm && bm;

            appWindow.Resize(new SizeInt32(width, height));

            if (settings.TryGetValue(XKey, out var x) && settings.TryGetValue(YKey, out var y) &&
                x is int ix && y is int iy)
            {
                appWindow.Move(new PointInt32(ix, iy));
            }

            if (isMaximized)
            {
                if (appWindow.Presenter is OverlappedPresenter presenter)
                {
                    presenter.Maximize();
                }
            }
        }
        catch
        {
            // Fallback gracefully to default WinUI window dimensions
        }
    }

    public static void SaveWindowState(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        try
        {
            var appWindow = window.AppWindow;
            if (appWindow == null) return;

            var settings = ApplicationData.Current.LocalSettings.Values;

            bool isMaximized = appWindow.Presenter is OverlappedPresenter presenter &&
                               presenter.State == OverlappedPresenterState.Maximized;

            settings[MaximizedKey] = isMaximized;

            if (!isMaximized)
            {
                var size = appWindow.Size;
                var position = appWindow.Position;

                settings[WidthKey] = size.Width;
                settings[HeightKey] = size.Height;
                settings[XKey] = position.X;
                settings[YKey] = position.Y;
            }
        }
        catch
        {
            // Suppress persistence failure on shutdown
        }
    }
}
