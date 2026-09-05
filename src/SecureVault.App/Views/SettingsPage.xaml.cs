using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SecureVault.App.ViewModels;
using SecureVault.Core;

namespace SecureVault.App.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel? ViewModel { get; private set; }

    public SettingsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is (VaultManager vault, IntPtr hwnd))
        {
            ViewModel = new SettingsViewModel(vault, hwnd);
        }
        else if (e.Parameter is VaultManager vm)
        {
            ViewModel = new SettingsViewModel(vm, IntPtr.Zero);
        }
        else
        {
            ViewModel = new SettingsViewModel(null, IntPtr.Zero);
        }

        DataContext = ViewModel;
        Bindings.Update();
    }

    private void OnBackClicked(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (Frame.CanGoBack) Frame.GoBack();
    }
}
