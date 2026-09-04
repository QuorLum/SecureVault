using Microsoft.UI.Xaml.Controls;
using SecureVault.App.ViewModels;

namespace SecureVault.App.Views;

public sealed partial class FilePropertiesDialog : ContentDialog
{
    public FilePropertiesViewModel ViewModel { get; }

    public FilePropertiesDialog(FilePropertiesViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
    }
}
