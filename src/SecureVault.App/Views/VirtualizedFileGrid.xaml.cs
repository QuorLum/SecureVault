using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SecureVault.App.ViewModels;

namespace SecureVault.App.Views;

public sealed partial class VirtualizedFileGrid : UserControl
{
    public MainLibraryViewModel? ViewModel => DataContext as MainLibraryViewModel;

    public VirtualizedFileGrid()
    {
        InitializeComponent();

        FileRepeater.ElementPrepared += (sender, args) =>
        {
            if (args.Element is FrameworkElement fe && ViewModel?.Files != null && args.Index >= 0 && args.Index < ViewModel.Files.Count)
            {
                var item = ViewModel.Files[args.Index];
                fe.DataContext = item;
                fe.Tag = item;
            }
        };
    }

    private static FileItemViewModel? ExtractItem(object sender)
    {
        if (sender is FrameworkElement fe)
        {
            if (fe.DataContext is FileItemViewModel vm) return vm;
            if (fe.Tag is FileItemViewModel tagVm) return tagVm;
        }
        if (sender is MenuFlyoutItem mfi)
        {
            if (mfi.DataContext is FileItemViewModel vm) return vm;
            if (mfi.Tag is FileItemViewModel tagVm) return tagVm;
        }
        return null;
    }

    private void OnItemPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border)
        {
            border.BorderBrush = (SolidColorBrush)Application.Current.Resources["AppBorderHoverBrush"];
            border.Background = (SolidColorBrush)Application.Current.Resources["AppCardHoverBrush"];
        }
    }

    private void OnItemPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border)
        {
            border.BorderBrush = (SolidColorBrush)Application.Current.Resources["AppBorderBrush"];
            border.Background = (SolidColorBrush)Application.Current.Resources["AppCardBrush"];
        }
    }

    private void OnItemDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var item = ExtractItem(sender);
        if (item != null)
        {
            if (item.IsFolder)
            {
                ViewModel?.NavigateToFolder(item.FileGuid);
            }
            else
            {
                ViewModel?.OpenFile(item);
            }
        }
    }

    private void OnOpenClicked(object sender, RoutedEventArgs e)
    {
        var item = ExtractItem(sender);
        if (item != null)
        {
            if (item.IsFolder)
            {
                ViewModel?.NavigateToFolder(item.FileGuid);
            }
            else
            {
                ViewModel?.OpenFile(item);
            }
        }
    }

    private void OnItemRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        // Context flyout opens automatically on right tap
    }

    private void OnExportClicked(object sender, RoutedEventArgs e)
    {
        var item = ExtractItem(sender);
        if (item != null)
        {
            ViewModel?.ExportFileCommand.Execute(item);
        }
    }

    private void OnCopyClicked(object sender, RoutedEventArgs e)
    {
        var item = ExtractItem(sender);
        if (item != null)
        {
            ViewModel?.CopyFileCommand.Execute(item);
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

    private void OnToggleFavoriteClicked(object sender, RoutedEventArgs e)
    {
        var item = ExtractItem(sender);
        if (item != null)
        {
            ViewModel?.ToggleFavorite(item);
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
