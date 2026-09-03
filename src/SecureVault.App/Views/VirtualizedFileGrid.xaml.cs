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
        if (sender is FrameworkElement fe && fe.DataContext is FileItemViewModel item)
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
        if (sender is FrameworkElement fe && fe.DataContext is FileItemViewModel item)
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
        if (sender is FrameworkElement fe && fe.DataContext is FileItemViewModel item)
        {
            ViewModel?.ExportFileCommand.Execute(item);
        }
    }

    private void OnCopyClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is FileItemViewModel item)
        {
            ViewModel?.CopyFileCommand.Execute(item);
        }
    }

    private void OnRenameClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is FileItemViewModel item)
        {
            ViewModel?.RenameFileCommand.Execute(item);
        }
    }

    private void OnToggleFavoriteClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is FileItemViewModel item)
        {
            ViewModel?.ToggleFavorite(item);
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
