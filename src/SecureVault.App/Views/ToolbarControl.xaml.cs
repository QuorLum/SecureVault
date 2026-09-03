using Microsoft.UI.Xaml.Controls;
using SecureVault.App.ViewModels;

namespace SecureVault.App.Views;

public sealed partial class ToolbarControl : UserControl
{
    public MainLibraryViewModel? ViewModel => DataContext as MainLibraryViewModel;

    public ToolbarControl()
    {
        InitializeComponent();
    }

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        ViewModel?.RefreshData();
    }

    private void OnSortSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SortCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            ViewModel?.SetSort(tag);
        }
    }
}
