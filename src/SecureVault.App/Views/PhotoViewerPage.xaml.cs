using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using SecureVault.App.ViewModels;
using SecureVault.Core;
using SecureVault.Core.Format;
using Windows.System;

namespace SecureVault.App.Views;

public sealed partial class PhotoViewerPage : Page
{
    public PhotoViewerViewModel? ViewModel { get; private set; }

    public PhotoViewerPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is (VaultManager vault, List<IndexEntry> entries, int index))
        {
            ViewModel = new PhotoViewerViewModel(vault, entries, index);
            DataContext = ViewModel;

            ViewModel.OnCloseRequested = () =>
            {
                if (Frame.CanGoBack) Frame.GoBack();
            };
        }
    }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        base.OnKeyDown(e);

        if (ViewModel == null) return;

        switch (e.Key)
        {
            case VirtualKey.Left:
                ViewModel.PreviousPhotoCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.Right:
                ViewModel.NextPhotoCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.Escape:
                ViewModel.CloseCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.R:
                ViewModel.RotateClockwiseCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }
}
