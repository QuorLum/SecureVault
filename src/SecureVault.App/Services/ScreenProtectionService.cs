using System.Runtime.InteropServices;

namespace SecureVault.App.Services;

/// <summary>
/// Controls OS-level window display affinity to block screen recorders, capture tools,
/// and screenshot utilities from capturing confidential vault data (M17).
/// </summary>
public static class ScreenProtectionService
{
    private const uint WDA_NONE = 0x00000000;
    private const uint WDA_MONITOR = 0x00000001;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    public static bool IsProtectionEnabled { get; private set; }

    /// <summary>
    /// Enables or disables screen capture protection for the specified window handle.
    /// Uses WDA_EXCLUDEFROMCAPTURE (Windows 10 2004+ / Windows 11) with fallback to WDA_MONITOR.
    /// </summary>
    public static bool SetProtection(IntPtr hWnd, bool enabled)
    {
        if (hWnd == IntPtr.Zero) return false;

        bool success;
        if (enabled)
        {
            // First attempt WDA_EXCLUDEFROMCAPTURE
            success = SetWindowDisplayAffinity(hWnd, WDA_EXCLUDEFROMCAPTURE);
            if (!success)
            {
                // Fallback to WDA_MONITOR for older Windows versions
                success = SetWindowDisplayAffinity(hWnd, WDA_MONITOR);
            }
        }
        else
        {
            success = SetWindowDisplayAffinity(hWnd, WDA_NONE);
        }

        if (success)
        {
            IsProtectionEnabled = enabled;
        }

        return success;
    }
}
