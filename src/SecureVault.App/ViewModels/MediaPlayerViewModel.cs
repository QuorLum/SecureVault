using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using SecureVault.Core;
using SecureVault.Core.Format;
using SecureVault.Core.Media;
using SecureVault.Core.Organization;

namespace SecureVault.App.ViewModels;

public partial class MediaPlayerViewModel : ObservableObject, IDisposable
{
    private static bool _libVlcInitialized = false;
    private readonly VaultManager _vault;
    private readonly IndexEntry _entry;
    private LibVLC? _libVLC;
    private MediaPlayer? _player;
    private VaultMediaInput? _mediaInput;
    private bool _disposed;

    public MediaPlayer? Player => _player;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _isAudioOnly;

    [ObservableProperty]
    private double _position;

    [ObservableProperty]
    private string _timeText = "00:00 / 00:00";

    [ObservableProperty]
    private int _volume = 80;

    [ObservableProperty]
    private float _playbackRate = 1.0f;

    [ObservableProperty]
    private bool _isMuted;

    public Action? OnCloseRequested { get; set; }

    public MediaPlayerViewModel(VaultManager vault, IndexEntry entry)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(entry);

        _vault = vault;
        _entry = entry;
        _fileName = entry.FileName;
        _isAudioOnly = entry.Category == (byte)FileCategory.Audio;

        InitializePlayer();
    }

    private void InitializePlayer()
    {
        if (!_libVlcInitialized)
        {
            LibVLCSharp.Shared.Core.Initialize();
            _libVlcInitialized = true;
        }

        _libVLC = new LibVLC("--no-video-title-show");
        _player = new MediaPlayer(_libVLC);

        _player.TimeChanged += (s, e) =>
        {
            long currentMs = e.Time;
            long lengthMs = _player.Length;
            if (lengthMs > 0)
            {
                var cur = TimeSpan.FromMilliseconds(currentMs);
                var total = TimeSpan.FromMilliseconds(lengthMs);
                TimeText = $"{cur:mm\\:ss} / {total:mm\\:ss}";
                Position = (double)currentMs / lengthMs;
            }
        };

        _player.Playing += (s, e) => IsPlaying = true;
        _player.Paused += (s, e) => IsPlaying = false;
        _player.Stopped += (s, e) => IsPlaying = false;
        _player.EndReached += (s, e) =>
        {
            IsPlaying = false;
            Position = 0;
        };

        StartPlayback();
    }

    private void StartPlayback()
    {
        var stream = _vault.OpenFileStream(_entry);
        _mediaInput = new VaultMediaInput(stream);

        using var media = new Media(_libVLC!, _mediaInput);
        _player!.Play(media);
        _player.Volume = Volume;
    }

    [RelayCommand]
    public void TogglePlayPause()
    {
        if (_player == null) return;
        if (_player.IsPlaying)
        {
            _player.Pause();
        }
        else
        {
            _player.Play();
        }
    }

    [RelayCommand]
    public void Stop()
    {
        _player?.Stop();
        IsPlaying = false;
        Position = 0;
    }

    [RelayCommand]
    public void Seek(double newPosition)
    {
        if (_player == null || _player.Length <= 0) return;
        _player.Position = (float)Math.Clamp(newPosition, 0.0, 1.0);
    }

    [RelayCommand]
    public void SetVolume(int vol)
    {
        Volume = Math.Clamp(vol, 0, 100);
        if (_player != null)
        {
            _player.Volume = Volume;
        }
    }

    [RelayCommand]
    public void ToggleMute()
    {
        IsMuted = !IsMuted;
        if (_player != null)
        {
            _player.Mute = IsMuted;
        }
    }

    [RelayCommand]
    public void SetRate(float rate)
    {
        PlaybackRate = rate;
        if (_player != null)
        {
            _player.SetRate(rate);
        }
    }

    [RelayCommand]
    public void Close()
    {
        Stop();
        OnCloseRequested?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _player?.Stop();
        _player?.Dispose();
        _mediaInput?.Dispose();
        _libVLC?.Dispose();
        _disposed = true;
    }
}
