using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SecureVault.App.ViewModels;

public enum VaultViewMode
{
    Grid = 0,
    DetailedList = 1,
    Timeline = 2
}

/// <summary>
/// ViewModel managing library presentation mode: Grid, Detailed List, and Timeline (D21, N10, N11).
/// </summary>
public partial class ViewModeViewModel : ObservableObject
{
    [ObservableProperty]
    private VaultViewMode _currentMode = VaultViewMode.Grid;

    public bool IsGrid => CurrentMode == VaultViewMode.Grid;
    public bool IsDetailedList => CurrentMode == VaultViewMode.DetailedList;
    public bool IsTimeline => CurrentMode == VaultViewMode.Timeline;

    public event EventHandler<VaultViewMode>? ViewModeChanged;

    partial void OnCurrentModeChanged(VaultViewMode value)
    {
        OnPropertyChanged(nameof(IsGrid));
        OnPropertyChanged(nameof(IsDetailedList));
        OnPropertyChanged(nameof(IsTimeline));
        ViewModeChanged?.Invoke(this, value);
    }

    [RelayCommand]
    public void SetGridMode() => CurrentMode = VaultViewMode.Grid;

    [RelayCommand]
    public void SetDetailedListMode() => CurrentMode = VaultViewMode.DetailedList;

    [RelayCommand]
    public void SetTimelineMode() => CurrentMode = VaultViewMode.Timeline;
}
