using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using SecureVault.Core;
using SecureVault.Core.Format;
using SecureVault.Core.Media;
using SecureVault.App.Diagnostics;
using Windows.Storage.Streams;

namespace SecureVault.App.ViewModels;

public partial class PhotoViewerViewModel : ObservableObject, IDisposable
{
    private readonly VaultManager _vault;
    private readonly List<IndexEntry> _photoEntries;
    private int _currentIndex;
    private float _currentRotationDegrees = 0;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string _dimensionsText = string.Empty;

    [ObservableProperty]
    private string _fileSizeText = string.Empty;

    [ObservableProperty]
    private string _positionIndicator = string.Empty;

    [ObservableProperty]
    private BitmapImage? _displayImage;

    [ObservableProperty]
    private double _zoomLevel = 1.0;

    [ObservableProperty]
    private bool _isExifPanelOpen;

    [ObservableProperty]
    private ExifData? _exifData;

    public Action? OnCloseRequested { get; set; }

    public VaultManager Vault => _vault;
    public IndexEntry? CurrentEntry => (_photoEntries.Count > 0 && _currentIndex >= 0 && _currentIndex < _photoEntries.Count) ? _photoEntries[_currentIndex] : null;

    public PhotoViewerViewModel(VaultManager vault, List<IndexEntry> photoEntries, int initialIndex)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(photoEntries);

        _vault = vault;
        _photoEntries = photoEntries;
        _currentIndex = Math.Clamp(initialIndex, 0, Math.Max(0, photoEntries.Count - 1));

        LoadCurrentPhoto();
    }

    public void LoadCurrentPhoto()
    {
        if (_photoEntries.Count == 0 || _currentIndex < 0 || _currentIndex >= _photoEntries.Count)
            return;

        var entry = _photoEntries[_currentIndex];
        FileName = entry.FileName;
        FileSizeText = FormatBytes(entry.OriginalSize);
        PositionIndicator = $"{_currentIndex + 1} of {_photoEntries.Count}";
        ZoomLevel = 1.0;
        _currentRotationDegrees = 0;

        try
        {
            using var stream = _vault.OpenFileStream(entry);

            // Extract EXIF in memory
            try
            {
                ExifData = ExifMetadataReader.Read(stream);
                stream.Seek(0, SeekOrigin.Begin);
            }
            catch (Exception ex)
            {
                CrashLog.Trace("PhotoViewer-Exif", ex);
                ExifData = null;
            }

            // Decode into SKBitmap for dimension retrieval and rotation support
            using var skBitmap = ImageDecoder.Decode(stream);
            DimensionsText = $"{skBitmap.Width} × {skBitmap.Height} px";

            // Render to WinUI BitmapImage in memory
            stream.Seek(0, SeekOrigin.Begin);
            var ras = new InMemoryRandomAccessStream();
            using (var outStream = ras.GetOutputStreamAt(0))
            {
                using var netStream = outStream.AsStreamForWrite();
                stream.CopyTo(netStream);
                netStream.Flush();
            }
            ras.Seek(0);

            var bmp = new BitmapImage();
            bmp.SetSource(ras);
            DisplayImage = bmp;
        }
        catch (Exception ex)
        {
            CrashLog.Write("PhotoViewer-Load", ex);
            DimensionsText = "Preview Unavailable";
            DisplayImage = null;
        }
    }

    [RelayCommand]
    public void NextPhoto()
    {
        if (_currentIndex < _photoEntries.Count - 1)
        {
            _currentIndex++;
            LoadCurrentPhoto();
        }
    }

    [RelayCommand]
    public void PreviousPhoto()
    {
        if (_currentIndex > 0)
        {
            _currentIndex--;
            LoadCurrentPhoto();
        }
    }

    [RelayCommand]
    public void ZoomIn()
    {
        ZoomLevel = Math.Min(5.0, ZoomLevel + 0.25);
    }

    [RelayCommand]
    public void ZoomOut()
    {
        ZoomLevel = Math.Max(0.25, ZoomLevel - 0.25);
    }

    [RelayCommand]
    public void ResetZoom()
    {
        ZoomLevel = 1.0;
    }

    [RelayCommand]
    public void RotateClockwise()
    {
        RotateImage(90);
    }

    [RelayCommand]
    public void RotateCounterClockwise()
    {
        RotateImage(-90);
    }

    private void RotateImage(float deltaDegrees)
    {
        if (_photoEntries.Count == 0 || _currentIndex < 0 || _currentIndex >= _photoEntries.Count) return;
        var entry = _photoEntries[_currentIndex];

        try
        {
            _currentRotationDegrees = (_currentRotationDegrees + deltaDegrees) % 360;

            using var stream = _vault.OpenFileStream(entry);
            using var original = ImageDecoder.Decode(stream);
            using var rotated = ImageDecoder.Rotate(original, _currentRotationDegrees);

            using var image = SkiaSharp.SKImage.FromBitmap(rotated);
            using var encoded = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);

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
            DisplayImage = bmp;
            DimensionsText = $"{rotated.Width} × {rotated.Height} px";
        }
        catch
        {
            DimensionsText = "Rotation Failed";
        }
    }

    [RelayCommand]
    public void ToggleExif()
    {
        IsExifPanelOpen = !IsExifPanelOpen;
    }

    [RelayCommand]
    public void Close()
    {
        OnCloseRequested?.Invoke();
    }

    private static string FormatBytes(ulong bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int i = 0;
        double d = bytes;
        while (d >= 1024 && i < suffixes.Length - 1)
        {
            d /= 1024;
            i++;
        }
        return $"{d:0.##} {suffixes[i]}";
    }

    public void Dispose()
    {
        DisplayImage = null;
        _photoEntries.Clear();
    }
}
