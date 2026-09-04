using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureVault.Core.Backup;

namespace SecureVault.App.ViewModels;

public sealed class PartInspectionItem
{
    public string FileName { get; set; } = string.Empty;
    public string StatusText { get; set; } = string.Empty;
    public string StatusColor { get; set; } = "#10B981"; // Emerald green
    public bool IsValid { get; set; }
}

public partial class BackupRestoreViewModel : ObservableObject
{
    private readonly string? _currentVaultPath;

    [ObservableProperty]
    private string _vaultPath = string.Empty;

    [ObservableProperty]
    private bool _isSplitBackup = false;

    [ObservableProperty]
    private int _selectedSplitSizeIndex = 0; // 0: 50GB, 1: 25GB, 2: 10GB, 3: 4GB, 4: 1GB, 5: 100MB, 6: 10MB

    [ObservableProperty]
    private string _backupDestinationFolder = string.Empty;

    [ObservableProperty]
    private string _restoreSourcePath = string.Empty;

    [ObservableProperty]
    private string _restoreDestinationFolder = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _busyStatus = string.Empty;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isSuccess;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public ObservableCollection<PartInspectionItem> PartInspectionItems { get; } = new();

    public Func<Task<string?>>? OnPickFolder { get; set; }
    public Func<Task<string?>>? OnPickRestoreSourceFile { get; set; }

    public BackupRestoreViewModel(string? currentVaultPath = null)
    {
        _currentVaultPath = currentVaultPath;
        if (!string.IsNullOrEmpty(currentVaultPath))
        {
            VaultPath = currentVaultPath;
            string? defaultBackupDir = Path.Combine(Path.GetDirectoryName(currentVaultPath) ?? "", "Backups");
            BackupDestinationFolder = defaultBackupDir;
        }
    }

    public long GetSelectedSplitSizeBytes()
    {
        return SelectedSplitSizeIndex switch
        {
            0 => 50L * 1024 * 1024 * 1024,   // 50 GB
            1 => 25L * 1024 * 1024 * 1024,   // 25 GB
            2 => 10L * 1024 * 1024 * 1024,   // 10 GB
            3 => 4L * 1024 * 1024 * 1024,    // 4 GB
            4 => 1L * 1024 * 1024 * 1024,    // 1 GB
            5 => 100L * 1024 * 1024,         // 100 MB
            6 => 10L * 1024 * 1024,          // 10 MB
            _ => 50L * 1024 * 1024 * 1024
        };
    }

    [RelayCommand]
    public async Task BrowseBackupDestinationAsync()
    {
        if (OnPickFolder != null)
        {
            var folder = await OnPickFolder();
            if (!string.IsNullOrEmpty(folder))
            {
                BackupDestinationFolder = folder;
            }
        }
    }

    [RelayCommand]
    public async Task BrowseRestoreSourceAsync()
    {
        if (OnPickRestoreSourceFile != null)
        {
            var file = await OnPickRestoreSourceFile();
            if (!string.IsNullOrEmpty(file))
            {
                RestoreSourcePath = file;
                await InspectRestoreSourceAsync();
            }
        }
    }

    [RelayCommand]
    public async Task BrowseRestoreDestinationAsync()
    {
        if (OnPickFolder != null)
        {
            var folder = await OnPickFolder();
            if (!string.IsNullOrEmpty(folder))
            {
                RestoreDestinationFolder = folder;
            }
        }
    }

