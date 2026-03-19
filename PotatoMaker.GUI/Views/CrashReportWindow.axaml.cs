using System.Diagnostics;
using Avalonia.Controls;
using PotatoMaker.GUI.Services;

namespace PotatoMaker.GUI.Views;

/// <summary>
/// Prompts the user to review and submit a saved crash report after the app restarts.
/// </summary>
public partial class CrashReportWindow : Window
{
    private readonly CrashReport _report;
    private readonly CrashReportService _crashReportService;

    public CrashReportWindow()
        : this(
            new CrashReport
            {
                ExceptionType = "Crash report",
                ExceptionMessage = "No crash report data is available."
            },
            CrashReportService.Shared)
    {
    }

    public CrashReportWindow(CrashReport report, CrashReportService crashReportService)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(crashReportService);

        InitializeComponent();

        _report = report;
        _crashReportService = crashReportService;
        ExceptionTypeTextBlock.Text = report.ExceptionType;
        ExceptionMessageTextBlock.Text = report.ExceptionMessage;

        if (!string.IsNullOrWhiteSpace(report.CurrentOperation))
        {
            OperationTextBlock.Text = $"Last known operation: {report.CurrentOperation}";
            OperationTextBlock.IsVisible = true;
        }
    }

    private async void OnCopyReportClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard is null)
        {
            ShowStatus("Clipboard is not available in this window.");
            return;
        }

        try
        {
            await topLevel.Clipboard.SetTextAsync(_report.ToClipboardText());
            ShowStatus("Crash report copied to the clipboard.");
        }
        catch
        {
            ShowStatus("PotatoMaker couldn't copy the crash report to the clipboard.");
        }
    }

    private void OnOpenReportFolderClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        string folderPath = !string.IsNullOrWhiteSpace(_report.FilePath)
            ? Path.GetDirectoryName(_report.FilePath) ?? _crashReportService.ReportsDirectoryPath
            : _crashReportService.ReportsDirectoryPath;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{folderPath}\"",
                UseShellExecute = true
            });
        }
        catch
        {
            ShowStatus("PotatoMaker couldn't open the crash report folder.");
        }
    }

    private void OnReportIssueClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _crashReportService.BuildIssueUrl(_report),
                UseShellExecute = true
            });
        }
        catch
        {
            ShowStatus("PotatoMaker couldn't open the GitHub issue page.");
        }
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void ShowStatus(string message)
    {
        StatusTextBlock.Text = message;
        StatusTextBlock.IsVisible = !string.IsNullOrWhiteSpace(message);
    }
}
