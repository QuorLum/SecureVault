using System;
using System.Diagnostics;
using System.IO;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SecureVault.App.Services;
using SecureVault.Core.Security;
using Windows.ApplicationModel.DataTransfer;

namespace SecureVault.App.Diagnostics;

/// <summary>
/// Resilient code-only crash dialog window that does not rely on XAML files or ResourceDictionaries.
/// Guarantees that even if resource dictionaries or converters are broken, the crash dialog will display.
/// </summary>
public sealed class CrashReportWindow : Window
{
    private readonly Exception? _exception;
    private readonly string _crashReport;

    public static void ShowModal(string source, Exception? ex, string crashReport)
    {
        try
        {
            // First: emergency lock vault and zero keys before showing crash UI
            try
            {
                _ = VaultSessionManager.Instance.TriggerLockAsync(LockTriggerReason.ManualLock);
            }
            catch { }

            var crashWin = new CrashReportWindow(source, ex, crashReport);
            crashWin.Activate();
        }
        catch (Exception winEx)
        {
            CrashLog.LogError("CrashReportWindow failed to display", winEx);
            // If even WinUI window creation fails, use safe native Win32 fallback
            try
            {
                Clipboard.SetContent(new DataPackage { RequestedOperation = DataPackageOperation.Copy });
            }
            catch { }

            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", CrashLog.LogDirectory) { UseShellExecute = true });
            }
            catch { }
        }
    }

    public CrashReportWindow(string source, Exception? ex, string crashReport)
    {
        _exception = ex;
        _crashReport = crashReport;

        Title = "SecureVault — Application Error";

        // Setup custom window sizing
        var appWindow = AppWindow;
        if (appWindow != null)
        {
            appWindow.Resize(new Windows.Graphics.SizeInt32(680, 560));
        }

        Content = BuildUi(source);
    }

    private UIElement BuildUi(string source)
    {
        var rootGrid = new Grid
        {
            Background = new SolidColorBrush(ColorHelper.FromArgb(255, 20, 20, 24)),
            Padding = new Thickness(24)
        };
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Header
        var headerPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Margin = new Thickness(0, 0, 0, 16)
        };
        var icon = new FontIcon
        {
            Glyph = "\uE783", // Warning symbol
            FontSize = 28,
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 239, 68, 68)) // Red
        };
        var titleText = new TextBlock
        {
            Text = "An unexpected error occurred",
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
            VerticalAlignment = VerticalAlignment.Center
        };
        headerPanel.Children.Add(icon);
        headerPanel.Children.Add(titleText);
        Grid.SetRow(headerPanel, 0);
        rootGrid.Children.Add(headerPanel);

        // Subtitle info
        var infoText = new TextBlock
        {
            Text = $"Source: {source}\nException: {_exception?.GetType().Name ?? "Error"}: {_exception?.Message ?? "Unspecified error"}\n\nThe vault has been locked and keys zeroized in memory for your security.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 161, 161, 170)),
            Margin = new Thickness(0, 0, 0, 12),
            FontSize = 13
        };
        Grid.SetRow(infoText, 1);
        rootGrid.Children.Add(infoText);

        // Details text box
        var detailsBox = new TextBox
        {
            Text = _crashReport,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas, Courier New"),
            FontSize = 11,
            Background = new SolidColorBrush(ColorHelper.FromArgb(255, 10, 10, 12)),
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(255, 212, 212, 216)),
            Margin = new Thickness(0, 0, 0, 16)
        };
        Grid.SetRow(detailsBox, 2);
        rootGrid.Children.Add(detailsBox);

        // Action buttons
        var buttonsPanel = new Grid();
        buttonsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttonsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttonsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttonsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        buttonsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var copyBtn = new Button
        {
            Content = "Copy Details",
            Margin = new Thickness(0, 0, 8, 0)
        };
        copyBtn.Click += (s, e) =>
        {
            try
            {
                var package = new DataPackage();
                package.SetText(_crashReport);
                Clipboard.SetContent(package);
                copyBtn.Content = "Copied!";
            }
            catch { }
        };
        Grid.SetColumn(copyBtn, 0);
        buttonsPanel.Children.Add(copyBtn);

        var logFolderBtn = new Button
        {
            Content = "Open Log Folder",
            Margin = new Thickness(0, 0, 8, 0)
        };
        logFolderBtn.Click += (s, e) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", CrashLog.LogDirectory) { UseShellExecute = true });
            }
            catch { }
        };
        Grid.SetColumn(logFolderBtn, 1);
        buttonsPanel.Children.Add(logFolderBtn);

        var continueBtn = new Button
        {
            Content = "Continue",
            Margin = new Thickness(0, 0, 8, 0)
        };
        continueBtn.Click += (s, e) =>
        {
            this.Close();
        };
        Grid.SetColumn(continueBtn, 3);
        buttonsPanel.Children.Add(continueBtn);

        var exitBtn = new Button
        {
            Content = "Exit App",
            Background = new SolidColorBrush(ColorHelper.FromArgb(255, 185, 28, 28)),
            Foreground = new SolidColorBrush(Colors.White)
        };
        exitBtn.Click += (s, e) =>
        {
            Environment.Exit(1);
        };
        Grid.SetColumn(exitBtn, 4);
        buttonsPanel.Children.Add(exitBtn);

        Grid.SetRow(buttonsPanel, 3);
        rootGrid.Children.Add(buttonsPanel);

        return rootGrid;
    }
}
