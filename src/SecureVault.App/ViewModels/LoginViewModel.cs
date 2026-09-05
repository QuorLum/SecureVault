using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureVault.App.Services;
using SecureVault.Core;
using SecureVault.Core.Exceptions;

namespace SecureVault.App.ViewModels;

public enum LoginViewMode
{
    FirstTimeWelcome,
    ReturningUserUnlock,
    CreateVaultWizard,
    RecoveryUnlock
}

/// <summary>
/// ViewModel controlling vault unlock, credential entry, first-time onboarding,
/// in-page vault creation wizard, and brute-force lockout protection (N02-N04, M11).
/// </summary>
public partial class LoginViewModel : ObservableObject
{
    private readonly AppSettingsService _settings = AppSettingsService.Instance;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWelcomeVisible))]
    [NotifyPropertyChangedFor(nameof(IsUnlockVisible))]
    [NotifyPropertyChangedFor(nameof(IsCreateWizardVisible))]
    [NotifyPropertyChangedFor(nameof(IsRecoveryVisible))]
    private LoginViewMode _currentViewMode = LoginViewMode.FirstTimeWelcome;

    public bool IsWelcomeVisible => CurrentViewMode == LoginViewMode.FirstTimeWelcome;
    public bool IsUnlockVisible => CurrentViewMode == LoginViewMode.ReturningUserUnlock;
    public bool IsCreateWizardVisible => CurrentViewMode == LoginViewMode.CreateVaultWizard;
    public bool IsRecoveryVisible => CurrentViewMode == LoginViewMode.RecoveryUnlock;

    // --- Unlock State ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VaultDisplayName))]
    [NotifyPropertyChangedFor(nameof(VaultDirectoryDisplay))]
    private string _vaultPath = string.Empty;

    public string VaultDisplayName => string.IsNullOrWhiteSpace(VaultPath) ? "No Vault Selected" : Path.GetFileName(VaultPath);
    public string VaultDirectoryDisplay => string.IsNullOrWhiteSpace(VaultPath) ? string.Empty : (Path.GetDirectoryName(VaultPath) ?? string.Empty);

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
                PasswordHint = string.Empty;
                ShowPasswordHint = false;
            }
        }
        else
        {
            PasswordHint = string.Empty;
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
    private string _recoveryWordsInput = string.Empty;

    [ObservableProperty]
    private int _failedAttempts;

    [ObservableProperty]
    private int _lockoutSecondsRemaining;

    [ObservableProperty]
    private bool _isLockedOut;

    // --- In-Page Vault Creation Wizard State ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewVaultComputedPath))]
    private string _newVaultName = "Personal";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewVaultComputedPath))]
    private string _newVaultFolder = string.Empty;

    public string NewVaultComputedPath
    {
        get
        {
            var folder = string.IsNullOrWhiteSpace(NewVaultFolder)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SecureVault")
                : NewVaultFolder;
            var name = string.IsNullOrWhiteSpace(NewVaultName) ? "Personal" : NewVaultName.Trim();
            if (!name.EndsWith(".vault", StringComparison.OrdinalIgnoreCase))
            {
                name += ".vault";
            }
            return Path.Combine(folder, name);
        }
    }

    [ObservableProperty]
    private string _newVaultPassword = string.Empty;

    [ObservableProperty]
    private string _newVaultPasswordConfirm = string.Empty;

    [ObservableProperty]
    private string _newVaultHint = string.Empty;

    // --- Callbacks ---
    public Func<VaultManager, Task>? OnUnlockSuccess { get; set; }
    public Func<string[], Task<bool>>? OnPromptRecoveryKeyConfirmation { get; set; }
    public Func<Task<string?>>? OnPickVaultFile { get; set; }
    public Func<Task<string?>>? OnPickFolder { get; set; }
    public Action? OnOpenRestoreRequested { get; set; }

    public LoginViewModel()
    {
        var defaultDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        NewVaultFolder = Path.Combine(defaultDocs, "SecureVault");

        InitializeStartupState();
    }

    public void InitializeStartupState()
    {
        if (!string.IsNullOrWhiteSpace(App.StartupVaultPath) && File.Exists(App.StartupVaultPath))
        {
            VaultPath = App.StartupVaultPath;
            CurrentViewMode = LoginViewMode.ReturningUserUnlock;
            return;
        }

        var lastVault = _settings.LastVaultPath;
        if (!string.IsNullOrWhiteSpace(lastVault) && File.Exists(lastVault))
        {
            VaultPath = lastVault;
            CurrentViewMode = LoginViewMode.ReturningUserUnlock;
        }
        else
        {
            CurrentViewMode = LoginViewMode.FirstTimeWelcome;
        }
    }

    // --- Navigation & Mode Switching Commands ---
    [RelayCommand]
    private void GoToWelcome()
    {
        ErrorMessage = string.Empty;
        CurrentViewMode = LoginViewMode.FirstTimeWelcome;
    }

    [RelayCommand]
    private void GoToCreateWizard()
    {
        ErrorMessage = string.Empty;
        NewVaultPassword = string.Empty;
        NewVaultPasswordConfirm = string.Empty;
        NewVaultHint = string.Empty;
        CurrentViewMode = LoginViewMode.CreateVaultWizard;
    }

    [RelayCommand]
    private void GoToUnlock()
    {
        ErrorMessage = string.Empty;
        Password = string.Empty;
        CurrentViewMode = LoginViewMode.ReturningUserUnlock;
    }

    [RelayCommand]
    private void GoToRecovery()
    {
        ErrorMessage = string.Empty;
        RecoveryWordsInput = string.Empty;
        CurrentViewMode = LoginViewMode.RecoveryUnlock;
    }

    [RelayCommand]
    private void CancelCreateWizard()
    {
        ErrorMessage = string.Empty;
        if (!string.IsNullOrWhiteSpace(VaultPath) && File.Exists(VaultPath))
        {
            CurrentViewMode = LoginViewMode.ReturningUserUnlock;
        }
        else
        {
            CurrentViewMode = LoginViewMode.FirstTimeWelcome;
        }
    }

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
                CurrentViewMode = LoginViewMode.ReturningUserUnlock;
            }
        }
    }

    [RelayCommand]
    private async Task BrowseNewVaultFolderAsync()
    {
        if (OnPickFolder != null)
        {
            var folder = await OnPickFolder();
            if (!string.IsNullOrWhiteSpace(folder))
            {
                NewVaultFolder = folder;
            }
        }
    }

    // --- Core Unlock Command ---
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
            ErrorMessage = "Please enter the vault master password.";
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

            // Remember last successful vault
            _settings.LastVaultPath = VaultPath;

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

    // --- Recovery Unlock Command ---
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

            // Remember last successful vault
            _settings.LastVaultPath = VaultPath;

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

    // --- In-Page Vault Creation Command ---
    [RelayCommand(CanExecute = nameof(CanUnlock))]
    private async Task CreateVaultInPageAsync()
    {
        ErrorMessage = string.Empty;

        var targetPath = NewVaultComputedPath;
        var folder = Path.GetDirectoryName(targetPath);

        if (string.IsNullOrWhiteSpace(NewVaultPassword))
        {
            ErrorMessage = "Please enter a master password for the new vault.";
            return;
        }

        if (NewVaultPassword.Length < 8)
        {
            ErrorMessage = "Master password must be at least 8 characters long for strong security.";
            return;
        }

        if (NewVaultPassword != NewVaultPasswordConfirm)
        {
            ErrorMessage = "Passwords do not match. Please re-enter your password.";
            return;
        }

        if (File.Exists(targetPath))
        {
            ErrorMessage = $"A vault file already exists at '{targetPath}'. Please choose a different name or folder.";
            return;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(folder) && !Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not create target directory: {ex.Message}";
            return;
        }

        IsBusy = true;
        BusyMessage = "Generating entropy, dual key-slots, and initialized vault container...";

        VaultManager? vault = null;
        string[]? recoveryWords = null;

        try
        {
            // Create the real vault directly — NO 0-byte orphan files!
            (vault, recoveryWords) = await VaultManager.CreateAsync(targetPath, NewVaultPassword);

            if (!string.IsNullOrWhiteSpace(NewVaultHint))
            {
                try
                {
                    vault.SetPasswordHint(NewVaultHint.Trim());
                }
                catch
                {
                    // Hint is non-critical
                }
            }

            // N23: Interactive 3-word verification gate ONLY during creation
            if (OnPromptRecoveryKeyConfirmation != null && recoveryWords != null)
            {
                bool verified = await OnPromptRecoveryKeyConfirmation(recoveryWords);
                if (!verified)
                {
                    vault.Dispose();
                    vault = null;

                    // Clean up created file if verification was aborted
                    if (File.Exists(targetPath))
                    {
                        try { File.Delete(targetPath); } catch { }
                    }

                    ErrorMessage = "Vault creation was cancelled because the recovery phrase was not verified.";
                    return;
                }
            }

            VaultPath = targetPath;
            _settings.LastVaultPath = targetPath;

            if (OnUnlockSuccess != null && vault != null)
            {
                await OnUnlockSuccess(vault);
            }
        }
        catch (Exception ex)
        {
            if (vault != null)
            {
                vault.Dispose();
            }
            if (File.Exists(targetPath))
            {
                try { File.Delete(targetPath); } catch { }
            }
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
        CreateVaultInPageCommand.NotifyCanExecuteChanged();

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
        CreateVaultInPageCommand.NotifyCanExecuteChanged();
    }
}
