using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage.Pickers;
using SecureVault.App.ViewModels;
using SecureVault.Core.Operations;
using SecureVault.Core.Organization;

namespace SecureVault.App.Views;

public sealed partial class FileListView : UserControl
{
    public MainLibraryViewModel? ViewModel => DataContext as MainLibraryViewModel;

    public FileListView()
    {
        InitializeComponent();
    }

    private void OnItemDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (FileListViewControl.SelectedItem is FileItemViewModel item)
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
    }

    private void OnOpenClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is FileItemViewModel item)
        {
            if (item.IsFolder)
                ViewModel?.NavigateToFolder(item.FileGuid);
            else
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

    private async void OnReplaceClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is FileItemViewModel item && ViewModel?.Vault != null)
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add("*");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                using var stream = await file.OpenStreamForReadAsync();
                var replacer = new FileReplaceOperation(ViewModel.Vault);
                await replacer.ReplaceFileDataAsync(item.FileGuid, stream);

                // Refresh files
                ViewModel.LoadFiles();
            }
        }
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

    private void OnSortByNameClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.ApplySort(SortField.Name);
    }

    private void OnSortByCategoryClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.ApplySort(SortField.Type);
    }

    private void OnSortBySizeClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.ApplySort(SortField.Size);
    }

    private void OnSortByDateClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.ApplySort(SortField.DateModified);
    }
}
