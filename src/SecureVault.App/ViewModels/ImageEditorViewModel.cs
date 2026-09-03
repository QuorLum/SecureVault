using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using SecureVault.Core;
using SecureVault.Core.Format;
using SecureVault.Core.Media;
using SkiaSharp;
using Windows.Storage.Streams;

namespace SecureVault.App.ViewModels;

public partial class ImageEditorViewModel : ObservableObject, IDisposable
{
    private readonly VaultManager _vault;
    private readonly IndexEntry _entry;
    private SKBitmap _currentBitmap;
    private bool _disposed;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private BitmapImage? _previewImage;

    [ObservableProperty]
    private string _dimensionsText = string.Empty;

    [ObservableProperty]
    private string _statusText = "Ready to edit";

    public Action? OnCloseRequested { get; set; }
    public Action? OnSavedSuccessfully { get; set; }

    public ImageEditorViewModel(VaultManager vault, IndexEntry entry)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(entry);

        _vault = vault;
        _entry = entry;
        _fileName = entry.FileName;

        using var stream = _vault.OpenFileStream(entry);
        _currentBitmap = ImageDecoder.Decode(stream);

        RefreshPreview();
    }

    private void RefreshPreview()
    {
        DimensionsText = $"{_currentBitmap.Width} × {_currentBitmap.Height} px";

        using var image = SKImage.FromBitmap(_currentBitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);

        var ras = new InMemoryRandomAccessStream();
        using (var outStream = ras.GetOutputStreamAt(0))
        {
            using var netStream = outStream.AsStreamForWrite();
            encoded.SaveTo(netStream);
            netStream.Flush();
        }
        ras.Seek(0);

        var bmp = new BitmapImage();
        bmp.SetSource(ras);
        PreviewImage = bmp;
    }

    [RelayCommand]
    public void FlipHorizontal()
    {
        var flipped = new SKBitmap(_currentBitmap.Width, _currentBitmap.Height);
        using (var canvas = new SKCanvas(flipped))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(-1, 1, _currentBitmap.Width / 2f, _currentBitmap.Height / 2f);
            canvas.DrawBitmap(_currentBitmap, 0, 0);
        }

        _currentBitmap.Dispose();
        _currentBitmap = flipped;
        StatusText = "Flipped horizontally";
        RefreshPreview();
    }

    [RelayCommand]
    public void FlipVertical()
    {
        var flipped = new SKBitmap(_currentBitmap.Width, _currentBitmap.Height);
        using (var canvas = new SKCanvas(flipped))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(1, -1, _currentBitmap.Width / 2f, _currentBitmap.Height / 2f);
            canvas.DrawBitmap(_currentBitmap, 0, 0);
        }

        _currentBitmap.Dispose();
        _currentBitmap = flipped;
        StatusText = "Flipped vertically";
        RefreshPreview();
    }

    [RelayCommand]
    public void CropCenter()
    {
        // 10% center crop for quick demonstration
        int insetX = (int)(_currentBitmap.Width * 0.1);
        int insetY = (int)(_currentBitmap.Height * 0.1);
        var cropRect = new SKRectI(insetX, insetY, _currentBitmap.Width - insetX, _currentBitmap.Height - insetY);

        var cropped = new SKBitmap(cropRect.Width, cropRect.Height);
        if (_currentBitmap.ExtractSubset(cropped, cropRect))
        {
            _currentBitmap.Dispose();
            _currentBitmap = cropped;
            StatusText = "Cropped center 80%";
            RefreshPreview();
        }
        else
        {
            cropped.Dispose();
        }
    }

    [RelayCommand]
    public async Task SaveEditsAsync()
    {
        StatusText = "Saving encrypted image back to vault...";

        using var image = SKImage.FromBitmap(_currentBitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 95);
        using var ms = new MemoryStream(encoded.ToArray());

        try
        {
            // Add replacement file under same path and protection mode
            await _vault.AddFileAsync(ms, _entry.FileName, _entry.VirtualFolderPath, _entry.ProtectionMode);
            // Delete previous version
            _vault.DeleteFile(_entry.FileGuid);

            StatusText = "Saved successfully to vault!";
            OnSavedSuccessfully?.Invoke();
        }
        catch (Exception ex)
        {
            StatusText = $"Save error: {ex.Message}";
        }
    }

    [RelayCommand]
    public void Close()
    {
        OnCloseRequested?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _currentBitmap?.Dispose();
        _disposed = true;
    }
}
