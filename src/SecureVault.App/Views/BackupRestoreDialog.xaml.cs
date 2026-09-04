using Microsoft.UI.Xaml.Controls;
using SecureVault.App.ViewModels;
using Windows.Storage.Pickers;

namespace SecureVault.App.Views;

public sealed partial class BackupRestoreDialog : ContentDialog
{
    public BackupRestoreViewModel ViewModel { get; }

    public BackupRestoreDialog(string? currentVaultPath = null)
    {
        ViewModel = new BackupRestoreViewModel(currentVaultPath);
        InitializeComponent();

        ViewModel.OnPickFolder = PickFolderAsync;
        ViewModel.OnPickRestoreSourceFile = PickRestoreSourceFileAsync;
    }

    private async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    private async Task<string?> PickRestoreSourceFileAsync()
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".manifest");
        picker.FileTypeFilter.Add(".vault");
        picker.FileTypeFilter.Add("*");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }
}
