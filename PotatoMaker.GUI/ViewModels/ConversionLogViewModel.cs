using System;
using CommunityToolkit.Mvvm.ComponentModel;
using PotatoMaker.Core;

namespace PotatoMaker.GUI.ViewModels;

/// <summary>
/// Stores the user-facing analysis and encode status for the UI.
/// </summary>
public partial class ConversionLogViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyPropertyChangedFor(nameof(IsAnalysing))]
    [NotifyPropertyChangedFor(nameof(IsEncoding))]
    [NotifyPropertyChangedFor(nameof(IsCancelled))]
    [NotifyPropertyChangedFor(nameof(IsError))]
    [NotifyPropertyChangedFor(nameof(IsDone))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(ShowProgress))]
    private ConversionStatus _status = ConversionStatus.Idle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string _idleText = "Choose a video";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string _doneText = "Done";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string? _statusStepText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowProgress))]
    private bool _isProcessing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    [NotifyPropertyChangedFor(nameof(ShowProgress))]
    private int _progressPercent;

    public bool IsIdle => Status == ConversionStatus.Idle;

    public bool IsAnalysing => Status == ConversionStatus.Analysing;

    public bool IsEncoding => Status == ConversionStatus.Encoding;

    public bool IsCancelled => Status == ConversionStatus.Cancelled;

    public bool IsError => Status == ConversionStatus.Error;

    public bool IsDone => Status == ConversionStatus.Done;

    public string StatusText => Status switch
    {
        ConversionStatus.Analysing => ComposeStatusText("Analysing"),
        ConversionStatus.Encoding => ComposeStatusText("Compressing"),
        ConversionStatus.Cancelled => "Cancelled",
        ConversionStatus.Error => "Error",
        ConversionStatus.Done => DoneText,
        _ => IdleText
    };

    public string ProgressText => $"{ProgressPercent}%";

    public bool ShowProgress => IsProcessing || Status == ConversionStatus.Done;

    public void BeginAnalysis()
    {
        if (IsProcessing)
            return;

        ProgressPercent = 0;
        DoneText = "Done";
        StatusStepText = null;
        Status = ConversionStatus.Analysing;
    }

    public void CompleteAnalysis()
    {
        if (IsProcessing)
            return;

        ProgressPercent = 0;
        DoneText = "Done";
        StatusStepText = null;
        Status = ConversionStatus.Idle;
    }

    public void BeginEncoding()
    {
        BeginEncoding(initialStatus: ConversionStatus.Analysing, initialStepText: null);
    }

    public void BeginEncoding(ConversionStatus initialStatus, string? initialStepText = null)
    {
        ProgressPercent = 0;
        IsProcessing = true;
        DoneText = "Done";
        StatusStepText = initialStepText;
        Status = initialStatus;
    }

    public void UpdateProgress(EncodeProgress value)
    {
        if (!IsProcessing)
            return;

        ProgressPercent = Math.Clamp(value.Percent, 0, 100);
        StatusStepText = GetProgressStepText(value.Label);
        Status = IsAnalysingLabel(value.Label)
            ? ConversionStatus.Analysing
            : ConversionStatus.Encoding;
    }

    public void MarkDone(TimeSpan? elapsed = null)
    {
        ProgressPercent = 100;
        IsProcessing = false;
        DoneText = elapsed is { } value
            ? $"Done in {FormatElapsed(value)}"
            : "Done";
        StatusStepText = null;
        Status = ConversionStatus.Done;
    }

    public void MarkCancelled()
    {
        ProgressPercent = 0;
        IsProcessing = false;
        DoneText = "Done";
        StatusStepText = null;
        Status = ConversionStatus.Cancelled;
    }

    public void MarkError()
    {
        ProgressPercent = 0;
        IsProcessing = false;
        DoneText = "Done";
        StatusStepText = null;
        Status = ConversionStatus.Error;
    }

    public void MarkAnalysisError()
    {
        if (IsProcessing)
            return;

        ProgressPercent = 0;
        DoneText = "Done";
        StatusStepText = null;
        Status = ConversionStatus.Error;
    }

    public void Clear()
    {
        ProgressPercent = 0;
        IsProcessing = false;
        DoneText = "Done";
        StatusStepText = null;
        Status = ConversionStatus.Idle;
    }

    public void ReturnToIdle()
    {
        ProgressPercent = 0;
        IsProcessing = false;
        DoneText = "Done";
        StatusStepText = null;
        Status = ConversionStatus.Idle;
    }

    public void SetIdleText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            value = "Ready";

        IdleText = value;
    }

    private static bool IsAnalysingLabel(string? label) =>
        !string.IsNullOrWhiteSpace(label) &&
        label.Contains("analy", StringComparison.OrdinalIgnoreCase);

    private static string? GetProgressStepText(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return null;

        ReadOnlySpan<char> span = label.AsSpan();
        int passIndex = span.IndexOf("[Pass ".AsSpan(), StringComparison.OrdinalIgnoreCase);
        if (passIndex < 0)
            return null;

        ReadOnlySpan<char> remainder = span[passIndex..];
        int closeIndex = remainder.IndexOf(']');
        if (closeIndex < 0)
            return null;

        ReadOnlySpan<char> passValue = remainder.Slice("[Pass ".Length, closeIndex - "[Pass ".Length).Trim();
        return passValue.IsEmpty ? null : passValue.ToString();
    }

    private string ComposeStatusText(string baseText) =>
        string.IsNullOrWhiteSpace(StatusStepText)
            ? baseText
            : $"{baseText} {StatusStepText}";

    private static string FormatElapsed(TimeSpan value)
    {
        int roundedSeconds = Math.Max(1, (int)Math.Round(value.TotalSeconds, MidpointRounding.AwayFromZero));
        var rounded = TimeSpan.FromSeconds(roundedSeconds);

        return rounded.TotalHours >= 1
            ? rounded.ToString(@"h\:mm\:ss")
            : rounded.ToString(@"m\:ss");
    }
}
