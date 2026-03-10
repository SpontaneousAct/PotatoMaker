using CommunityToolkit.Mvvm.Input;
using Avalonia.Media.Imaging;
using PotatoMaker.Core;

namespace PotatoMaker.GUI.ViewModels;

/// <summary>
/// Holds the selected clip range within the loaded source file.
/// </summary>
public partial class ClipRangeViewModel : ViewModelBase
{
    private const double MinimumGapSeconds = 0.1;

    private double _maximumSeconds;
    private double _startSeconds;
    private double _endSeconds;
    private bool _hasDuration;
    private bool _suppressSelectionChanged;
    private Bitmap? _startPreviewImage;
    private Bitmap? _endPreviewImage;
    private bool _isStartPreviewLoading;
    private bool _isEndPreviewLoading;
    private string? _startPreviewMessage;
    private string? _endPreviewMessage;

    public event Action<ClipPreviewTarget>? SelectionChanged;

    public event Action<ClipPreviewTarget>? PreviewCommitRequested;

    public double MaximumSeconds
    {
        get => _maximumSeconds;
        private set => SetProperty(ref _maximumSeconds, value);
    }

    public double StartSeconds
    {
        get => _startSeconds;
        set => SetRange(value, _endSeconds);
    }

    public double EndSeconds
    {
        get => _endSeconds;
        set => SetRange(_startSeconds, value);
    }

    public bool HasDuration
    {
        get => _hasDuration;
        private set => SetProperty(ref _hasDuration, value);
    }

    public bool IsTrimmed => HasDuration && (Start > TimeSpan.Zero || End < SourceDuration);

    public bool CanResetSelection => HasDuration && IsTrimmed;

    public TimeSpan SourceDuration { get; private set; }

    public TimeSpan Start => TimeSpan.FromSeconds(StartSeconds);

    public TimeSpan End => TimeSpan.FromSeconds(EndSeconds);

    public TimeSpan SelectedDuration => End - Start;

    public VideoClipRange Selection => new(Start, End);

    public string StartDisplay => FormatTime(Start);

    public string EndDisplay => FormatTime(End);

    public string SelectedDurationDisplay => FormatTime(SelectedDuration);

    public string RangeSummary => HasDuration
        ? $"{StartDisplay} - {EndDisplay}"
        : "Load a video to choose a clip.";

    public Bitmap? StartPreviewImage
    {
        get => _startPreviewImage;
        private set
        {
            Bitmap? previous = _startPreviewImage;
            if (SetProperty(ref _startPreviewImage, value))
                previous?.Dispose();
        }
    }

    public Bitmap? EndPreviewImage
    {
        get => _endPreviewImage;
        private set
        {
            Bitmap? previous = _endPreviewImage;
            if (SetProperty(ref _endPreviewImage, value))
                previous?.Dispose();
        }
    }

    public bool IsStartPreviewLoading
    {
        get => _isStartPreviewLoading;
        private set => SetProperty(ref _isStartPreviewLoading, value);
    }

    public bool IsEndPreviewLoading
    {
        get => _isEndPreviewLoading;
        private set => SetProperty(ref _isEndPreviewLoading, value);
    }

    public string? StartPreviewMessage
    {
        get => _startPreviewMessage;
        private set => SetProperty(ref _startPreviewMessage, value);
    }

    public string? EndPreviewMessage
    {
        get => _endPreviewMessage;
        private set => SetProperty(ref _endPreviewMessage, value);
    }

    public bool HasStartPreview => StartPreviewImage is not null;

    public bool HasEndPreview => EndPreviewImage is not null;

    [RelayCommand(CanExecute = nameof(CanResetSelection))]
    private void ResetSelection()
    {
        if (!HasDuration)
            return;

        SetRange(0, MaximumSeconds);
    }

    public void SetSourceDuration(TimeSpan duration)
    {
        SourceDuration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        MaximumSeconds = SourceDuration.TotalSeconds;
        HasDuration = SourceDuration > TimeSpan.Zero;

        _suppressSelectionChanged = true;
        try
        {
            SetRangeCore(0, MaximumSeconds);
        }
        finally
        {
            _suppressSelectionChanged = false;
        }

        NotifyDerivedProperties();
    }

    public void Clear()
    {
        SourceDuration = TimeSpan.Zero;
        MaximumSeconds = 0;
        HasDuration = false;

        _suppressSelectionChanged = true;
        try
        {
            SetRangeCore(0, 0);
        }
        finally
        {
            _suppressSelectionChanged = false;
        }

        NotifyDerivedProperties();
        SetPreviewLoading(ClipPreviewTarget.Start, false);
        SetPreviewLoading(ClipPreviewTarget.End, false);
        SetPreview(ClipPreviewTarget.Start, null, "Load a video to see a preview frame.");
        SetPreview(ClipPreviewTarget.End, null, "Load a video to see a preview frame.");
    }

