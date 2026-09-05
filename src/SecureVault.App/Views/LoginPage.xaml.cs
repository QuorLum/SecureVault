using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SecureVault.App.ViewModels;
using Windows.Storage.Pickers;
using Windows.System;

namespace SecureVault.App.Views;

public sealed partial class LoginPage : Page
{
    public LoginViewModel ViewModel { get; } = new();

    public LoginPage()
    {
        InitializeComponent();
        DataContext = ViewModel;

        // Wire password box bindings
        MasterPasswordBox.PasswordChanged += (s, e) => ViewModel.Password = MasterPasswordBox.Password;
        NewVaultPasswordBox.PasswordChanged += (s, e) => ViewModel.NewVaultPassword = NewVaultPasswordBox.Password;
        NewVaultPasswordConfirmBox.PasswordChanged += (s, e) => ViewModel.NewVaultPasswordConfirm = NewVaultPasswordConfirmBox.Password;

        // Wire ViewModel delegates
        ViewModel.OnPickVaultFile = PickVaultFileAsync;
        ViewModel.OnPickFolder = PickFolderAsync;
        ViewModel.OnPromptRecoveryKeyConfirmation = PromptRecoveryKeyConfirmationAsync;

        ViewModel.OnUnlockSuccess = vault =>
        {
            Services.VaultSessionManager.Instance.StartSession(vault, Frame);
            Frame.Navigate(typeof(MainLibraryPage), vault);
            return Task.CompletedTask;
        };

        ViewModel.OnOpenRestoreRequested = async () =>
        {
            var dialog = new BackupRestoreDialog
            {
                XamlRoot = XamlRoot
            };
            await dialog.ShowAsync();
        };

        Loaded += (s, e) =>
        {
            if (ViewModel.IsUnlockVisible)
            {
                MasterPasswordBox.Focus(FocusState.Programmatic);
            }
        };
    }

    private void MasterPasswordBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            if (ViewModel.UnlockCommand.CanExecute(null))
            {
                ViewModel.UnlockCommand.Execute(null);
            }
        }
    }

    private void NewVaultPasswordConfirmBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            if (ViewModel.CreateVaultInPageCommand.CanExecute(null))
            {
                ViewModel.CreateVaultInPageCommand.Execute(null);
            }
        }
    }

    private async Task<string?> PickVaultFileAsync()
    {
        var picker = new FileOpenPicker();
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add(".vault");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    private async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker();
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add("*");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    private async Task<bool> PromptRecoveryKeyConfirmationAsync(string[] recoveryWords)
    {
        var dialog = new RecoveryKeyConfirmationDialog(recoveryWords)
        {
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }
}
