using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using PotatoMaker.Core;
using PotatoMaker.GUI.Diagnostics;

namespace PotatoMaker.GUI.ViewModels;

/// <summary>
/// Owns the embedded media player state for source preview playback.
/// </summary>
public partial class VideoPlayerViewModel : ViewModelBase, IDisposable
{
    private const long SeekPreviewNoOpToleranceMilliseconds = 8;
    private static readonly TimeSpan SeekPreviewDispatchInterval = TimeSpan.FromMilliseconds(16);
    private static readonly TimeSpan SeekPreviewTraceInterval = TimeSpan.FromMilliseconds(350);
    private const double PlaybackEndRestartThresholdSeconds = 0.05;

    private readonly DispatcherTimer _positionTimer;
    private readonly DispatcherTimer _seekPreviewTimer;

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
    private bool _preferCurrentPreviewTargetOnCommit;
    private TimeSpan? _pendingSeekPreviewTarget;
    private TimeSpan? _lastAppliedSeekPreviewTarget;
    private DateTimeOffset _lastSeekPreviewDispatchAt;
    private DateTimeOffset _lastSeekPreviewTraceAt;
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
            Interval = SeekPreviewDispatchInterval
        };
        _seekPreviewTimer.Tick += OnSeekPreviewTimerTick;

        _hasAttemptedPlayerInitialization = initializePlayer;
        _statusMessage = "Select a video to preview it.";
        Trace($"ctor initializePlayer={initializePlayer} logPath={VideoPlayerDiagnostics.LogPath}");

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

        Trace($"TogglePlayback start isPlaying={IsPlaying} playerIsPlaying={MediaPlayer.IsPlaying} timeline={TimelineSeconds:F3}");
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
        Trace($"TogglePlayback end isPlaying={IsPlaying} playerIsPlaying={MediaPlayer.IsPlaying} timeline={TimelineSeconds:F3}");
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

        Trace($"PausePlaybackIfPlaying start isPlaying={IsPlaying} playerIsPlaying={MediaPlayer.IsPlaying}");
        if (!IsPlaying)
            return;

        if (HasPendingSeekPreviewState())
            CommitPendingSeekInteraction();

        TryPause();

        UpdatePlaybackState();
        Trace($"PausePlaybackIfPlaying end isPlaying={IsPlaying} playerIsPlaying={MediaPlayer.IsPlaying}");
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

        _isSeekInteractionActive = true;
        _isSeekPlaybackRestorePending = false;
        _preferCurrentPreviewTargetOnCommit = false;
        _resumePlaybackAfterSeek = ShouldResumePlaybackAfterSeekInteraction();
        _lastAppliedSeekPreviewTarget = null;
        _lastSeekPreviewDispatchAt = DateTimeOffset.MinValue;
        _pendingSeekPreviewTarget = CurrentPosition;
        _seekPreviewTimer.Start();
        TryPause();
        UpdatePlaybackState();
        Trace($"BeginSeekInteraction resumeAfterSeek={_resumePlaybackAfterSeek} timeline={TimelineSeconds:F3}");
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

        _preferCurrentPreviewTargetOnCommit = preferCurrentPreviewTarget;
        Trace($"EndSeekInteraction target={_pendingSeekPreviewTarget?.TotalSeconds:F3} resumeAfterSeek={_resumePlaybackAfterSeek} preferCurrentPreview={preferCurrentPreviewTarget}");
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
            LibVlcRuntime.EnsureInitialized();
            _libVlc = LibVlcRuntime.PackagedPluginsDirectory is { } pluginsDirectory
                ? new LibVLC($"--plugin-path={pluginsDirectory}", "--quiet")
                : new LibVLC("--quiet");
            MediaPlayer = new MediaPlayer(_libVlc);
            MediaPlayer.Playing += OnMediaPlayerPlaying;
            MediaPlayer.EndReached += OnMediaPlayerEndReached;
            ApplyAudioSettings();
            IsPlayerAvailable = true;
            PlayerErrorMessage = null;
            StatusMessage = "Select a video to preview it.";
            Trace("TryInitializePlayer success");
        }
        catch (Exception ex)
        {
            IsPlayerAvailable = false;
            PlayerErrorMessage = ex.Message;
            StatusMessage = "Video player could not be initialized.";
            Trace($"TryInitializePlayer failed error={ex.Message}");
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
        Trace($"OnMediaPlayerPlaying priming={_isPrimingInitialFrame} timeline={TimelineSeconds:F3}");
        if (_isSeekPlaybackRestorePending)
        {
            _isSeekPlaybackRestorePending = false;
            _resumePlaybackAfterSeek = false;
            UpdatePlaybackState();
            Trace("OnMediaPlayerPlaying seekRestoreCompleted");
        }

        if (!_isPrimingInitialFrame)
            return;

        Dispatcher.UIThread.Post(CompleteInitialFramePrime);
    }

    private void OnMediaPlayerEndReached(object? sender, EventArgs e)
    {
        Trace("OnMediaPlayerEndReached");
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
        Trace($"CancelPendingSeekInteraction active={_isSeekInteractionActive} pendingTarget={_pendingSeekPreviewTarget?.TotalSeconds:F3}");
        _seekPreviewTimer.Stop();
        _isSeekInteractionActive = false;
        _isSeekPlaybackRestorePending = false;
        _preferCurrentPreviewTargetOnCommit = false;
        _resumePlaybackAfterSeek = false;
        _pendingSeekPreviewTarget = null;
        _lastAppliedSeekPreviewTarget = null;
        _lastSeekPreviewDispatchAt = DateTimeOffset.MinValue;
        UpdatePlaybackState();
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
        bool preferCurrentPreviewTarget = _preferCurrentPreviewTargetOnCommit;
        bool previewTargetMatchesLastApplied =
            _lastAppliedSeekPreviewTarget is { } lastPreviewTarget &&
            AreSeekPositionsEquivalent(lastPreviewTarget, targetPosition);
        bool previewAlreadyAtTarget =
            previewTargetMatchesLastApplied &&
            (preferCurrentPreviewTarget || !shouldResumePlayback);
        Trace($"CommitPendingSeekInteraction target={targetPosition.TotalSeconds:F3} shouldResume={shouldResumePlayback} preferCurrentPreview={preferCurrentPreviewTarget} previewAtTarget={previewAlreadyAtTarget}");

        _isSeekPlaybackRestorePending = shouldResumePlayback;
        _isSeekInteractionActive = false;
        _seekPreviewTimer.Stop();
        _pendingSeekPreviewTarget = null;

        if (!previewAlreadyAtTarget)
            SeekToPlayerCore(targetPosition);
        else
            SetTimelineFromPlayer(targetPosition);

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
        }

        _lastAppliedSeekPreviewTarget = null;
        _preferCurrentPreviewTargetOnCommit = false;
        UpdatePlaybackState();
        Trace($"CommitPendingSeekInteraction end isPlaying={IsPlaying} playerState={MediaPlayer.State} restorePending={_isSeekPlaybackRestorePending} timeline={TimelineSeconds:F3}");
    }

    private void ResetToFirstFrame()
    {
        if (MediaPlayer is null || !HasMedia)
            return;

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
            Trace($"SeekToPlayerCore failed targetMs={time}");
            return;
        }

        SetTimelineFromPlayer(TimeSpan.FromMilliseconds(time));
        if (!usePreviewMode)
            Trace($"SeekToPlayerCore mode=time targetMs={time} playerTime={MediaPlayer.Time}");
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
            Trace($"SeekToPlayerCore previewPositionFailed targetMs={targetTimeMilliseconds}");
            return false;
        }

        long afterTime = MediaPlayer.Time;
        bool targetAlreadyCurrent =
            beforeTime >= 0 &&
            Math.Abs(beforeTime - targetTimeMilliseconds) <= SeekPreviewNoOpToleranceMilliseconds;

        bool positionSeekMovedPlayback =
            afterTime < 0 ||
            Math.Abs(afterTime - beforeTime) > SeekPreviewNoOpToleranceMilliseconds ||
            targetAlreadyCurrent;

        if (!positionSeekMovedPlayback)
            return false;

        SetTimelineFromPlayer(targetPosition);
        TracePreviewSeek(targetPosition, targetTimeMilliseconds, afterTime, normalized);
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

        if (_lastAppliedSeekPreviewTarget is { } appliedTarget &&
            AreSeekPositionsEquivalent(appliedTarget, pendingTarget))
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

        if (_lastAppliedSeekPreviewTarget is { } lastTarget &&
            AreSeekPositionsEquivalent(lastTarget, targetPosition))
        {
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (now - _lastSeekPreviewDispatchAt < SeekPreviewDispatchInterval)
            return;

        _lastSeekPreviewDispatchAt = now;
        SeekPreviewToPlayerCore(targetPosition);
        _lastAppliedSeekPreviewTarget = targetPosition;
    }

    private void TracePreviewSeek(TimeSpan targetPosition, long targetTimeMilliseconds, long playerTime, double normalized)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (now - _lastSeekPreviewTraceAt < SeekPreviewTraceInterval)
            return;

        _lastSeekPreviewTraceAt = now;
        Trace($"SeekToPlayerCore mode=position targetMs={targetTimeMilliseconds} playerTime={playerTime} normalized={normalized:F6} target={targetPosition.TotalSeconds:F3}");
    }

    private void TryFinalizeSeekPlaybackRestoreIfReady()
    {
        if (!_isSeekPlaybackRestorePending || MediaPlayer is null)
            return;

        if (!IsPlayerActivelyPlaying(MediaPlayer))
            return;

        _isSeekPlaybackRestorePending = false;
        _resumePlaybackAfterSeek = false;
        Trace("TryFinalizeSeekPlaybackRestoreIfReady completed");
    }

    private void TryPause()
    {
        if (MediaPlayer is null)
            return;

        try
        {
            MediaPlayer.SetPause(true);
            Trace($"TryPause via SetPause playerIsPlaying={MediaPlayer.IsPlaying}");
        }
        catch
        {
            try
            {
                MediaPlayer.Pause();
                Trace($"TryPause fallback Pause playerIsPlaying={MediaPlayer.IsPlaying}");
            }
            catch
            {
                Trace("TryPause failed");
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
            Trace($"TryPlay playerIsPlaying={MediaPlayer.IsPlaying}");
        }
        catch
        {
            Trace("TryPlay failed");
        }
    }

    private static void Trace(string message) =>
        VideoPlayerDiagnostics.Log("VideoPlayerVM", message);

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
