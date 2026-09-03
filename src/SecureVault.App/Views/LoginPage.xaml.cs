using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SecureVault.App.ViewModels;
using Windows.Storage.Pickers;

namespace SecureVault.App.Views;

public sealed partial class LoginPage : Page
{
    public LoginViewModel ViewModel { get; } = new();

    public LoginPage()
    {
        InitializeComponent();
        DataContext = ViewModel;

        ViewModel.OnPickVaultFile = PickVaultFileAsync;
        ViewModel.OnPickSaveVaultLocation = PickSaveVaultLocationAsync;
        ViewModel.OnPromptNewVaultPassword = PromptNewVaultPasswordAsync;
        ViewModel.OnPromptRecoveryKeyConfirmation = PromptRecoveryKeyConfirmationAsync;
        ViewModel.OnUnlockSuccess = vault =>
        {
            Frame.Navigate(typeof(MainLibraryPage), vault);
            return Task.CompletedTask;
        };
    }

    private async Task<string?> PickVaultFileAsync()
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".vault");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    private async Task<string?> PickSaveVaultLocationAsync()
    {
        var picker = new FileSavePicker();
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeChoices.Add("SecureVault Container", new List<string>() { ".vault" });
        picker.SuggestedFileName = "MyVault.vault";

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    private async Task<(bool Confirmed, string Password)> PromptNewVaultPasswordAsync(string vaultPath)
    {
        var passwordBox1 = new PasswordBox { PlaceholderText = "Enter password", Margin = new Thickness(0, 0, 0, 8) };
        var passwordBox2 = new PasswordBox { PlaceholderText = "Confirm password" };

        var dialog = new ContentDialog
        {
            Title = "Create New Vault",
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot,
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = $"Set a master password for '{System.IO.Path.GetFileName(vaultPath)}':", FontSize = 12 },
                    passwordBox1,
                    passwordBox2
                }
            }
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
            return (false, string.Empty);

        if (string.IsNullOrWhiteSpace(passwordBox1.Password))
            return (false, string.Empty);

        if (passwordBox1.Password != passwordBox2.Password)
        {
            var err = new ContentDialog
            {
                Title = "Password Mismatch",
                Content = "The entered passwords do not match. Vault creation was cancelled.",
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            await err.ShowAsync();
            return (false, string.Empty);
        }

        return (true, passwordBox1.Password);
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
