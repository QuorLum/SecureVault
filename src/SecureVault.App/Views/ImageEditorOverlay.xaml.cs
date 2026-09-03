using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SecureVault.App.ViewModels;
using SecureVault.Core;
using SecureVault.Core.Format;

namespace SecureVault.App.Views;

public sealed partial class ImageEditorOverlay : Page
{
    public ImageEditorViewModel? ViewModel { get; private set; }

    public ImageEditorOverlay()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is (VaultManager vault, IndexEntry entry))
        {
            ViewModel = new ImageEditorViewModel(vault, entry);
            DataContext = ViewModel;

            ViewModel.OnCloseRequested = () =>
            {
                if (Frame.CanGoBack) Frame.GoBack();
            };

            ViewModel.OnSavedSuccessfully = () =>
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
}
