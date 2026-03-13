using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PotatoMaker.Core;
using PotatoMaker.GUI.ViewModels;

namespace PotatoMaker.GUI.Views;

/// <summary>
/// Displays embedded playback controls for the selected source file.
/// </summary>
public partial class VideoPlayerView : UserControl
{
    private const double TrackInset = 14;

    private EncodeWorkspaceViewModel? _workspace;
    private readonly Canvas _timelineCanvas;
    private readonly Border _trackBackground;
    private readonly Border _timelineProgressBar;
    private readonly Border _trimRangeBar;
    private readonly Border _trimStartStem;
    private readonly Border _trimEndStem;
    private readonly Border _trimStartHandle;
    private readonly Border _trimEndHandle;
    private readonly Border _timelineThumb;
    private readonly Border _playerDropZone;
    private bool _deferredPlayerInitializationQueued;
    private bool _isLoaded;
    private DragTarget _activeDragTarget;

    public VideoPlayerView()
    {
        InitializeComponent();

        _timelineCanvas = this.FindControl<Canvas>("PlaybackTimelineCanvas")!;
        _trackBackground = this.FindControl<Border>("TimelineTrackBackground")!;
        _timelineProgressBar = this.FindControl<Border>("TimelineProgressBar")!;
        _trimRangeBar = this.FindControl<Border>("TrimRangeBar")!;
        _trimStartStem = this.FindControl<Border>("TrimStartStem")!;
        _trimEndStem = this.FindControl<Border>("TrimEndStem")!;
        _trimStartHandle = this.FindControl<Border>("TrimStartHandle")!;
        _trimEndHandle = this.FindControl<Border>("TrimEndHandle")!;
        _timelineThumb = this.FindControl<Border>("TimelineThumb")!;
        _playerDropZone = this.FindControl<Border>("PlayerDropZone")!;

        _timelineCanvas.PointerPressed += OnTimelineCanvasPressed;
        _timelineCanvas.PointerMoved += OnTimelineCanvasPointerMoved;
        _timelineCanvas.PointerReleased += OnTimelinePointerReleased;
        _timelineCanvas.PointerCaptureLost += OnTimelinePointerCaptureLost;
        _timelineCanvas.PropertyChanged += OnTimelineCanvasPropertyChanged;
        _timelineThumb.PointerPressed += OnTimelineThumbPressed;
        _timelineThumb.PointerReleased += OnTimelinePointerReleased;
        _timelineThumb.PointerCaptureLost += OnTimelinePointerCaptureLost;
        _trimStartHandle.PointerPressed += OnTrimStartPressed;
        _trimStartHandle.PointerReleased += OnTimelinePointerReleased;
        _trimStartHandle.PointerCaptureLost += OnTimelinePointerCaptureLost;
        _trimEndHandle.PointerPressed += OnTrimEndPressed;
        _trimEndHandle.PointerReleased += OnTimelinePointerReleased;
        _trimEndHandle.PointerCaptureLost += OnTimelinePointerCaptureLost;
        _playerDropZone.AddHandler(DragDrop.DropEvent, OnDrop);
        _playerDropZone.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        _playerDropZone.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);

