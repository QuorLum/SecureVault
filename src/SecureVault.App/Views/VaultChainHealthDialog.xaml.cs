using Microsoft.UI.Xaml.Controls;
using SecureVault.App.ViewModels;
using SecureVault.Core.MultiVault;

namespace SecureVault.App.Views;

public sealed partial class VaultChainHealthDialog : ContentDialog
{
    public VaultChainHealthViewModel ViewModel { get; }

    public VaultChainHealthDialog(VaultChainManager chainManager)
    {
        ViewModel = new VaultChainHealthViewModel(chainManager);
        InitializeComponent();
    }
}
