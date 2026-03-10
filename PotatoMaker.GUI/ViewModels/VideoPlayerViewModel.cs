using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using PotatoMaker.Core;

namespace PotatoMaker.GUI.ViewModels;

/// <summary>
/// Owns the embedded media player state for source preview playback.
/// </summary>
public partial class VideoPlayerViewModel : ViewModelBase, IDisposable
{
    private readonly DispatcherTimer _positionTimer;
    private LibVLC? _libVlc;
    private Media? _media;
    private MediaPlayer? _mediaPlayer;
    private double _durationSeconds;
    private double _timelineSeconds;
    private double _volumePercent = 100;
    private double _lastAudibleVolumePercent = 100;
    private bool _isPlayerAvailable;
    private bool _hasMedia;
    private bool _isPlaying;
    private bool _isMuted;
    private bool _isSeekInteractionActive;
    private bool _isUpdatingFromPlayer;
    private bool _isPrimingInitialFrame;
    private bool _muteBeforeInitialFramePrime;
    private string _statusMessage;
    private string? _playerErrorMessage;
    private string? _sourcePath;
    private TimeSpan _selectionStart;
    private TimeSpan _selectionEnd;

    public event Action<ClipBoundary>? TrimBoundaryRequested;

    public VideoPlayerViewModel()
        : this(initializePlayer: false)
    {
    }

    public VideoPlayerViewModel(bool initializePlayer)
    {
        _positionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _positionTimer.Tick += OnPositionTimerTick;
        _positionTimer.Start();

        _statusMessage = initializePlayer
            ? "Select a video to preview it."
            : "Video playback is disabled in the previewer.";

        if (initializePlayer)
            TryInitializePlayer();
    }

    public MediaPlayer? MediaPlayer
    {
        get => _mediaPlayer;
        private set => SetProperty(ref _mediaPlayer, value);
    }

