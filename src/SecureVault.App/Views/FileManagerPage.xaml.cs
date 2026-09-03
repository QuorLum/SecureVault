using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SecureVault.App.ViewModels;
using SecureVault.Core;

namespace SecureVault.App.Views;

public sealed partial class FileManagerPage : Page
{
    public FileManagerViewModel? ViewModel { get; private set; }

    public FileManagerPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is VaultManager vault)
        {
            ViewModel = new FileManagerViewModel(vault);
            DataContext = ViewModel;

            ViewModel.OnCloseRequested = () =>
            {
                if (Frame.CanGoBack) Frame.GoBack();
            };
        }
    }
}
