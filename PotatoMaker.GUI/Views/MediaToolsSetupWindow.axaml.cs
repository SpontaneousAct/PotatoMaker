using Avalonia.Controls;
using Avalonia.Interactivity;
using PotatoMaker.Core;
using PotatoMaker.GUI.Services;
using System.Diagnostics;

namespace PotatoMaker.GUI.Views;

/// <summary>
/// Required first-run setup for PotatoMaker's pinned FFmpeg and VLC runtimes.
/// </summary>
public partial class MediaToolsSetupWindow : Window
{
    private readonly IMediaToolsRuntimeService? _runtimeService;
    private readonly TextBlock _downloadSummaryTextBlock;
    private readonly TextBlock _statusTextBlock;
    private readonly ProgressBar _downloadProgressBar;
    private readonly Button _downloadButton;
    private readonly Button _exitButton;
    private CancellationTokenSource? _downloadCts;

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
        _statusTextBlock = this.FindControl<TextBlock>("StatusTextBlock")!;
        _downloadProgressBar = this.FindControl<ProgressBar>("DownloadProgressBar")!;
        _downloadButton = this.FindControl<Button>("DownloadButton")!;
        _exitButton = this.FindControl<Button>("ExitButton")!;
        UpdateStatus(initialStatus);
        Closing += (_, _) => _downloadCts?.Cancel();
    }

    private async void OnDownloadClick(object? sender, RoutedEventArgs e)
    {
        if (_runtimeService is null || _downloadCts is not null)
            return;

        SetBusy(true);
        _downloadCts = new CancellationTokenSource();
        var progress = new Progress<MediaToolsDownloadProgress>(value =>
        {
            _downloadProgressBar.IsVisible = true;
            _statusTextBlock.IsVisible = true;
            _downloadProgressBar.IsIndeterminate = false;
            _downloadProgressBar.Value = value.Percent;
            _statusTextBlock.Text = $"{value.Stage} ({value.ToolNumber} of {value.ToolCount})... {value.Percent}%";
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

            _statusTextBlock.IsVisible = true;
            _statusTextBlock.Text = "Media-tools setup did not complete. Check the details above and try again.";
        }
        catch (OperationCanceledException)
        {
            _statusTextBlock.IsVisible = true;
            _statusTextBlock.Text = "Download cancelled. Temporary files were removed.";
        }
        catch (Exception ex)
        {
            _statusTextBlock.IsVisible = true;
            _statusTextBlock.Text = $"Media tools could not be installed: {ex.Message}";
        }
        finally
        {
            _downloadCts?.Dispose();
            _downloadCts = null;
            SetBusy(false);
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

    private static void OnOpenLicensingClick(object? sender, RoutedEventArgs e) =>
        OpenUrl(AppLinkCatalog.ThirdPartyNoticesUrl);

    private void UpdateStatus(MediaToolsRuntimeStatus status)
    {
        _downloadSummaryTextBlock.Text = status.IsReady
            ? "The required dependencies are installed and ready."
            : $"This is a one-time download of about {FormatMegabytes(status.RequiredDownloadBytes)} MB.";
        _statusTextBlock.IsVisible = false;
        _statusTextBlock.Text = string.Empty;
    }

    private void SetBusy(bool busy)
    {
        _downloadButton.IsEnabled = !busy;
        _exitButton.Content = busy ? "Cancel" : "Exit";
    }

    private static string FormatMegabytes(long bytes) =>
        Math.Ceiling(bytes / (1024d * 1024d)).ToString("0");

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
        }
    }

    private static MediaToolsRuntimeStatus MissingStatus() =>
        new(
            FfmpegRuntimeValidationResult.Invalid("FFmpeg setup is required."),
            LibVlcRuntimeValidationResult.Missing("VLC setup is required."));
}
