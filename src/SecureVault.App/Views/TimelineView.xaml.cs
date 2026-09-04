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
        RefreshTimeline();
        if (ViewModel != null)
        {
            ViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MainLibraryViewModel.Files))
                {
                    RefreshTimeline();
                }
            };
        }
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
        if (sender is FrameworkElement fe && fe.DataContext is FileItemViewModel item)
        {
            ViewModel?.OpenFile(item);
        }
    }

    private void OnOpenClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is FileItemViewModel item)
        {
            ViewModel?.OpenFile(item);
        }
    }

    private async void OnPropertiesClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is FileItemViewModel item)
        {
            var propVm = new FilePropertiesViewModel(item.Entry);
            var dialog = new FilePropertiesDialog(propVm)
            {
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowAsync();
        }
    }

    private void OnExportClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is FileItemViewModel item)
        {
            ViewModel?.ExportFileCommand.Execute(item);
        }
    }

    private void OnRenameClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is FileItemViewModel item)
        {
            ViewModel?.RenameFileCommand.Execute(item);
        }
    }

    private void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is FileItemViewModel item)
        {
            ViewModel?.DeleteFileCommand.Execute(item);
        }
    }
}
