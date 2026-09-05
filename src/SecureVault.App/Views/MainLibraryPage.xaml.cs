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

            ViewModel.OnOpenFileRequested = async item =>
            {
                var ext = System.IO.Path.GetExtension(item.FileName).ToLowerInvariant();
                var cat = (Core.Organization.FileCategory)item.Category;

                // 1. PDF Documents
                if (ext == ".pdf")
                {
                    Frame.Navigate(typeof(PdfViewerPage), (vault, item.Entry));
                }
                // 2. Videos and Audio
                else if (cat == Core.Organization.FileCategory.Videos || 
                         cat == Core.Organization.FileCategory.Audio ||
                         ext is ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" or ".flv" or ".wmv" or ".m4v" or ".3gp" or ".ts" or
                                ".mp3" or ".flac" or ".wav" or ".aac" or ".ogg" or ".wma" or ".opus" or ".m4a" or ".aiff")
                {
                    Frame.Navigate(typeof(MediaPlayerPage), (vault, item.Entry));
                }
                // 3. Text Notes, Markdown, Code and Plaintext
                else if (cat == Core.Organization.FileCategory.TextNotes ||
                         ext is ".txt" or ".md" or ".json" or ".xml" or ".csv" or ".log" or ".ini" or ".yaml" or ".yml" or
                                ".cs" or ".js" or ".ts" or ".html" or ".css" or ".py" or ".sql" or ".sh" or ".bat" or ".config")
                {
                    Frame.Navigate(typeof(NotesEditorPage), (vault, item.Entry));
                }
                // 4. Photos and Graphics
                else if (cat == Core.Organization.FileCategory.Photos ||
                         ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".bmp" or ".svg" or ".ico" or ".tiff" or ".tif" or
                                ".heic" or ".heif" or ".cr2" or ".nef" or ".arw" or ".dng" or ".rw2")
                {
                    var photos = ViewModel.Files
                        .Where(f => f.Category == Core.Organization.FileCategory.Photos ||
                                    (new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".svg", ".ico", ".tiff", ".tif", ".heic", ".heif" })
                                    .Contains(System.IO.Path.GetExtension(f.FileName).ToLowerInvariant()))
                        .Select(f => f.Entry)
                        .ToList();
                    int idx = photos.FindIndex(p => p.FileGuid == item.FileGuid);
                    Frame.Navigate(typeof(PhotoViewerPage), (vault, photos, Math.Max(0, idx)));
                }
                else
                {
                    // Fallback for binaries, archives, and unsupported formats: show file properties dialog
                    var propVm = new FilePropertiesViewModel(item.Entry);
                    var dialog = new FilePropertiesDialog(propVm)
                    {
                        XamlRoot = XamlRoot
                    };
                    await dialog.ShowAsync();
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

            ViewModel.OnOpenCompactionRequested = async () =>
            {
                var chain = new Core.MultiVault.VaultChainManager(vault);
                var compVm = new CompactionDialogViewModel(vault, chain);
                var dialog = new CompactionDialog(compVm)
                {
                    XamlRoot = XamlRoot
                };
                await dialog.ShowAsync();
            };

            ViewModel.OnOpenSettingsRequested = () =>
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentWindow);
                Frame.Navigate(typeof(SettingsPage), (vault, hwnd));
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
