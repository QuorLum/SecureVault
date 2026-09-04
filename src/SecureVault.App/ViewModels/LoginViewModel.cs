using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureVault.Core;
using SecureVault.Core.Exceptions;

namespace SecureVault.App.ViewModels;

/// <summary>
/// ViewModel controlling vault unlock, credential entry, brute-force delays, and creation flow (N02-N04, M11).
/// </summary>
public partial class LoginViewModel : ObservableObject
{
    [ObservableProperty]
    private string _vaultPath = string.Empty;

    partial void OnVaultPathChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && File.Exists(value))
        {
            try
            {
                var hint = VaultManager.GetPasswordHint(value);
                if (!string.IsNullOrWhiteSpace(hint))
                {
                    PasswordHint = hint;
                    ShowPasswordHint = true;
                }
                else
                {
                    PasswordHint = string.Empty;
                    ShowPasswordHint = false;
                }
            }
            catch
            {
                ShowPasswordHint = false;
            }
        }
        else
        {
            ShowPasswordHint = false;
        }
    }

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _passwordHint = string.Empty;

    [ObservableProperty]
    private bool _showPasswordHint;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _busyMessage = "Decrypting vault container...";

    [ObservableProperty]
    private bool _showRecoveryInput;

    [ObservableProperty]
    private string _recoveryWordsInput = string.Empty;

    [ObservableProperty]
    private int _failedAttempts;

    [ObservableProperty]
    private int _lockoutSecondsRemaining;

    [ObservableProperty]
    private bool _isLockedOut;

    public Func<VaultManager, Task>? OnUnlockSuccess { get; set; }
    public Func<string, Task<(bool Confirmed, string Password)>>? OnPromptNewVaultPassword { get; set; }
    public Func<string[], Task<bool>>? OnPromptRecoveryKeyConfirmation { get; set; }
    public Func<Task<string?>>? OnPickVaultFile { get; set; }
    public Func<Task<string?>>? OnPickSaveVaultLocation { get; set; }
    public Action? OnOpenRestoreRequested { get; set; }

    [RelayCommand]
    private void OpenRestoreDialog()
    {
        OnOpenRestoreRequested?.Invoke();
    }

    [RelayCommand]
    private async Task BrowseVaultAsync()
    {
        if (OnPickVaultFile != null)
        {
            var path = await OnPickVaultFile();
            if (!string.IsNullOrWhiteSpace(path))
            {
                VaultPath = path;
                ErrorMessage = string.Empty;
            }
        }
    }

    [RelayCommand]
    private void ToggleRecoveryMode()
    {
        ShowRecoveryInput = !ShowRecoveryInput;
        ErrorMessage = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanUnlock))]
    private async Task UnlockAsync()
    {
        if (string.IsNullOrWhiteSpace(VaultPath))
        {
            ErrorMessage = "Please select or enter a vault file path.";
            return;
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Please enter the vault password.";
            return;
        }

        IsBusy = true;
        BusyMessage = "Verifying cryptographic key derivation (Argon2id)...";
        ErrorMessage = string.Empty;

        try
        {
            var vault = await VaultManager.OpenAsync(VaultPath, Password);
            FailedAttempts = 0;
            Password = string.Empty;

            if (OnUnlockSuccess != null)
            {
                await OnUnlockSuccess(vault);
            }
        }
        catch (InvalidPasswordException)
        {
            FailedAttempts++;
            await ApplyBruteForceDelayAsync("Incorrect password. Access denied.");
        }
        catch (FileNotFoundException)
        {
            ErrorMessage = $"Vault file was not found at '{VaultPath}'.";
        }
        catch (VaultAlreadyOpenException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (CorruptedVaultException ex)
        {
            ErrorMessage = $"Vault corruption detected: {ex.Message}";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to open vault: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanUnlock))]
    private async Task UnlockWithRecoveryAsync()
    {
        if (string.IsNullOrWhiteSpace(VaultPath))
        {
            ErrorMessage = "Please select a vault file.";
            return;
        }

        if (string.IsNullOrWhiteSpace(RecoveryWordsInput))
        {
            ErrorMessage = "Please enter the 24-word recovery phrase.";
            return;
        }

        var words = RecoveryWordsInput.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length != 24)
        {
            ErrorMessage = $"A valid recovery phrase requires exactly 24 words (entered {words.Length}).";
            return;
        }

        IsBusy = true;
        BusyMessage = "Reconstructing master key from recovery seed...";
        ErrorMessage = string.Empty;

        try
        {
            var vault = await VaultManager.OpenWithRecoveryKeyAsync(VaultPath, words);
            FailedAttempts = 0;
            RecoveryWordsInput = string.Empty;

            if (OnUnlockSuccess != null)
            {
                await OnUnlockSuccess(vault);
            }
        }
        catch (InvalidRecoveryKeyException ex)
        {
            FailedAttempts++;
            await ApplyBruteForceDelayAsync($"Invalid recovery key: {ex.Message}");
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Recovery failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanUnlock))]
    private async Task CreateVaultAsync()
    {
        if (OnPickSaveVaultLocation == null || OnPromptNewVaultPassword == null || OnPromptRecoveryKeyConfirmation == null)
            return;

        var savePath = await OnPickSaveVaultLocation();
        if (string.IsNullOrWhiteSpace(savePath))
            return;

        var (confirmed, newPassword) = await OnPromptNewVaultPassword(savePath);
        if (!confirmed || string.IsNullOrWhiteSpace(newPassword))
            return;

        IsBusy = true;
        BusyMessage = "Generating entropy, dual key-slots, and initialized vault container...";
        ErrorMessage = string.Empty;

        try
        {
            var (vault, recoveryWords) = await VaultManager.CreateAsync(savePath, newPassword);

            // N23: Enforce interactive 3-word verification gate ONLY during creation
            bool verified = await OnPromptRecoveryKeyConfirmation(recoveryWords);
            if (!verified)
            {
                vault.Dispose();
                if (File.Exists(savePath))
                {
                    try { File.Delete(savePath); } catch { }
                }
                ErrorMessage = "Vault creation cancelled: Recovery key was not verified.";
                return;
            }

            VaultPath = savePath;
            if (OnUnlockSuccess != null)
            {
                await OnUnlockSuccess(vault);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to create vault: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanUnlock() => !IsBusy && !IsLockedOut;

    private async Task ApplyBruteForceDelayAsync(string message)
    {
        int delaySeconds = Math.Min((int)Math.Pow(2, FailedAttempts), 60);
        IsLockedOut = true;
        UnlockCommand.NotifyCanExecuteChanged();
        UnlockWithRecoveryCommand.NotifyCanExecuteChanged();
        CreateVaultCommand.NotifyCanExecuteChanged();

        for (int sec = delaySeconds; sec > 0; sec--)
        {
            LockoutSecondsRemaining = sec;
            ErrorMessage = $"{message} Too many attempts. Try again in {sec}s.";
            await Task.Delay(1000);
        }

        ErrorMessage = message;
        IsLockedOut = false;
        LockoutSecondsRemaining = 0;
        UnlockCommand.NotifyCanExecuteChanged();
        UnlockWithRecoveryCommand.NotifyCanExecuteChanged();
        CreateVaultCommand.NotifyCanExecuteChanged();
    }
}
