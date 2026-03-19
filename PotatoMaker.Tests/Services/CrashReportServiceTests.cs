using System.Text;
using PotatoMaker.GUI.Services;
using Xunit;

namespace PotatoMaker.Tests.Services;

public sealed class CrashReportServiceTests
{
    [Fact]
    public void WriteUnhandledException_savesPendingReportWithSanitizedPaths()
    {
        string reportsDirectory = Path.Combine(Path.GetTempPath(), $"potatomaker-crash-tests-{Guid.NewGuid():N}");

        try
        {
            var service = new CrashReportService(
                reportsDirectoryPath: reportsDirectory,
                appVersionProvider: () => "9.8.7",
                osDescriptionProvider: () => "TestOS",
                clock: () => new DateTimeOffset(2026, 3, 19, 12, 0, 0, TimeSpan.Zero));

            using IDisposable operation = service.BeginOperation("Compressing video");
            var exception = new InvalidOperationException("Failed to open C:\\Users\\Alice\\Videos\\secret.mp4");

            CrashReport? report = service.WriteUnhandledException(exception, "Program.Main");

            Assert.NotNull(report);
            Assert.True(report.PromptPending);
            Assert.Equal("9.8.7", report.AppVersion);
            Assert.Equal("TestOS", report.OperatingSystem);
            Assert.Equal("Compressing video", report.CurrentOperation);
            Assert.Contains("<path>", report.ExceptionMessage);
            Assert.DoesNotContain("C:\\Users\\Alice\\Videos\\secret.mp4", report.ExceptionMessage);

            CrashReport? pendingReport = service.TryGetLatestPendingReport();
            Assert.NotNull(pendingReport);
            Assert.Equal(report.Id, pendingReport.Id);

            string persistedJson = File.ReadAllText(report.FilePath!, Encoding.UTF8);
            Assert.Contains("\"PromptPending\": true", persistedJson);
            Assert.DoesNotContain("C:\\Users\\Alice\\Videos\\secret.mp4", persistedJson);
        }
        finally
        {
            if (Directory.Exists(reportsDirectory))
                Directory.Delete(reportsDirectory, recursive: true);
        }
    }

    [Fact]
    public void MarkReportAsReviewed_clearsPendingPromptFlag()
    {
        string reportsDirectory = Path.Combine(Path.GetTempPath(), $"potatomaker-crash-tests-{Guid.NewGuid():N}");

        try
        {
            var service = new CrashReportService(
                reportsDirectoryPath: reportsDirectory,
                clock: () => new DateTimeOffset(2026, 3, 19, 12, 0, 0, TimeSpan.Zero));

            CrashReport report = service.WriteUnhandledException(new Exception("boom"), "Program.Main")!;

            service.MarkReportAsReviewed(report);

            CrashReport? pendingReport = service.TryGetLatestPendingReport();
            Assert.Null(pendingReport);

            string persistedJson = File.ReadAllText(report.FilePath!, Encoding.UTF8);
            Assert.Contains("\"PromptPending\": false", persistedJson);
        }
        finally
        {
            if (Directory.Exists(reportsDirectory))
                Directory.Delete(reportsDirectory, recursive: true);
        }
    }

    [Fact]
    public void WriteUnhandledException_deduplicatesSameCrashAcrossDifferentHandlers()
    {
        string reportsDirectory = Path.Combine(Path.GetTempPath(), $"potatomaker-crash-tests-{Guid.NewGuid():N}");

        try
        {
            var service = new CrashReportService(
                reportsDirectoryPath: reportsDirectory,
                clock: () => new DateTimeOffset(2026, 3, 19, 12, 0, 0, TimeSpan.Zero));

            var exception = new InvalidOperationException("boom");

            CrashReport? first = service.WriteUnhandledException(exception, "Program.Main");
            CrashReport? second = service.WriteUnhandledException(exception, "AppDomain.CurrentDomain.UnhandledException");

            Assert.NotNull(first);
            Assert.Null(second);
            Assert.Single(Directory.EnumerateFiles(reportsDirectory, "crash-*.json"));
        }
        finally
        {
            if (Directory.Exists(reportsDirectory))
                Directory.Delete(reportsDirectory, recursive: true);
        }
    }

    [Fact]
    public void BuildIssueUrl_prefillsCrashTitleAndBody()
    {
        var service = new CrashReportService(issueUrl: "https://github.com/SpontaneousAct/PotatoMaker/issues/new");
        var report = new CrashReport
        {
            ExceptionType = "System.InvalidOperationException",
            ExceptionMessage = "Unexpected error while compressing video."
        };

        string issueUrl = service.BuildIssueUrl(report);

        Assert.StartsWith("https://github.com/SpontaneousAct/PotatoMaker/issues/new?", issueUrl, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString("Crash report: System.InvalidOperationException - Unexpected error while compressing video."), issueUrl);
        Assert.Contains(Uri.EscapeDataString("[paste crash report here]"), issueUrl);
    }
}
