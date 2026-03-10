using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using PotatoMaker.GUI.ViewModels;

namespace PotatoMaker.GUI.Views;

/// <summary>
/// Displays embedded playback controls for the selected source file.
/// </summary>
public partial class VideoPlayerView : UserControl
{
    private EncodeWorkspaceViewModel? _workspace;

    public VideoPlayerView()
    {
        InitializeComponent();

        var timelineSlider = this.FindControl<Slider>("TimelineSlider")!;
        var markerCanvas = this.FindControl<Canvas>("TimelineMarkerCanvas")!;
        timelineSlider.AddHandler(PointerPressedEvent, OnTimelineSeekStarted);
        timelineSlider.AddHandler(PointerReleasedEvent, OnTimelineSeekFinished);
        timelineSlider.AddHandler(PointerCaptureLostEvent, OnTimelineSeekCaptureLost);
        markerCanvas.PropertyChanged += OnMarkerCanvasPropertyChanged;

        UpdateTimelineMarkers();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        Unsubscribe(_workspace);
        _workspace = DataContext as EncodeWorkspaceViewModel;
        Subscribe(_workspace);
        UpdateTimelineMarkers();
        base.OnDataContextChanged(e);
    }

    private void OnTimelineSeekStarted(object? sender, PointerPressedEventArgs e) => _workspace?.VideoPlayer.BeginSeekInteraction();

    private void OnTimelineSeekFinished(object? sender, PointerEventArgs e) => _workspace?.VideoPlayer.EndSeekInteraction();

    private void OnTimelineSeekCaptureLost(object? sender, PointerCaptureLostEventArgs e) => _workspace?.VideoPlayer.EndSeekInteraction();

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

    private void OnObservedPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ClipRangeViewModel.StartSeconds)
            or nameof(ClipRangeViewModel.EndSeconds)
            or nameof(ClipRangeViewModel.MaximumSeconds)
            or nameof(ClipRangeViewModel.HasDuration)
            or nameof(VideoPlayerViewModel.DurationSeconds))
        {
            UpdateTimelineMarkers();
        }
    }

    private void OnMarkerCanvasPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == BoundsProperty)
            UpdateTimelineMarkers();
    }

    private void UpdateTimelineMarkers()
    {
        var markerCanvas = this.FindControl<Canvas>("TimelineMarkerCanvas");
        var selectionRangeBar = this.FindControl<Border>("SelectionRangeBar");
        var startMarker = this.FindControl<Border>("StartMarker");
        var endMarker = this.FindControl<Border>("EndMarker");
        if (markerCanvas is null || selectionRangeBar is null || startMarker is null || endMarker is null)
            return;

        double width = markerCanvas.Bounds.Width;
        double duration = _workspace?.ClipRange.MaximumSeconds ?? 0;
        bool canShowMarkers = _workspace?.ClipRange.HasDuration == true && width > 0 && duration > 0;

        markerCanvas.IsVisible = canShowMarkers;
        if (!canShowMarkers)
            return;

        double startSeconds = _workspace!.ClipRange.StartSeconds;
        double endSeconds = _workspace.ClipRange.EndSeconds;
        double startX = width * Clamp(startSeconds / duration, 0, 1);
        double endX = width * Clamp(endSeconds / duration, 0, 1);

        double rangeLeft = Math.Min(startX, endX);
        double rangeWidth = Math.Max(2, Math.Abs(endX - startX));

        Canvas.SetLeft(startMarker, Clamp(startX - (startMarker.Width / 2), 0, Math.Max(0, width - startMarker.Width)));
        Canvas.SetLeft(endMarker, Clamp(endX - (endMarker.Width / 2), 0, Math.Max(0, width - endMarker.Width)));
        Canvas.SetLeft(selectionRangeBar, Clamp(rangeLeft, 0, width));
        selectionRangeBar.Width = Math.Min(rangeWidth, Math.Max(2, width - rangeLeft));
    }

    private static double Clamp(double value, double min, double max)
    {
        if (value < min)
            return min;

        return value > max ? max : value;
    }
}
