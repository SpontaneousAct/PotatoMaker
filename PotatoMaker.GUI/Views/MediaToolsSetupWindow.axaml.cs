using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PotatoMaker.Core;
using PotatoMaker.GUI.Services;

namespace PotatoMaker.GUI.Views;

/// <summary>
/// Required first-run setup for PotatoMaker's pinned FFmpeg and VLC runtimes.
/// </summary>
public partial class MediaToolsSetupWindow : Window
{
    private readonly IMediaToolsRuntimeService? _runtimeService;
    private readonly TextBlock _downloadSummaryTextBlock;
    private readonly TextBlock _ffmpegStatusTextBlock;
    private readonly TextBlock _libVlcStatusTextBlock;
    private readonly StackPanel _progressPanel;
    private readonly ProgressBar _downloadProgressBar;
    private readonly TextBlock _progressTextBlock;
    private readonly TextBox _statusTextBox;
    private readonly Button _downloadButton;
    private readonly Button _checkAgainButton;
    private readonly Button _exitButton;
    private CancellationTokenSource? _downloadCts;
    private MediaToolsDownloadProgress? _lastProgress;

    public MediaToolsSetupWindow()
        : this(null, MissingStatus())
    {
    }

    public MediaToolsSetupWindow(
        IMediaToolsRuntimeService? runtimeService,
        MediaToolsRuntimeStatus initialStatus)
    {
        InitializeComponent();
        _runtimeService = runtimeService;
        _downloadSummaryTextBlock = this.FindControl<TextBlock>("DownloadSummaryTextBlock")!;
        _ffmpegStatusTextBlock = this.FindControl<TextBlock>("FfmpegStatusTextBlock")!;
        _libVlcStatusTextBlock = this.FindControl<TextBlock>("LibVlcStatusTextBlock")!;
        _progressPanel = this.FindControl<StackPanel>("ProgressPanel")!;
        _downloadProgressBar = this.FindControl<ProgressBar>("DownloadProgressBar")!;
        _progressTextBlock = this.FindControl<TextBlock>("ProgressTextBlock")!;
        _statusTextBox = this.FindControl<TextBox>("StatusTextBox")!;
        _downloadButton = this.FindControl<Button>("DownloadButton")!;
        _checkAgainButton = this.FindControl<Button>("CheckAgainButton")!;
        _exitButton = this.FindControl<Button>("ExitButton")!;

        PopulatePackageDetails();
        UpdateStatus(initialStatus);
        Closing += (_, _) => _downloadCts?.Cancel();
    }

    private async void OnDownloadClick(object? sender, RoutedEventArgs e)
    {
        if (_runtimeService is null || _downloadCts is not null)
            return;

        SetBusy(true);
        HideError();
        _lastProgress = null;
        _downloadCts = new CancellationTokenSource();
        var progress = new Progress<MediaToolsDownloadProgress>(value =>
        {
            _lastProgress = value;
            _progressPanel.IsVisible = true;
            _downloadProgressBar.IsIndeterminate = false;
            _downloadProgressBar.Value = value.Percent;
            _progressTextBlock.Text =
                $"{value.Stage} ({value.ToolNumber} of {value.ToolCount}) — {value.Percent}%";
        });

        try
        {
            MediaToolsRuntimeStatus result = await _runtimeService.InstallMissingAsync(progress, _downloadCts.Token);
            UpdateStatus(result);
            if (result.IsReady)
            {
                Close(true);
                return;
            }

            ShowError(BuildIncompleteMessage(result));
        }
        catch (OperationCanceledException)
        {
            ShowError("Download cancelled. Temporary files were removed; you can retry when ready.");
        }
        catch (Exception ex)
        {
            string? logPath = MediaToolsSetupDiagnostics.TryWriteFailure(ex, _lastProgress);
            string message = $"Media tools could not be installed.\n\n{ex.Message}";
            if (!string.IsNullOrWhiteSpace(logPath))
                message += $"\n\nTechnical details were saved to:\n{logPath}";
            ShowError(message);
        }
        finally
        {
            _downloadCts?.Dispose();
            _downloadCts = null;
            SetBusy(false);
        }
    }

