using Microsoft.UI.Xaml.Controls;
using SecureVault.App.ViewModels;

namespace SecureVault.App.Views;

public sealed partial class CompactionDialog : ContentDialog
{
    public CompactionDialogViewModel ViewModel { get; }

    public CompactionDialog(CompactionDialogViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
    }
}