    public void RequestPreviewCommit(ClipPreviewTarget target)
    {
        if (target is ClipPreviewTarget.None)
            return;

        PreviewCommitRequested?.Invoke(target);
    }

    public TimeSpan ResolvePreviewPosition(ClipPreviewTarget target)
    {
        TimeSpan basePosition = target == ClipPreviewTarget.End ? End : Start;
        if (SourceDuration <= TimeSpan.Zero)
            return TimeSpan.Zero;

        TimeSpan maxPreviewPosition = SourceDuration - TimeSpan.FromMilliseconds(100);
        if (maxPreviewPosition < TimeSpan.Zero)
            maxPreviewPosition = TimeSpan.Zero;

        return basePosition > maxPreviewPosition ? maxPreviewPosition : basePosition;
    }

    public void SetPreviewLoading(ClipPreviewTarget target, bool isLoading)
    {
        switch (target)
        {
            case ClipPreviewTarget.Start:
                IsStartPreviewLoading = isLoading;
                if (isLoading)
                    StartPreviewMessage = "Updating preview...";
                break;
            case ClipPreviewTarget.End:
                IsEndPreviewLoading = isLoading;
                if (isLoading)
                    EndPreviewMessage = "Updating preview...";
                break;
        }
    }

    public void SetPreview(ClipPreviewTarget target, Bitmap? image, string? message = null)
    {
        switch (target)
        {
            case ClipPreviewTarget.Start:
                IsStartPreviewLoading = false;
                StartPreviewImage = image;
                StartPreviewMessage = message ?? (image is null ? "Preview unavailable." : null);
                OnPropertyChanged(nameof(HasStartPreview));
                break;
            case ClipPreviewTarget.End:
                IsEndPreviewLoading = false;
                EndPreviewImage = image;
                EndPreviewMessage = message ?? (image is null ? "Preview unavailable." : null);
                OnPropertyChanged(nameof(HasEndPreview));
                break;
            default:
                image?.Dispose();
                break;
        }
    }

    private void SetRange(double startSeconds, double endSeconds)
    {
        double previousStart = _startSeconds;
        double previousEnd = _endSeconds;
        var normalized = Normalize(startSeconds, endSeconds);
        if (Math.Abs(normalized.Start - previousStart) < double.Epsilon &&
            Math.Abs(normalized.End - previousEnd) < double.Epsilon)
        {
            return;
        }

        SetRangeCore(normalized.Start, normalized.End);
        NotifyDerivedProperties();

        if (!_suppressSelectionChanged)
        {
            ClipPreviewTarget changedTargets = ClipPreviewTarget.None;
            if (Math.Abs(normalized.Start - previousStart) >= double.Epsilon)
                changedTargets |= ClipPreviewTarget.Start;
            if (Math.Abs(normalized.End - previousEnd) >= double.Epsilon)
                changedTargets |= ClipPreviewTarget.End;

            SelectionChanged?.Invoke(changedTargets);
        }
    }

    private void SetRangeCore(double startSeconds, double endSeconds)
    {
        SetProperty(ref _startSeconds, startSeconds, nameof(StartSeconds));
        SetProperty(ref _endSeconds, endSeconds, nameof(EndSeconds));
    }

    private (double Start, double End) Normalize(double startSeconds, double endSeconds)
    {
        double max = Math.Max(0, MaximumSeconds);
        double normalizedStart = Clamp(startSeconds, 0, max);
        double normalizedEnd = Clamp(endSeconds, 0, max);

        if (max > 0)
        {
            double gap = Math.Min(MinimumGapSeconds, max);
            if (normalizedEnd - normalizedStart < gap)
            {
                if (startSeconds != _startSeconds)
                {
                    normalizedEnd = Math.Min(max, normalizedStart + gap);
                }
                else
                {
                    normalizedStart = Math.Max(0, normalizedEnd - gap);
                }
            }
        }
        else
        {
            normalizedStart = 0;
            normalizedEnd = 0;
        }

        return (normalizedStart, normalizedEnd);
    }

    private void NotifyDerivedProperties()
    {
        OnPropertyChanged(nameof(Start));
        OnPropertyChanged(nameof(End));
        OnPropertyChanged(nameof(SelectedDuration));
        OnPropertyChanged(nameof(StartDisplay));
        OnPropertyChanged(nameof(EndDisplay));
        OnPropertyChanged(nameof(SelectedDurationDisplay));
        OnPropertyChanged(nameof(RangeSummary));
        OnPropertyChanged(nameof(IsTrimmed));
        OnPropertyChanged(nameof(CanResetSelection));
        OnPropertyChanged(nameof(Selection));
        ResetSelectionCommand.NotifyCanExecuteChanged();
    }

    private static double Clamp(double value, double min, double max)
    {
        if (value < min)
            return min;

        return value > max ? max : value;
    }

    private static string FormatTime(TimeSpan value) =>
        value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss\.f")
            : value.ToString(@"m\:ss\.f");
}
