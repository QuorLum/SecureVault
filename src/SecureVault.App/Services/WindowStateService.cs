using System.IO;
using System.Text.Json;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace SecureVault.App.Services;

/// <summary>
/// Persists and restores window geometry and maximized state across application sessions (N20).
/// Uses local JSON config safe for unpackaged desktop execution.
/// </summary>
public static class WindowStateService
{
    private static readonly string StateFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SecureVault", "window_state.json");

    private sealed class WindowStateModel
    {
        public int Width { get; set; } = 1280;
        public int Height { get; set; } = 800;
        public int? X { get; set; }
        public int? Y { get; set; }
        public bool IsMaximized { get; set; } = true; // Default to full screen / maximized
    }

    public static void RestoreWindowState(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        try
        {
            var appWindow = window.AppWindow;
            if (appWindow == null) return;

            WindowStateModel state = new();
            if (File.Exists(StateFilePath))
            {
                try
                {
                    var json = File.ReadAllText(StateFilePath);
                    state = JsonSerializer.Deserialize<WindowStateModel>(json) ?? new();
                }
                catch
                {
                    state = new();
                }
            }

            if (state.Width > 300 && state.Height > 300)
            {
                appWindow.Resize(new SizeInt32(state.Width, state.Height));
            }

            if (state.X.HasValue && state.Y.HasValue)
            {
                appWindow.Move(new PointInt32(state.X.Value, state.Y.Value));
            }

            // User requirement: app should open in full screen / maximized
            if (state.IsMaximized)
            {
                if (appWindow.Presenter is OverlappedPresenter presenter)
                {
                    presenter.Maximize();
                }
            }
        }
        catch
        {
            try
            {
                if (window.AppWindow?.Presenter is OverlappedPresenter presenter)
                {
                    presenter.Maximize();
                }
            }
            catch { }
        }
    }

    public static void SaveWindowState(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        try
        {
            var appWindow = window.AppWindow;
            if (appWindow == null) return;

            bool isMaximized = appWindow.Presenter is OverlappedPresenter presenter &&
                               presenter.State == OverlappedPresenterState.Maximized;

            var state = new WindowStateModel
            {
                IsMaximized = isMaximized
            };

            if (!isMaximized)
            {
                var size = appWindow.Size;
                var position = appWindow.Position;

                state.Width = size.Width;
                state.Height = size.Height;
                state.X = position.X;
                state.Y = position.Y;
            }

            var dir = Path.GetDirectoryName(StateFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(state);
            File.WriteAllText(StateFilePath, json);
        }
        catch
        {
            // Suppress persistence failure on shutdown
        }
    }
}
