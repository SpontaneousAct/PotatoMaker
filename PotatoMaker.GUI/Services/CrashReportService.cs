using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PotatoMaker.GUI.Services;

/// <summary>
/// Captures unhandled exceptions into sanitized crash reports that can be reviewed on the next launch.
/// </summary>
public sealed class CrashReportService
{
    public const string DefaultIssueUrl = "https://github.com/SpontaneousAct/PotatoMaker/issues/new";

    private static readonly Regex WindowsPathRegex = new(
        @"(?<![A-Za-z0-9])(?<path>(?:[A-Za-z]:\\|\\\\)[^:\r\n""<>|?*]+(?:\\[^:\r\n""<>|?*]+)*)(?<line>:line \d+)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly AsyncLocal<string?> CurrentOperation = new();

    private readonly object _gate = new();
    private readonly Func<string> _appVersionProvider;
    private readonly Func<string> _osDescriptionProvider;
    private readonly Func<DateTimeOffset> _clock;
    private string? _lastFingerprint;
    private DateTimeOffset _lastWrittenAtUtc;
    private bool _globalHandlersInstalled;

    static CrashReportService()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public CrashReportService(
        string? reportsDirectoryPath = null,
        string? issueUrl = null,
        Func<string>? appVersionProvider = null,
        Func<string>? osDescriptionProvider = null,
        Func<DateTimeOffset>? clock = null)
    {
        ReportsDirectoryPath = reportsDirectoryPath ?? Path.Combine(JsonAppSettingsService.GetSettingsDirectoryPath(), "Crashes");
        IssueUrl = string.IsNullOrWhiteSpace(issueUrl) ? DefaultIssueUrl : issueUrl.Trim();
        _appVersionProvider = appVersionProvider ?? CreateDefaultAppVersionProvider;
        _osDescriptionProvider = osDescriptionProvider ?? CreateDefaultOsDescriptionProvider;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public static CrashReportService Shared { get; } = new();

    public string ReportsDirectoryPath { get; }

    public string IssueUrl { get; }

    public void InstallGlobalHandlers()
    {
        lock (_gate)
        {
            if (_globalHandlersInstalled)
                return;

            AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnTaskSchedulerUnobservedTaskException;
            _globalHandlersInstalled = true;
        }
    }

    public IDisposable BeginOperation(string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        return new CrashOperationScope(operation.Trim());
    }

    public CrashReport? WriteUnhandledException(Exception exception, string source, bool isTerminating = true)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        DateTimeOffset occurredAtUtc = _clock();
        string sanitizedMessage = SanitizeValue(exception.Message);
        string? sanitizedStackTrace = SanitizeValue(exception.ToString());
        string? currentOperation = SanitizeValue(CurrentOperation.Value);

        var report = new CrashReport
        {
            Id = Guid.NewGuid().ToString("N"),
            OccurredAtUtc = occurredAtUtc,
            PromptPending = true,
            Source = source.Trim(),
            AppVersion = _appVersionProvider(),
            OperatingSystem = _osDescriptionProvider(),
            CurrentOperation = string.IsNullOrWhiteSpace(currentOperation) ? null : currentOperation,
            ExceptionType = exception.GetType().FullName ?? exception.GetType().Name,
            ExceptionMessage = string.IsNullOrWhiteSpace(sanitizedMessage) ? "No exception message was provided." : sanitizedMessage,
            StackTrace = string.IsNullOrWhiteSpace(sanitizedStackTrace) ? null : sanitizedStackTrace,
            IsTerminating = isTerminating
        };

        if (ShouldSkipDuplicate(report))
            return null;

        Directory.CreateDirectory(ReportsDirectoryPath);

        string filePath = Path.Combine(
            ReportsDirectoryPath,
            $"crash-{occurredAtUtc:yyyyMMdd-HHmmss}-{report.Id}.json");

        try
        {
            string json = JsonSerializer.Serialize(report, JsonOptions);
            File.WriteAllText(filePath, json, Encoding.UTF8);
            return report with { FilePath = filePath };
        }
        catch
        {
            return null;
        }
    }

    public CrashReport? TryGetLatestPendingReport()
    {
        if (!Directory.Exists(ReportsDirectoryPath))
            return null;

        foreach (string filePath in Directory.EnumerateFiles(ReportsDirectoryPath, "crash-*.json")
                     .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            CrashReport? report = TryReadReport(filePath);
            if (report?.PromptPending == true)
                return report;
        }

        return null;
    }

    public void MarkReportAsReviewed(CrashReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (string.IsNullOrWhiteSpace(report.FilePath) || !File.Exists(report.FilePath))
            return;

        try
        {
            string json = JsonSerializer.Serialize(report with { PromptPending = false }, JsonOptions);
            File.WriteAllText(report.FilePath, json, Encoding.UTF8);
        }
        catch
        {
            // Keep the original report intact if the review marker cannot be saved.
        }
    }

    public string BuildIssueUrl(CrashReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        string titleSuffix = Truncate(report.ExceptionMessage, 80);
        string title = string.IsNullOrWhiteSpace(titleSuffix)
            ? $"Crash report: {report.ExceptionType}"
            : $"Crash report: {report.ExceptionType} - {titleSuffix}";

        const string body =
            "PotatoMaker saved a crash report locally. Please describe what you were doing, then paste the copied crash report below.\n\n```text\n[paste crash report here]\n```";

        return $"{IssueUrl}?title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(body)}";
    }

    private void OnCurrentDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            WriteUnhandledException(exception, "AppDomain.CurrentDomain.UnhandledException", e.IsTerminating);
            return;
        }

