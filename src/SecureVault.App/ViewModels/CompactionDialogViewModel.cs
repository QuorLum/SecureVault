using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureVault.Core;
using SecureVault.Core.MultiVault;
using SecureVault.Core.Operations;

namespace SecureVault.App.ViewModels;

public partial class CompactionDialogViewModel : ObservableObject
{
    private readonly VaultManager _vault;
    private readonly VaultChainManager? _chain;

    [ObservableProperty]
    private string _currentSizeBytes = string.Empty;

    [ObservableProperty]
    private string _estimatedReclaimableBytes = string.Empty;

    [ObservableProperty]
    private string _duplicateWastedBytes = string.Empty;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private bool _isCompacting;

    [ObservableProperty]
    private bool _isCompleted;

    [ObservableProperty]
    private string _resultSummary = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public CompactionDialogViewModel(VaultManager vault, VaultChainManager? chain = null)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _chain = chain;

        AnalyzeStorage();
    }

    private void AnalyzeStorage()
    {
        long currentSize = new FileInfo(_vault.VaultPath).Length;
        CurrentSizeBytes = FormatBytes((ulong)currentSize);

        // Find duplicates
        var dups = DuplicateDetector.FindDuplicates(_vault.Files);
        ulong duplicateWasted = (ulong)dups.Sum(d => (long)d.WastedStorageBytes);
        DuplicateWastedBytes = FormatBytes(duplicateWasted);

        // Estimate deleted/unreferenced space
        var liveFiles = _vault.Files;
        ulong liveContentSize = (ulong)liveFiles.Sum(f => (long)f.OriginalSize);
        long estimatedOrphaned = Math.Max(0, currentSize - (long)liveContentSize - 16384);
        EstimatedReclaimableBytes = FormatBytes((ulong)estimatedOrphaned);
    }

    [RelayCommand]
    public async Task StartCompactionAsync()
    {
        if (IsCompacting) return;

        IsCompacting = true;
        ErrorMessage = string.Empty;
        ProgressValue = 0;

        var progress = new Progress<double>(p => ProgressValue = p * 100);

        try
        {
            CompactionResult result;
            if (_chain != null)
            {
                result = await VaultCompaction.CompactChainAsync(_chain, progress: progress);
            }
            else
            {
                result = await VaultCompaction.CompactAsync(_vault, progress: progress);
            }

            IsCompleted = true;
            ResultSummary = $"Compaction complete! Successfully reclaimed {FormatBytes((ulong)result.ReclaimedBytes)}. New vault size: {FormatBytes((ulong)result.NewSizeBytes)}.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Compaction failed: {ex.Message}";
        }
        finally
        {
            IsCompacting = false;
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
