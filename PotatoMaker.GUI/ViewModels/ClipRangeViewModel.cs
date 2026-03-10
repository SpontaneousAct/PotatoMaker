using CommunityToolkit.Mvvm.Input;
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

    public event Action? SelectionChanged;

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
    }

    public void SetBoundary(ClipBoundary boundary, TimeSpan position)
    {
        if (!HasDuration)
            return;

        double seconds = Clamp(position.TotalSeconds, 0, MaximumSeconds);
        if (boundary == ClipBoundary.Start)
            SetRange(seconds, _endSeconds);
        else
            SetRange(_startSeconds, seconds);
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
            SelectionChanged?.Invoke();
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