    private async void OnCheckAgainClick(object? sender, RoutedEventArgs e)
    {
        if (_runtimeService is null || _downloadCts is not null)
            return;

        SetBusy(true);
        HideError();
        try
        {
            MediaToolsRuntimeStatus status = await _runtimeService.DetectAsync();
            UpdateStatus(status);
            if (status.IsReady)
            {
                Close(true);
                return;
            }

            ShowError(BuildIncompleteMessage(status));
        }
        catch (Exception ex)
        {
            ShowError($"PotatoMaker could not check the media tools.\n\n{ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnFfmpegManualDownloadClick(object? sender, RoutedEventArgs e) =>
        OpenWithShell(FfmpegRuntimePackage.DownloadUrl);

    private void OnLibVlcManualDownloadClick(object? sender, RoutedEventArgs e) =>
        OpenWithShell(LibVlcRuntimePackage.DownloadUrl);

    private void OnOpenRuntimeFolderClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(MediaRuntimePaths.Root);
            Directory.CreateDirectory(MediaRuntimePaths.FfmpegRoot);
            Directory.CreateDirectory(MediaRuntimePaths.LibVlcRoot);
            OpenWithShell(MediaRuntimePaths.Root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError($"Windows could not open the media-tools folder.\n\n{ex.Message}");
        }
    }

    private void OnExitClick(object? sender, RoutedEventArgs e)
    {
        if (_downloadCts is not null)
        {
            _downloadCts.Cancel();
            return;
        }

        Close(false);
    }

    private void PopulatePackageDetails()
    {
        this.FindControl<SelectableTextBlock>("FfmpegVersionTextBlock")!.Text =
            $"{FfmpegRuntimePackage.DisplayVersion} — {FormatMegabytes(FfmpegRuntimePackage.ArchiveSizeBytes)} MB";
        this.FindControl<SelectableTextBlock>("FfmpegProviderTextBlock")!.Text =
            $"{FfmpegRuntimePackage.ProviderName} — {FfmpegRuntimePackage.ProviderUrl}";
        this.FindControl<SelectableTextBlock>("FfmpegUrlTextBlock")!.Text = FfmpegRuntimePackage.DownloadUrl;
        this.FindControl<SelectableTextBlock>("FfmpegDestinationTextBlock")!.Text = MediaRuntimePaths.FfmpegRoot;

        this.FindControl<SelectableTextBlock>("LibVlcVersionTextBlock")!.Text =
            $"{LibVlcRuntimePackage.DisplayVersion} — {FormatMegabytes(LibVlcRuntimePackage.ArchiveSizeBytes)} MB";
        this.FindControl<SelectableTextBlock>("LibVlcProviderTextBlock")!.Text =
            $"{LibVlcRuntimePackage.ProviderName} — {LibVlcRuntimePackage.ProviderUrl}";
        this.FindControl<SelectableTextBlock>("LibVlcUrlTextBlock")!.Text = LibVlcRuntimePackage.DownloadUrl;
        this.FindControl<SelectableTextBlock>("LibVlcDestinationTextBlock")!.Text = MediaRuntimePaths.LibVlcRoot;

        this.FindControl<SelectableTextBlock>("HashSummaryTextBlock")!.Text =
            $"FFmpeg SHA-256: {FfmpegRuntimePackage.ArchiveSha256}\n" +
            $"VLC SHA-256: {LibVlcRuntimePackage.ArchiveSha256}";
    }

    private void UpdateStatus(MediaToolsRuntimeStatus status)
    {
        _downloadSummaryTextBlock.Text = status.IsReady
            ? "The required dependencies are installed and ready."
            : $"Automatic setup will download about {FormatMegabytes(status.RequiredDownloadBytes)} MB. Nothing is downloaded until you choose Download and install.";

        _ffmpegStatusTextBlock.Text = status.Ffmpeg.IsValid
            ? $"Ready — {status.Ffmpeg.DisplayName}"
            : "Required — not installed or not compatible";
        _libVlcStatusTextBlock.Text = status.LibVlc.IsValid
            ? $"Ready — VLC {status.LibVlc.Version ?? "detected"}"
            : "Required — not installed or not compatible";
        _progressPanel.IsVisible = false;
    }

    private void SetBusy(bool busy)
    {
        _downloadButton.IsEnabled = !busy;
        _checkAgainButton.IsEnabled = !busy;
        _exitButton.Content = busy ? "Cancel" : "Exit";
    }

    private void ShowError(string message)
    {
        _statusTextBox.Text = message;
        _statusTextBox.IsVisible = true;
    }

    private void HideError()
    {
        _statusTextBox.Text = string.Empty;
        _statusTextBox.IsVisible = false;
    }

    private static string BuildIncompleteMessage(MediaToolsRuntimeStatus status)
    {
        var messages = new List<string>();
        if (!status.Ffmpeg.IsValid)
            messages.Add($"FFmpeg: {status.Ffmpeg.Message}");
        if (!status.LibVlc.IsValid)
            messages.Add($"VLC: {status.LibVlc.Message}");
        return "Setup is not complete.\n\n" + string.Join("\n", messages);
    }

    private void OpenWithShell(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ShowError($"Windows could not open this location. You can copy it from the details above.\n\n{ex.Message}");
        }
    }

    private static string FormatMegabytes(long bytes) =>
        Math.Ceiling(bytes / (1024d * 1024d)).ToString("0");

    private static MediaToolsRuntimeStatus MissingStatus() =>
        new(
            FfmpegRuntimeValidationResult.Invalid("FFmpeg setup is required."),
            LibVlcRuntimeValidationResult.Missing("VLC setup is required."));
}