    public double DurationSeconds
    {
        get => _durationSeconds;
        private set
        {
            double normalized = value < 0 ? 0 : value;
            if (SetProperty(ref _durationSeconds, normalized))
            {
                OnPropertyChanged(nameof(DurationDisplay));
                OnPropertyChanged(nameof(CanSeek));
                SetTrimStartCommand.NotifyCanExecuteChanged();
                SetTrimEndCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public double VolumePercent
    {
        get => _volumePercent;
        set
        {
            double normalized = Clamp(value, 0, 100);
            if (!SetProperty(ref _volumePercent, normalized))
                return;

            if (normalized > 0.01d)
                _lastAudibleVolumePercent = normalized;

            bool shouldMute = normalized <= 0.01d;
            if (_isMuted != shouldMute)
                SetMutedCore(shouldMute, applyAudio: false);

            ApplyAudioSettings();
            OnPropertyChanged(nameof(VolumeDisplay));
        }
    }

    public double TimelineSeconds
    {
        get => _timelineSeconds;
        set
        {
            double normalized = Clamp(value, 0, DurationSeconds);
            if (!SetProperty(ref _timelineSeconds, normalized))
                return;

            OnPropertyChanged(nameof(CurrentTimeDisplay));
            OnPropertyChanged(nameof(CanResetPlayback));
            StopPlaybackCommand.NotifyCanExecuteChanged();

            if (!_isUpdatingFromPlayer && !_isSeekInteractionActive)
                SeekTo(TimeSpan.FromSeconds(normalized));
        }
    }

    public bool IsPlayerAvailable
    {
        get => _isPlayerAvailable;
        private set
        {
            if (SetProperty(ref _isPlayerAvailable, value))
            {
                OnPropertyChanged(nameof(IsVideoSurfaceVisible));
                OnPropertyChanged(nameof(CanControlPlayback));
                OnPropertyChanged(nameof(CanSeek));
                OnPropertyChanged(nameof(CanResetPlayback));
                OnPropertyChanged(nameof(CanAdjustVolume));
                TogglePlaybackCommand.NotifyCanExecuteChanged();
                StopPlaybackCommand.NotifyCanExecuteChanged();
                ToggleMuteCommand.NotifyCanExecuteChanged();
                SetTrimStartCommand.NotifyCanExecuteChanged();
                SetTrimEndCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasMedia
    {
        get => _hasMedia;
        private set
        {
            if (SetProperty(ref _hasMedia, value))
            {
                OnPropertyChanged(nameof(IsVideoSurfaceVisible));
                OnPropertyChanged(nameof(CanControlPlayback));
                OnPropertyChanged(nameof(CanSeek));
                OnPropertyChanged(nameof(CanResetPlayback));
                OnPropertyChanged(nameof(CanAdjustVolume));
                TogglePlaybackCommand.NotifyCanExecuteChanged();
                StopPlaybackCommand.NotifyCanExecuteChanged();
                ToggleMuteCommand.NotifyCanExecuteChanged();
                SetTrimStartCommand.NotifyCanExecuteChanged();
                SetTrimEndCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        private set
        {
            if (SetProperty(ref _isPlaying, value))
            {
                OnPropertyChanged(nameof(TogglePlaybackText));
                OnPropertyChanged(nameof(CanResetPlayback));
                StopPlaybackCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool CanControlPlayback => IsPlayerAvailable && HasMedia && MediaPlayer is not null;

    public bool CanSeek => CanControlPlayback && DurationSeconds > 0;

    public bool CanResetPlayback => CanControlPlayback && (IsPlaying || TimelineSeconds > 0.01d);

    public bool CanAdjustVolume => CanControlPlayback;

    public bool IsVideoSurfaceVisible => CanControlPlayback;

    public string TogglePlaybackText => IsPlaying ? "Pause" : "Play";

    public string ToggleMuteText => IsMuted ? "Unmute" : "Mute";

    public string CurrentTimeDisplay => FormatTime(TimeSpan.FromSeconds(TimelineSeconds));

    public TimeSpan CurrentPosition => TimeSpan.FromSeconds(TimelineSeconds);

    public string DurationDisplay => FormatTime(TimeSpan.FromSeconds(DurationSeconds));

    public bool IsMuted
    {
        get => _isMuted;
        private set => SetMutedCore(value);
    }

    public string VolumeDisplay => $"{Math.Round(VolumePercent):0}%";

    public string SelectedRangeDisplay => HasMedia
        ? $"{FormatTime(_selectionStart)} - {FormatTime(_selectionEnd)}"
        : "--";

    public string LoadedFileName => _sourcePath is null ? "No video selected" : Path.GetFileName(_sourcePath);

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string? PlayerErrorMessage
    {
        get => _playerErrorMessage;
        private set
        {
            if (SetProperty(ref _playerErrorMessage, value))
                OnPropertyChanged(nameof(HasPlayerError));
        }
    }

    public bool HasPlayerError => !string.IsNullOrWhiteSpace(PlayerErrorMessage);

    [RelayCommand(CanExecute = nameof(CanTogglePlayback))]
    private void TogglePlayback()
    {
        if (MediaPlayer is null)
            return;

        if (IsPlaying)
        {
            MediaPlayer.Pause();
        }
        else
        {
            MediaPlayer.Play();
        }

        UpdatePlaybackState();
    }

    private bool CanTogglePlayback() => CanControlPlayback;

    [RelayCommand(CanExecute = nameof(CanToggleMute))]
    private void ToggleMute()
    {
        if (IsMuted)
        {
            if (VolumePercent <= 0.01d)
                VolumePercent = _lastAudibleVolumePercent;

            IsMuted = false;
        }
        else
        {
            if (VolumePercent > 0.01d)
                _lastAudibleVolumePercent = VolumePercent;

            IsMuted = true;
        }
    }

    private bool CanToggleMute() => CanAdjustVolume;

    [RelayCommand(CanExecute = nameof(CanStopPlayback))]
    private void StopPlayback()
    {
        ResetPlayback();
    }

    public void ResetPlayback()
    {
        if (MediaPlayer is null)
            return;

        MediaPlayer.SetPause(true);
        SeekTo(TimeSpan.Zero);
        UpdatePlaybackState();
        SetTimelineFromPlayer(TimeSpan.Zero);
    }

    private bool CanStopPlayback() => CanResetPlayback;

    [RelayCommand(CanExecute = nameof(CanSetTrimBoundary))]
    private void SetTrimStart() => TrimBoundaryRequested?.Invoke(ClipBoundary.Start);

    [RelayCommand(CanExecute = nameof(CanSetTrimBoundary))]
    private void SetTrimEnd() => TrimBoundaryRequested?.Invoke(ClipBoundary.End);

    private bool CanSetTrimBoundary() => DurationSeconds > 0;

    public void LoadSource(string path, TimeSpan duration, VideoClipRange selection)
    {
        ArgumentNullException.ThrowIfNull(path);

        _sourcePath = Path.GetFullPath(path);
        DurationSeconds = duration.TotalSeconds;
        SetSelection(selection);

        if (!IsPlayerAvailable || MediaPlayer is null || _libVlc is null)
        {
            StatusMessage = "Video player could not be initialized.";
            OnPropertyChanged(nameof(LoadedFileName));
            return;
        }

        try
        {
            MediaPlayer.Stop();
            ReleaseMedia();
            _media = new Media(_libVlc, _sourcePath, FromType.FromPath);
            MediaPlayer.Media = _media;
            HasMedia = true;
            IsPlaying = false;
            PlayerErrorMessage = null;
            StatusMessage = $"Ready to play {Path.GetFileName(_sourcePath)}";
            SetTimelineFromPlayer(TimeSpan.Zero);
            PrimeInitialFrame();
            OnPropertyChanged(nameof(LoadedFileName));
        }
        catch (Exception ex)
        {
            ReleaseMedia();
            HasMedia = false;
            PlayerErrorMessage = ex.Message;
            StatusMessage = "Video player could not open the selected file.";
        }
    }

    public void Clear()
    {
        if (MediaPlayer is not null)
            MediaPlayer.Stop();

        ReleaseMedia();
        _sourcePath = null;
        _isPrimingInitialFrame = false;
        HasMedia = false;
        IsPlaying = false;
        DurationSeconds = 0;
        SetTimelineFromPlayer(TimeSpan.Zero);
        _selectionStart = TimeSpan.Zero;
        _selectionEnd = TimeSpan.Zero;
        PlayerErrorMessage = IsPlayerAvailable ? null : PlayerErrorMessage;
        StatusMessage = IsPlayerAvailable
            ? "Select a video to preview it."
            : "Video player could not be initialized.";
        OnPropertyChanged(nameof(LoadedFileName));
        OnPropertyChanged(nameof(SelectedRangeDisplay));
    }

    public void SetSelection(VideoClipRange selection)
    {
        _selectionStart = selection.Start < TimeSpan.Zero ? TimeSpan.Zero : selection.Start;
        TimeSpan maxDuration = TimeSpan.FromSeconds(DurationSeconds);
        _selectionEnd = selection.End < _selectionStart ? _selectionStart : selection.End;

        if (DurationSeconds > 0 && _selectionEnd > maxDuration)
            _selectionEnd = maxDuration;

        OnPropertyChanged(nameof(SelectedRangeDisplay));
        SetTrimStartCommand.NotifyCanExecuteChanged();
        SetTrimEndCommand.NotifyCanExecuteChanged();
    }

    public void BeginSeekInteraction() => _isSeekInteractionActive = true;

    public void SeekDuringInteraction(TimeSpan position)
    {
        if (!CanSeek)
            return;

        SeekTo(position);
    }

    public void EndSeekInteraction()
    {
        if (!_isSeekInteractionActive)
            return;

        _isSeekInteractionActive = false;
        SeekTo(TimeSpan.FromSeconds(TimelineSeconds));
    }

    public void Dispose()
    {
        _positionTimer.Stop();
        _positionTimer.Tick -= OnPositionTimerTick;
        var mediaPlayer = _mediaPlayer;
        _mediaPlayer = null;

        if (mediaPlayer is not null)
        {
            mediaPlayer.Playing -= OnMediaPlayerPlaying;
            mediaPlayer.EndReached -= OnMediaPlayerEndReached;
        }

        try
        {
            mediaPlayer?.Stop();
        }
        catch
        {
        }

        try
        {
            if (mediaPlayer is not null)
                mediaPlayer.Media = null;
        }
        catch
        {
        }

        _media?.Dispose();
        _media = null;

        try
        {
            mediaPlayer?.Dispose();
        }
        catch
        {
        }

        _libVlc?.Dispose();
        _libVlc = null;
        GC.SuppressFinalize(this);
    }

    private void TryInitializePlayer()
    {
        try
        {
            _libVlc = new LibVLC("--quiet");
            MediaPlayer = new MediaPlayer(_libVlc);
            MediaPlayer.Playing += OnMediaPlayerPlaying;
            MediaPlayer.EndReached += OnMediaPlayerEndReached;
            ApplyAudioSettings();
            IsPlayerAvailable = true;
            PlayerErrorMessage = null;
            StatusMessage = "Select a video to preview it.";
        }
        catch (Exception ex)
        {
            IsPlayerAvailable = false;
            PlayerErrorMessage = ex.Message;
            StatusMessage = "Video player could not be initialized.";
        }
    }

    private void OnPositionTimerTick(object? sender, EventArgs e)
    {
        if (MediaPlayer is null)
            return;

        UpdatePlaybackState();

        if (DurationSeconds <= 0 && MediaPlayer.Length > 0)
            DurationSeconds = MediaPlayer.Length / 1000d;

        if (_isSeekInteractionActive)
            return;

        long currentTime = MediaPlayer.Time;
        if (currentTime >= 0)
            SetTimelineFromPlayer(TimeSpan.FromMilliseconds(currentTime));
    }

    private void UpdatePlaybackState()
    {
        IsPlaying = MediaPlayer?.IsPlaying ?? false;
    }

    private void SetMutedCore(bool value, bool applyAudio = true)
    {
        if (!SetProperty(ref _isMuted, value, nameof(IsMuted)))
            return;

        OnPropertyChanged(nameof(ToggleMuteText));

        if (applyAudio)
            ApplyAudioSettings();
    }

    private void ApplyAudioSettings()
    {
        if (MediaPlayer is null)
            return;

        try
        {
            MediaPlayer.Volume = (int)Math.Round(VolumePercent);
            MediaPlayer.Mute = IsMuted;
        }
        catch
        {
        }
    }

    private void PrimeInitialFrame()
    {
        if (MediaPlayer is null || !HasMedia)
            return;

        try
        {
            _isPrimingInitialFrame = true;
            _muteBeforeInitialFramePrime = MediaPlayer.Mute;
            MediaPlayer.Mute = true;
            MediaPlayer.Play();
        }
        catch
        {
            _isPrimingInitialFrame = false;

            if (MediaPlayer is not null)
                MediaPlayer.Mute = _muteBeforeInitialFramePrime;
        }
    }

    private void OnMediaPlayerPlaying(object? sender, EventArgs e)
    {
        if (!_isPrimingInitialFrame)
            return;

        Dispatcher.UIThread.Post(CompleteInitialFramePrime);
    }

    private void OnMediaPlayerEndReached(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(ResetAfterPlaybackEnded);
    }

    private void CompleteInitialFramePrime()
    {
        if (!_isPrimingInitialFrame || MediaPlayer is null)
            return;

        _isPrimingInitialFrame = false;

        try
        {
            MediaPlayer.SetPause(true);

            long currentTime = MediaPlayer.Time;
            if (currentTime >= 0)
                SetTimelineFromPlayer(TimeSpan.FromMilliseconds(currentTime));
        }
        finally
        {
            MediaPlayer.Mute = _muteBeforeInitialFramePrime;
            UpdatePlaybackState();
        }
    }

    private void ResetAfterPlaybackEnded()
    {
        if (MediaPlayer is null || !HasMedia)
            return;

        _isSeekInteractionActive = false;
        _isPrimingInitialFrame = false;

        try
        {
            MediaPlayer.Stop();
        }
        catch
        {
        }

        SetTimelineFromPlayer(TimeSpan.Zero);
        UpdatePlaybackState();
        PrimeInitialFrame();
    }

    private void SetTimelineFromPlayer(TimeSpan value)
    {
        _isUpdatingFromPlayer = true;
        try
        {
            TimelineSeconds = Clamp(value.TotalSeconds, 0, DurationSeconds);
        }
        finally
        {
            _isUpdatingFromPlayer = false;
        }
    }

    private void SeekTo(TimeSpan position)
    {
        if (MediaPlayer is null || !CanSeek)
            return;

        long time = (long)Clamp(position.TotalMilliseconds, 0, DurationSeconds * 1000d);
        MediaPlayer.Time = time;
        SetTimelineFromPlayer(TimeSpan.FromMilliseconds(time));
    }

    private void ReleaseMedia()
    {
        if (_mediaPlayer is not null)
            _mediaPlayer.Media = null;

        _media?.Dispose();
        _media = null;
    }

    private static double Clamp(double value, double min, double max)
    {
        if (value < min)
            return min;

        return value > max ? max : value;
    }

    private static string FormatTime(TimeSpan value) =>
        value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss")
            : value.ToString(@"m\:ss");
}
