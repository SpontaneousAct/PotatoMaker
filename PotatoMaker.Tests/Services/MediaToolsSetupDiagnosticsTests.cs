using PotatoMaker.GUI.Services;
using Xunit;

namespace PotatoMaker.Tests.Services;

public sealed class MediaToolsSetupDiagnosticsTests
{
    [Fact]
    public void TryWriteFailure_SavesStageAndTechnicalException()
    {
        string root = Path.Combine(Path.GetTempPath(), $"potatomaker-setup-log-{Guid.NewGuid():N}");

        try
        {
            var progress = new MediaToolsDownloadProgress(
                "FFmpeg",
                "Installing FFmpeg",
                100,
                1,
                2);
            var exception = new UnauthorizedAccessException("Access denied for test.");

            string? logPath = MediaToolsSetupDiagnostics.TryWriteFailure(exception, progress, root);

            Assert.NotNull(logPath);
            string contents = File.ReadAllText(logPath);
            Assert.Contains("Installing FFmpeg", contents, StringComparison.Ordinal);
            Assert.Contains("UnauthorizedAccessException", contents, StringComparison.Ordinal);
            Assert.Contains("Access denied for test", contents, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
