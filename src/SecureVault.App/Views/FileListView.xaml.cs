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

    private FileItemViewModel? ExtractItem(object sender)
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
        return FileListViewControl.SelectedItem as FileItemViewModel;
    }

    private void OnItemDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var item = ExtractItem(e.OriginalSource) ?? ExtractItem(sender);
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
    }

    private void OnOpenClicked(object sender, RoutedEventArgs e)
    {
        var item = ExtractItem(sender);
        if (item != null)
        {
            if (item.IsFolder)
                ViewModel?.NavigateToFolder(item.FileGuid);
            else
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
                System.Diagnostics.Debug.WriteLine($"Properties dialog error: {ex.Message}");
            }
        }
    }

    private async void OnReplaceClicked(object sender, RoutedEventArgs e)
    {
        var item = ExtractItem(sender);
        if (item != null && ViewModel != null)
        {
            try
            {
                var picker = new FileOpenPicker();
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
                picker.FileTypeFilter.Add("*");

                var file = await picker.PickSingleFileAsync();
                if (file != null)
                {
                    using var stream = await file.OpenStreamForReadAsync();
                    await ViewModel.ReplaceFileContentAsync(item, stream);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"File replacement error: {ex.Message}");
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
