using System.Runtime.InteropServices.WindowsRuntime;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using SecureVault.Core;
using SecureVault.Core.Format;
using SecureVault.Core.Media;

namespace SecureVault.App.ViewModels;

public partial class PdfViewerViewModel : ObservableObject, IDisposable
{
    private readonly VaultManager _vault;
    private readonly IndexEntry _entry;
    private PdfRenderer? _renderer;
    private int _currentPageIndex = 0;
    private bool _disposed;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private int _currentPage = 1;

    [ObservableProperty]
    private int _totalPages = 1;

    [ObservableProperty]
    private string _pageIndicator = "Page 1 of 1";

    [ObservableProperty]
    private double _zoomLevel = 1.0;

    [ObservableProperty]
    private WriteableBitmap? _currentPageBitmap;

    public Action? OnCloseRequested { get; set; }

    public PdfViewerViewModel(VaultManager vault, IndexEntry entry)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(entry);

        _vault = vault;
        _entry = entry;
        _fileName = entry.FileName;

        LoadPdf();
    }

    private void LoadPdf()
    {
        using var stream = _vault.OpenFileStream(_entry);
        _renderer = new PdfRenderer(stream);
        TotalPages = Math.Max(1, _renderer.PageCount);
        _currentPageIndex = 0;
        CurrentPage = 1;

        RenderCurrentPage();
    }

    public void RenderCurrentPage()
    {
        if (_renderer == null || _renderer.PageCount == 0) return;

        PageIndicator = $"Page {_currentPageIndex + 1} of {TotalPages}";
        CurrentPage = _currentPageIndex + 1;

        try
        {
            var (bgraBytes, width, height) = _renderer.RenderPage(_currentPageIndex, ZoomLevel);
            var writeableBmp = new WriteableBitmap(width, height);

            using (var pixelStream = writeableBmp.PixelBuffer.AsStream())
            {
                pixelStream.Write(bgraBytes, 0, bgraBytes.Length);
            }

            writeableBmp.Invalidate();
            CurrentPageBitmap = writeableBmp;
        }
        catch
        {
            CurrentPageBitmap = null;
        }
    }

    [RelayCommand]
    public void NextPage()
    {
        if (_currentPageIndex < TotalPages - 1)
        {
            _currentPageIndex++;
            RenderCurrentPage();
        }
    }

    [RelayCommand]
    public void PreviousPage()
    {
        if (_currentPageIndex > 0)
        {
            _currentPageIndex--;
            RenderCurrentPage();
        }
    }

    [RelayCommand]
    public void GoToPage(int pageNumber)
    {
        int target = Math.Clamp(pageNumber - 1, 0, TotalPages - 1);
        if (target != _currentPageIndex)
        {
            _currentPageIndex = target;
            RenderCurrentPage();
        }
    }

    [RelayCommand]
    public void ZoomIn()
    {
        ZoomLevel = Math.Min(3.0, ZoomLevel + 0.25);
        RenderCurrentPage();
    }

    [RelayCommand]
    public void ZoomOut()
    {
        ZoomLevel = Math.Max(0.5, ZoomLevel - 0.25);
        RenderCurrentPage();
    }

    [RelayCommand]
    public void ResetZoom()
    {
        ZoomLevel = 1.0;
        RenderCurrentPage();
    }

    [RelayCommand]
    public void Close()
    {
        OnCloseRequested?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _renderer?.Dispose();
        _disposed = true;
    }
}
