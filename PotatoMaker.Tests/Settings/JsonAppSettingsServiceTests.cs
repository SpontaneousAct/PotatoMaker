using System.Diagnostics;
using PotatoMaker.GUI.Services;
using Xunit;

namespace PotatoMaker.Tests.Settings;

public sealed class JsonAppSettingsServiceTests
{
    [Fact]
    public void Load_InvalidJson_ReturnsDefaultsAndReportsWarning()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"potatomaker-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        string settingsPath = Path.Combine(tempDirectory, "settings.json");
        File.WriteAllText(settingsPath, "{ invalid json");

        var listener = new RecordingTraceListener();
        TraceListenerCollection listeners = Trace.Listeners;
        listeners.Add(listener);

        try
        {
            var service = new JsonAppSettingsService(settingsPath);

            AppSettings settings = service.Load();

            Assert.Equal(new AppSettings(), settings);
            Assert.Contains(listener.Messages, message => message.Contains(settingsPath, StringComparison.Ordinal));
        }
        finally
        {
            listeners.Remove(listener);
            listener.Dispose();
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_ReplacesExistingFileWithoutLeavingTemporaryFiles()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"potatomaker-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        string settingsPath = Path.Combine(tempDirectory, "settings.json");
        await File.WriteAllTextAsync(settingsPath, "{}");

        try
        {
            var service = new JsonAppSettingsService(settingsPath);

            await service.SaveAsync(new AppSettings
            {
                IsDarkMode = true,
                UseNvencEncoder = false,
                PreviewVolumePercent = 42,
                SvtAv1Preset = 8,
                LastOutputFolder = "C:\\encoded"
            });

            Assert.True(File.Exists(settingsPath));
            Assert.Empty(Directory.GetFiles(tempDirectory, "*.tmp"));

            AppSettings settings = service.Load();
            Assert.True(settings.IsDarkMode);
            Assert.False(settings.UseNvencEncoder);
            Assert.Equal(42, settings.PreviewVolumePercent);
            Assert.Equal(8, settings.SvtAv1Preset);
            Assert.Equal("C:\\encoded", settings.LastOutputFolder);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private sealed class RecordingTraceListener : TraceListener
    {
        public List<string> Messages { get; } = [];

        public override void Write(string? message)
        {
            if (!string.IsNullOrWhiteSpace(message))
                Messages.Add(message);
        }

        public override void WriteLine(string? message)
        {
            if (!string.IsNullOrWhiteSpace(message))
                Messages.Add(message);
        }
    }
}
