using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PotatoMaker.Core;
using PotatoMaker.GUI.Services;

namespace PotatoMaker.GUI.ViewModels;

/// <summary>
/// Represents one row on the compression queue screen.
/// </summary>
public partial class CompressionQueueItemViewModel : ViewModelBase
{
    private const string WaitingInQueueText = "Ready";
    private const double CommonAspectRatioTolerance = 0.02d;
    private static readonly (string Label, double Value)[] CommonAspectRatios =
    [
        ("32:9", 32d / 9d),
        ("21:9", 21d / 9d),
        ("16:9", 16d / 9d),
        ("4:3", 4d / 3d),
        ("1:1", 1d),
        ("3:4", 3d / 4d),
        ("9:16", 9d / 16d)
    ];

    private CancellationTokenSource? _encodeCts;
    private Action<CompressionQueueItemViewModel>? _cancelAction;
    private Func<CompressionQueueItemViewModel, Task>? _restartAction;
    private Func<CompressionQueueItemViewModel, Task>? _removeAction;

    private CompressionQueueItemViewModel(
        string id,
        string inputPath,
        string outputDirectory,
        VideoInfo info,
        StrategyAnalysis strategy,
        EncodeSettings settings,
        VideoClipRange clipRange,
        long selectedSizeBytes,
        CompressionQueueItemStatus status,
        int progressPercent,
        string progressStateText,
        long? outputSizeBytes,
        string? failureMessage,
        DateTimeOffset addedAtUtc)
    {
        Id = id;
        InputPath = Path.GetFullPath(inputPath);
        OutputDirectory = Path.GetFullPath(outputDirectory);
        Info = info;
        Strategy = strategy;
        Settings = settings;
        ClipRange = clipRange.Normalize(info.Duration);
        SelectedSizeBytes = Math.Max(0, selectedSizeBytes);
        AddedAtUtc = addedAtUtc;
        _status = status;
        _progressPercent = Math.Clamp(progressPercent, 0, 100);
        _progressStateText = string.IsNullOrWhiteSpace(progressStateText)
            ? ResolveDefaultProgressState(status, failureMessage)
            : progressStateText;
        _outputSizeBytes = outputSizeBytes;
        _failureMessage = failureMessage;
        PrimaryActionCommand = new AsyncRelayCommand(
            ExecutePrimaryActionAsync,
            CanExecutePrimaryAction,
            AsyncRelayCommandOptions.AllowConcurrentExecutions);
        RemoveCommand = new AsyncRelayCommand(ExecuteRemoveAsync, CanExecuteRemove);
    }

    public string Id { get; }

    public string InputPath { get; }

    public string OutputDirectory { get; }

    public VideoInfo Info { get; }

    public StrategyAnalysis Strategy { get; }

    public EncodeSettings Settings { get; }

    public VideoClipRange ClipRange { get; }

    public long SelectedSizeBytes { get; }

    public DateTimeOffset AddedAtUtc { get; }

    public string FileName => Path.GetFileName(InputPath);

    public string ElapsedText => string.IsNullOrWhiteSpace(ElapsedDisplay)
        ? "--"
        : ElapsedDisplay;

    public string ProgressSummaryText => Status == CompressionQueueItemStatus.Completed
        ? string.IsNullOrWhiteSpace(ElapsedDisplay)
            ? "Done"
            : $"Done in {ElapsedDisplay}"
        : $"{ProgressPercent}%";

    public string ProgressText => Status == CompressionQueueItemStatus.Completed
        ? string.Empty
        : ProgressStateText;

    public bool HasProgressText => !string.IsNullOrWhiteSpace(ProgressText);

    public string OutputFpsText => FormatFrameRate(Strategy.OutputFrameRate);

    public string CropText => FormatCrop(Strategy.CropFilter);

    public string OutputPartsText => Strategy.Plan.Parts.ToString(CultureInfo.InvariantCulture);

    public bool IsWaiting => Status == CompressionQueueItemStatus.Queued;

    public bool IsEncoding => Status == CompressionQueueItemStatus.Encoding;

    public bool IsCompleted => Status == CompressionQueueItemStatus.Completed;

