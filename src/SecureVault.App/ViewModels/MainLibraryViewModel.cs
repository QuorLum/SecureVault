using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureVault.Core;
using SecureVault.Core.Cache;
using SecureVault.Core.Format;
using SecureVault.Core.Operations;
using SecureVault.Core.Organization;

namespace SecureVault.App.ViewModels;

/// <summary>
/// Primary ViewModel driving the Main Library explorer view, navigation, toolbar actions,
/// search, sort, and background batch file operations (N05-N09, C02-C04, C07, C10-C15).
/// </summary>
public partial class MainLibraryViewModel : ObservableObject
{
    private readonly VaultManager _vault;
    private readonly VirtualFolderService _folderService;
    private readonly TagService _tagService;
    private readonly SearchService _searchService;
    private readonly FileManagementOperations _fileOps;
    private readonly BatchFileAddOperation _batchAdd;
    private CancellationTokenSource? _operationCts;

    public VaultManager Vault => _vault;

    [ObservableProperty]
    private string _currentFolderPath = "/";

    [ObservableProperty]
    private Guid? _currentFolderGuid;

    [ObservableProperty]
    private FileCategory? _selectedCategory;

    [ObservableProperty]
    private bool _isFavoritesView;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private SortField _currentSortField = SortField.Name;

    [ObservableProperty]
    private SortDirection _currentSortDirection = SortDirection.Ascending;

    [ObservableProperty]
    private string _fileCountText = "0 items";

    [ObservableProperty]
    private string _vaultSizeText = "0 B";

    [ObservableProperty]
    private string _diskFreeSpaceText = "Ready";

    [ObservableProperty]
    private bool _isOperationRunning;

    [ObservableProperty]
    private string _operationTitle = string.Empty;

    [ObservableProperty]
    private string _operationStatusText = string.Empty;

    [ObservableProperty]
    private double _operationProgressPercentage;

    [ObservableProperty]
    private string _operationSpeedText = string.Empty;

    [ObservableProperty]
    private string _operationEtaText = string.Empty;

    public ObservableCollection<FileItemViewModel> Files { get; } = new();
    public ObservableCollection<VirtualFolder> Folders { get; } = new();
    public ObservableCollection<string> AllTags { get; } = new();

    public Action? OnLockRequested { get; set; }
    public Action<FileItemViewModel>? OnOpenFileRequested { get; set; }
    public Action? OnCreateNewNoteRequested { get; set; }
    public Action? OnOpenFileManagerRequested { get; set; }
    public Action? OnOpenBackupRequested { get; set; }
    public ViewModeViewModel ViewMode { get; } = new();
    public Action? OnOpenCompactionRequested { get; set; }
    public Action? OnOpenSettingsRequested { get; set; }
    public Action? OnOpenVaultChainHealthRequested { get; set; }
    public Func<Task<IReadOnlyList<string>>>? OnPickFilesToAdd { get; set; }
    public Func<Task<string?>>? OnPickFolderToAdd { get; set; }
    public Func<string, Task<string?>>? OnPickExportDestinationFile { get; set; }
    public Func<Task<string?>>? OnPickExportDestinationFolder { get; set; }
    public Func<string, Task<string?>>? OnPromptRename { get; set; }
    public Func<string, string, Task<bool>>? OnConfirmAction { get; set; }

    [RelayCommand]
    public void OpenCompaction() => OnOpenCompactionRequested?.Invoke();

    [RelayCommand]
    public void OpenSettings() => OnOpenSettingsRequested?.Invoke();

    [RelayCommand]
    public async Task PasteClipboardAsync()
    {
        if (!Services.ClipboardService.CanPaste()) return;

        IsOperationRunning = true;
        OperationTitle = "Pasting from Clipboard";
        OperationStatusText = "Ingesting data directly into vault memory...";

        try
        {
            var added = await Services.ClipboardService.PasteToVaultAsync(_vault, CurrentFolderPath);
            RefreshData();
            Services.NotificationService.Shared.ShowSuccess("Clipboard Ingested", $"Added {added.Count} item(s) from clipboard.");
        }
        catch (Exception ex)
        {
            Services.NotificationService.Shared.ShowError("Paste Failed", ex.Message);
        }
        finally
        {
            IsOperationRunning = false;
        }
    }

