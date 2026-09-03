using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using SecureVault.Core;
using SecureVault.Core.Format;
using SecureVault.Core.Notes;

namespace SecureVault.App.ViewModels;

public partial class NotesEditorViewModel : ObservableObject, IDisposable
{
    private readonly VaultManager _vault;
    private readonly IndexEntry? _existingEntry;
    private NoteDocument _document;
    private DispatcherQueueTimer? _autoSaveTimer;
    private bool _hasUnsavedChanges;
    private bool _disposed;

    [ObservableProperty]
    private string _title = "Untitled Note";

    [ObservableProperty]
    private string _content = string.Empty;

    [ObservableProperty]
    private NoteFormat _format = NoteFormat.Markdown;

    [ObservableProperty]
    private string _renderedHtml = string.Empty;

    [ObservableProperty]
    private string _wordCountText = "0 words";

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private bool _isPreviewVisible = true;

    public Action? OnCloseRequested { get; set; }

    public NotesEditorViewModel(VaultManager vault, IndexEntry? existingEntry = null)
    {
        ArgumentNullException.ThrowIfNull(vault);
        _vault = vault;
        _existingEntry = existingEntry;

        if (existingEntry != null)
        {
            _title = existingEntry.FileName;
            using var stream = _vault.OpenFileStream(existingEntry);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            byte[] bytes = ms.ToArray();

            try
            {
                _document = NoteDocument.Deserialize(bytes);
                _content = _document.Content;
                _format = _document.Format;
            }
            catch
            {
                // Fallback: raw text
                _document = new NoteDocument
                {
                    Title = _title,
                    Content = Encoding.UTF8.GetString(bytes),
                    Format = NoteFormat.Markdown
                };
                _content = _document.Content;
            }
        }
        else
        {
            _document = new NoteDocument
            {
                Title = "Untitled Note",
                Format = NoteFormat.Markdown,
                Content = "# Welcome to SecureVault Notes\n\nStart typing encrypted notes here..."
            };
            _content = _document.Content;
            _title = _document.Title;
        }

        UpdateMetrics();
        InitializeAutoSaveTimer();
    }

    private void InitializeAutoSaveTimer()
    {
        var dq = DispatcherQueue.GetForCurrentThread();
        if (dq != null)
        {
            _autoSaveTimer = dq.CreateTimer();
            _autoSaveTimer.Interval = TimeSpan.FromSeconds(3); // 3-second debounce (J08)
            _autoSaveTimer.Tick += async (s, e) =>
            {
                if (_hasUnsavedChanges)
                {
                    await SaveAsync();
                }
            };
            _autoSaveTimer.Start();
        }
    }

    partial void OnContentChanged(string value)
    {
        _document.Content = value;
        _hasUnsavedChanges = true;
        StatusText = "Unsaved changes...";
        UpdateMetrics();
    }

    partial void OnTitleChanged(string value)
    {
        _document.Title = value;
        _hasUnsavedChanges = true;
        StatusText = "Unsaved changes...";
    }

    private void UpdateMetrics()
    {
        WordCountText = $"{_document.WordCount} words  •  {Content.Length} chars";
        RenderedHtml = _document.RenderMarkdownToHtml();
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        _document.Title = Title;
        _document.Content = Content;
        _document.Format = Format;
        _document.ModifiedUtc = DateTime.UtcNow;

        byte[] serialized = _document.Serialize();
        using var ms = new MemoryStream(serialized);

        string fileName = Title.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            ? Title
            : Title + ".md";

        StatusText = "Saving to vault...";

        try
        {
            if (_existingEntry != null)
            {
                // Replace note data: delete old entry and add updated entry
                _existingEntry.FileName = fileName;
                _existingEntry.DateModifiedTicks = DateTime.UtcNow.Ticks;
                // Add new version
                await _vault.AddFileAsync(ms, fileName, _existingEntry.VirtualFolderPath, _existingEntry.ProtectionMode);
                _vault.DeleteFile(_existingEntry.FileGuid);
            }
            else
            {
                await _vault.AddFileAsync(ms, fileName, "/", ProtectionMode.SecureMode);
            }

            _hasUnsavedChanges = false;
            StatusText = $"Saved at {DateTime.Now:T}";
        }
        catch (Exception ex)
        {
            StatusText = $"Save error: {ex.Message}";
        }
    }

    [RelayCommand]
    public void TogglePreview()
    {
        IsPreviewVisible = !IsPreviewVisible;
    }

    [RelayCommand]
    public async Task CloseAsync()
    {
        if (_hasUnsavedChanges)
        {
            await SaveAsync();
        }
        OnCloseRequested?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _autoSaveTimer?.Stop();
        _disposed = true;
    }
}
