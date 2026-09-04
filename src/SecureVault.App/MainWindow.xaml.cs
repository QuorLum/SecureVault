using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace SecureVault.App;

/// <summary>
/// The application window. This hosts a Frame that displays pages.
/// </summary>
public sealed partial class MainWindow : Window
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool SetWindowText(IntPtr hWnd, string lpString);

    private const uint WM_SETICON = 0x0080;
    private const IntPtr ICON_SMALL = 0;
    private const IntPtr ICON_BIG = 1;
    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        Title = "SecureVault";
        AppWindow.Title = "SecureVault";

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (hwnd != IntPtr.Zero)
        {
            SetWindowText(hwnd, "SecureVault");
        }

        SetApplicationIcon();

        // Restore window geometry (N20)
        Services.WindowStateService.RestoreWindowState(this);
        Closed += (s, e) => Services.WindowStateService.SaveWindowState(this);

        // Navigate the root frame to the login page on startup.
        RootFrame.Navigate(typeof(Views.LoginPage));
    }

    private void SetApplicationIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(iconPath))
            {
                AppWindow.SetIcon(iconPath);

                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                if (hwnd != IntPtr.Zero)
                {
                    IntPtr hIconBig = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 32, 32, LR_LOADFROMFILE);
                    IntPtr hIconSmall = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
                    if (hIconBig != IntPtr.Zero)
                    {
                        SendMessage(hwnd, WM_SETICON, ICON_BIG, hIconBig);
                    }
                    if (hIconSmall != IntPtr.Zero)
                    {
                        SendMessage(hwnd, WM_SETICON, ICON_SMALL, hIconSmall);
                    }
                }
            }
        }
        catch
        {
            // Graceful fallback if window icon cannot be set
        }
    }
}