    public void ApplySort(SortField field)
    {
        if (CurrentSortField == field)
        {
            CurrentSortDirection = CurrentSortDirection == SortDirection.Ascending
                ? SortDirection.Descending
                : SortDirection.Ascending;
        }
        else
        {
            CurrentSortField = field;
            CurrentSortDirection = SortDirection.Ascending;
        }

        RefreshData();
    }

    public void LoadFiles() => RefreshData();

    public MainLibraryViewModel(VaultManager vault)
    {
        ArgumentNullException.ThrowIfNull(vault);
        _vault = vault;

        // Retrieve private index via internal service instances
        var dummyIndex = new VaultIndex();
        dummyIndex.Entries.AddRange(vault.Files);

        _folderService = new VirtualFolderService(dummyIndex);
        _tagService = new TagService(dummyIndex);
        _searchService = new SearchService(dummyIndex);
        _fileOps = new FileManagementOperations(vault, _folderService);
        _batchAdd = new BatchFileAddOperation(vault);

        // Restore UI State from encrypted cache if available
        try
        {
            var (_, _, cachedUI) = _vault.Cache.LoadSnapshot();
            if (cachedUI != null)
            {
                _currentSortField = (SortField)cachedUI.SortField;
                _currentSortDirection = (SortDirection)cachedUI.SortDirection;
                if (cachedUI.ViewMode >= 0 && cachedUI.ViewMode <= 2)
                {
                    ViewMode.CurrentMode = (VaultViewMode)cachedUI.ViewMode;
                }
                if (cachedUI.SelectedCategory.HasValue)
                {
                    _selectedCategory = (FileCategory)cachedUI.SelectedCategory.Value;
                }
                if (cachedUI.LastViewedFolderGuid.HasValue)
                {
                    _currentFolderGuid = cachedUI.LastViewedFolderGuid.Value;
                    var folder = dummyIndex.Entries.FirstOrDefault(e => e.FileGuid == _currentFolderGuid);
                    if (folder != null)
                    {
                        _currentFolderPath = folder.VirtualFolderPath;
                    }
                }
            }
        }
        catch { }

        RefreshData();
    }

