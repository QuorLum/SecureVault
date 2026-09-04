using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SecureVault.Core.MultiVault;

namespace SecureVault.App.ViewModels;

public partial class VaultChainHealthViewModel : ObservableObject
{
    private readonly VaultChainManager _chainManager;

    [ObservableProperty]
    private bool _isChainHealthy = true;

    [ObservableProperty]
    private int _totalPartsCount;

    [ObservableProperty]
    private int _presentPartsCount;

    [ObservableProperty]
    private int _missingPartsCount;

    [ObservableProperty]
    private string _totalSizeFormatted = "0 B";

    [ObservableProperty]
    private string _missingPartsWarning = string.Empty;

    [ObservableProperty]
    private bool _hasMissingParts;

    [ObservableProperty]
    private string _maxPartSizeFormatted = "200 GB";

    public ObservableCollection<VaultPartHealthStatus> PartStatuses { get; } = new();

    public VaultChainHealthViewModel(VaultChainManager chainManager)
    {
        _chainManager = chainManager ?? throw new ArgumentNullException(nameof(chainManager));
        RefreshHealth();
    }

    [RelayCommand]
    public void RefreshHealth()
    {
        var report = VaultChainHealth.CheckHealth(_chainManager);
        IsChainHealthy = report.IsHealthy;
        TotalPartsCount = report.TotalParts;
        PresentPartsCount = report.PresentParts;
        MissingPartsCount = report.MissingPartsCount;
        TotalSizeFormatted = FormatBytes((ulong)report.TotalSizeBytes);
        MaxPartSizeFormatted = FormatBytes((ulong)_chainManager.MaxPartSizeBytes);

        HasMissingParts = report.MissingPartsCount > 0;
        MissingPartsWarning = HasMissingParts
            ? $"Warning: {report.MissingPartsCount} secondary vault part(s) are missing from disk ({string.Join(", ", report.MissingPartFileNames)}). Reconnect drive to restore access to unavailable files."
            : string.Empty;

        PartStatuses.Clear();
        foreach (var status in report.PartStatuses)
        {
            PartStatuses.Add(status);
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
