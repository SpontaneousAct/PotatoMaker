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
    private const long SeekPreviewNoOpToleranceMilliseconds = 8;
    private const int PausedSeekRefreshMaxAttempts = 2;
    private static readonly TimeSpan SeekPreviewTimerInterval = TimeSpan.FromMilliseconds(16);
    private static readonly TimeSpan SeekPreviewPlayingDispatchInterval = TimeSpan.FromMilliseconds(33);
    private static readonly TimeSpan SeekPreviewPausedDispatchInterval = TimeSpan.FromMilliseconds(75);
    private const double PlaybackEndRestartThresholdSeconds = 0.05;

    private readonly DispatcherTimer _positionTimer;
    private readonly DispatcherTimer _seekPreviewTimer;
    private readonly DispatcherTimer _pausedSeekRefreshTimer;

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
    private bool _suppressVideoSurface;
    private bool _isSeekInteractionActive;
    private bool _isUpdatingFromPlayer;
    private bool _isPrimingInitialFrame;
    private bool _isResettingToFirstFrame;
    private bool _muteBeforeInitialFramePrime;
    private int _volumeBeforeInitialFramePrime;
    private bool _resumePlaybackAfterSeek;
    private bool _isSeekPlaybackRestorePending;
    private bool _hasAttemptedPlayerInitialization;
    private TimeSpan? _pendingSeekPreviewTarget;
    private TimeSpan? _pendingPausedSeekRefreshTarget;
    private TimeSpan? _lastRequestedSeekPreviewTarget;
    private DateTimeOffset _lastSeekPreviewDispatchAt;
    private int _remainingPausedSeekRefreshAttempts;
    private (string Path, TimeSpan Duration, VideoClipRange Selection)? _pendingSourceLoad;
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
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _positionTimer.Tick += OnPositionTimerTick;
        _positionTimer.Start();
        _seekPreviewTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = SeekPreviewTimerInterval
        };
        _seekPreviewTimer.Tick += OnSeekPreviewTimerTick;
        _pausedSeekRefreshTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(45)
        };
        _pausedSeekRefreshTimer.Tick += OnPausedSeekRefreshTimerTick;

        _hasAttemptedPlayerInitialization = initializePlayer;
        _statusMessage = "Select a video to preview it.";

        if (initializePlayer)
            TryInitializePlayer();
    }

    public void EnsureInitialized()
    {
        if (_hasAttemptedPlayerInitialization)
            return;

        _hasAttemptedPlayerInitialization = true;
        TryInitializePlayer();

        if (!IsPlayerAvailable)
        {
            _pendingSourceLoad = null;
            return;
        }

        if (_pendingSourceLoad is { } pendingSourceLoad)
        {
            _pendingSourceLoad = null;
            LoadSource(pendingSourceLoad.Path, pendingSourceLoad.Duration, pendingSourceLoad.Selection);
        }
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
                OnPropertyChanged(nameof(IsVideoStatusVisible));
                OnPropertyChanged(nameof(ShowStatusMessage));
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
                OnPropertyChanged(nameof(IsVideoStatusVisible));
                OnPropertyChanged(nameof(ShowStatusMessage));
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

    public bool IsVideoSurfaceVisible => CanControlPlayback && !SuppressVideoSurface;

    public bool IsVideoStatusVisible => !CanControlPlayback;

    public bool ShowStatusMessage => IsVideoStatusVisible && !HasPlayerError;

    public bool SuppressVideoSurface
    {
        get => _suppressVideoSurface;
        set
        {
            if (!SetProperty(ref _suppressVideoSurface, value))
                return;

            OnPropertyChanged(nameof(IsVideoSurfaceVisible));
            OnPropertyChanged(nameof(IsVideoStatusVisible));
        }
    }

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
            {
                OnPropertyChanged(nameof(HasPlayerError));
                OnPropertyChanged(nameof(ShowStatusMessage));
            }
        }
    }

    public bool HasPlayerError => !string.IsNullOrWhiteSpace(PlayerErrorMessage);

    [RelayCommand(CanExecute = nameof(CanTogglePlayback))]
    private void TogglePlayback()
    {
        if (MediaPlayer is null)
            return;

        bool shouldPause = IsPlaying;
        if (HasPendingSeekPreviewState())
            CommitPendingSeekInteraction();

        if (shouldPause)
        {
            TryPause();
        }
        else
        {
            if (IsAtPlaybackEndPosition(TimelineSeconds, DurationSeconds))
            {
                RestartPlaybackFromBeginningAndPlay();
            }
            else
            {
                MediaPlayer.Play();
            }
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

        ResetToFirstFrame();
    }

    public void PausePlaybackIfPlaying()
    {
        if (MediaPlayer is null)
            return;

        if (!IsPlaying)
            return;

        if (HasPendingSeekPreviewState())
            CommitPendingSeekInteraction();

        TryPause();

        UpdatePlaybackState();
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

        CancelPendingPausedSeekRefresh();
        CancelPendingSeekInteraction();
        _sourcePath = Path.GetFullPath(path);
        DurationSeconds = duration.TotalSeconds;
        SetSelection(selection);

        if (!IsPlayerAvailable || MediaPlayer is null || _libVlc is null)
        {
            _pendingSourceLoad = !_hasAttemptedPlayerInitialization
                ? (_sourcePath, duration, selection)
                : null;
            PlayerErrorMessage = _hasAttemptedPlayerInitialization
                ? PlayerErrorMessage
                : null;
            StatusMessage = _hasAttemptedPlayerInitialization
                ? "Video player could not be initialized."
                : "Preparing video preview...";
            OnPropertyChanged(nameof(LoadedFileName));
            return;
        }

        _pendingSourceLoad = null;

        try
        {
            DetachCurrentMedia();
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
        CancelPendingPausedSeekRefresh();
        CancelPendingSeekInteraction();
        _pendingSourceLoad = null;

        DetachCurrentMedia();
        _sourcePath = null;
        _isPrimingInitialFrame = false;
        _isResettingToFirstFrame = false;
        HasMedia = false;
        IsPlaying = false;
        DurationSeconds = 0;
        SetTimelineFromPlayer(TimeSpan.Zero);
        _selectionStart = TimeSpan.Zero;
        _selectionEnd = TimeSpan.Zero;
        bool hasInitializationFailure = _hasAttemptedPlayerInitialization && !IsPlayerAvailable;
        PlayerErrorMessage = hasInitializationFailure ? PlayerErrorMessage : null;
        StatusMessage = hasInitializationFailure
            ? "Video player could not be initialized."
            : "Select a video to preview it.";
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

    public void BeginSeekInteraction()
    {
        if (!CanSeek || _isSeekInteractionActive)
            return;

        CancelPendingPausedSeekRefresh();
        _isSeekInteractionActive = true;
        _isSeekPlaybackRestorePending = false;
        _resumePlaybackAfterSeek = ShouldResumePlaybackAfterSeekInteraction();
        _lastRequestedSeekPreviewTarget = null;
        _lastSeekPreviewDispatchAt = DateTimeOffset.MinValue;
        _pendingSeekPreviewTarget = CurrentPosition;
        _seekPreviewTimer.Start();
        TryPause();
        UpdatePlaybackState();
    }

    public void BeginTrimPreview()
    {
        if (CanSeek)
            BeginSeekInteraction();
    }

    public void SeekDuringInteraction(TimeSpan position)
    {
        if (!CanSeek)
            return;

        if (!_isSeekInteractionActive)
            BeginSeekInteraction();

        TimeSpan clampedPosition = ClampPosition(position);
        _pendingSeekPreviewTarget = clampedPosition;
        SetTimelineFromPlayer(clampedPosition);
        QueueSeekDuringInteraction(clampedPosition);
    }

    public void PreviewTrimPosition(TimeSpan position)
    {
        TimeSpan clampedPosition = ClampPosition(position);
        if (CanSeek)
        {
            SeekDuringInteraction(clampedPosition);
            return;
        }

        SetTimelineFromPlayer(clampedPosition);
    }

    public void EndSeekInteraction(bool preferCurrentPreviewTarget = false)
    {
        if (!_isSeekInteractionActive)
            return;

        CommitPendingSeekInteraction();
    }

    public void EndTrimPreview()
    {
        if (CanSeek)
            EndSeekInteraction();
    }

    public void Dispose()
    {
        _positionTimer.Stop();
        _positionTimer.Tick -= OnPositionTimerTick;
        _seekPreviewTimer.Stop();
        _seekPreviewTimer.Tick -= OnSeekPreviewTimerTick;
        _pausedSeekRefreshTimer.Stop();
        _pausedSeekRefreshTimer.Tick -= OnPausedSeekRefreshTimerTick;

        var mediaPlayer = _mediaPlayer;
        _mediaPlayer = null;

        if (mediaPlayer is not null)
        {
            mediaPlayer.Playing -= OnMediaPlayerPlaying;
            mediaPlayer.EndReached -= OnMediaPlayerEndReached;
            mediaPlayer.Paused -= OnMediaPlayerPaused;
            mediaPlayer.Stopped -= OnMediaPlayerStopped;
            mediaPlayer.EncounteredError -= OnMediaPlayerEncounteredError;
            mediaPlayer.TimeChanged -= OnMediaPlayerTimeChanged;
            mediaPlayer.PositionChanged -= OnMediaPlayerPositionChanged;
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
            LibVlcRuntime.EnsureInitialized();
            _libVlc = new LibVLC("--quiet");
            MediaPlayer = new MediaPlayer(_libVlc);
            MediaPlayer.Playing += OnMediaPlayerPlaying;
            MediaPlayer.EndReached += OnMediaPlayerEndReached;
            MediaPlayer.Paused += OnMediaPlayerPaused;
            MediaPlayer.Stopped += OnMediaPlayerStopped;
            MediaPlayer.EncounteredError += OnMediaPlayerEncounteredError;
            MediaPlayer.TimeChanged += OnMediaPlayerTimeChanged;
            MediaPlayer.PositionChanged += OnMediaPlayerPositionChanged;
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

        TryFinalizeSeekPlaybackRestoreIfReady();
        UpdatePlaybackState();

        if (DurationSeconds <= 0 && MediaPlayer.Length > 0)
            DurationSeconds = MediaPlayer.Length / 1000d;

        if (ShouldIgnorePlayerTimelineUpdate(
                _isSeekInteractionActive,
                _pendingSeekPreviewTarget is not null))
            return;

        long currentTime = MediaPlayer.Time;
        if (currentTime >= 0)
            SetTimelineFromPlayer(TimeSpan.FromMilliseconds(currentTime));
    }

    private void UpdatePlaybackState()
    {
        bool mediaPlayerIsPlaying = IsPlayerActivelyPlaying(MediaPlayer);
        IsPlaying = ShouldExposePlayingState(
            mediaPlayerIsPlaying,
            _isPrimingInitialFrame,
            _isResettingToFirstFrame,
            IsSeekPreviewPlaybackManaged(),
            _resumePlaybackAfterSeek);
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
            _volumeBeforeInitialFramePrime = MediaPlayer.Volume;
            MediaPlayer.Volume = 0;
            MediaPlayer.Mute = true;
            MediaPlayer.Play();
        }
        catch
        {
            _isPrimingInitialFrame = false;
            _isResettingToFirstFrame = false;

            if (MediaPlayer is not null)
            {
                MediaPlayer.Volume = _volumeBeforeInitialFramePrime;
                MediaPlayer.Mute = _muteBeforeInitialFramePrime;
            }
        }
    }

    private void OnMediaPlayerPlaying(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            HandleMediaPlayerPlaying();
        }
        else
        {
            Dispatcher.UIThread.Post(HandleMediaPlayerPlaying);
        }
    }

    private void HandleMediaPlayerPlaying()
    {
        if (_isSeekPlaybackRestorePending)
        {
            _isSeekPlaybackRestorePending = false;
            _resumePlaybackAfterSeek = false;
            UpdatePlaybackState();
        }

        if (_isPrimingInitialFrame)
            CompleteInitialFramePrime();
    }

    private void OnMediaPlayerPaused(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(HandlePausedSeekRefreshPlayerStateChanged);
    }

    private void OnMediaPlayerStopped(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(CancelPendingPausedSeekRefresh);
    }

    private void OnMediaPlayerEncounteredError(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(CancelPendingPausedSeekRefresh);
    }

    private void OnMediaPlayerTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
    {
        if (_pendingPausedSeekRefreshTarget is not { } targetPosition)
            return;

        if (!IsSeekTargetReached(e.Time, ToPlayerMilliseconds(targetPosition, DurationSeconds)))
            return;

        Dispatcher.UIThread.Post(CancelPendingPausedSeekRefresh);
    }

    private void OnMediaPlayerPositionChanged(object? sender, MediaPlayerPositionChangedEventArgs e)
    {
        if (_pendingPausedSeekRefreshTarget is not { } targetPosition)
            return;

        if (!IsSeekPositionReached(e.Position, targetPosition, DurationSeconds))
            return;

        Dispatcher.UIThread.Post(CancelPendingPausedSeekRefresh);
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
            _isResettingToFirstFrame = false;
            MediaPlayer.Volume = _volumeBeforeInitialFramePrime;
            MediaPlayer.Mute = _muteBeforeInitialFramePrime;
            ApplyAudioSettings();
            UpdatePlaybackState();
        }
    }

    private void ResetAfterPlaybackEnded()
    {
        if (MediaPlayer is null || !HasMedia)
            return;

        ResetToFirstFrame();
    }

    private void RestartPlaybackFromBeginningAndPlay()
    {
        if (MediaPlayer is null || !HasMedia)
            return;

        CancelPendingPausedSeekRefresh();
        CancelPendingSeekInteraction();
        _isPrimingInitialFrame = false;
        _isResettingToFirstFrame = false;

        try
        {
            MediaPlayer.Stop();
        }
        catch
        {
        }

        SetTimelineFromPlayer(TimeSpan.Zero);
        MediaPlayer.Play();
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

        SeekToPlayerCore(ClampPosition(position));
    }

    private void ReleaseMedia()
    {
        if (_mediaPlayer is not null)
            _mediaPlayer.Media = null;

        _media?.Dispose();
        _media = null;
    }

    private void DetachCurrentMedia()
    {
        if (_mediaPlayer is not null)
        {
            try
            {
                _mediaPlayer.SetPause(true);
            }
            catch
            {
            }
        }

        ReleaseMedia();
    }

    private static double Clamp(double value, double min, double max)
    {
        if (value < min)
            return min;

        return value > max ? max : value;
    }

    private void QueueSeekDuringInteraction(TimeSpan position)
    {
        if (MediaPlayer is null || !_isSeekInteractionActive)
            return;

        _pendingSeekPreviewTarget = position;
        if (!_seekPreviewTimer.IsEnabled)
        {
            _seekPreviewTimer.Start();
            DispatchPendingSeekPreview();
        }
    }

    private void CancelPendingSeekInteraction()
    {
        _seekPreviewTimer.Stop();
        _isSeekInteractionActive = false;
        _isSeekPlaybackRestorePending = false;
        _resumePlaybackAfterSeek = false;
        _pendingSeekPreviewTarget = null;
        _lastRequestedSeekPreviewTarget = null;
        _lastSeekPreviewDispatchAt = DateTimeOffset.MinValue;
        UpdatePlaybackState();
    }

    private void CancelPendingPausedSeekRefresh()
    {
        _pausedSeekRefreshTimer.Stop();
        _pendingPausedSeekRefreshTarget = null;
        _remainingPausedSeekRefreshAttempts = 0;
    }

    private void CommitPendingSeekInteraction()
    {
        if (MediaPlayer is null)
        {
            CancelPendingSeekInteraction();
            return;
        }

        bool shouldResumePlayback = _resumePlaybackAfterSeek;
        TimeSpan targetPosition = ClampPosition(_pendingSeekPreviewTarget ?? CurrentPosition);

        CancelPendingPausedSeekRefresh();
        _isSeekPlaybackRestorePending = shouldResumePlayback;
        _isSeekInteractionActive = false;
        _seekPreviewTimer.Stop();
        _pendingSeekPreviewTarget = null;

        // Always finish a drag with one exact seek so the displayed frame
        // catches up even if preview-mode seeks were dropped by LibVLC.
        SeekToPlayerCore(targetPosition);

        if (shouldResumePlayback)
        {
            TryPlay();
            TryFinalizeSeekPlaybackRestoreIfReady();
        }
        else
        {
            _isSeekPlaybackRestorePending = false;
            _resumePlaybackAfterSeek = false;
            if (IsPlayerActivelyPlaying(MediaPlayer))
                TryPause();

            QueuePausedSeekRefresh(targetPosition);
        }

        _lastRequestedSeekPreviewTarget = null;
        UpdatePlaybackState();
    }

    private void ResetToFirstFrame()
    {
        if (MediaPlayer is null || !HasMedia)
            return;

        CancelPendingPausedSeekRefresh();
        CancelPendingSeekInteraction();
        _isPrimingInitialFrame = false;
        _isResettingToFirstFrame = true;

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

    private TimeSpan ClampPosition(TimeSpan position)
    {
        double maxSeconds = Math.Max(DurationSeconds, 0);
        return TimeSpan.FromSeconds(Clamp(position.TotalSeconds, 0, maxSeconds));
    }

    private void SeekToPlayerCore(TimeSpan position)
    {
        SeekToPlayerCore(position, usePreviewMode: false);
    }

    private void SeekPreviewToPlayerCore(TimeSpan position)
    {
        SeekToPlayerCore(position, usePreviewMode: true);
    }

    private void SeekToPlayerCore(TimeSpan position, bool usePreviewMode)
    {
        if (MediaPlayer is null || !CanSeek)
            return;

        long time = (long)Clamp(position.TotalMilliseconds, 0, DurationSeconds * 1000d);
        if (usePreviewMode && TryPreviewSeekByPosition(time, position))
            return;

        try
        {
            MediaPlayer.Time = time;
        }
        catch
        {
            return;
        }

        SetTimelineFromPlayer(TimeSpan.FromMilliseconds(time));
    }

    private bool TryPreviewSeekByPosition(long targetTimeMilliseconds, TimeSpan targetPosition)
    {
        if (MediaPlayer is null || DurationSeconds <= 0)
            return false;

        double normalized = Clamp(targetPosition.TotalSeconds / DurationSeconds, 0, 1);
        float position = (float)normalized;
        long beforeTime = MediaPlayer.Time;
        try
        {
            MediaPlayer.Position = position;
        }
        catch
        {
            return false;
        }

        long afterTime = MediaPlayer.Time;
        if (!IsPreviewSeekConfirmed(beforeTime, afterTime, targetTimeMilliseconds))
            return false;

        SetTimelineFromPlayer(targetPosition);
        return true;
    }

    private void OnSeekPreviewTimerTick(object? sender, EventArgs e)
    {
        if (!_isSeekInteractionActive)
        {
            _seekPreviewTimer.Stop();
            return;
        }

        DispatchPendingSeekPreview();
        if (_pendingSeekPreviewTarget is not { } pendingTarget)
        {
            _seekPreviewTimer.Stop();
            return;
        }

        if (_lastRequestedSeekPreviewTarget is { } requestedTarget &&
            AreSeekPositionsEquivalent(requestedTarget, pendingTarget))
            _seekPreviewTimer.Stop();
    }

    private void DispatchPendingSeekPreview()
    {
        if (MediaPlayer is null ||
            !_isSeekInteractionActive ||
            _pendingSeekPreviewTarget is not { } targetPosition)
        {
            return;
        }

        if (_lastRequestedSeekPreviewTarget is { } lastTarget &&
            AreSeekPositionsEquivalent(lastTarget, targetPosition))
        {
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (now - _lastSeekPreviewDispatchAt < GetSeekPreviewDispatchInterval(_resumePlaybackAfterSeek))
            return;

        _lastSeekPreviewDispatchAt = now;
        SeekPreviewToPlayerCore(targetPosition);
        _lastRequestedSeekPreviewTarget = targetPosition;
    }

    private void QueuePausedSeekRefresh(TimeSpan targetPosition)
    {
        _pendingPausedSeekRefreshTarget = targetPosition;
        _remainingPausedSeekRefreshAttempts = PausedSeekRefreshMaxAttempts;
        _pausedSeekRefreshTimer.Stop();
        _pausedSeekRefreshTimer.Start();
    }

    private void HandlePausedSeekRefreshPlayerStateChanged()
    {
        if (_pendingPausedSeekRefreshTarget is not { } targetPosition)
            return;

        if (TryCompletePausedSeekRefresh(targetPosition))
            return;

        _pausedSeekRefreshTimer.Stop();
        _pausedSeekRefreshTimer.Start();
    }

    private void OnPausedSeekRefreshTimerTick(object? sender, EventArgs e)
    {
        _pausedSeekRefreshTimer.Stop();

        if (_pendingPausedSeekRefreshTarget is not { } targetPosition)
            return;

        if (MediaPlayer is null)
            return;

        if (TryCompletePausedSeekRefresh(targetPosition))
            return;

        if (!ShouldApplyPausedSeekRefresh(
                _isSeekInteractionActive,
                _resumePlaybackAfterSeek,
                _isSeekPlaybackRestorePending,
                IsPlayerActivelyPlaying(MediaPlayer)))
        {
            CancelPendingPausedSeekRefresh();
            return;
        }

        if (_remainingPausedSeekRefreshAttempts <= 0)
        {
            CancelPendingPausedSeekRefresh();
            return;
        }

        _remainingPausedSeekRefreshAttempts--;
        SeekToPlayerCore(targetPosition);
        TryPause();
        _pausedSeekRefreshTimer.Start();
    }

    private bool TryCompletePausedSeekRefresh(TimeSpan targetPosition)
    {
        if (MediaPlayer is null)
            return false;

        long targetTimeMilliseconds = ToPlayerMilliseconds(targetPosition, DurationSeconds);
        bool targetReached =
            IsSeekTargetReached(MediaPlayer.Time, targetTimeMilliseconds) ||
            IsSeekPositionReached(MediaPlayer.Position, targetPosition, DurationSeconds);

        if (!targetReached)
            return false;

        CancelPendingPausedSeekRefresh();
        return true;
    }

    private void TryFinalizeSeekPlaybackRestoreIfReady()
    {
        if (!_isSeekPlaybackRestorePending || MediaPlayer is null)
            return;

        if (!IsPlayerActivelyPlaying(MediaPlayer))
            return;

        _isSeekPlaybackRestorePending = false;
        _resumePlaybackAfterSeek = false;
    }

    private void TryPause()
    {
        if (MediaPlayer is null)
            return;

        try
        {
            MediaPlayer.SetPause(true);
        }
        catch
        {
            if (!ShouldUsePauseToggleFallback(IsPlayerActivelyPlaying(MediaPlayer)))
                return;

            try
            {
                MediaPlayer.Pause();
            }
            catch
            {
            }
        }
    }

    private void TryPlay()
    {
        if (MediaPlayer is null)
            return;

        try
        {
            MediaPlayer.Play();
        }
        catch
        {
        }
    }

    private bool ShouldResumePlaybackAfterSeekInteraction() =>
        IsPlaying || IsPlayerActivelyPlaying(MediaPlayer);

    private bool HasPendingSeekPreviewState() =>
        _isSeekInteractionActive ||
        _pendingSeekPreviewTarget is not null;

    private bool IsSeekPreviewPlaybackManaged() =>
        _isSeekInteractionActive || _isSeekPlaybackRestorePending;

    private static bool IsPlayerActivelyPlaying(MediaPlayer? mediaPlayer)
    {
        if (mediaPlayer is null)
            return false;

        return mediaPlayer.State is VLCState.Playing or VLCState.Buffering or VLCState.Opening;
    }

    private static bool AreSeekPositionsEquivalent(TimeSpan left, TimeSpan right) =>
        Math.Abs((left - right).TotalMilliseconds) <= SeekPreviewNoOpToleranceMilliseconds;

    private static bool IsSeekPositionReached(float currentPosition, TimeSpan targetPosition, double durationSeconds)
    {
        if (durationSeconds <= 0 || float.IsNaN(currentPosition))
            return false;

        double normalizedTarget = Clamp(targetPosition.TotalSeconds / durationSeconds, 0, 1);
        double normalizedTolerance = SeekPreviewNoOpToleranceMilliseconds / Math.Max(durationSeconds * 1000d, 1d);
        return Math.Abs(currentPosition - normalizedTarget) <= normalizedTolerance;
    }

    private static bool IsSeekTargetReached(long currentTimeMilliseconds, long targetTimeMilliseconds) =>
        currentTimeMilliseconds >= 0 &&
        Math.Abs(currentTimeMilliseconds - targetTimeMilliseconds) <= SeekPreviewNoOpToleranceMilliseconds;

    private static long ToPlayerMilliseconds(TimeSpan position, double durationSeconds) =>
        (long)Clamp(position.TotalMilliseconds, 0, durationSeconds * 1000d);

    private static TimeSpan GetSeekPreviewDispatchInterval(bool resumePlaybackAfterSeek) =>
        resumePlaybackAfterSeek
            ? SeekPreviewPlayingDispatchInterval
            : SeekPreviewPausedDispatchInterval;

    private static bool ShouldUsePauseToggleFallback(bool mediaPlayerIsPlaying) =>
        mediaPlayerIsPlaying;

    private static bool ShouldApplyPausedSeekRefresh(
        bool isSeekInteractionActive,
        bool resumePlaybackAfterSeek,
        bool isSeekPlaybackRestorePending,
        bool mediaPlayerIsPlaying) =>
        !isSeekInteractionActive &&
        !resumePlaybackAfterSeek &&
        !isSeekPlaybackRestorePending &&
        !mediaPlayerIsPlaying;

    private static bool IsPreviewSeekConfirmed(
        long beforeTime,
        long afterTime,
        long targetTimeMilliseconds)
    {
        bool targetAlreadyCurrent =
            beforeTime >= 0 &&
            Math.Abs(beforeTime - targetTimeMilliseconds) <= SeekPreviewNoOpToleranceMilliseconds;

        if (targetAlreadyCurrent)
            return true;

        if (afterTime < 0)
            return false;

        return Math.Abs(afterTime - beforeTime) > SeekPreviewNoOpToleranceMilliseconds;
    }

    private static bool ShouldIgnorePlayerTimelineUpdate(
        bool isSeekInteractionActive,
        bool pendingSeekPreviewTarget) =>
        isSeekInteractionActive ||
        pendingSeekPreviewTarget;

    private static string FormatTime(TimeSpan value) =>
        value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss")
            : value.ToString(@"m\:ss");

    private static bool ShouldExposePlayingState(
        bool mediaPlayerIsPlaying,
        bool isPrimingInitialFrame,
        bool isResettingToFirstFrame,
        bool isSeekPreviewPlaybackManaged,
        bool resumePlaybackAfterSeek)
    {
        if (isPrimingInitialFrame || isResettingToFirstFrame)
            return false;

        if (isSeekPreviewPlaybackManaged)
            return resumePlaybackAfterSeek;

        return mediaPlayerIsPlaying;
    }

    private static bool IsAtPlaybackEndPosition(double timelineSeconds, double durationSeconds) =>
        durationSeconds > 0 &&
        timelineSeconds >= Math.Max(0, durationSeconds - PlaybackEndRestartThresholdSeconds);
}