    public void RefreshData()
    {
        var entries = _vault.Files;

        // Apply filters
        IEnumerable<IndexEntry> filtered = entries;

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            filtered = filtered.Where(e => e.FileName.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));
        }
        else if (IsFavoritesView)
        {
            filtered = filtered.Where(e => e.IsFavorite);
        }
        else if (SelectedCategory.HasValue)
        {
            byte catByte = (byte)SelectedCategory.Value;
            filtered = filtered.Where(e => e.Category == catByte);
        }
        else
        {
            // Folder view
            filtered = filtered.Where(e => e.ParentFolderGuid == CurrentFolderGuid);
        }

        // Apply sort
        var sorted = SortService.Sort(filtered, CurrentSortField, CurrentSortDirection, foldersFirst: true);

        Files.Clear();
        foreach (var entry in sorted)
        {
            Files.Add(new FileItemViewModel(entry));
        }

        // Refresh subfolders
        Folders.Clear();
        var subfolders = _folderService.GetSubfolders(CurrentFolderGuid);
        foreach (var folder in subfolders)
        {
            Folders.Add(folder);
        }

        // Refresh tags
        AllTags.Clear();
        foreach (var tag in _tagService.GetAllUniqueTags())
        {
            AllTags.Add(tag);
        }

        // Update statistics
        int totalFiles = entries.Count(e => !e.IsFolder);
        ulong totalSize = 0;
        foreach (var e in entries)
        {
            if (!e.IsFolder) totalSize += e.OriginalSize;
        }

        FileCountText = $"{Files.Count} items displayed ({totalFiles} total in vault)";
        VaultSizeText = $"Total Data: {FormatBytes(totalSize)}";
        CurrentFolderPath = _folderService.GetFullPath(CurrentFolderGuid);

        UpdateDiskSpace();
    }

    private void UpdateDiskSpace()
    {
        try
        {
            string root = Path.GetPathRoot(_vault.VaultPath) ?? "C:\\";
            var drive = new DriveInfo(root);
            DiskFreeSpaceText = $"Disk Free: {FormatBytes((ulong)drive.AvailableFreeSpace)}";
        }
        catch
        {
            DiskFreeSpaceText = "Ready";
        }
    }

    [RelayCommand]
    public void NavigateToFolder(Guid? folderGuid)
    {
        CurrentFolderGuid = folderGuid;
        SelectedCategory = null;
        IsFavoritesView = false;
        RefreshData();
    }

    [RelayCommand]
    public void NavigateToCategory(FileCategory? category)
    {
        SelectedCategory = category;
        IsFavoritesView = false;
        CurrentFolderGuid = null;
        RefreshData();
    }

    [RelayCommand]
    public void NavigateToFavorites()
    {
        IsFavoritesView = true;
        SelectedCategory = null;
        CurrentFolderGuid = null;
        RefreshData();
    }

    [RelayCommand]
    public void NavigateToAllFiles()
    {
        IsFavoritesView = false;
        SelectedCategory = null;
        CurrentFolderGuid = null;
        RefreshData();
    }

    [RelayCommand]
    public void SetSort(string fieldString)
    {
        if (Enum.TryParse<SortField>(fieldString, true, out var field))
        {
            if (CurrentSortField == field)
            {
                CurrentSortDirection = CurrentSortDirection == SortDirection.Ascending
                    ? SortDirection.Descending
                    : SortDirection.Ascending;
            }
            else
            {
                CurrentSortField = field;
                CurrentSortDirection = SortDirection.Ascending;
            }
            RefreshData();
        }
    }

    [RelayCommand]
    public void OpenFile(FileItemViewModel? item)
    {
        if (item != null)
        {
            OnOpenFileRequested?.Invoke(item);
        }
    }

    [RelayCommand]
    public void CreateNewNote()
    {
        OnCreateNewNoteRequested?.Invoke();
    }

    [RelayCommand]
    public void OpenFileManager()
    {
        OnOpenFileManagerRequested?.Invoke();
    }

    [RelayCommand]
    public async Task AddFilesAsync()
    {
        if (OnPickFilesToAdd == null) return;
        var paths = await OnPickFilesToAdd();
        if (paths == null || paths.Count == 0) return;

        await RunOperationAsync("Adding Files to Vault", async (progress, ct) =>
        {
            await _batchAdd.AddFilesAsync(paths, CurrentFolderPath, ProtectionMode.SecureMode, progress, ct);
        });

        RefreshData();
    }

    [RelayCommand]
    public async Task AddFolderAsync()
    {
        if (OnPickFolderToAdd == null) return;
        var folder = await OnPickFolderToAdd();
        if (string.IsNullOrWhiteSpace(folder)) return;

        await RunOperationAsync("Importing Folder to Vault", async (progress, ct) =>
        {
            await _batchAdd.AddFolderAsync(folder, CurrentFolderPath, ProtectionMode.SecureMode, true, progress, ct);
        });

        RefreshData();
    }

    [RelayCommand]
    public async Task ExportFileAsync(FileItemViewModel? item)
    {
        if (item == null || item.IsFolder || OnPickExportDestinationFile == null) return;

        string? dest = await OnPickExportDestinationFile(item.FileName);
        if (string.IsNullOrWhiteSpace(dest)) return;

        await RunOperationAsync($"Exporting {item.FileName}", async (progress, ct) =>
        {
            var p = new Progress<double>(fraction =>
            {
                OperationProgressPercentage = fraction * 100;
                OperationStatusText = $"{Math.Round(fraction * 100)}%";
            });
            await _fileOps.ExportFileAsync(item.FileGuid, dest, p, ct);
        });
    }

    [RelayCommand]
    public async Task DeleteFileAsync(FileItemViewModel? item)
    {
        if (item == null) return;

        if (OnConfirmAction != null)
        {
            bool confirm = await OnConfirmAction("Delete File", $"Are you sure you want to remove '{item.FileName}' from the vault?");
            if (!confirm) return;
        }

        if (item.IsFolder)
        {
            _folderService.DeleteFolder(item.FileGuid, deleteFiles: false);
        }
        else
        {
            _vault.DeleteFile(item.FileGuid);
        }

        RefreshData();
    }

    [RelayCommand]
    public async Task RenameFileAsync(FileItemViewModel? item)
    {
        if (item == null || OnPromptRename == null) return;

        string? newName = await OnPromptRename(item.FileName);
        if (string.IsNullOrWhiteSpace(newName) || newName == item.FileName) return;

        if (item.IsFolder)
        {
            _folderService.RenameFolder(item.FileGuid, newName);
        }
        else
        {
            _fileOps.Rename(item.FileGuid, newName);
        }

        RefreshData();
    }

    [RelayCommand]
    public async Task CopyFileAsync(FileItemViewModel? item)
    {
        if (item == null || item.IsFolder) return;

        await RunOperationAsync($"Duplicating {item.FileName}", async (_, ct) =>
        {
            await _fileOps.CopyAsync(item.FileGuid, item.Entry.ParentFolderGuid, null, ct);
        });

        RefreshData();
    }

    public async Task ReplaceFileContentAsync(FileItemViewModel? item, Stream newContent)
    {
        if (item == null || item.IsFolder) return;

        await RunOperationAsync($"Updating {item.FileName}", async (_, ct) =>
        {
            await _vault.AddFileAsync(newContent, item.FileName, item.Entry.VirtualFolderPath, item.ProtectionMode, cancellationToken: ct);
            _vault.DeleteFile(item.FileGuid);
        });

        RefreshData();
    }

    [RelayCommand]
    public void ToggleFavorite(FileItemViewModel? item)
    {
        if (item == null || item.IsFolder) return;

        item.IsFavorite = !item.IsFavorite;
        _tagService.SetFavorite(item.FileGuid, item.IsFavorite);
        RefreshData();
    }

    [RelayCommand]
    public void OpenBackupDialog()
    {
        OnOpenBackupRequested?.Invoke();
    }

    [RelayCommand]
    public void OpenVaultChainHealthDialog()
    {
        OnOpenVaultChainHealthRequested?.Invoke();
    }

    [RelayCommand]
    public void CancelOperation()
    {
        _operationCts?.Cancel();
    }

    public void PersistUIState()
    {
        try
        {
            var ui = new UIState
            {
                SortField = (int)CurrentSortField,
                SortDirection = (int)CurrentSortDirection,
                ViewMode = (int)ViewMode.CurrentMode,
                SelectedCategory = SelectedCategory.HasValue ? (byte)SelectedCategory.Value : null,
                LastViewedFolderGuid = CurrentFolderGuid
            };
            _vault.SaveCacheSnapshot(ui);
        }
        catch { }
    }

    [RelayCommand]
    public void LockVault()
    {
        PersistUIState();
        _vault.Lock();
        OnLockRequested?.Invoke();
    }

    private async Task RunOperationAsync(string title, Func<IProgress<FileAddProgress>, CancellationToken, Task> action)
    {
        IsOperationRunning = true;
        OperationTitle = title;
        OperationStatusText = "Preparing...";
        OperationProgressPercentage = 0;
        OperationSpeedText = string.Empty;
        OperationEtaText = string.Empty;

        _operationCts = new CancellationTokenSource();

        var progress = new Progress<FileAddProgress>(p =>
        {
            double pct = p.TotalBytes > 0 ? (double)p.BytesProcessed / p.TotalBytes * 100 : 0;
            OperationProgressPercentage = Math.Min(100, Math.Max(0, pct));
            OperationStatusText = $"{p.FileName} ({p.FileIndex}/{p.TotalFiles}) — {FormatBytes((ulong)p.BytesProcessed)} / {FormatBytes((ulong)p.TotalBytes)}";
            OperationSpeedText = p.SpeedBytesPerSec > 0 ? $"{FormatBytes((ulong)p.SpeedBytesPerSec)}/s" : "";
            OperationEtaText = p.EstimatedTimeRemaining > TimeSpan.Zero ? $"ETA: {p.EstimatedTimeRemaining:mm\\:ss}" : "";
        });

        try
        {
            await action(progress, _operationCts.Token);
        }
        catch (OperationCanceledException)
        {
            OperationStatusText = "Operation cancelled.";
        }
        catch (Exception ex)
        {
            OperationStatusText = $"Error: {ex.Message}";
        }
        finally
        {
            await Task.Delay(500); // brief visual completion settle
            IsOperationRunning = false;
            _operationCts.Dispose();
            _operationCts = null;
        }
    }

    private static string FormatBytes(ulong bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int i = 0;
        double d = bytes;
        while (d >= 1024 && i < suffixes.Length - 1)
        {
            d /= 1024;
            i++;
        }
        return $"{d:0.##} {suffixes[i]}";
    }
}
