using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using PotatoMaker.Core;

namespace PotatoMaker.GUI.ViewModels;

public partial class VideoSummaryViewModel : ViewModelBase
{
    [ObservableProperty] private string? _fileSize;
    [ObservableProperty] private string? _duration;
    [ObservableProperty] private string? _resolution;
    [ObservableProperty] private string? _frameRate;
    [ObservableProperty] private bool _hasData;

    /// <summary>
    /// The last successful probe result. Available after <see cref="ProbeAsync"/> completes.
    /// </summary>
    public VideoInfo? Info { get; private set; }

    public async Task ProbeAsync(string path)
    {
        try
        {
            FileSize = FormatFileSize(new FileInfo(path).Length);

            var info = await VideoInfo.ProbeAsync(path);
            Info = info;

            Duration = info.Duration.TotalHours >= 1
                ? info.Duration.ToString(@"h\:mm\:ss")
                : info.Duration.ToString(@"m\:ss");

            Resolution = info.Width > 0
                ? $"{info.Width}×{info.Height}"
                : "N/A";

            FrameRate = info.FrameRate > 0
                ? $"{info.FrameRate:0.##} fps"
                : "N/A";

            HasData = true;
        }
        catch (Exception)
        {
            Clear();
            throw; // let the caller handle logging
        }
    }

    public void Clear()
    {
        Info = null;
        FileSize = null;
        Duration = null;
        Resolution = null;
        FrameRate = null;
        HasData = false;
    }

    private static string FormatFileSize(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F2} GB",
        >= 1_048_576     => $"{bytes / 1_048_576.0:F1} MB",
        >= 1024          => $"{bytes / 1024.0:F0} KB",
        _                => $"{bytes} B"
    };
}
