using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SecureVault.App.ViewModels;
using SecureVault.Core.Organization;

namespace SecureVault.App.Views;

public sealed partial class SidebarControl : UserControl
{
    private MainLibraryViewModel? ViewModel => DataContext as MainLibraryViewModel;

    public SidebarControl()
    {
        InitializeComponent();
    }

    private void OnAllFilesClicked(object sender, RoutedEventArgs e) => ViewModel?.NavigateToAllFiles();
    private void OnFavoritesClicked(object sender, RoutedEventArgs e) => ViewModel?.NavigateToFavorites();

    private void OnCategoryPhotosClicked(object sender, RoutedEventArgs e) => ViewModel?.NavigateToCategory(FileCategory.Photos);
    private void OnCategoryVideosClicked(object sender, RoutedEventArgs e) => ViewModel?.NavigateToCategory(FileCategory.Videos);
    private void OnCategoryAudioClicked(object sender, RoutedEventArgs e) => ViewModel?.NavigateToCategory(FileCategory.Audio);
    private void OnCategoryDocumentsClicked(object sender, RoutedEventArgs e) => ViewModel?.NavigateToCategory(FileCategory.Documents);
    private void OnCategoryNotesClicked(object sender, RoutedEventArgs e) => ViewModel?.NavigateToCategory(FileCategory.TextNotes);
    private void OnCategoryAppsClicked(object sender, RoutedEventArgs e) => ViewModel?.NavigateToCategory(FileCategory.Applications);
    private void OnCategoryArchivesClicked(object sender, RoutedEventArgs e) => ViewModel?.NavigateToCategory(FileCategory.Archives);
}