        string description = e.ExceptionObject?.ToString() ?? "Unknown unhandled exception object.";
        WriteUnhandledException(
            new InvalidOperationException(SanitizeValue(description)),
            "AppDomain.CurrentDomain.UnhandledException",
            e.IsTerminating);
    }

    private void OnTaskSchedulerUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        try
        {
            WriteUnhandledException(e.Exception, "TaskScheduler.UnobservedTaskException", isTerminating: false);
        }
        finally
        {
            e.SetObserved();
        }
    }

    private CrashReport? TryReadReport(string filePath)
    {
        try
        {
            string json = File.ReadAllText(filePath, Encoding.UTF8);
            CrashReport? report = JsonSerializer.Deserialize<CrashReport>(json, JsonOptions);
            return report is null ? null : report with { FilePath = filePath };
        }
        catch
        {
            return null;
        }
    }

    private bool ShouldSkipDuplicate(CrashReport report)
    {
        string fingerprint = string.Join(
            "|",
            report.ExceptionType,
            report.ExceptionMessage,
            report.StackTrace);

        lock (_gate)
        {
            DateTimeOffset now = _clock();
            if (string.Equals(_lastFingerprint, fingerprint, StringComparison.Ordinal) &&
                now - _lastWrittenAtUtc <= TimeSpan.FromSeconds(5))
            {
                return true;
            }

            _lastFingerprint = fingerprint;
            _lastWrittenAtUtc = now;
            return false;
        }
    }

    private static string CreateDefaultAppVersionProvider()
        => new AssemblyAppVersionService(typeof(CrashReportService).Assembly).InformationalVersion;

    private static string CreateDefaultOsDescriptionProvider()
        => $"{RuntimeInformation.OSDescription.Trim()} ({RuntimeInformation.OSArchitecture})";

    private static string SanitizeValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return WindowsPathRegex.Replace(value, match =>
        {
            string lineSuffix = match.Groups["line"].Value;
            return $"<path>{lineSuffix}";
        });
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLength)
            return value;

        return value[..Math.Max(0, maxLength - 3)].TrimEnd() + "...";
    }

    private sealed class CrashOperationScope : IDisposable
    {
        private readonly string? _previousOperation;
        private bool _disposed;

        public CrashOperationScope(string operation)
        {
            _previousOperation = CurrentOperation.Value;
            CurrentOperation.Value = operation;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            CurrentOperation.Value = _previousOperation;
            _disposed = true;
        }
    }
}

/// <summary>
/// Serializable crash report payload saved locally after an unhandled exception.
/// </summary>
public sealed record CrashReport
{
    public string Id { get; init; } = string.Empty;

    public DateTimeOffset OccurredAtUtc { get; init; }

    public bool PromptPending { get; init; }

    public string Source { get; init; } = string.Empty;

    public string AppVersion { get; init; } = string.Empty;

    public string OperatingSystem { get; init; } = string.Empty;

    public string? CurrentOperation { get; init; }

    public string ExceptionType { get; init; } = string.Empty;

    public string ExceptionMessage { get; init; } = string.Empty;

    public string? StackTrace { get; init; }

    public bool IsTerminating { get; init; }

    [JsonIgnore]
    public string? FilePath { get; init; }

    public string ToClipboardText()
    {
        var builder = new StringBuilder();
        builder.AppendLine("PotatoMaker crash report");
        builder.Append("Occurred (UTC): ").AppendLine(OccurredAtUtc.ToString("u"));
        builder.Append("App version: ").AppendLine(AppVersion);
        builder.Append("OS: ").AppendLine(OperatingSystem);
        builder.Append("Source: ").AppendLine(Source);

        if (!string.IsNullOrWhiteSpace(CurrentOperation))
            builder.Append("Current operation: ").AppendLine(CurrentOperation);

        builder.Append("Terminating: ").AppendLine(IsTerminating ? "Yes" : "No");
        builder.Append("Exception type: ").AppendLine(ExceptionType);
        builder.Append("Exception message: ").AppendLine(ExceptionMessage);
        builder.AppendLine();
        builder.AppendLine("Stack trace:");
        builder.AppendLine(string.IsNullOrWhiteSpace(StackTrace) ? "(not available)" : StackTrace);
        return builder.ToString().TrimEnd();
    }
}
