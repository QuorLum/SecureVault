using Microsoft.UI.Xaml.Controls;
using SecureVault.App.ViewModels;

namespace SecureVault.App.Views;

public sealed partial class StatusBarControl : UserControl
{
    public MainLibraryViewModel? ViewModel => DataContext as MainLibraryViewModel;

    public StatusBarControl()
    {
        InitializeComponent();
    }
}