        UpdateTimelineVisuals();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        DetachFilePickerHandler(_workspace);
        Unsubscribe(_workspace);
        _workspace = DataContext as EncodeWorkspaceViewModel;
        Subscribe(_workspace);
        AttachFilePickerHandler(_workspace);
        RequestDeferredPlayerInitialization();
        UpdateTimelineVisuals();
        base.OnDataContextChanged(e);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _isLoaded = true;
        AttachFilePickerHandler(_workspace);
        RequestDeferredPlayerInitialization();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        _isLoaded = false;
        base.OnUnloaded(e);
        DetachFilePickerHandler(_workspace);
    }

    private void OnTimelineCanvasPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_workspace?.VideoPlayer.CanSeek != true)
            return;

        _activeDragTarget = DragTarget.Playback;
        _workspace.VideoPlayer.BeginSeekInteraction();
        e.Pointer.Capture(_timelineCanvas);
        SeekPlaybackToPointer(e.GetPosition(_timelineCanvas).X);
        e.Handled = true;
    }

    private void OnTimelineThumbPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_workspace?.VideoPlayer.CanSeek != true)
            return;

        _activeDragTarget = DragTarget.Playback;
        _workspace.VideoPlayer.BeginSeekInteraction();
        e.Pointer.Capture(_timelineThumb);
        SeekPlaybackToPointer(e.GetPosition(_timelineCanvas).X);
        e.Handled = true;
    }

    private void OnTrimStartPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginTrimDrag(e, DragTarget.TrimStart, _trimStartHandle);
    }

    private void OnTrimEndPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginTrimDrag(e, DragTarget.TrimEnd, _trimEndHandle);
    }

    private void BeginTrimDrag(PointerPressedEventArgs e, DragTarget target, IInputElement captureTarget)
    {
        if (_workspace?.ClipRange.HasDuration != true)
            return;

        _activeDragTarget = target;
        _workspace.BeginTrimBoundaryPreview();
        e.Pointer.Capture(captureTarget);
        UpdateTrimBoundaryFromPointer(e.GetPosition(_timelineCanvas).X);
        e.Handled = true;
    }

    private void OnTimelineCanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_activeDragTarget == DragTarget.None)
            return;

        double pointerX = e.GetPosition(_timelineCanvas).X;
        if (_activeDragTarget == DragTarget.Playback)
        {
            SeekPlaybackToPointer(pointerX);
        }
        else
        {
            UpdateTrimBoundaryFromPointer(pointerX);
        }

        e.Handled = true;
    }

    private void OnTimelinePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        EndActiveDrag(e.Pointer);
        e.Handled = true;
    }

    private void OnTimelinePointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        EndActiveDrag(e.Pointer);
    }

    private void Subscribe(EncodeWorkspaceViewModel? workspace)
    {
        if (workspace is null)
            return;

        workspace.ClipRange.PropertyChanged += OnObservedPropertyChanged;
        workspace.VideoPlayer.PropertyChanged += OnObservedPropertyChanged;
    }

    private void Unsubscribe(EncodeWorkspaceViewModel? workspace)
    {
        if (workspace is null)
            return;

        workspace.ClipRange.PropertyChanged -= OnObservedPropertyChanged;
        workspace.VideoPlayer.PropertyChanged -= OnObservedPropertyChanged;
    }

    private void AttachFilePickerHandler(EncodeWorkspaceViewModel? workspace)
    {
        if (workspace is not null)
            workspace.FileInput.FilePickerRequested = OpenFilePickerAsync;
    }

    private void DetachFilePickerHandler(EncodeWorkspaceViewModel? workspace)
    {
        if (workspace?.FileInput.FilePickerRequested == OpenFilePickerAsync)
            workspace.FileInput.FilePickerRequested = null;
    }

    private void RequestDeferredPlayerInitialization()
    {
        if (!_isLoaded || _workspace is null || _deferredPlayerInitializationQueued)
            return;

        _deferredPlayerInitializationQueued = true;

        Dispatcher.UIThread.Post(() =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                _deferredPlayerInitializationQueued = false;

                if (_isLoaded)
                    _workspace?.VideoPlayer.EnsureInitialized();
            }, DispatcherPriority.Render);
        }, DispatcherPriority.Background);
    }

    private void OnObservedPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ClipRangeViewModel.StartSeconds)
            or nameof(ClipRangeViewModel.EndSeconds)
            or nameof(ClipRangeViewModel.MaximumSeconds)
            or nameof(ClipRangeViewModel.HasDuration)
            or nameof(VideoPlayerViewModel.DurationSeconds)
            or nameof(VideoPlayerViewModel.TimelineSeconds)
            or nameof(VideoPlayerViewModel.CanSeek))
        {
            UpdateTimelineVisuals();
        }
    }

    private void OnTimelineCanvasPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == BoundsProperty)
            UpdateTimelineVisuals();
    }

    private void UpdateTimelineVisuals()
    {
        double width = _timelineCanvas.Bounds.Width;
        double duration = _workspace?.VideoPlayer.DurationSeconds ?? 0;
        bool canRenderTimeline = width > 0;
        bool hasDuration = duration > 0;
        bool hasTrimRange = _workspace?.ClipRange.HasDuration == true && (_workspace?.ClipRange.MaximumSeconds ?? 0) > 0;

        _timelineCanvas.IsEnabled = _workspace?.VideoPlayer.CanSeek == true;
        if (!canRenderTimeline)
            return;

        double trackLeft = TrackInset;
        double trackWidth = Math.Max(1, width - (TrackInset * 2));
        double trackRight = trackLeft + trackWidth;

        Canvas.SetLeft(_trackBackground, trackLeft);
        _trackBackground.Width = trackWidth;

        double currentSeconds = _workspace?.VideoPlayer.TimelineSeconds ?? 0;
        double currentX = hasDuration
            ? trackLeft + (trackWidth * Clamp(currentSeconds / duration, 0, 1))
            : trackLeft;

        Canvas.SetLeft(_timelineProgressBar, trackLeft);
        _timelineProgressBar.Width = Math.Max(0, currentX - trackLeft);
        Canvas.SetLeft(_timelineThumb, Clamp(currentX - (_timelineThumb.Width / 2), trackLeft - (_timelineThumb.Width / 2), trackRight - (_timelineThumb.Width / 2)));
        _timelineThumb.IsVisible = hasDuration;

        _trimRangeBar.IsVisible = hasTrimRange;
        _trimStartStem.IsVisible = hasTrimRange;
        _trimEndStem.IsVisible = hasTrimRange;
        _trimStartHandle.IsVisible = hasTrimRange;
        _trimEndHandle.IsVisible = hasTrimRange;

        if (!hasTrimRange)
            return;

        double trimDuration = _workspace!.ClipRange.MaximumSeconds;
        double startX = trackLeft + (trackWidth * Clamp(_workspace.ClipRange.StartSeconds / trimDuration, 0, 1));
        double endX = trackLeft + (trackWidth * Clamp(_workspace.ClipRange.EndSeconds / trimDuration, 0, 1));
        double rangeLeft = Math.Min(startX, endX);
        double rangeWidth = Math.Max(4, Math.Abs(endX - startX));

        Canvas.SetLeft(_trimRangeBar, Clamp(rangeLeft, trackLeft, trackRight));
        _trimRangeBar.Width = Math.Min(rangeWidth, Math.Max(4, trackRight - rangeLeft));
        Canvas.SetLeft(_trimStartStem, Clamp(startX - (_trimStartStem.Width / 2), 0, Math.Max(0, width - _trimStartStem.Width)));
        Canvas.SetLeft(_trimEndStem, Clamp(endX - (_trimEndStem.Width / 2), 0, Math.Max(0, width - _trimEndStem.Width)));
        Canvas.SetLeft(_trimStartHandle, Clamp(startX - (_trimStartHandle.Width / 2), 0, Math.Max(0, width - _trimStartHandle.Width)));
        Canvas.SetLeft(_trimEndHandle, Clamp(endX - (_trimEndHandle.Width / 2), 0, Math.Max(0, width - _trimEndHandle.Width)));
    }

    private void SeekPlaybackToPointer(double pointerX)
    {
        if (_workspace?.VideoPlayer.CanSeek != true)
            return;

        double duration = _workspace.VideoPlayer.DurationSeconds;
        if (duration <= 0)
            return;

        double seconds = duration * NormalizePointer(pointerX);
        _workspace.VideoPlayer.SeekDuringInteraction(TimeSpan.FromSeconds(seconds));
    }

    private void UpdateTrimBoundaryFromPointer(double pointerX)
    {
        if (_workspace?.ClipRange.HasDuration != true)
            return;

        double duration = _workspace.ClipRange.MaximumSeconds;
        if (duration <= 0)
            return;

        ClipBoundary boundary = _activeDragTarget switch
        {
            DragTarget.TrimStart => ClipBoundary.Start,
            DragTarget.TrimEnd => ClipBoundary.End,
            _ => throw new InvalidOperationException("Trim boundary requested without an active trim drag.")
        };

        double seconds = duration * NormalizePointer(pointerX);
        _workspace.PreviewTrimBoundary(boundary, TimeSpan.FromSeconds(seconds));
    }

    private double NormalizePointer(double pointerX)
    {
        double width = _timelineCanvas.Bounds.Width;
        double trackWidth = Math.Max(1, width - (TrackInset * 2));
        return Clamp((pointerX - TrackInset) / trackWidth, 0, 1);
    }

    private void EndActiveDrag(IPointer pointer)
    {
        pointer.Capture(null);

        if (_activeDragTarget == DragTarget.Playback)
        {
            _workspace?.VideoPlayer.EndSeekInteraction();
        }
        else if (_activeDragTarget is DragTarget.TrimStart or DragTarget.TrimEnd)
        {
            _workspace?.EndTrimBoundaryPreview();
        }

        _activeDragTarget = DragTarget.None;
    }

    private async void OpenFilePickerAsync()
    {
        if (_workspace is null || !_workspace.FileInput.CanSelectFile)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select a video file",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Supported video files")
                {
                    Patterns = InputMediaSupport.FileDialogPatterns.ToArray()
                }
            ]
        });

        string? path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null)
            _workspace.FileInput.SetFile(path);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        bool hasSupportedFile = _workspace?.FileInput.CanSelectFile == true &&
            TryGetSingleLocalFilePath(e.DataTransfer, out string? path) &&
            InputMediaSupport.IsSupportedPath(path);

        e.DragEffects = hasSupportedFile
            ? DragDropEffects.Copy
            : DragDropEffects.None;

        if (hasSupportedFile)
            _playerDropZone.Classes.Add("drag-over");
        else
            _playerDropZone.Classes.Remove("drag-over");
    }

    private void OnDragLeave(object? sender, RoutedEventArgs e)
    {
        _playerDropZone.Classes.Remove("drag-over");
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        _playerDropZone.Classes.Remove("drag-over");

        if (_workspace is null)
            return;

        if (!_workspace.FileInput.CanSelectFile)
        {
            _workspace.FileInput.RejectFileSelection(FileInputViewModel.LockedSelectionMessage);
            return;
        }

        if (!TryGetSingleLocalFilePath(e.DataTransfer, out string? path) || path is null)
        {
            _workspace.FileInput.RejectFileSelection("Drop exactly one supported video file.");
            return;
        }

        _workspace.FileInput.SetFile(path);
    }

    private static bool TryGetSingleLocalFilePath(IDataTransfer dataTransfer, out string? path)
    {
        path = null;

        if (!dataTransfer.Contains(DataFormat.File))
            return false;

        var files = dataTransfer.TryGetFiles()?.ToList();
        if (files is null || files.Count != 1)
            return false;

        path = files[0].TryGetLocalPath();
        return path is not null;
    }

    private static double Clamp(double value, double min, double max)
    {
        if (value < min)
            return min;

        return value > max ? max : value;
    }

    private enum DragTarget
    {
        None,
        Playback,
        TrimStart,
        TrimEnd
    }
}
