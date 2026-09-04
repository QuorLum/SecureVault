using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SecureVault.App.Services;
using SecureVault.App.ViewModels;
using SecureVault.Core;
using SecureVault.Core.Format;
using Windows.Storage.Pickers;

namespace SecureVault.App.Views;

public sealed partial class MainLibraryPage : Page
{
    public MainLibraryViewModel? ViewModel { get; private set; }
    private IdleLockService? _idleLockService;
    private SystemLockDetector? _systemLockDetector;

    public MainLibraryPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is VaultManager vault)
        {
            ViewModel = new MainLibraryViewModel(vault);
            DataContext = ViewModel;

            ViewModel.OnLockRequested = () =>
            {
                Frame.Navigate(typeof(LoginPage));
            };

            ViewModel.OnOpenFileRequested = item =>
            {
                if (item.Category == Core.Organization.FileCategory.Photos)
                {
                    var photos = ViewModel.Files
                        .Where(f => f.Category == Core.Organization.FileCategory.Photos)
                        .Select(f => f.Entry)
                        .ToList();
                    int idx = photos.FindIndex(p => p.FileGuid == item.FileGuid);
                    Frame.Navigate(typeof(PhotoViewerPage), (vault, photos, Math.Max(0, idx)));
                }
                else if (item.Category == Core.Organization.FileCategory.Videos || item.Category == Core.Organization.FileCategory.Audio)
                {
                    Frame.Navigate(typeof(MediaPlayerPage), (vault, item.Entry));
                }
                else if (item.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    Frame.Navigate(typeof(PdfViewerPage), (vault, item.Entry));
                }
                else if (item.Category == Core.Organization.FileCategory.TextNotes || item.FileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || item.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    Frame.Navigate(typeof(NotesEditorPage), (vault, item.Entry));
                }
            };

            ViewModel.OnCreateNewNoteRequested = () =>
            {
                Frame.Navigate(typeof(NotesEditorPage), (vault, (IndexEntry?)null));
            };

            ViewModel.OnOpenFileManagerRequested = () =>
            {
                Frame.Navigate(typeof(FileManagerPage), vault);
            };

            ViewModel.OnOpenBackupRequested = async () =>
            {
                var dialog = new BackupRestoreDialog(vault.VaultPath)
                {
                    XamlRoot = XamlRoot
                };
                await dialog.ShowAsync();
            };

            ViewModel.OnOpenVaultChainHealthRequested = async () =>
            {
                var chain = new Core.MultiVault.VaultChainManager(vault);
                var dialog = new VaultChainHealthDialog(chain)
                {
                    XamlRoot = XamlRoot
                };
                await dialog.ShowAsync();
            };

            ViewModel.OnPickFilesToAdd = PickFilesToAddAsync;
            ViewModel.OnPickFolderToAdd = PickFolderToAddAsync;
            ViewModel.OnPickExportDestinationFile = PickExportDestinationFileAsync;
            ViewModel.OnPromptRename = PromptRenameAsync;
            ViewModel.OnConfirmAction = ConfirmActionAsync;

            // Auto-lock on 5 minutes idle or Windows workstation lock (A08, M08)
            _idleLockService?.Dispose();
            _idleLockService = new IdleLockService(TimeSpan.FromMinutes(5), () =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    vault.Lock();
                    Frame.Navigate(typeof(LoginPage));
                });
            });
            _idleLockService.Start();

            _systemLockDetector?.Dispose();
            _systemLockDetector = new SystemLockDetector(() =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    vault.Lock();
                    Frame.Navigate(typeof(LoginPage));
                });
            });
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _idleLockService?.Dispose();
        _idleLockService = null;
        _systemLockDetector?.Dispose();
        _systemLockDetector = null;
    }

    private async Task<IReadOnlyList<string>> PickFilesToAddAsync()
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add("*");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var files = await picker.PickMultipleFilesAsync();
        return files?.Select(f => f.Path).ToList() ?? (IReadOnlyList<string>)Array.Empty<string>();
    }

    private async Task<string?> PickFolderToAddAsync()
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    private async Task<string?> PickExportDestinationFileAsync(string defaultFileName)
    {
        var picker = new FileSavePicker();
        picker.SuggestedFileName = defaultFileName;
        string ext = System.IO.Path.GetExtension(defaultFileName);
        picker.FileTypeChoices.Add("File", new List<string> { string.IsNullOrEmpty(ext) ? "." : ext });

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    private async Task<string?> PromptRenameAsync(string currentName)
    {
        var textBox = new TextBox { Text = currentName, HorizontalAlignment = HorizontalAlignment.Stretch };
        var dialog = new ContentDialog
        {
            Title = "Rename File",
            PrimaryButtonText = "Rename",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.XamlRoot,
            Content = textBox
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? textBox.Text.Trim() : null;
    }

    private async Task<bool> ConfirmActionAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = "Confirm",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }
}