    public bool IsCancelled => Status == CompressionQueueItemStatus.Cancelled;

    public bool IsFailed => Status == CompressionQueueItemStatus.Failed;

    public bool CanCancel => Status == CompressionQueueItemStatus.Encoding;

    public bool CanRestart => Status == CompressionQueueItemStatus.Cancelled;

    public bool CanRemove => Status != CompressionQueueItemStatus.Encoding;

    public bool HasPrimaryAction => CanCancel || CanRestart;

    public string PrimaryActionToolTip => CanCancel
        ? "Cancel encode"
        : CanRestart
            ? "Restart encode"
            : string.Empty;

    public bool BlocksDuplicateEntries => Status is CompressionQueueItemStatus.Queued or CompressionQueueItemStatus.Encoding;

    public bool PersistsAcrossSessions => Status is CompressionQueueItemStatus.Queued or CompressionQueueItemStatus.Encoding;

    public AsyncRelayCommand PrimaryActionCommand { get; }

    public AsyncRelayCommand RemoveCommand { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWaiting))]
    [NotifyPropertyChangedFor(nameof(IsEncoding))]
    [NotifyPropertyChangedFor(nameof(IsCompleted))]
    [NotifyPropertyChangedFor(nameof(IsCancelled))]
    [NotifyPropertyChangedFor(nameof(IsFailed))]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    [NotifyPropertyChangedFor(nameof(CanRestart))]
    [NotifyPropertyChangedFor(nameof(CanRemove))]
    [NotifyPropertyChangedFor(nameof(HasPrimaryAction))]
    [NotifyPropertyChangedFor(nameof(PrimaryActionToolTip))]
    [NotifyPropertyChangedFor(nameof(PersistsAcrossSessions))]
    [NotifyPropertyChangedFor(nameof(ProgressSummaryText))]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    [NotifyPropertyChangedFor(nameof(HasProgressText))]
    private CompressionQueueItemStatus _status;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressSummaryText))]
    private int _progressPercent;

    [ObservableProperty]
    private long? _outputSizeBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    [NotifyPropertyChangedFor(nameof(HasProgressText))]
    private string _progressStateText;

    [ObservableProperty]
    private string? _failureMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ElapsedText))]
    [NotifyPropertyChangedFor(nameof(ProgressSummaryText))]
    private string? _elapsedDisplay;

    public static CompressionQueueItemViewModel Create(QueuedCompressionItemDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        return new CompressionQueueItemViewModel(
            Guid.NewGuid().ToString("N"),
            draft.InputPath,
            draft.OutputDirectory,
            draft.Info,
            draft.Strategy,
            draft.Settings,
            draft.ClipRange,
            draft.SelectedSizeBytes,
            CompressionQueueItemStatus.Queued,
            0,
            WaitingInQueueText,
            null,
            null,
            DateTimeOffset.UtcNow);
    }

    public static CompressionQueueItemViewModel FromRecord(QueuedCompressionItemRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        CompressionQueueItemStatus status = record.Status == CompressionQueueItemStatus.Encoding
            ? CompressionQueueItemStatus.Queued
            : record.Status;
        int progressPercent = status == CompressionQueueItemStatus.Queued
            ? 0
            : Math.Clamp(record.ProgressPercent, 0, 100);
        string progressStateText = status == CompressionQueueItemStatus.Queued
            ? WaitingInQueueText
            : record.ProgressStateText;

        return new CompressionQueueItemViewModel(
            record.Id,
            record.InputPath,
            record.OutputDirectory,
            record.Info,
            record.Strategy,
            record.Settings,
            new VideoClipRange(
                TimeSpan.FromTicks(record.ClipStartTicks),
                TimeSpan.FromTicks(record.ClipEndTicks)),
            record.SelectedSizeBytes,
            status,
            progressPercent,
            progressStateText,
            record.OutputSizeBytes,
            record.FailureMessage,
            record.AddedAtUtc);
    }

    public static string BuildDuplicateKey(QueuedCompressionItemDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        return BuildDuplicateKey(
            draft.InputPath,
            draft.OutputDirectory,
            draft.ClipRange.Normalize(draft.Info.Duration),
            draft.Settings,
            draft.Strategy.CropFilter);
    }

    public string DuplicateKey => BuildDuplicateKey(InputPath, OutputDirectory, ClipRange, Settings, Strategy.CropFilter);

    public EncodeRequest BuildEncodeRequest() =>
        new(
            InputPath,
            OutputDirectory,
            Info,
            Strategy,
            Settings,
            ClipRange);

    public QueuedCompressionItemRecord ToRecord() =>
        new(
            Id,
            InputPath,
            OutputDirectory,
            Info,
            Strategy,
            Settings,
            ClipRange.Start.Ticks,
            ClipRange.End.Ticks,
            SelectedSizeBytes,
            Status == CompressionQueueItemStatus.Encoding
                ? CompressionQueueItemStatus.Queued
                : Status,
            Status == CompressionQueueItemStatus.Encoding ? 0 : ProgressPercent,
            Status == CompressionQueueItemStatus.Encoding ? WaitingInQueueText : ProgressStateText,
            OutputSizeBytes,
            FailureMessage,
            AddedAtUtc);

    public void MarkEncoding()
    {
        FailureMessage = null;
        OutputSizeBytes = null;
        ElapsedDisplay = null;
        ProgressPercent = 0;
        ProgressStateText = "Preparing...";
        Status = CompressionQueueItemStatus.Encoding;
    }

    public void MarkQueued()
    {
        FailureMessage = null;
        OutputSizeBytes = null;
        ElapsedDisplay = null;
        ProgressPercent = 0;
        ProgressStateText = WaitingInQueueText;
        Status = CompressionQueueItemStatus.Queued;
    }

    public void UpdateProgress(EncodeProgress value)
    {
        if (Status != CompressionQueueItemStatus.Encoding)
            return;

        Status = CompressionQueueItemStatus.Encoding;
        ProgressPercent = Math.Clamp(value.Percent, 0, 100);
        ProgressStateText = BuildProgressText(value.Label, ProgressPercent);
    }

    public void MarkCompleted(ProcessingPipelineResult result, TimeSpan? elapsed = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        FailureMessage = null;
        ProgressPercent = 100;
        OutputSizeBytes = result.TotalOutputBytes;
        ElapsedDisplay = elapsed is { } value ? FormatElapsed(value) : null;
        ProgressStateText = "Done";
        Status = CompressionQueueItemStatus.Completed;
    }

    public void MarkCancelled()
    {
        FailureMessage = null;
        ProgressPercent = 0;
        OutputSizeBytes = null;
        ElapsedDisplay = null;
        ProgressStateText = "Cancelled";
        Status = CompressionQueueItemStatus.Cancelled;
    }

    public void MarkFailed(string? message)
    {
        string normalizedMessage = string.IsNullOrWhiteSpace(message)
            ? "Compression failed."
            : message.Trim();

        FailureMessage = normalizedMessage;
        ProgressPercent = 0;
        OutputSizeBytes = null;
        ElapsedDisplay = null;
        ProgressStateText = normalizedMessage;
        Status = CompressionQueueItemStatus.Failed;
    }

    public void CancelActiveEncode() => _encodeCts?.Cancel();

    internal void AttachActions(
        Action<CompressionQueueItemViewModel>? cancelAction,
        Func<CompressionQueueItemViewModel, Task>? restartAction,
        Func<CompressionQueueItemViewModel, Task>? removeAction)
    {
        _cancelAction = cancelAction;
        _restartAction = restartAction;
        _removeAction = removeAction;
        NotifyActionCommandStateChanged();
    }

    internal void AttachCancellationSource(CancellationTokenSource encodeCts) => _encodeCts = encodeCts;

    internal void DetachCancellationSource(CancellationTokenSource encodeCts)
    {
        if (ReferenceEquals(_encodeCts, encodeCts))
            _encodeCts = null;
    }

    partial void OnStatusChanged(CompressionQueueItemStatus value) => NotifyActionCommandStateChanged();

    private static string BuildDuplicateKey(
        string inputPath,
        string outputDirectory,
        VideoClipRange clipRange,
        EncodeSettings settings,
        string? cropFilter)
    {
        string normalizedInputPath = Path.GetFullPath(inputPath);
        string normalizedOutputDirectory = Path.GetFullPath(outputDirectory);
        string normalizedCropFilter = string.IsNullOrWhiteSpace(cropFilter)
            ? "NO_CROP"
            : cropFilter.Trim();

        return string.Join(
            "|",
            normalizedInputPath.ToUpperInvariant(),
            normalizedOutputDirectory.ToUpperInvariant(),
            clipRange.Start.Ticks,
            clipRange.End.Ticks,
            settings.Encoder,
            settings.FrameRateMode,
            EncodeSettings.NormalizeOutputNameAffix(settings.OutputNamePrefix),
            EncodeSettings.NormalizeOutputNameAffix(settings.OutputNameSuffix),
            EncodeSettings.NormalizeSvtAv1Preset(settings.SvtAv1Preset),
            normalizedCropFilter);
    }

    private static string ResolveDefaultProgressState(CompressionQueueItemStatus status, string? failureMessage) => status switch
    {
        CompressionQueueItemStatus.Encoding => "Preparing...",
        CompressionQueueItemStatus.Completed => "Done",
        CompressionQueueItemStatus.Cancelled => "Cancelled",
        CompressionQueueItemStatus.Failed => string.IsNullOrWhiteSpace(failureMessage) ? "Compression failed." : failureMessage,
        _ => WaitingInQueueText
    };

    private static string BuildProgressText(string? label, int progressPercent)
    {
        if (!string.IsNullOrWhiteSpace(label) &&
            label.Contains("analy", StringComparison.OrdinalIgnoreCase))
        {
            return "Analyzing...";
        }

        return "Encoding...";
    }

    private static string FormatElapsed(TimeSpan value)
    {
        int roundedSeconds = Math.Max(1, (int)Math.Round(value.TotalSeconds, MidpointRounding.AwayFromZero));
        var rounded = TimeSpan.FromSeconds(roundedSeconds);

        return rounded.TotalHours >= 1
            ? rounded.ToString(@"h\:mm\:ss")
            : rounded.ToString(@"m\:ss");
    }

    private static string FormatFrameRate(double value)
    {
        if (value <= 0)
            return "--";

        double roundedInteger = Math.Round(value, MidpointRounding.AwayFromZero);
        return Math.Abs(value - roundedInteger) < 0.01
            ? roundedInteger.ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string FormatCrop(string? cropFilter)
    {
        if (string.IsNullOrWhiteSpace(cropFilter))
            return "None";

        string cropExpression = cropFilter.Split(',', 2)[0].Trim();
        if (!cropExpression.StartsWith("crop=", StringComparison.OrdinalIgnoreCase))
            return "Applied";

        string[] values = cropExpression["crop=".Length..].Split(':');
        return values.Length >= 2 &&
               int.TryParse(values[0], out int width) &&
               int.TryParse(values[1], out int height) &&
               width > 0 &&
               height > 0
            ? FormatAspectRatio(width, height)
            : "Applied";
    }

    private static string FormatAspectRatio(int width, int height)
    {
        double actualRatio = width / (double)height;
        foreach ((string label, double presetRatio) in CommonAspectRatios)
        {
            if (Math.Abs(actualRatio - presetRatio) / presetRatio <= CommonAspectRatioTolerance)
                return label;
        }

        return CropDetector.AspectLabel(width, height);
    }

    private bool CanExecutePrimaryAction() =>
        (CanCancel && _cancelAction is not null) ||
        (CanRestart && _restartAction is not null);

    private Task ExecutePrimaryActionAsync()
    {
        if (CanCancel && _cancelAction is not null)
        {
            _cancelAction(this);
            return Task.CompletedTask;
        }

        if (CanRestart && _restartAction is not null)
            return _restartAction(this);

        return Task.CompletedTask;
    }

    private bool CanExecuteRemove() => CanRemove && _removeAction is not null;

    private Task ExecuteRemoveAsync() =>
        CanExecuteRemove()
            ? _removeAction!(this)
            : Task.CompletedTask;

    private void NotifyActionCommandStateChanged()
    {
        PrimaryActionCommand.NotifyCanExecuteChanged();
        RemoveCommand.NotifyCanExecuteChanged();
    }
}
