using PotatoMaker.GUI.Services;
using Xunit;

namespace PotatoMaker.Tests.Services;

public sealed class RecentVideoDiscoveryServiceTests
{
    [Fact]
    public void GetRecentVideos_FiltersSortsAndLimitsSupportedFiles()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"potatomaker-recent-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            string[] supportedPaths =
            [
                CreateFile(directory, "clip-01.mp4", DateTime.UtcNow.AddMinutes(-7)),
                CreateFile(directory, "clip-02.mkv", DateTime.UtcNow.AddMinutes(-6)),
                CreateFile(directory, "clip-03.mov", DateTime.UtcNow.AddMinutes(-5)),
                CreateFile(directory, "clip-04.webm", DateTime.UtcNow.AddMinutes(-4)),
                CreateFile(directory, "clip-05.avi", DateTime.UtcNow.AddMinutes(-3)),
                CreateFile(directory, "clip-06.mp4", DateTime.UtcNow.AddMinutes(-2))
            ];
            CreateFile(directory, "notes.txt", DateTime.UtcNow.AddMinutes(-1));

            var service = new RecentVideoDiscoveryService();

            IReadOnlyList<RecentVideoFile> results = service.GetRecentVideos(directory);

            Assert.Equal(5, results.Count);
            Assert.Equal("clip-06.mp4", results[0].FileName);
            Assert.Equal("clip-05.avi", results[1].FileName);
            Assert.DoesNotContain(results, file => file.FileName == "notes.txt");
            Assert.DoesNotContain(results, file => file.FileName == Path.GetFileName(supportedPaths[0]));
            Assert.All(results, file => Assert.StartsWith(directory, file.FullPath, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void GetRecentVideos_MissingDirectory_ReturnsEmptyList()
    {
        var service = new RecentVideoDiscoveryService();

        IReadOnlyList<RecentVideoFile> results = service.GetRecentVideos(@"Z:\this\folder\should\not\exist");

        Assert.Empty(results);
    }

    private static string CreateFile(string directory, string name, DateTime lastWriteTimeUtc)
    {
        string path = Path.Combine(directory, name);
        File.WriteAllText(path, name);
        File.SetLastWriteTimeUtc(path, lastWriteTimeUtc);
        return path;
    }
}
