using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureVault.Core;
using SecureVault.Core.Archives;
using SecureVault.Core.Format;
using SecureVault.Core.Operations;
using SecureVault.Core.Organization;

namespace SecureVault.App.ViewModels;

public record DuplicateGroup(string Hash, IReadOnlyList<IndexEntry> Files);
public record CategoryStat(FileCategory Category, int Count, string FormattedSize);

public partial class FileManagerViewModel : ObservableObject
{
    private readonly VaultManager _vault;
    private readonly VirtualFolderService _folderService;
    private readonly ProtectionModeOperation _protectionOp;

    [ObservableProperty]
    private string _currentPath = "/";

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private string _totalFolderSizeText = "0 B";

    [ObservableProperty]
    private bool _isBusy;

    public ObservableCollection<VirtualFolder> Folders { get; } = new();
    public ObservableCollection<FileItemViewModel> CurrentFiles { get; } = new();
    public ObservableCollection<DuplicateGroup> DuplicateGroups { get; } = new();
    public ObservableCollection<CategoryStat> CategoryStatistics { get; } = new();

    public Action? OnCloseRequested { get; set; }

    public FileManagerViewModel(VaultManager vault)
    {
        ArgumentNullException.ThrowIfNull(vault);
        _vault = vault;

        var dummyIndex = new VaultIndex();
        dummyIndex.Entries.AddRange(vault.Files);
        _folderService = new VirtualFolderService(dummyIndex);
        _protectionOp = new ProtectionModeOperation(vault);

        RefreshData();
    }

    public void RefreshData()
    {
        // Load folders
        Folders.Clear();
        foreach (var folder in _folderService.GetSubfolders(null))
        {
            Folders.Add(folder);
        }

        // Load files in current folder
        CurrentFiles.Clear();
        var filesInFolder = _vault.Files
            .Where(f => !f.IsFolder && string.Equals(f.VirtualFolderPath, CurrentPath, StringComparison.OrdinalIgnoreCase));

        foreach (var file in filesInFolder)
        {
            CurrentFiles.Add(new FileItemViewModel(file));
        }

        // Calculate folder size (K07)
        long totalFolderBytes = _vault.Files
            .Where(f => !f.IsFolder && f.VirtualFolderPath.StartsWith(CurrentPath, StringComparison.OrdinalIgnoreCase))
            .Sum(f => (long)f.OriginalSize);
        TotalFolderSizeText = FormatBytes((ulong)totalFolderBytes);

        // Compute duplicates (K08)
        DuplicateGroups.Clear();
        var dupes = _vault.Files
            .Where(f => !f.IsFolder && f.PlaintextSHA256 != null && f.PlaintextSHA256.Length == 32)
            .GroupBy(f => Convert.ToHexString(f.PlaintextSHA256))
            .Where(g => g.Count() > 1);

        foreach (var group in dupes)
        {
            DuplicateGroups.Add(new DuplicateGroup(group.Key, group.ToList()));
        }

        // Compute statistics (K12)
        CategoryStatistics.Clear();
        var stats = _vault.Files
            .Where(f => !f.IsFolder)
            .GroupBy(f => (FileCategory)f.Category)
            .Select(g => new CategoryStat(g.Key, g.Count(), FormatBytes((ulong)g.Sum(x => (long)x.OriginalSize))))
            .OrderByDescending(s => s.Count);

        foreach (var stat in stats)
        {
            CategoryStatistics.Add(stat);
        }
    }

    [RelayCommand]
    public void NavigateToFolder(string path)
    {
        CurrentPath = path;
        RefreshData();
    }

    [RelayCommand]
    public async Task ToggleProtectionModeAsync(FileItemViewModel? item)
    {
        if (item == null) return;
        var targetMode = item.ProtectionMode == ProtectionMode.SecureMode
            ? ProtectionMode.FastObfuscation
            : ProtectionMode.SecureMode;

        IsBusy = true;
        StatusMessage = $"Converting '{item.FileName}' to {targetMode}...";

        try
        {
            await _protectionOp.ChangeProtectionModeAsync(item.FileGuid, targetMode);
            StatusMessage = $"'{item.FileName}' converted to {targetMode}.";
            RefreshData();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Mode change error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task EncryptEverythingAsync()
    {
        IsBusy = true;
        StatusMessage = "Encrypting all files with AES-256-GCM...";

        try
        {
            int converted = await _protectionOp.EncryptAllAsync();
            StatusMessage = $"Finished: {converted} files converted to Secure Mode.";
            RefreshData();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Encryption error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task ExtractArchiveAsync(FileItemViewModel? archiveItem)
    {
        if (archiveItem == null) return;

        IsBusy = true;
        StatusMessage = $"Reading archive '{archiveItem.FileName}' in memory...";

        try
        {
            using var stream = _vault.OpenFileStream(archiveItem.Entry);
            using var reader = new ArchiveReader(stream);

            var extracted = reader.ExtractAll();
            StatusMessage = $"Unpacking {extracted.Count} files directly into vault...";

            foreach (var (relativePath, data) in extracted)
            {
                using var fileStream = new MemoryStream(data);
                string fileName = Path.GetFileName(relativePath);
                string virtualFolder = Path.Combine(CurrentPath, Path.GetDirectoryName(relativePath) ?? string.Empty).Replace('\\', '/');

                await _vault.AddFileAsync(fileStream, fileName, virtualFolder, ProtectionMode.SecureMode);
            }

            StatusMessage = $"Extracted {extracted.Count} files into vault successfully.";
            RefreshData();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Archive extraction error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void Close()
    {
        OnCloseRequested?.Invoke();
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
