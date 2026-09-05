using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SecureVault.App.ViewModels;

namespace SecureVault.App.Views;

public sealed class TimelineGroup
{
    public string Header { get; set; } = string.Empty;
    public List<FileItemViewModel> Items { get; set; } = new();
    public string ItemCountText => $"({Items.Count})";
}

public sealed partial class TimelineView : UserControl
{
    public MainLibraryViewModel? ViewModel => DataContext as MainLibraryViewModel;
    public ObservableCollection<TimelineGroup> TimelineGroups { get; } = new();

    public TimelineView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        Bindings.Update();
        RefreshTimeline();
        if (ViewModel != null)
        {
            ViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MainLibraryViewModel.Files))
                {
                    HookCollectionChanged();
                    RefreshTimeline();
                }
            };
            HookCollectionChanged();
        }
    }

    private static FileItemViewModel? ExtractItem(object sender)
    {
        if (sender is FrameworkElement fe)
        {
            if (fe.Tag is FileItemViewModel tagItem) return tagItem;
            if (fe.DataContext is FileItemViewModel dcItem) return dcItem;
        }
        return null;
    }

    private void HookCollectionChanged()
    {
        if (ViewModel?.Files != null)
        {
            ViewModel.Files.CollectionChanged -= OnFilesCollectionChanged;
            ViewModel.Files.CollectionChanged += OnFilesCollectionChanged;
        }
    }

    private void OnFilesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        RefreshTimeline();
    }

    private void RefreshTimeline()
    {
        TimelineGroups.Clear();
        if (ViewModel?.Files == null || ViewModel.Files.Count == 0) return;

        var groups = ViewModel.Files
            .Where(f => !f.IsFolder)
            .GroupBy(f =>
            {
                var dt = new DateTime(f.Entry.DateModifiedTicks, DateTimeKind.Utc).ToLocalTime();
                return dt.ToString("MMMM yyyy");
            })
            .Select(g => new TimelineGroup
            {
                Header = g.Key,
                Items = g.OrderByDescending(f => f.Entry.DateModifiedTicks).ToList()
            });

        foreach (var group in groups)
        {
            TimelineGroups.Add(group);
        }
    }

    private void OnItemDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var item = ExtractItem(sender);
        if (item != null)
        {
            ViewModel?.OpenFile(item);
        }
    }

    private void OnOpenClicked(object sender, RoutedEventArgs e)
    {
        var item = ExtractItem(sender);
        if (item != null)
        {
            ViewModel?.OpenFile(item);
        }
    }

    private async void OnPropertiesClicked(object sender, RoutedEventArgs e)
    {
        var item = ExtractItem(sender);
        if (item != null)
        {
            try
            {
                var propVm = new FilePropertiesViewModel(item.Entry);
                var dialog = new FilePropertiesDialog(propVm)
                {
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing properties: {ex.Message}");
            }
        }
    }

    private void OnExportClicked(object sender, RoutedEventArgs e)
    {
        var item = ExtractItem(sender);
        if (item != null)
        {
            ViewModel?.ExportFileCommand.Execute(item);
        }
    }

    private void OnRenameClicked(object sender, RoutedEventArgs e)
    {
        var item = ExtractItem(sender);
        if (item != null)
        {
            ViewModel?.RenameFileCommand.Execute(item);
        }
    }

    private void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        var item = ExtractItem(sender);
        if (item != null)
        {
            ViewModel?.DeleteFileCommand.Execute(item);
        }
    }
}
