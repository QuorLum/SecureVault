using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using SecureVault.App.ViewModels;
using SecureVault.Core;
using SecureVault.Core.Format;
using Windows.System;

namespace SecureVault.App.Views;

public sealed partial class PdfViewerPage : Page
{
    public PdfViewerViewModel? ViewModel { get; private set; }

    public PdfViewerPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is (VaultManager vault, IndexEntry entry))
        {
            ViewModel = new PdfViewerViewModel(vault, entry);
            DataContext = ViewModel;
            Bindings.Update();

            ViewModel.OnCloseRequested = () =>
            {
                if (Frame.CanGoBack) Frame.GoBack();
            };
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel?.Dispose();
        ViewModel = null;
    }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        base.OnKeyDown(e);
        if (ViewModel == null) return;

        switch (e.Key)
        {
            case VirtualKey.Left:
            case VirtualKey.PageUp:
                ViewModel.PreviousPageCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.Right:
            case VirtualKey.PageDown:
                ViewModel.NextPageCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.Escape:
                ViewModel.CloseCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }
}
