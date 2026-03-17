namespace PotatoMaker.GUI.Services;

/// <summary>
/// Represents a source video that has already been successfully processed.
/// </summary>
public sealed record ProcessedVideoRecord(
    string FullPath,
    long SourceLastWriteUtcTicks,
    DateTimeOffset ProcessedAtUtc);

/// <summary>
/// Tracks which source videos have already been successfully compressed.
/// </summary>
public interface IProcessedVideoTracker
{
    event EventHandler? ProcessedVideosChanged;

    bool IsProcessed(string videoPath, DateTimeOffset lastModified);

    Task MarkProcessedAsync(string videoPath, CancellationToken ct = default);
}

/// <summary>
/// Disables processed-video tracking while preserving call sites.
/// </summary>
public sealed class DisabledProcessedVideoTracker : IProcessedVideoTracker
{
    public static DisabledProcessedVideoTracker Instance { get; } = new();

    private DisabledProcessedVideoTracker()
    {
    }

    public event EventHandler? ProcessedVideosChanged
    {
        add { }
        remove { }
    }

    public bool IsProcessed(string videoPath, DateTimeOffset lastModified) => false;

    public Task MarkProcessedAsync(string videoPath, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>
/// Persists processed source-video history and exposes a simple lookup API.
/// </summary>
public sealed class ProcessedVideoTracker : IProcessedVideoTracker
{
    public const int MaxTrackedVideos = 256;

    private readonly IAppSettingsCoordinator _settingsCoordinator;
    private readonly Lock _sync = new();
    private ProcessedVideoRecord[] _records;

    public ProcessedVideoTracker(IAppSettingsCoordinator settingsCoordinator)
    {
        _settingsCoordinator = settingsCoordinator;
        _records = NormalizeRecords(settingsCoordinator.Current.ProcessedVideos);
    }

    public event EventHandler? ProcessedVideosChanged;

    public bool IsProcessed(string videoPath, DateTimeOffset lastModified)
    {
        if (!TryCreateKey(videoPath, lastModified, out ProcessedVideoKey key))
            return false;

        lock (_sync)
        {
            return _records.Any(record => Matches(record, key));
        }
    }

    public async Task MarkProcessedAsync(string videoPath, CancellationToken ct = default)
    {
        if (!TryCreateRecord(videoPath, out ProcessedVideoRecord? record) || record is null)
            return;

        ProcessedVideoRecord[] updatedRecords = [];
        await _settingsCoordinator.UpdateAsync(settings =>
        {
            updatedRecords = UpsertRecord(settings.ProcessedVideos, record);
            return settings with
            {
                ProcessedVideos = updatedRecords
            };
        }, ct).ConfigureAwait(false);

        lock (_sync)
        {
            _records = updatedRecords;
        }

        ProcessedVideosChanged?.Invoke(this, EventArgs.Empty);
    }

    private static ProcessedVideoRecord[] UpsertRecord(
        ProcessedVideoRecord[]? existingRecords,
        ProcessedVideoRecord incomingRecord)
    {
        ProcessedVideoRecord[] normalized = NormalizeRecords(existingRecords);
        var updated = new List<ProcessedVideoRecord>(normalized.Length + 1)
        {
            incomingRecord
        };

        ProcessedVideoKey incomingKey = new(incomingRecord.FullPath, incomingRecord.SourceLastWriteUtcTicks);
        foreach (ProcessedVideoRecord record in normalized)
        {
            if (Matches(record, incomingKey))
                continue;

            updated.Add(record);
        }

        return updated
            .OrderByDescending(record => record.ProcessedAtUtc.UtcTicks)
            .Take(MaxTrackedVideos)
            .ToArray();
    }

    private static ProcessedVideoRecord[] NormalizeRecords(ProcessedVideoRecord[]? records)
    {
        if (records is null || records.Length == 0)
            return [];

        return records
            .Where(record => !string.IsNullOrWhiteSpace(record.FullPath) && record.SourceLastWriteUtcTicks > 0)
            .Select(record => record with
            {
                FullPath = NormalizePath(record.FullPath)
            })
            .GroupBy(
                record => new ProcessedVideoKey(record.FullPath, record.SourceLastWriteUtcTicks),
                ProcessedVideoKeyComparer.Instance)
            .Select(group => group
                .OrderByDescending(record => record.ProcessedAtUtc.UtcTicks)
                .First())
            .OrderByDescending(record => record.ProcessedAtUtc.UtcTicks)
            .Take(MaxTrackedVideos)
            .ToArray();
    }

    private static bool TryCreateRecord(string videoPath, out ProcessedVideoRecord? record)
    {
        record = null;
        if (string.IsNullOrWhiteSpace(videoPath))
            return false;

        try
        {
            string normalizedPath = NormalizePath(videoPath);
            if (!File.Exists(normalizedPath))
                return false;

            long lastWriteTicks = File.GetLastWriteTimeUtc(normalizedPath).Ticks;
            if (lastWriteTicks <= 0)
                return false;

            record = new ProcessedVideoRecord(
                normalizedPath,
                lastWriteTicks,
                DateTimeOffset.UtcNow);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool TryCreateKey(string videoPath, DateTimeOffset lastModified, out ProcessedVideoKey key)
    {
        key = default;
        if (string.IsNullOrWhiteSpace(videoPath))
            return false;

        try
        {
            key = new ProcessedVideoKey(
                NormalizePath(videoPath),
                lastModified.UtcDateTime.Ticks);
            return key.SourceLastWriteUtcTicks > 0;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private static string NormalizePath(string path) => Path.GetFullPath(path);

    private static bool Matches(ProcessedVideoRecord record, ProcessedVideoKey key) =>
        key.SourceLastWriteUtcTicks == record.SourceLastWriteUtcTicks &&
        string.Equals(record.FullPath, key.FullPath, StringComparison.OrdinalIgnoreCase);

    private readonly record struct ProcessedVideoKey(string FullPath, long SourceLastWriteUtcTicks);

    private sealed class ProcessedVideoKeyComparer : IEqualityComparer<ProcessedVideoKey>
    {
        public static ProcessedVideoKeyComparer Instance { get; } = new();

        public bool Equals(ProcessedVideoKey x, ProcessedVideoKey y) =>
            x.SourceLastWriteUtcTicks == y.SourceLastWriteUtcTicks &&
            string.Equals(x.FullPath, y.FullPath, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(ProcessedVideoKey obj) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.FullPath), obj.SourceLastWriteUtcTicks);
    }
}