    [RelayCommand]
    public async Task CreateBackupAsync()
    {
        if (string.IsNullOrWhiteSpace(VaultPath) || !File.Exists(VaultPath))
        {
            ErrorMessage = "Please specify a valid vault file to back up.";
            HasError = true;
            return;
        }

        if (string.IsNullOrWhiteSpace(BackupDestinationFolder))
        {
            ErrorMessage = "Please select a backup destination folder.";
            HasError = true;
            return;
        }

        IsBusy = true;
        HasError = false;
        IsSuccess = false;
        StatusMessage = "Starting backup...";
        ProgressPercent = 0;

        try
        {
            var progress = new Progress<double>(pct =>
            {
                ProgressPercent = pct * 100.0;
                StatusMessage = $"Backing up: {ProgressPercent:0.0}%";
            });

            if (IsSplitBackup)
            {
                long splitSize = GetSelectedSplitSizeBytes();
                await SplitBackupService.BackupSplitChainAsync(VaultPath, BackupDestinationFolder, splitSize, progress);
            }
            else
            {
                await BackupService.BackupChainAsync(VaultPath, BackupDestinationFolder, progress);
            }

            IsSuccess = true;
            StatusMessage = "Backup completed successfully and verified with SHA-256.";
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = $"Backup failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task InspectRestoreSourceAsync()
    {
        if (string.IsNullOrWhiteSpace(RestoreSourcePath) || !File.Exists(RestoreSourcePath))
            return;

        IsBusy = true;
        BusyStatus = "Inspecting backup archive integrity...";
        PartInspectionItems.Clear();

        try
        {
            if (RestoreSourcePath.EndsWith(".backup.manifest", StringComparison.OrdinalIgnoreCase) ||
                RestoreSourcePath.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase))
            {
                var check = await RestoreService.CheckPartsAsync(RestoreSourcePath);
                var manifest = BackupManifest.LoadFromFile(RestoreSourcePath);

                foreach (var chainPart in manifest.ChainParts)
                {
                    if (manifest.IsSplit)
                    {
                        foreach (var split in chainPart.Splits)
                        {
                            var failed = check.FailedParts.FirstOrDefault(f => f.SplitFileName == split.FileName);
                            bool isValid = failed == null;

                            PartInspectionItems.Add(new PartInspectionItem
                            {
                                FileName = $"{chainPart.VaultFileName} → {split.FileName}",
                                IsValid = isValid,
                                StatusText = isValid ? "Verified (Valid SHA-256)" : failed?.Reason ?? "Corrupted",
                                StatusColor = isValid ? "#10B981" : "#EF4444"
                            });
                        }
                    }
                    else
                    {
                        var failed = check.FailedParts.FirstOrDefault(f => f.VaultFileName == chainPart.VaultFileName);
                        bool isValid = failed == null;

                        PartInspectionItems.Add(new PartInspectionItem
                        {
                            FileName = chainPart.VaultFileName,
                            IsValid = isValid,
                            StatusText = isValid ? "Verified (Valid SHA-256)" : failed?.Reason ?? "Corrupted",
                            StatusColor = isValid ? "#10B981" : "#EF4444"
                        });
                    }
                }
            }
            else
            {
                var report = await BackupVerifier.VerifyBackupAsync(RestoreSourcePath);
                bool isValid = report.IsHealthy;

                PartInspectionItems.Add(new PartInspectionItem
                {
                    FileName = Path.GetFileName(RestoreSourcePath),
                    IsValid = isValid,
                    StatusText = isValid ? "Verified Single Container" : string.Join("; ", report.Issues),
                    StatusColor = isValid ? "#10B981" : "#EF4444"
                });
            }
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = $"Inspection error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task ExecuteRestoreAsync()
    {
        if (string.IsNullOrWhiteSpace(RestoreSourcePath) || !File.Exists(RestoreSourcePath))
        {
            ErrorMessage = "Please select a backup archive to restore.";
            HasError = true;
            return;
        }

        if (string.IsNullOrWhiteSpace(RestoreDestinationFolder))
        {
            ErrorMessage = "Please choose a destination folder for the restored vault.";
            HasError = true;
            return;
        }

        IsBusy = true;
        HasError = false;
        IsSuccess = false;
        StatusMessage = "Restoring vault...";
        ProgressPercent = 0;

        try
        {
            var progress = new Progress<double>(pct =>
            {
                ProgressPercent = pct * 100.0;
                StatusMessage = $"Restoring: {ProgressPercent:0.0}%";
            });

            if (RestoreSourcePath.EndsWith(".backup.manifest", StringComparison.OrdinalIgnoreCase) ||
                RestoreSourcePath.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase))
            {
                await RestoreService.RestoreChainAsync(RestoreSourcePath, RestoreDestinationFolder, progress);
            }
            else
            {
                string targetFileName = Path.GetFileName(RestoreSourcePath);
                string targetPath = Path.Combine(RestoreDestinationFolder, targetFileName);
                await RestoreService.RestoreSingleFileAsync(RestoreSourcePath, targetPath, progress);
            }

            IsSuccess = true;
            StatusMessage = "Restoration completed! All vault parts reassembled and validated.";
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = $"Restore failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
